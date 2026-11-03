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
    using Armada.Core.Settings;

    internal sealed partial class CaptainSelectionMethods
    {
        /// <inheritdoc />
        public Task<bool> TryAdmitLegacyAsync(string captainId, string missionId, string? executionHostId, int ceiling = 256, CancellationToken token = default)
        {
            Required(captainId, nameof(captainId)); Required(missionId, nameof(missionId));
            return RunAsync(connection => new CaptainSlotStore(connection).ClaimAsync(captainId, missionId, null, token, ceiling, usageHostId: executionHostId), token, write: true);
        }

        /// <inheritdoc />
        public Task<bool> TryAdmitLegacyRepairAsync(string captainId, string missionId, DateTime verificationVersion, string? executionHostId, int ceiling = 256, CancellationToken token = default)
        {
            Required(captainId, nameof(captainId)); Required(missionId, nameof(missionId));
            return RunAsync(connection => new CaptainSlotStore(connection).ResumeVerificationAsync(captainId, missionId, verificationVersion, ceiling, token, usageHostId: executionHostId), token, write: true);
        }

        internal static async Task TrackUnreservedExecutionAsync(DbConnection connection, DbTransaction transaction, string missionId,
            CaptainSelectionRecordSet? records, CancellationToken token, string? localHostId = null)
        {
            if (records != null && records.Reservations.Count > 0
                && records.Candidates.SingleOrDefault(c => c.CaptainId == records.Decision.SelectedCaptainId)?.Band != UsageSelectionBandEnum.Unknown) return;
            UsageUntrackedExecution row;
            string? executionHost;
            using (DbCommand command = Command(connection, transaction, "SELECT m.tenant_id,m.user_id,m.captain_id,m.usage_execution_key,m.assigned_harbor_id,c.runtime FROM missions m JOIN captains c ON c.id=m.captain_id WHERE m.id=@mission;"))
            {
                Add(command, "@mission", missionId);
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader["tenant_id"] == DBNull.Value || reader["usage_execution_key"] == DBNull.Value) return;
                row = new UsageUntrackedExecution { TenantId = reader["tenant_id"].ToString()!, UserId = reader["user_id"] == DBNull.Value ? null : reader["user_id"].ToString(),
                    MissionId = missionId, CaptainId = reader["captain_id"].ToString()!, ExecutionKey = reader["usage_execution_key"].ToString()!, Runtime = reader["runtime"].ToString()!,
                    SettlementLagSeconds = records?.Policy.SettlementLagSeconds ?? new UsageSelectionSettings().SettlementLagSeconds,
                    MaximumUnsettledSeconds = records?.Policy.MaximumUnsettledSeconds ?? new UsageSelectionSettings().MaximumUnsettledSeconds,
                    TransitionReason = "ExecutionWithoutCompleteReservation" };
                executionHost = records?.AdmissionHostId ?? (reader["assigned_harbor_id"] == DBNull.Value ? localHostId : reader["assigned_harbor_id"].ToString());
            }
            using (DbCommand exists = Command(connection, transaction, "SELECT COUNT(*) FROM usage_untracked_executions WHERE tenant_id=@tenant AND execution_key=@execution;"))
            {
                Add(exists, "@tenant", row.TenantId); Add(exists, "@execution", row.ExecutionKey);
                if (Convert.ToInt32(await exists.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0) return;
            }
            List<UsageSelectionAccountEvidence> evidence = await ReadAccountEvidenceAsync(connection, transaction, row.TenantId, new[] { row.CaptainId }, token).ConfigureAwait(false);
            if (evidence.Count == 1 && evidence[0].Runtime.ToString() == row.Runtime)
            {
                row.HostId = evidence[0].HostId; row.AccountKey = evidence[0].AccountKey; row.ConfigurationRevision = evidence[0].ConfigurationRevision;
                row.AccountAttributionVerified = evidence[0].HostId == executionHost;
            }
            using DbCommand insert = Command(connection, transaction, @"INSERT INTO usage_untracked_executions(id,tenant_id,user_id,created_utc,last_update_utc,active,mission_id,execution_key,captain_id,
                host_id,runtime,account_key,account_attribution_verified,configuration_revision,state,revision,execution_ended_utc,settlement_lag_seconds,maximum_unsettled_seconds,settled_by_snapshot_id,transition_reason)
                VALUES(@id,@tenant,@owner,@at,@at,1,@mission,@execution,@captain,@host,@runtime,@account,@verified,@config,'Active',0,NULL,@lag,@age,NULL,@reason);");
            Add(insert, "@id", row.Id); Add(insert, "@tenant", row.TenantId); Add(insert, "@owner", row.UserId); Add(insert, "@at", Utc(row.CreatedUtc));
            Add(insert, "@mission", row.MissionId); Add(insert, "@execution", row.ExecutionKey); Add(insert, "@captain", row.CaptainId);
            Add(insert, "@host", row.HostId); Add(insert, "@runtime", row.Runtime); Add(insert, "@account", row.AccountKey); Add(insert, "@config", row.ConfigurationRevision); Add(insert, "@verified", row.AccountAttributionVerified ? 1 : 0);
            Add(insert, "@lag", row.SettlementLagSeconds); Add(insert, "@age", row.MaximumUnsettledSeconds); Add(insert, "@reason", row.TransitionReason);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<int> AdoptUntrackedExecutionsAsync(int batchSize = 100, string? localHostId = null, CancellationToken token = default)
        {
            if (batchSize < 1 || batchSize > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
                using DbCommand command = Command(connection, transaction, @"SELECT m.id FROM missions m WHERE m.tenant_id IS NOT NULL AND m.captain_id IS NOT NULL
                    AND EXISTS(SELECT 1 FROM captains cp WHERE cp.id=m.captain_id AND cp.tenant_id=m.tenant_id)
                    AND m.status IN ('Assigned','InProgress') AND m.usage_execution_key IS NOT NULL
                    AND NOT EXISTS(SELECT 1 FROM usage_untracked_executions u WHERE u.tenant_id=m.tenant_id AND u.execution_key=m.usage_execution_key)
                    AND (NOT EXISTS(SELECT 1 FROM usage_budget_reservations r WHERE r.tenant_id=m.tenant_id AND r.decision_id=m.selection_decision_id AND r.state IN ('Active','Unsettled','Uncertain'))
                        OR EXISTS(SELECT 1 FROM captain_selection_candidates c WHERE c.tenant_id=m.tenant_id AND c.decision_id=m.selection_decision_id AND c.captain_id=m.captain_id AND c.band='Unknown'))
                    ORDER BY m.id" + PageSql(connection));
                Add(command, "@skip", 0); Add(command, "@take", batchSize);
                List<string> ids = new List<string>();
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) ids.Add(reader["id"].ToString()!);
                foreach (string id in ids) await TrackUnreservedExecutionAsync(connection, transaction, id, null, token, localHostId).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return ids.Count;
            }, token, write: true);
        }

        /// <inheritdoc />
        public Task<List<UsageUntrackedExecution>> ReadUntrackedExecutionsAsync(int batchSize = 100, CancellationToken token = default)
        {
            if (batchSize < 1 || batchSize > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT * FROM usage_untracked_executions WHERE state IN ('Active','Unsettled','Uncertain') ORDER BY last_update_utc,id" + PageSql(connection));
                Add(command, "@skip", 0); Add(command, "@take", batchSize);
                List<UsageUntrackedExecution> rows = new List<UsageUntrackedExecution>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(new UsageUntrackedExecution
                {
                    Id = reader["id"].ToString()!, TenantId = reader["tenant_id"].ToString()!, UserId = reader["user_id"] == DBNull.Value ? null : reader["user_id"].ToString(),
                    CreatedUtc = ParseUtc(reader["created_utc"]), LastUpdateUtc = ParseUtc(reader["last_update_utc"]), Active = Convert.ToBoolean(reader["active"], CultureInfo.InvariantCulture),
                    MissionId = reader["mission_id"].ToString()!, ExecutionKey = reader["execution_key"].ToString()!, CaptainId = reader["captain_id"].ToString()!,
                    HostId = reader["host_id"] == DBNull.Value ? null : reader["host_id"].ToString(), Runtime = reader["runtime"].ToString()!, AccountKey = reader["account_key"] == DBNull.Value ? null : reader["account_key"].ToString(),
                    AccountAttributionVerified = Convert.ToBoolean(reader["account_attribution_verified"], CultureInfo.InvariantCulture),
                    ConfigurationRevision = reader["configuration_revision"] == DBNull.Value ? null : reader["configuration_revision"].ToString(),
                    State = Enum.Parse<UsageReservationStateEnum>(reader["state"].ToString()!), Revision = Convert.ToInt32(reader["revision"], CultureInfo.InvariantCulture),
                    ExecutionEndedUtc = reader["execution_ended_utc"] == DBNull.Value ? null : ParseUtc(reader["execution_ended_utc"]), SettlementLagSeconds = Convert.ToInt32(reader["settlement_lag_seconds"], CultureInfo.InvariantCulture),
                    MaximumUnsettledSeconds = Convert.ToInt32(reader["maximum_unsettled_seconds"], CultureInfo.InvariantCulture),
                    SettledBySnapshotId = reader["settled_by_snapshot_id"] == DBNull.Value ? null : reader["settled_by_snapshot_id"].ToString(), TransitionReason = reader["transition_reason"] == DBNull.Value ? null : reader["transition_reason"].ToString()
                });
                return rows;
            }, token);
        }

        /// <inheritdoc />
        public Task<bool> UpdateUntrackedExecutionAsync(UsageUntrackedExecution row, int expectedRevision, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(row); Required(row.TenantId, nameof(row.TenantId)); Required(row.Id, nameof(row.Id));
            if (expectedRevision < 0 || expectedRevision == Int32.MaxValue) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            if (row.State == UsageReservationStateEnum.Settled && (row.ExecutionEndedUtc == null || String.IsNullOrWhiteSpace(row.SettledBySnapshotId))) throw new ArgumentException("Settlement requires an execution end and capture proof.", nameof(row));
            if (row.State == UsageReservationStateEnum.Released) throw new ArgumentException("Untracked work cannot be released as proven unstarted.", nameof(row));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, @"UPDATE usage_untracked_executions SET last_update_utc=@at,state=@state,active=@active,revision=revision+1,
                    execution_ended_utc=@ended,settled_by_snapshot_id=@snapshot,transition_reason=@reason WHERE tenant_id=@tenant AND id=@id AND revision=@revision
                    AND (execution_ended_utc IS NULL OR execution_ended_utc=@ended) AND last_update_utc<=@at AND state IN ('Active','Unsettled','Uncertain') AND (state=@state OR (state='Active' AND @state='Unsettled') OR (state='Unsettled' AND @state='Uncertain') OR (state IN ('Unsettled','Uncertain') AND @state='Settled'));");
                Add(command, "@active", row.State == UsageReservationStateEnum.Settled ? 0 : 1); Add(command, "@at", Utc(row.LastUpdateUtc)); Add(command, "@state", row.State.ToString()); Add(command, "@ended", row.ExecutionEndedUtc.HasValue ? Utc(row.ExecutionEndedUtc.Value) : null);
                Add(command, "@snapshot", row.SettledBySnapshotId); Add(command, "@reason", row.TransitionReason); Add(command, "@tenant", row.TenantId); Add(command, "@id", row.Id); Add(command, "@revision", expectedRevision);
                return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            }, token, write: true);
        }

        /// <inheritdoc />
        public Task<bool> IsUntrackedExecutionRunningAsync(string tenantId, string missionId, string executionKey, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(missionId, nameof(missionId)); Required(executionKey, nameof(executionKey));
            return RunAsync(connection => IsUntrackedExecutionRunningAsync(connection, tenantId, missionId, executionKey, token), token);
        }

        internal static async Task<bool> IsUntrackedExecutionRunningAsync(DbConnection connection, string tenantId, string missionId, string executionKey, CancellationToken token)
        {
            using DbCommand command = Command(connection, null, "SELECT COUNT(*) FROM missions WHERE tenant_id=@tenant AND id=@mission AND usage_execution_key=@execution AND status IN ('Assigned','InProgress');");
            Add(command, "@tenant", tenantId); Add(command, "@mission", missionId); Add(command, "@execution", executionKey);
            return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1;
        }
    }
}
