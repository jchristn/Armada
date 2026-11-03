namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal sealed partial class CaptainSelectionMethods
    {
        /// <inheritdoc />
        public Task<List<UsageBudgetReservation>> ReadReconciliationBatchAsync(int batchSize = 100, CancellationToken token = default)
        {
            if (batchSize < 1 || batchSize > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT * FROM usage_budget_reservations WHERE state IN ('Active','Unsettled','Uncertain') ORDER BY last_update_utc,id" + PageSql(connection));
                Add(command, "@skip", 0); Add(command, "@take", batchSize);
                List<UsageBudgetReservation> rows = new List<UsageBudgetReservation>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(MapUsageBudgetReservation(reader));
                return rows;
            }, token);
        }

        /// <inheritdoc />
        public Task<bool> TouchReservationAsync(string tenantId, string reservationId, int revision, DateTime atUtc, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(reservationId, nameof(reservationId));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "UPDATE usage_budget_reservations SET last_update_utc=@at,revision=revision+1 WHERE tenant_id=@tenant AND id=@id AND revision=@revision AND last_update_utc<@at AND state IN ('Active','Unsettled','Uncertain');");
                Add(command, "@at", Utc(atUtc)); Add(command, "@tenant", tenantId); Add(command, "@id", reservationId); Add(command, "@revision", revision);
                return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            }, token, write: true);
        }

        /// <inheritdoc />
        public Task<List<UsageBudgetReservation>> ReadReservationsAsync(string tenantId, string hostId, string runtime, string accountKey, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(hostId, nameof(hostId)); Required(runtime, nameof(runtime)); Required(accountKey, nameof(accountKey));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT * FROM usage_budget_reservations WHERE tenant_id=@tenant AND host_id=@host AND runtime=@runtime AND account_key=@account AND state IN ('Active','Unsettled','Uncertain') ORDER BY created_utc,id;");
                Add(command, "@tenant", tenantId); Add(command, "@host", hostId); Add(command, "@runtime", runtime); Add(command, "@account", accountKey);
                List<UsageBudgetReservation> rows = new List<UsageBudgetReservation>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(MapUsageBudgetReservation(reader));
                return rows;
            }, token);
        }

        /// <inheritdoc />
        public Task<bool> TryTransitionReservationAsync(string tenantId, string reservationId, int revision, UsageReservationStateEnum expected,
            UsageReservationStateEnum next, DateTime atUtc, string reason, string? snapshotId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(reservationId, nameof(reservationId)); Required(reason, nameof(reason));
            if (revision < 0 || revision == Int32.MaxValue) throw new ArgumentOutOfRangeException(nameof(revision));
            bool valid = expected == UsageReservationStateEnum.Active && (next == UsageReservationStateEnum.Unsettled || next == UsageReservationStateEnum.Released)
                || (expected == UsageReservationStateEnum.Unsettled || expected == UsageReservationStateEnum.Uncertain) && next == UsageReservationStateEnum.Settled
                || expected == UsageReservationStateEnum.Unsettled && next == UsageReservationStateEnum.Uncertain;
            if (!valid) throw new ArgumentException("Invalid usage reservation lifecycle transition.", nameof(next));
            if (next == UsageReservationStateEnum.Settled) Required(snapshotId!, nameof(snapshotId));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
                using DbCommand command = Command(connection, transaction, "UPDATE usage_budget_reservations SET state=@next,revision=revision+1,last_update_utc=@at,transition_reason=@reason"
                    + (next == UsageReservationStateEnum.Unsettled ? ",execution_ended_utc=@at" : "")
                    + (next == UsageReservationStateEnum.Released ? ",released_utc=@at" : "")
                    + (next == UsageReservationStateEnum.Settled ? ",settled_by_snapshot_id=@snapshot" : "")
                    + " WHERE tenant_id=@tenant AND id=@id AND revision=@revision AND state=@expected AND created_utc<=@at AND last_update_utc<=@at"
                    + (next == UsageReservationStateEnum.Released ? " AND execution_started_utc IS NULL" : "") + ";");
                Add(command, "@next", next.ToString()); Add(command, "@at", Utc(atUtc)); Add(command, "@reason", reason);
                Add(command, "@tenant", tenantId); Add(command, "@id", reservationId); Add(command, "@revision", revision); Add(command, "@expected", expected.ToString());
                if (next == UsageReservationStateEnum.Settled) Add(command, "@snapshot", snapshotId);
                bool changed = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return changed;
            }, token, write: true);
        }

        /// <inheritdoc />
        public Task AppendExecutionEventAsync(CaptainSelectionExecutionEvent entry, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            Required(entry.TenantId, nameof(entry.TenantId)); Required(entry.DecisionId, nameof(entry.DecisionId));
            Required(entry.ExecutionAttemptId, nameof(entry.ExecutionAttemptId)); Required(entry.EventKind, nameof(entry.EventKind));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
                CaptainSelectionDecision? parent = await ReadAttemptAsync(connection, transaction, entry.TenantId, entry.ExecutionAttemptId, token).ConfigureAwait(false);
                if (parent == null || parent.Id != entry.DecisionId || parent.UserId != entry.UserId)
                    throw new ArgumentException("Execution event must reference its tenant, owner and decision attempt.", nameof(entry));
                await InsertCaptainSelectionExecutionEventAsync(connection, transaction, entry, token).ConfigureAwait(false);
                if (entry.EventKind == "ExecutionStarted")
                {
                    using DbCommand started = Command(connection, transaction, "UPDATE usage_budget_reservations SET execution_started_utc=@at,last_update_utc=@at,revision=revision+1 WHERE tenant_id=@tenant AND decision_id=@decision AND state='Active' AND execution_started_utc IS NULL AND created_utc<=@at AND last_update_utc<=@at;");
                    Add(started, "@at", Utc(entry.OccurredUtc)); Add(started, "@tenant", entry.TenantId); Add(started, "@decision", entry.DecisionId);
                    await started.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return true;
            }, token, write: true);
        }

        /// <inheritdoc />
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize = 100, CancellationToken token = default)
        {
            if (batchSize < 1 || batchSize > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
                using DbCommand command = Command(connection, transaction, "SELECT d.id,d.tenant_id FROM captain_selection_decisions d WHERE d.evaluated_utc<@cutoff"
                    + " AND NOT EXISTS (SELECT 1 FROM usage_budget_reservations r WHERE r.decision_id=d.id AND r.tenant_id=d.tenant_id AND r.state IN ('Active','Unsettled','Uncertain'))"
                    + " AND NOT EXISTS (SELECT 1 FROM missions m WHERE m.id=d.mission_id AND m.tenant_id=d.tenant_id AND m.status IN ('Pending','Assigned','InProgress','Verifying'))"
                    + " ORDER BY d.evaluated_utc,d.id" + PageSql(connection));
                Add(command, "@cutoff", Utc(cutoffUtc)); Add(command, "@skip", 0); Add(command, "@take", batchSize);
                List<CaptainSelectionDecision> rows = new List<CaptainSelectionDecision>();
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(new CaptainSelectionDecision {
                        Id = Convert.ToString(reader["id"], CultureInfo.InvariantCulture)!, TenantId = Convert.ToString(reader["tenant_id"], CultureInfo.InvariantCulture)! });
                }
                foreach (CaptainSelectionDecision row in rows)
                {
                    foreach (string table in _ChildTables)
                    {
                        using DbCommand child = Command(connection, transaction, "DELETE FROM " + table + " WHERE tenant_id=@tenant AND decision_id=@decision;");
                        Add(child, "@tenant", row.TenantId); Add(child, "@decision", row.Id);
                        await child.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                    using DbCommand parent = Command(connection, transaction, "DELETE FROM captain_selection_decisions WHERE tenant_id=@tenant AND id=@decision;");
                    Add(parent, "@tenant", row.TenantId); Add(parent, "@decision", row.Id);
                    await parent.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                // Policy revisions are kept until their last retained decision is removed.
                // Restrict cleanup to this bounded batch's tenants, rather than scanning all tenants on every pass.
                foreach (string tenant in rows.ConvertAll(r => r.TenantId).Distinct(StringComparer.Ordinal))
                {
                    using DbCommand policy = Command(connection, transaction, "DELETE FROM captain_selection_policy_revisions WHERE tenant_id=@tenant AND created_utc<@cutoff AND NOT EXISTS (SELECT 1 FROM captain_selection_decisions d WHERE d.policy_revision_id=captain_selection_policy_revisions.id AND d.tenant_id=@tenant);");
                    Add(policy, "@tenant", tenant); Add(policy, "@cutoff", Utc(cutoffUtc));
                    await policy.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return rows.Count;
            }, token, write: true);
        }

        /// <inheritdoc />
        public Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
                int count = 0;
                foreach (string table in _ChildTables.Concat(new[] { "usage_account_cooldowns", "captain_selection_decisions", "captain_selection_policy_revisions" }))
                {
                    using DbCommand command = Command(connection, transaction, "DELETE FROM " + table + " WHERE tenant_id=@tenant;");
                    Add(command, "@tenant", tenantId); count += await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return count;
            }, token, write: true);
        }
    }
}
