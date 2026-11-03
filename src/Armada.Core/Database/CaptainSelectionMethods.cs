namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Structured selection history and budget lifecycle operations over provider-owned connections.</summary>
    internal sealed partial class CaptainSelectionMethods : ICaptainSelectionMethods
    {
        private readonly Func<DbConnection> _Factory;
        private readonly SqliteWriteGate? _Gate;
        private static readonly string[] _ChildTables = new[] { "captain_selection_execution_events", "usage_budget_reservations",
            "captain_selection_windows", "captain_selection_candidate_reasons", "captain_selection_candidates" };

        internal CaptainSelectionMethods(Func<DbConnection> factory, SqliteWriteGate? gate = null)
        {
            _Factory = factory;
            _Gate = gate;
        }

        private async Task<T> RunAsync<T>(Func<DbConnection, Task<T>> action, CancellationToken token, bool write = false)
        {
            using IDisposable? lease = write && _Gate != null ? await _Gate.EnterAsync(token).ConfigureAwait(false) : null;
            using DbConnection connection = _Factory();
            await connection.OpenAsync(token).ConfigureAwait(false);
            return await action(connection).ConfigureAwait(false);
        }

        private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql)
        {
            DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        private static void Add(DbCommand command, string name, object? value)
        {
            DbParameter parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        private static string Utc(DateTime value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        private static DateTime ParseUtc(object value) => DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        private static string PageSql(DbConnection connection) => connection.GetType().Name == "SqlConnection"
            ? " OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY" : " LIMIT @take OFFSET @skip";
        private static void Required(string value, string name)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required.", name);
        }
        private static void Paging(int pageNumber, int pageSize)
        {
            if (pageNumber < 1 || pageSize < 1 || pageSize > 1000 || (long)(pageNumber - 1) * pageSize > Int32.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be positive and page size between 1 and 1000.");
        }

        /// <inheritdoc />
        public Task<CaptainSelectionAdmissionReceipt> TryAdmitObservedAsync(CaptainSelectionRecordSet records, string? dockId = null, int ceiling = 256, CancellationToken token = default)
        {
            Validate(records);
            if (records.Decision.Mode != UsageSelectionModeEnum.Observe || String.IsNullOrWhiteSpace(records.Decision.SelectedCaptainId))
                throw new ArgumentException("Observed admission requires Observe mode and an actual selected captain.", nameof(records));
            return RunAsync(async connection =>
            {
                CaptainSelectionDecision? existing = await ReadAttemptAsync(connection, null, records.Decision.TenantId, records.Decision.ExecutionAttemptId, token).ConfigureAwait(false);
                if (existing != null) return Replay(existing, records.Decision);
                bool claimed = await new CaptainSlotStore(connection).ClaimAsync(records.Decision.SelectedCaptainId!, records.Decision.MissionId, dockId, token, ceiling, records).ConfigureAwait(false);
                if (claimed) return new CaptainSelectionAdmissionReceipt { Claimed = true, DecisionId = records.Decision.Id };
                existing = await ReadAttemptAsync(connection, null, records.Decision.TenantId, records.Decision.ExecutionAttemptId, token).ConfigureAwait(false);
                return existing == null ? new CaptainSelectionAdmissionReceipt() : Replay(existing, records.Decision);
            }, token, write: true);
        }

        private static CaptainSelectionAdmissionReceipt Replay(CaptainSelectionDecision existing, CaptainSelectionDecision requested)
        {
            if (existing.MissionId != requested.MissionId || existing.UserId != requested.UserId || existing.SelectedCaptainId != requested.SelectedCaptainId)
                throw new InvalidOperationException("Selection attempt identity belongs to a different admission.");
            return new CaptainSelectionAdmissionReceipt { AlreadyCommitted = true, DecisionId = existing.Id };
        }

        /// <inheritdoc />
        public async Task<CaptainSelectionDecision> CreateDecisionAsync(CaptainSelectionRecordSet records, CancellationToken token = default)
        {
            Validate(records);
            for (int retry = 0; ; retry++)
            {
                try
                {
                    return await RunAsync(async connection =>
                    {
                        using DbTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
                        CaptainSelectionDecision? existing = await ReadAttemptAsync(connection, transaction, records.Decision.TenantId, records.Decision.ExecutionAttemptId, token).ConfigureAwait(false);
                        if (existing != null)
                        {
                            if (existing.MissionId != records.Decision.MissionId || existing.UserId != records.Decision.UserId)
                                throw new InvalidOperationException("Selection attempt identity belongs to a different mission or owner.");
                            return existing;
                        }
                        await WriteAggregateAsync(connection, transaction, records, token).ConfigureAwait(false);
                        await transaction.CommitAsync(token).ConfigureAwait(false);
                        return records.Decision;
                    }, token, write: true).ConfigureAwait(false);
                }
                catch (DbException ex) when (retry < 2 && (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
                {
                    token.ThrowIfCancellationRequested();
                }
            }
        }

        internal static async Task WriteAggregateAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionRecordSet records, CancellationToken token)
        {
            Validate(records);
            using (DbCommand command = Command(connection, transaction, "SELECT id FROM captain_selection_policy_revisions WHERE tenant_id=@tenant AND fingerprint=@fingerprint;"))
            {
                Add(command, "@tenant", records.Decision.TenantId); Add(command, "@fingerprint", records.Policy.Fingerprint);
                object? policyId = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                if (policyId == null || policyId == DBNull.Value) await InsertCaptainSelectionPolicyRevisionAsync(connection, transaction, records.Policy, token).ConfigureAwait(false);
                else
                {
                    records.Policy.Id = Convert.ToString(policyId, CultureInfo.InvariantCulture)!;
                    records.Decision.PolicyRevisionId = records.Policy.Id;
                }
            }
            await InsertCaptainSelectionDecisionAsync(connection, transaction, records.Decision, token).ConfigureAwait(false);
            foreach (CaptainSelectionCandidateRecord row in records.Candidates) await InsertCaptainSelectionCandidateRecordAsync(connection, transaction, row, token).ConfigureAwait(false);
            foreach (CaptainSelectionReason row in records.Reasons) await InsertCaptainSelectionReasonAsync(connection, transaction, row, token).ConfigureAwait(false);
            foreach (CaptainSelectionWindowRecord row in records.Windows) await InsertCaptainSelectionWindowRecordAsync(connection, transaction, row, token).ConfigureAwait(false);
            foreach (UsageBudgetReservation row in records.Reservations) await InsertUsageBudgetReservationAsync(connection, transaction, row, token).ConfigureAwait(false);
            foreach (CaptainSelectionExecutionEvent row in records.Events) await InsertCaptainSelectionExecutionEventAsync(connection, transaction, row, token).ConfigureAwait(false);
        }

        private static void Validate(CaptainSelectionRecordSet records)
        {
            ArgumentNullException.ThrowIfNull(records);
            ArgumentNullException.ThrowIfNull(records.Decision);
            ArgumentNullException.ThrowIfNull(records.Policy);
            CaptainSelectionDecision decision = records.Decision;
            Required(decision.TenantId, nameof(decision.TenantId)); Required(decision.MissionId, nameof(decision.MissionId));
            Required(decision.ExecutionAttemptId, nameof(decision.ExecutionAttemptId)); Required(records.Policy.Fingerprint, nameof(records.Policy.Fingerprint));
            CaptainSelectionPolicyRevision canonical = CaptainSelectionPolicyRevision.FromSettings(decision.TenantId, records.Policy.ToSettings());
            if (records.Policy.Fingerprint != canonical.Fingerprint || records.Policy.AlgorithmVersion != canonical.AlgorithmVersion
                || decision.AlgorithmVersion != canonical.AlgorithmVersion || decision.Mode != records.Policy.Mode)
                throw new ArgumentException("Selection policy evidence must match its validated effective settings, mode and algorithm fingerprint.");
            if (decision.PolicyRevisionId != records.Policy.Id) throw new ArgumentException("Decision must reference the supplied policy revision.");
            if (records.Candidates.Count > records.Policy.MaximumCandidates) throw new ArgumentException("Decision candidates exceed the effective policy bound.");
            if (decision.CandidateCount != records.Candidates.Count) throw new ArgumentException("Decision candidate count must match the complete supplied set.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<CaptainSelectionRecord> rows = new List<CaptainSelectionRecord> { records.Policy, decision };
            rows.AddRange(records.Candidates); rows.AddRange(records.Reasons); rows.AddRange(records.Windows); rows.AddRange(records.Reservations); rows.AddRange(records.Events);
            foreach (CaptainSelectionRecord row in rows)
            {
                if (row == null || String.IsNullOrWhiteSpace(row.Id) || row.Id.Length > 64 || !ids.Add(row.Id) || row.TenantId != decision.TenantId)
                    throw new ArgumentException("Selection records require unique bounded IDs and the decision's tenant scope.");
            }
            HashSet<string> candidates = records.Candidates.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (CaptainSelectionCandidateRecord row in records.Candidates)
                if (row.DecisionId != decision.Id) throw new ArgumentException("Candidate belongs to another decision.");
            foreach (CaptainSelectionWindowRecord row in records.Windows)
                if (row.DecisionId != decision.Id || !candidates.Contains(row.CandidateId)) throw new ArgumentException("Window belongs to another decision or candidate.");
            foreach (CaptainSelectionReason row in records.Reasons)
                if (row.DecisionId != decision.Id || !candidates.Contains(row.CandidateId)) throw new ArgumentException("Reason belongs to another decision or candidate.");
            foreach (UsageBudgetReservation row in records.Reservations)
            {
                if (row.DecisionId != decision.Id || row.ExecutionAttemptId != decision.ExecutionAttemptId || row.MissionId != decision.MissionId || !Double.IsFinite(row.ReservedPercent) || row.ReservedPercent <= 0 || row.ReservedPercent > 100)
                    throw new ArgumentException("Reservation must match the decision attempt and contain a finite positive percentage.");
                Required(row.AccountKey, nameof(row.AccountKey)); Required(row.HostId, nameof(row.HostId)); Required(row.WindowKey, nameof(row.WindowKey));
            }
            foreach (CaptainSelectionExecutionEvent row in records.Events)
                if (row.DecisionId != decision.Id || row.ExecutionAttemptId != decision.ExecutionAttemptId || row.UserId != decision.UserId) throw new ArgumentException("Execution event belongs to another attempt or owner.");
        }

        private static async Task<CaptainSelectionDecision?> ReadAttemptAsync(DbConnection connection, DbTransaction? transaction, string tenantId, string attemptId, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "SELECT * FROM captain_selection_decisions WHERE tenant_id=@tenant AND execution_attempt_id=@attempt;");
            Add(command, "@tenant", tenantId); Add(command, "@attempt", attemptId);
            using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? MapCaptainSelectionDecision(reader) : null;
        }

        /// <inheritdoc />
        public Task<CaptainSelectionDecision?> ReadDecisionAsync(string tenantId, string decisionId, string? ownerUserId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT * FROM captain_selection_decisions WHERE tenant_id=@tenant AND id=@id" + (ownerUserId == null ? "" : " AND user_id=@owner") + ";");
                Add(command, "@tenant", tenantId); Add(command, "@id", decisionId); if (ownerUserId != null) Add(command, "@owner", ownerUserId);
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false) ? MapCaptainSelectionDecision(reader) : null;
            }, token);
        }

        /// <inheritdoc />
        public Task<CaptainSelectionPolicyRevision?> ReadPolicyAsync(string tenantId, string policyId, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(policyId, nameof(policyId));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT * FROM captain_selection_policy_revisions WHERE tenant_id=@tenant AND id=@id;");
                Add(command, "@tenant", tenantId); Add(command, "@id", policyId);
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false) ? MapCaptainSelectionPolicyRevision(reader) : null;
            }, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<CaptainSelectionDecision>> EnumerateByMissionAsync(string tenantId, string missionId, string? ownerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(missionId, nameof(missionId)); Paging(pageNumber, pageSize);
            return RunAsync(async connection =>
            {
                string where = " FROM captain_selection_decisions WHERE tenant_id=@tenant AND mission_id=@mission" + (ownerUserId == null ? "" : " AND user_id=@owner");
                using DbCommand command = Command(connection, null, "SELECT COUNT(*)" + where);
                Add(command, "@tenant", tenantId); Add(command, "@mission", missionId); if (ownerUserId != null) Add(command, "@owner", ownerUserId);
                long count = Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                command.CommandText = "SELECT *" + where + " ORDER BY evaluated_utc DESC,id DESC" + PageSql(connection);
                Add(command, "@skip", (pageNumber - 1) * pageSize); Add(command, "@take", pageSize);
                List<CaptainSelectionDecision> rows = new List<CaptainSelectionDecision>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(MapCaptainSelectionDecision(reader));
                return EnumerationResult<CaptainSelectionDecision>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, rows, count);
            }, token);
        }

        private static string CandidateScope(string? owner, string? candidateOwner)
            => "c.tenant_id=@tenant AND c.decision_id=@decision AND d.tenant_id=c.tenant_id AND d.id=c.decision_id"
                + (owner == null ? "" : " AND d.user_id=@owner") + (candidateOwner == null ? "" : " AND c.user_id=@candidateOwner");
        private static void CandidateParameters(DbCommand command, string tenant, string decision, string? owner, string? candidateOwner)
        {
            Add(command, "@tenant", tenant); Add(command, "@decision", decision);
            if (owner != null) Add(command, "@owner", owner); if (candidateOwner != null) Add(command, "@candidateOwner", candidateOwner);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<CaptainSelectionCandidateRecord>> EnumerateCandidatesAsync(string tenantId, string decisionId, string? ownerUserId = null, string? candidateOwnerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId)); Paging(pageNumber, pageSize);
            return RunAsync(async connection =>
            {
                string where = " FROM captain_selection_candidates c JOIN captain_selection_decisions d ON d.id=c.decision_id WHERE " + CandidateScope(ownerUserId, candidateOwnerUserId);
                using DbCommand command = Command(connection, null, "SELECT COUNT(*)" + where);
                CandidateParameters(command, tenantId, decisionId, ownerUserId, candidateOwnerUserId);
                long count = Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                command.CommandText = "SELECT c.*" + where + " ORDER BY c.rank_number,c.id" + PageSql(connection);
                Add(command, "@skip", (pageNumber - 1) * pageSize); Add(command, "@take", pageSize);
                List<CaptainSelectionCandidateRecord> rows = new List<CaptainSelectionCandidateRecord>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(MapCaptainSelectionCandidateRecord(reader));
                return EnumerationResult<CaptainSelectionCandidateRecord>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, rows, count);
            }, token);
        }

        /// <inheritdoc />
        public Task<List<CaptainSelectionWindowRecord>> ReadWindowsAsync(string tenantId, string decisionId, string candidateId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId)); Required(candidateId, nameof(candidateId));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT w.* FROM captain_selection_windows w JOIN captain_selection_candidates c ON c.id=w.candidate_id AND c.tenant_id=w.tenant_id JOIN captain_selection_decisions d ON d.id=c.decision_id WHERE "
                    + CandidateScope(ownerUserId, candidateOwnerUserId) + " AND w.decision_id=c.decision_id AND c.id=@candidate ORDER BY w.id;");
                CandidateParameters(command, tenantId, decisionId, ownerUserId, candidateOwnerUserId); Add(command, "@candidate", candidateId);
                List<CaptainSelectionWindowRecord> rows = new List<CaptainSelectionWindowRecord>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(MapCaptainSelectionWindowRecord(reader));
                return rows;
            }, token);
        }
    }
}
