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
    using static Armada.Core.Database.CaptainSelectionCandidateRows;
    using static Armada.Core.Database.CaptainSelectionDecisionRows;
    using static Armada.Core.Database.CaptainSelectionEvidence;
    using static Armada.Core.Database.CaptainSelectionExecutionEventRows;
    using static Armada.Core.Database.CaptainSelectionOmittedCandidateStore;
    using static Armada.Core.Database.CaptainSelectionPolicyRevisionRows;
    using static Armada.Core.Database.CaptainSelectionReasonRows;
    using static Armada.Core.Database.CaptainSelectionSql;
    using static Armada.Core.Database.CaptainSelectionWindowRows;
    using static Armada.Core.Database.UsageBudgetReservationRows;

    /// <summary>Structured selection history and budget lifecycle operations over provider-owned connections.</summary>
    internal sealed class CaptainSelectionMethods : ICaptainSelectionMethods
    {
        /// <summary>Transition reason on a reservation an automatic redispatch took over.</summary>
        internal const string CarriedToRedispatchReason = "CarriedToRedispatch";

        private readonly CaptainSelectionConnector _Connector;
        private readonly CaptainSelectionAccountEvidence _AccountEvidence;
        private readonly CaptainSelectionWaits _Waits;
        private readonly CaptainSelectionEvidence _Evidence;
        private readonly CaptainSelectionLifecycle _Lifecycle;
        private readonly CaptainSelectionProjection _Projection;
        private readonly CaptainSelectionRecoveryAuthorization _RecoveryAuthorization;
        private readonly CaptainSelectionLaunchBoundary _LaunchBoundary;
        private readonly CaptainSelectionUntrackedExecutions _Untracked;
        private readonly CaptainSelectionOmittedCandidateStore _OmittedCandidates;
        internal static readonly string[] ChildTables = new[] { "captain_selection_execution_events", "usage_budget_reservations",
            "captain_selection_windows", "captain_selection_candidate_reasons", "captain_selection_omitted_candidates", "captain_selection_candidates" };

        internal CaptainSelectionMethods(Func<DbConnection> factory, SqliteWriteGate? gate = null)
        {
            _Connector = new CaptainSelectionConnector(factory, gate);
            _AccountEvidence = new CaptainSelectionAccountEvidence(_Connector);
            _Waits = new CaptainSelectionWaits(_Connector);
            _Evidence = new CaptainSelectionEvidence(_Connector);
            _Lifecycle = new CaptainSelectionLifecycle(_Connector);
            _Projection = new CaptainSelectionProjection(_Connector);
            _RecoveryAuthorization = new CaptainSelectionRecoveryAuthorization(_Connector);
            _LaunchBoundary = new CaptainSelectionLaunchBoundary(_Connector);
            _Untracked = new CaptainSelectionUntrackedExecutions(_Connector);
            _OmittedCandidates = new CaptainSelectionOmittedCandidateStore(_Connector);
        }

        /// <inheritdoc />
        public Task<CaptainSelectionAdmissionReceipt> TryAdmitObservedAsync(CaptainSelectionRecordSet records, string? dockId = null, int ceiling = 256, CancellationToken token = default)
            => AdmitSelectionAsync(records, dockId, ceiling, null, UsageSelectionModeEnum.Observe, token);

        /// <inheritdoc />
        public Task<CaptainSelectionAdmissionReceipt> TryAdmitObservedRepairAsync(CaptainSelectionRecordSet records, DateTime verificationVersion, int ceiling = 256, CancellationToken token = default)
            => AdmitSelectionAsync(records, null, ceiling, verificationVersion, UsageSelectionModeEnum.Observe, token);

        private Task<CaptainSelectionAdmissionReceipt> AdmitSelectionAsync(CaptainSelectionRecordSet records, string? dockId, int ceiling, DateTime? verificationVersion, UsageSelectionModeEnum expectedMode, CancellationToken token)
        {
            Validate(records);
            if (records.Decision.Mode != expectedMode || String.IsNullOrWhiteSpace(records.Decision.SelectedCaptainId))
                throw new ArgumentException("Selection admission requires the requested mode and an actual selected captain.", nameof(records));
            return _Connector.RunAsync(async connection =>
            {
                CaptainSelectionDecision? existing = await ReadAttemptAsync(connection, null, records.Decision.TenantId, records.Decision.ExecutionAttemptId, token).ConfigureAwait(false);
                if (existing != null) return Replay(existing, records.Decision);
                CaptainSlotStore slots = new CaptainSlotStore(connection);
                bool claimed = verificationVersion.HasValue
                    ? await slots.ResumeVerificationAsync(records.Decision.SelectedCaptainId!, records.Decision.MissionId, verificationVersion.Value, ceiling, token, records).ConfigureAwait(false)
                    : await slots.ClaimAsync(records.Decision.SelectedCaptainId!, records.Decision.MissionId, dockId, token, ceiling, records).ConfigureAwait(false);
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
                    return await _Connector.RunAsync(async connection =>
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
                catch (DbException ex) when (retry < 2 && UniqueConstraintViolation.IsUniqueViolation(ex))
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
            foreach (CaptainSelectionOmittedCandidates row in records.OmittedCandidates) await InsertCaptainSelectionOmittedCandidatesAsync(connection, transaction, row, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(records.AdmissionCarriedAttemptId) && records.Decision.Outcome == CaptainSelectionOutcomes.Admitted && records.Reservations.Count > 0)
                await CarryReservationsAsync(connection, transaction, records, token).ConfigureAwait(false);
            foreach (UsageBudgetReservation row in records.Reservations) await InsertUsageBudgetReservationAsync(connection, transaction, row, token).ConfigureAwait(false);
            foreach (CaptainSelectionExecutionEvent row in records.Events) await InsertCaptainSelectionExecutionEventAsync(connection, transaction, row, token).ConfigureAwait(false);
        }

        // An automatic redispatch after a no-work attempt takes over that attempt's reservation on the same account window
        // instead of adding a second one: the new reservation keeps the larger amount and the old one is released. A row
        // that changed concurrently is left alone and keeps counting, which only over-reserves.
        private static async Task CarryReservationsAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionRecordSet records, CancellationToken token)
        {
            List<UsageBudgetReservation> earlier = new List<UsageBudgetReservation>();
            using (DbCommand read = Command(connection, transaction, "SELECT * FROM usage_budget_reservations WHERE tenant_id=@tenant AND mission_id=@mission AND execution_attempt_id=@carried AND state IN ('Active','Unsettled');"))
            {
                Add(read, "@tenant", records.Decision.TenantId); Add(read, "@mission", records.Decision.MissionId); Add(read, "@carried", records.AdmissionCarriedAttemptId);
                using DbDataReader reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) earlier.Add(MapUsageBudgetReservation(reader));
            }
            DateTime now = DateTime.UtcNow;
            foreach (UsageBudgetReservation next in records.Reservations)
            {
                UsageBudgetReservation? carried = earlier.FirstOrDefault(r => r.HostId == next.HostId && r.Runtime == next.Runtime && r.AccountKey == next.AccountKey
                    && r.WindowKey == next.WindowKey && r.Generation == next.Generation);
                if (carried == null) continue;
                using DbCommand release = Command(connection, transaction, "UPDATE usage_budget_reservations SET state='Released',revision=revision+1,last_update_utc=@at,released_utc=@at,transition_reason=@reason"
                    + " WHERE tenant_id=@tenant AND id=@id AND revision=@revision AND state=@state;");
                Add(release, "@at", Utc(now)); Add(release, "@reason", CarriedToRedispatchReason); Add(release, "@tenant", carried.TenantId);
                Add(release, "@id", carried.Id); Add(release, "@revision", carried.Revision); Add(release, "@state", carried.State.ToString());
                if (await release.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1) next.ReservedPercent = Math.Max(next.ReservedPercent, carried.ReservedPercent);
                earlier.Remove(carried);
            }
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
            if (records.OmittedCandidates.Any(o => o.DecisionId != decision.Id || o.CandidateCount < 1 || String.IsNullOrWhiteSpace(o.ReasonCode)))
                throw new ArgumentException("Omitted candidate counts must be positive, carry a reason and belong to the decision.");
            if (decision.CandidateCount != records.Candidates.Count + records.OmittedCandidates.Sum(o => o.CandidateCount))
                throw new ArgumentException("Decision candidate count must equal the stored candidates plus the omitted candidate counts.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<CaptainSelectionRecord> rows = new List<CaptainSelectionRecord> { records.Policy, decision };
            rows.AddRange(records.Candidates); rows.AddRange(records.Reasons); rows.AddRange(records.Windows); rows.AddRange(records.OmittedCandidates); rows.AddRange(records.Reservations); rows.AddRange(records.Events);
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

        internal static async Task<CaptainSelectionDecision?> ReadAttemptAsync(DbConnection connection, DbTransaction? transaction, string tenantId, string attemptId, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "SELECT * FROM captain_selection_decisions WHERE tenant_id=@tenant AND execution_attempt_id=@attempt;");
            Add(command, "@tenant", tenantId); Add(command, "@attempt", attemptId);
            using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? MapCaptainSelectionDecision(reader) : null;
        }

        /// <inheritdoc />
        public Task<CaptainSelectionDecision?> ReadCurrentDecisionAsync(string tenantId, string missionId, string? ownerUserId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(missionId, nameof(missionId));
            return _Connector.RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT d.* FROM captain_selection_decisions d JOIN missions m ON m.selection_decision_id=d.id AND m.tenant_id=d.tenant_id WHERE d.tenant_id=@tenant AND m.id=@mission"
                    + (ownerUserId == null ? "" : " AND d.user_id=@owner AND m.user_id=@owner") + ";");
                Add(command, "@tenant", tenantId); Add(command, "@mission", missionId); if (ownerUserId != null) Add(command, "@owner", ownerUserId);
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false) ? MapCaptainSelectionDecision(reader) : null;
            }, token);
        }

        /// <inheritdoc />
        public Task<CaptainSelectionDecision?> ReadDecisionAsync(string tenantId, string decisionId, string? ownerUserId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId));
            return _Connector.RunAsync(async connection =>
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
            return _Connector.RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT * FROM captain_selection_policy_revisions WHERE tenant_id=@tenant AND id=@id;");
                Add(command, "@tenant", tenantId); Add(command, "@id", policyId);
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false) ? MapCaptainSelectionPolicyRevision(reader) : null;
            }, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<CaptainSelectionDecision>> EnumerateByMissionAsync(string tenantId, string missionId, string? ownerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => EnumerateHistoryAsync(tenantId, missionId, ownerUserId, new CaptainSelectionHistoryQuery { PageNumber = pageNumber, PageSize = pageSize }, token);

        /// <inheritdoc />
        public Task<EnumerationResult<CaptainSelectionDecision>> EnumerateHistoryAsync(string tenantId, string missionId, string? ownerUserId, CaptainSelectionHistoryQuery query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            int pageNumber = query.PageNumber; int pageSize = query.PageSize;
            Required(tenantId, nameof(tenantId)); Required(missionId, nameof(missionId)); Paging(pageNumber, pageSize);
            if (query.FromUtc >= query.ToUtc) throw new ArgumentException("History start must precede its end.", nameof(query));
            return _Connector.RunAsync(async connection =>
            {
                string where = " FROM captain_selection_decisions WHERE tenant_id=@tenant AND mission_id=@mission" + (ownerUserId == null ? "" : " AND user_id=@owner")
                    + (query.Trigger == null ? "" : " AND trigger_kind=@trigger") + (query.Outcome == null ? "" : " AND outcome=@outcome")
                    + (query.FromUtc.HasValue ? " AND evaluated_utc>=@from" : "") + (query.ToUtc.HasValue ? " AND evaluated_utc<@to" : "");
                using DbCommand command = Command(connection, null, "SELECT COUNT(*)" + where);
                Add(command, "@tenant", tenantId); Add(command, "@mission", missionId); if (ownerUserId != null) Add(command, "@owner", ownerUserId);
                if (query.Trigger != null) Add(command, "@trigger", query.Trigger);
                if (query.Outcome != null) Add(command, "@outcome", query.Outcome);
                if (query.FromUtc.HasValue) Add(command, "@from", Utc(query.FromUtc.Value));
                if (query.ToUtc.HasValue) Add(command, "@to", Utc(query.ToUtc.Value));
                long count = Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                command.CommandText = "SELECT *" + where + " ORDER BY evaluated_utc DESC,id DESC" + PageSql(connection);
                Add(command, "@skip", (pageNumber - 1) * pageSize); Add(command, "@take", pageSize);
                List<CaptainSelectionDecision> rows = new List<CaptainSelectionDecision>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(MapCaptainSelectionDecision(reader));
                return EnumerationResult<CaptainSelectionDecision>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, rows, count);
            }, token);
        }

        internal static string CandidateScope(string? owner, string? candidateOwner)
            => "c.tenant_id=@tenant AND c.decision_id=@decision AND d.tenant_id=c.tenant_id AND d.id=c.decision_id"
                + (owner == null ? "" : " AND d.user_id=@owner") + (candidateOwner == null ? "" : " AND (c.user_id=@candidateOwner OR c.user_id IS NULL)");
        internal static void CandidateParameters(DbCommand command, string tenant, string decision, string? owner, string? candidateOwner)
        {
            Add(command, "@tenant", tenant); Add(command, "@decision", decision);
            if (owner != null) Add(command, "@owner", owner); if (candidateOwner != null) Add(command, "@candidateOwner", candidateOwner);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<CaptainSelectionCandidateRecord>> EnumerateCandidatesAsync(string tenantId, string decisionId, string? ownerUserId = null, string? candidateOwnerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId)); Paging(pageNumber, pageSize);
            return _Connector.RunAsync(async connection =>
            {
                string where = " FROM captain_selection_candidates c JOIN captain_selection_decisions d ON d.id=c.decision_id WHERE " + CandidateScope(ownerUserId, candidateOwnerUserId);
                using DbCommand command = Command(connection, null, "SELECT COUNT(*)" + where);
                CandidateParameters(command, tenantId, decisionId, ownerUserId, candidateOwnerUserId);
                long count = Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                command.CommandText = "SELECT c.*," + CurrentCaptainExistsSql(candidateOwnerUserId) + " AS captain_available" + where + " ORDER BY c.rank_number,c.id" + PageSql(connection);
                Add(command, "@skip", (pageNumber - 1) * pageSize); Add(command, "@take", pageSize);
                List<CaptainSelectionCandidateRecord> rows = new List<CaptainSelectionCandidateRecord>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    CaptainSelectionCandidateRecord row = MapCaptainSelectionCandidateRecord(reader);
                    row.CurrentCaptainExists = Convert.ToInt32(reader["captain_available"], CultureInfo.InvariantCulture) != 0;
                    rows.Add(row);
                }
                return EnumerationResult<CaptainSelectionCandidateRecord>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, rows, count);
            }, token);
        }

        /// <inheritdoc />
        public Task<List<CaptainSelectionWindowRecord>> ReadWindowsAsync(string tenantId, string decisionId, string candidateId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId)); Required(candidateId, nameof(candidateId));
            return _Connector.RunAsync(async connection =>
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
        /// <inheritdoc />
        public Task<CaptainSelectionAdmissionReceipt> TryAdmitEnforcedAsync(CaptainSelectionRecordSet records, string? dockId = null, int ceiling = 256, CancellationToken token = default)
            => AdmitSelectionAsync(records, dockId, ceiling, null, UsageSelectionModeEnum.Enforce, token);

        /// <inheritdoc />
        public Task<CaptainSelectionAdmissionReceipt> TryAdmitEnforcedRepairAsync(CaptainSelectionRecordSet records, DateTime verificationVersion, int ceiling = 256, CancellationToken token = default)
            => AdmitSelectionAsync(records, null, ceiling, verificationVersion, UsageSelectionModeEnum.Enforce, token);

        /// <inheritdoc />
        public Task<List<UsageSelectionBudgetTotals>> ReadBudgetTotalsAsync(string tenantId, IReadOnlyList<string> captainIds, CancellationToken token = default, string? carriedExecutionAttemptId = null)
            => _AccountEvidence.ReadBudgetTotalsAsync(tenantId, captainIds, token, carriedExecutionAttemptId);

        /// <inheritdoc />
        public Task<Dictionary<string, DateTime>> ReadLastAssignedAsync(string tenantId, IReadOnlyList<string> captainIds, CancellationToken token = default)
            => _AccountEvidence.ReadLastAssignedAsync(tenantId, captainIds, token);

        /// <inheritdoc />
        public Task<Dictionary<UsageReservationStateEnum, long>> CountReservationsByStateAsync(CancellationToken token = default)
            => _AccountEvidence.CountReservationsByStateAsync(token);

        /// <inheritdoc />
        public Task<List<UsageSelectionAccountEvidence>> ReadAccountEvidenceAsync(string tenantId, IReadOnlyList<string> captainIds, CancellationToken token = default)
            => _AccountEvidence.ReadAccountEvidenceAsync(tenantId, captainIds, token);

        /// <inheritdoc />
        public Task<UsageSelectionAccountEvidence?> ReadMembershipEvidenceAsync(string tenantId, string membershipId, CancellationToken token = default)
            => _AccountEvidence.ReadMembershipEvidenceAsync(tenantId, membershipId, token);

        /// <inheritdoc />
        public Task<EnumerationResult<CaptainSelectionWaitingItem>> EnumerateActionableWaitsAsync(string? tenantId, string? userId, IReadOnlyList<string> reasonCodes, DateTime waitingSinceBeforeUtc, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => _Waits.EnumerateActionableWaitsAsync(tenantId, userId, reasonCodes, waitingSinceBeforeUtc, pageNumber, pageSize, token);

        /// <inheritdoc />
        public Task<CaptainSelectionCandidateRecord?> ReadCandidateByCaptainAsync(string tenantId, string decisionId, string captainId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
            => _Evidence.ReadCandidateByCaptainAsync(tenantId, decisionId, captainId, ownerUserId, candidateOwnerUserId, token);

        /// <inheritdoc />
        public Task<CaptainSelectionExecutionEvent?> ReadLastModelReportAsync(string tenantId, string decisionId, string? ownerUserId = null, CancellationToken token = default)
            => _Evidence.ReadLastModelReportAsync(tenantId, decisionId, ownerUserId, token);

        /// <inheritdoc />
        public Task<List<CaptainSelectionReason>> ReadReasonsAsync(string tenantId, string decisionId, string candidateId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
            => _Evidence.ReadReasonsAsync(tenantId, decisionId, candidateId, ownerUserId, candidateOwnerUserId, token);

        /// <inheritdoc />
        public Task<EnumerationResult<CaptainSelectionExecutionEvent>> EnumerateExecutionEventsAsync(string tenantId, string decisionId, string? ownerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => _Evidence.EnumerateExecutionEventsAsync(tenantId, decisionId, ownerUserId, pageNumber, pageSize, token);

        /// <inheritdoc />
        public Task<List<UsageBudgetReservation>> ReadReconciliationBatchAsync(int batchSize = 100, CancellationToken token = default)
            => _Lifecycle.ReadReconciliationBatchAsync(batchSize, token);

        /// <inheritdoc />
        public Task<bool> TouchReservationAsync(string tenantId, string reservationId, int revision, DateTime atUtc, CancellationToken token = default)
            => _Lifecycle.TouchReservationAsync(tenantId, reservationId, revision, atUtc, token);

        /// <inheritdoc />
        public Task<List<UsageBudgetReservation>> ReadReservationsAsync(string tenantId, string hostId, string runtime, string accountKey, CancellationToken token = default)
            => _Lifecycle.ReadReservationsAsync(tenantId, hostId, runtime, accountKey, token);

        /// <inheritdoc />
        public Task<bool> TryTransitionReservationAsync(string tenantId, string reservationId, int revision, UsageReservationStateEnum expected, UsageReservationStateEnum next, DateTime atUtc, string reason, string? snapshotId = null, CancellationToken token = default)
            => _Lifecycle.TryTransitionReservationAsync(tenantId, reservationId, revision, expected, next, atUtc, reason, snapshotId, token);

        /// <inheritdoc />
        public Task AppendExecutionEventAsync(CaptainSelectionExecutionEvent entry, CancellationToken token = default)
            => _Lifecycle.AppendExecutionEventAsync(entry, token);

        /// <inheritdoc />
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize = 100, CancellationToken token = default)
            => _Lifecycle.DeleteExpiredAsync(cutoffUtc, batchSize, token);

        /// <inheritdoc />
        public Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
            => _Lifecycle.DeleteByTenantAsync(tenantId, token);

        /// <inheritdoc />
        public Task<List<CaptainSelectionDecisionView>> ReadDecisionViewsAsync(string tenantId, IReadOnlyList<string> decisionIds, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
            => _Projection.ReadDecisionViewsAsync(tenantId, decisionIds, ownerUserId, candidateOwnerUserId, token);

        /// <inheritdoc />
        public Task<UsageSelectionRecovery?> ReadDecisionRecoveryAsync(string tenantId, string decisionId, CancellationToken token = default)
            => _RecoveryAuthorization.ReadDecisionRecoveryAsync(tenantId, decisionId, token);

        /// <inheritdoc />
        public Task<UsageSelectionRecovery?> ReadRecoveryAsync(string tenantId, string missionId, CancellationToken token = default)
            => _RecoveryAuthorization.ReadRecoveryAsync(tenantId, missionId, token);

        /// <inheritdoc />
        public Task<UsageSelectionRecovery?> TryAuthorizeRecoveryAsync(string tenantId, string missionId, string actorUserId, UsageSelectionRecoveryRequest request, CancellationToken token = default)
            => _RecoveryAuthorization.TryAuthorizeRecoveryAsync(tenantId, missionId, actorUserId, request, token);

        /// <inheritdoc />
        public Task<string?> ReadUsageExecutionKeyAsync(string tenantId, string missionId, CancellationToken token = default)
            => _LaunchBoundary.ReadUsageExecutionKeyAsync(tenantId, missionId, token);

        /// <inheritdoc />
        public Task<bool> MarkLaunchAttemptedAsync(string tenantId, string missionId, string captainId, string executionKey, CancellationToken token = default)
            => _LaunchBoundary.MarkLaunchAttemptedAsync(tenantId, missionId, captainId, executionKey, token);

        /// <inheritdoc />
        public Task<bool> ReleaseUnstartedAsync(string tenantId, string missionId, string captainId, string executionKey, CancellationToken token = default)
            => _LaunchBoundary.ReleaseUnstartedAsync(tenantId, missionId, captainId, executionKey, token);

        /// <inheritdoc />
        public Task<bool> ReleaseStoppedUnstartedAsync(string tenantId, string executionKey, CancellationToken token = default)
            => _LaunchBoundary.ReleaseStoppedUnstartedAsync(tenantId, executionKey, token);

        /// <inheritdoc />
        public Task<bool> TryRecordProviderCooldownAsync(UsageProviderCooldownRequest request, CancellationToken token = default)
            => _LaunchBoundary.TryRecordProviderCooldownAsync(request, token);

        /// <inheritdoc />
        public Task<bool> TryAdmitLegacyAsync(string captainId, string missionId, string? executionHostId, int ceiling = 256, CancellationToken token = default)
            => _Untracked.TryAdmitLegacyAsync(captainId, missionId, executionHostId, ceiling, token);

        /// <inheritdoc />
        public Task<bool> TryAdmitLegacyRepairAsync(string captainId, string missionId, DateTime verificationVersion, string? executionHostId, int ceiling = 256, CancellationToken token = default)
            => _Untracked.TryAdmitLegacyRepairAsync(captainId, missionId, verificationVersion, executionHostId, ceiling, token);

        /// <inheritdoc />
        public Task<int> AdoptUntrackedExecutionsAsync(int batchSize = 100, string? localHostId = null, CancellationToken token = default)
            => _Untracked.AdoptUntrackedExecutionsAsync(batchSize, localHostId, token);

        /// <inheritdoc />
        public Task<List<UsageUntrackedExecution>> ReadUntrackedExecutionsAsync(int batchSize = 100, CancellationToken token = default)
            => _Untracked.ReadUntrackedExecutionsAsync(batchSize, token);

        /// <inheritdoc />
        public Task<bool> UpdateUntrackedExecutionAsync(UsageUntrackedExecution row, int expectedRevision, CancellationToken token = default)
            => _Untracked.UpdateUntrackedExecutionAsync(row, expectedRevision, token);

        /// <inheritdoc />
        public Task<bool> IsUntrackedExecutionRunningAsync(string tenantId, string missionId, string executionKey, CancellationToken token = default)
            => _Untracked.IsUntrackedExecutionRunningAsync(tenantId, missionId, executionKey, token);

        /// <inheritdoc />
        public Task<List<CaptainSelectionOmittedReason>> ReadOmittedCandidatesAsync(string tenantId, string decisionId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
            => _OmittedCandidates.ReadOmittedCandidatesAsync(tenantId, decisionId, ownerUserId, candidateOwnerUserId, token);

    }
}
