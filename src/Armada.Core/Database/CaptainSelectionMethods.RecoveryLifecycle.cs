namespace Armada.Core.Database
{
    using System;
    using System.Data;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    internal sealed partial class CaptainSelectionMethods
    {
        private static async Task TrackExecutionSourceAsync(DbConnection connection, DbTransaction transaction, string missionId,
            CaptainSelectionRecordSet? records, string? localHostId, bool adopting, CancellationToken token)
        {
            UsageExecutionSource row;
            using (DbCommand command = Command(connection, transaction, "SELECT m.tenant_id,m.captain_id,m.selection_decision_id,m.usage_execution_key,m.assigned_harbor_id,c.runtime FROM missions m JOIN captains c ON c.id=m.captain_id AND c.tenant_id=m.tenant_id WHERE m.id=@mission;"))
            {
                Add(command, "@mission", missionId);
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader["tenant_id"] == DBNull.Value || reader["usage_execution_key"] == DBNull.Value) return;
                row = new UsageExecutionSource { TenantId = reader["tenant_id"].ToString()!, MissionId = missionId,
                    CaptainId = reader["captain_id"].ToString()!, Runtime = reader["runtime"].ToString()!, ExecutionKey = reader["usage_execution_key"].ToString()!,
                    DecisionId = reader["selection_decision_id"] == DBNull.Value ? null : reader["selection_decision_id"].ToString(),
                    HostId = records?.AdmissionHostId ?? (reader["assigned_harbor_id"] == DBNull.Value ? localHostId : reader["assigned_harbor_id"].ToString()), CreatedUtc = DateTime.UtcNow };
            }
            if (await ReadExecutionSourceAsync(connection, transaction, row.TenantId, row.ExecutionKey, token).ConfigureAwait(false) != null) return;
            System.Collections.Generic.List<UsageSelectionAccountEvidence> evidence = await ReadAccountEvidenceAsync(connection, transaction, row.TenantId, new[] { row.CaptainId }, token).ConfigureAwait(false);
            UsageSelectionAccountEvidence? account = evidence.Count == 1 ? evidence[0] : null;
            if (!adopting && account != null && account.HostId == row.HostId && account.Runtime.ToString() == row.Runtime)
            {
                string? fingerprint;
                using (DbCommand config = Command(connection, transaction, "SELECT usage_capture_mode,usage_capture_command FROM captains WHERE tenant_id=@tenant AND id=@captain;"))
                {
                    Add(config, "@tenant", row.TenantId); Add(config, "@captain", row.CaptainId);
                    using DbDataReader reader = await config.ExecuteReaderAsync(token).ConfigureAwait(false);
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) return;
                    fingerprint = reader["usage_capture_mode"].ToString() == "Custom" ? UsageAccountResolver.CommandFingerprint(reader["usage_capture_command"] == DBNull.Value ? null : reader["usage_capture_command"].ToString()) : null;
                }
                string revision = UsageAccountResolver.ConfigurationRevision(account.HostId, account.Runtime, fingerprint);
                string group = UsageAccountResolver.GroupKey(row.TenantId, account.HostId, account.Runtime, fingerprint);
                if (account.ConfigurationRevision == revision && account.CaptureContextKey == UsageAccountResolver.CaptureContextKey(group, revision))
                { row.AccountKey = account.AccountKey; row.ConfigurationRevision = revision; }
            }
            // Observe can deliberately execute somewhere other than its hypothetical capture host. Never charge that account.
            if (records?.Policy.Mode == UsageSelectionModeEnum.Observe && records.Reservations.Any(r => r.HostId != row.HostId || r.AccountKey != row.AccountKey))
            {
                using DbCommand remove = Command(connection, transaction, "DELETE FROM usage_budget_reservations WHERE tenant_id=@tenant AND decision_id=@decision;");
                Add(remove, "@tenant", row.TenantId); Add(remove, "@decision", row.DecisionId);
                await remove.ExecuteNonQueryAsync(token).ConfigureAwait(false); records.Reservations.Clear();
            }
            using DbCommand insert = Command(connection, transaction, @"INSERT INTO usage_execution_sources(execution_key,tenant_id,mission_id,captain_id,decision_id,host_id,runtime,account_key,configuration_revision,created_utc,launch_attempted_utc,released_utc)
                VALUES(@execution,@tenant,@mission,@captain,@decision,@host,@runtime,@account,@config,@at,@attempted,NULL);");
            Add(insert, "@execution", row.ExecutionKey); Add(insert, "@tenant", row.TenantId); Add(insert, "@mission", row.MissionId); Add(insert, "@captain", row.CaptainId);
            Add(insert, "@decision", row.DecisionId); Add(insert, "@host", row.HostId); Add(insert, "@runtime", row.Runtime); Add(insert, "@account", row.AccountKey);
            Add(insert, "@config", row.ConfigurationRevision); Add(insert, "@at", Utc(row.CreatedUtc)); Add(insert, "@attempted", adopting ? Utc(row.CreatedUtc) : null);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static async Task<UsageExecutionSource?> ReadExecutionSourceAsync(DbConnection connection, DbTransaction? transaction, string tenantId, string executionKey, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "SELECT * FROM usage_execution_sources WHERE tenant_id=@tenant AND execution_key=@execution;");
            Add(command, "@tenant", tenantId); Add(command, "@execution", executionKey);
            using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
            string? Text(string key) => reader[key] == DBNull.Value ? null : reader[key].ToString();
            return new UsageExecutionSource { ExecutionKey = executionKey, TenantId = tenantId, MissionId = Text("mission_id")!, CaptainId = Text("captain_id")!,
                DecisionId = Text("decision_id"), HostId = Text("host_id"), Runtime = Text("runtime")!, AccountKey = Text("account_key"), ConfigurationRevision = Text("configuration_revision"),
                ReleasedUtc = reader["released_utc"] == DBNull.Value ? null : ParseUtc(reader["released_utc"]), CreatedUtc = ParseUtc(reader["created_utc"]), LaunchAttemptedUtc = reader["launch_attempted_utc"] == DBNull.Value ? null : ParseUtc(reader["launch_attempted_utc"]) };
        }

        /// <inheritdoc />
        public Task<string?> ReadUsageExecutionKeyAsync(string tenantId, string missionId, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(missionId, nameof(missionId));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT usage_execution_key FROM missions WHERE tenant_id=@tenant AND id=@mission;");
                Add(command, "@tenant", tenantId); Add(command, "@mission", missionId);
                object? value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                return value == null || value == DBNull.Value ? null : value.ToString();
            }, token);
        }

        /// <inheritdoc />
        public Task<bool> MarkLaunchAttemptedAsync(string tenantId, string missionId, string captainId, string executionKey, CancellationToken token = default)
            => ChangeLaunchBoundaryAsync(tenantId, missionId, captainId, executionKey, false, token);

        /// <inheritdoc />
        public Task<bool> ReleaseUnstartedAsync(string tenantId, string missionId, string captainId, string executionKey, CancellationToken token = default)
            => ChangeLaunchBoundaryAsync(tenantId, missionId, captainId, executionKey, true, token);

        private Task<bool> ChangeLaunchBoundaryAsync(string tenantId, string missionId, string captainId, string executionKey, bool release, CancellationToken token)
        {
            Required(tenantId, nameof(tenantId)); Required(missionId, nameof(missionId)); Required(captainId, nameof(captainId)); Required(executionKey, nameof(executionKey));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(connection.GetType().Name == "SqliteConnection" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
                using DbCommand locked = Command(connection, transaction, "UPDATE missions SET usage_execution_key=usage_execution_key WHERE tenant_id=@tenant AND id=@mission AND captain_id=@captain AND usage_execution_key=@execution AND status IN ('Assigned','InProgress')" + (release ? " AND process_id IS NULL" : "") + ";");
                Add(locked, "@tenant", tenantId); Add(locked, "@mission", missionId); Add(locked, "@captain", captainId); Add(locked, "@execution", executionKey);
                if (await locked.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) return false;
                string key = executionKey;
                UsageExecutionSource? source = await ReadExecutionSourceAsync(connection, transaction, tenantId, key, token).ConfigureAwait(false);
                // Legacy/adopted work has no proof that a launcher was never called.
                if (source == null)
                {
                    // Adopted legacy work has no negative launch proof and can never be released as unstarted.
                    await transaction.CommitAsync(token).ConfigureAwait(false); return !release;
                }
                if (source.ReleasedUtc.HasValue || release && source.LaunchAttemptedUtc.HasValue) return false;
                DateTime now = DateTime.UtcNow;
                if (!release)
                {
                    using DbCommand mark = Command(connection, transaction, "UPDATE usage_execution_sources SET launch_attempted_utc=COALESCE(launch_attempted_utc,@at) WHERE tenant_id=@tenant AND execution_key=@execution;");
                    Add(mark, "@at", Utc(now)); Add(mark, "@tenant", tenantId); Add(mark, "@execution", key);
                    await mark.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                using (DbCommand reservations = Command(connection, transaction, release
                    ? "UPDATE usage_budget_reservations SET state='Released',released_utc=@at,last_update_utc=@at,revision=revision+1,transition_reason='ProvenUnstarted' WHERE tenant_id=@tenant AND decision_id=@decision AND state='Active' AND execution_started_utc IS NULL;"
                    : "UPDATE usage_budget_reservations SET execution_started_utc=COALESCE(execution_started_utc,@at),last_update_utc=@at,revision=revision+1 WHERE tenant_id=@tenant AND decision_id=@decision AND state='Active';"))
                {
                    Add(reservations, "@tenant", tenantId); Add(reservations, "@decision", source.DecisionId); Add(reservations, "@at", Utc(now));
                    await reservations.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                if (release)
                {
                    using DbCommand sourceRelease = Command(connection, transaction, "UPDATE usage_execution_sources SET released_utc=@at WHERE tenant_id=@tenant AND execution_key=@execution;");
                    Add(sourceRelease, "@tenant", tenantId); Add(sourceRelease, "@execution", key); Add(sourceRelease, "@at", Utc(now));
                    await sourceRelease.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    using DbCommand uncertain = Command(connection, transaction, "UPDATE usage_untracked_executions SET state='Released',revision=revision+1,last_update_utc=@at,transition_reason='ProvenUnstarted' WHERE tenant_id=@tenant AND execution_key=@execution AND state='Active';");
                    Add(uncertain, "@tenant", tenantId); Add(uncertain, "@execution", key); Add(uncertain, "@at", Utc(now));
                    await uncertain.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                if (source.DecisionId != null)
                    await InsertCaptainSelectionExecutionEventAsync(connection, transaction, new CaptainSelectionExecutionEvent { TenantId = tenantId,
                        UserId = (await ReadAttemptAsync(connection, transaction, tenantId, key!, token).ConfigureAwait(false))?.UserId,
                        DecisionId = source.DecisionId, ExecutionAttemptId = key!, EventKind = release ? "ProvenUnstarted" : "LaunchAttempted", OccurredUtc = now, ReportingSource = "AdmissionLifecycle" }, token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false); return true;
            }, token, write: true);
        }

        /// <inheritdoc />
        public Task<bool> ReleaseStoppedUnstartedAsync(string tenantId, string executionKey, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(executionKey, nameof(executionKey));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(connection.GetType().Name == "SqliteConnection" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
                UsageExecutionSource? source = await ReadExecutionSourceAsync(connection, transaction, tenantId, executionKey, token).ConfigureAwait(false);
                if (source == null || source.LaunchAttemptedUtc.HasValue || source.ReleasedUtc.HasValue) return false;
                using (DbCommand locked = Command(connection, transaction, "UPDATE missions SET usage_execution_key=usage_execution_key WHERE tenant_id=@tenant AND id=@mission;"))
                { Add(locked, "@tenant", tenantId); Add(locked, "@mission", source.MissionId); await locked.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
                using (DbCommand current = Command(connection, transaction, "SELECT COUNT(*) FROM missions WHERE tenant_id=@tenant AND id=@mission AND usage_execution_key=@execution AND status IN ('Assigned','InProgress','Testing');"))
                {
                    Add(current, "@tenant", tenantId); Add(current, "@mission", source.MissionId); Add(current, "@execution", executionKey);
                    if (Convert.ToInt32(await current.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0) return false;
                }
                // Re-read after acquiring the same mission lock used by the pre-launch marker.
                source = await ReadExecutionSourceAsync(connection, transaction, tenantId, executionKey, token).ConfigureAwait(false);
                if (source == null || source.LaunchAttemptedUtc.HasValue || source.ReleasedUtc.HasValue) return false;
                DateTime now = DateTime.UtcNow;
                foreach (string sql in new[] {
                    "UPDATE usage_execution_sources SET released_utc=@at WHERE tenant_id=@tenant AND execution_key=@execution;",
                    "UPDATE usage_budget_reservations SET state='Released',released_utc=@at,last_update_utc=@at,revision=revision+1,transition_reason='ProvenUnstarted' WHERE tenant_id=@tenant AND execution_attempt_id=@execution AND state='Active' AND execution_started_utc IS NULL;",
                    "UPDATE usage_untracked_executions SET state='Released',revision=revision+1,last_update_utc=@at,transition_reason='ProvenUnstarted' WHERE tenant_id=@tenant AND execution_key=@execution AND state='Active';" })
                {
                    using DbCommand update = Command(connection, transaction, sql); Add(update, "@tenant", tenantId); Add(update, "@execution", executionKey); Add(update, "@at", Utc(now));
                    await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                if (source.DecisionId != null)
                {
                    CaptainSelectionDecision? decision = await ReadAttemptAsync(connection, transaction, tenantId, executionKey, token).ConfigureAwait(false);
                    await InsertCaptainSelectionExecutionEventAsync(connection, transaction, new CaptainSelectionExecutionEvent { TenantId = tenantId, UserId = decision?.UserId,
                        DecisionId = source.DecisionId, ExecutionAttemptId = executionKey, EventKind = "ProvenUnstarted", ReportingSource = "StoppedExecutionReconciliation", OccurredUtc = now }, token).ConfigureAwait(false);
                }
                await transaction.CommitAsync(token).ConfigureAwait(false); return true;
            }, token, write: true);
        }

        /// <inheritdoc />
        public Task<bool> TryRecordProviderCooldownAsync(UsageProviderCooldownRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Required(request.TenantId, nameof(request.TenantId)); Required(request.MissionId, nameof(request.MissionId)); Required(request.CaptainId, nameof(request.CaptainId)); Required(request.ExecutionKey, nameof(request.ExecutionKey));
            if (request.ObservedUtc.Kind != DateTimeKind.Utc || request.UntilUtc.Kind != DateTimeKind.Utc || request.UntilUtc <= request.ObservedUtc || request.UntilUtc > request.ObservedUtc.AddHours(24))
                throw new ArgumentException("A UTC provider cooldown must be positive and bounded to 24 hours.", nameof(request));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(connection.GetType().Name == "SqliteConnection" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
                using DbCommand locked = Command(connection, transaction, "UPDATE missions SET usage_execution_key=usage_execution_key WHERE tenant_id=@tenant AND id=@mission AND captain_id=@captain AND usage_execution_key=@execution;");
                Add(locked, "@tenant", request.TenantId); Add(locked, "@mission", request.MissionId); Add(locked, "@captain", request.CaptainId); Add(locked, "@execution", request.ExecutionKey);
                if (await locked.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) return false;
                UsageExecutionSource? source = await ReadExecutionSourceAsync(connection, transaction, request.TenantId, request.ExecutionKey, token).ConfigureAwait(false);
                if (source?.AccountKey == null || source.HostId == null || !source.LaunchAttemptedUtc.HasValue || source.CaptainId != request.CaptainId || source.MissionId != request.MissionId) return false;
                using (DbCommand account = Command(connection, transaction, "UPDATE usage_latest_state SET last_good_snapshot_id=last_good_snapshot_id WHERE tenant_id=@tenant AND host_id=@host AND runtime=@runtime AND account_key=@account;"))
                { Add(account, "@tenant", request.TenantId); Add(account, "@host", source.HostId); Add(account, "@runtime", source.Runtime); Add(account, "@account", source.AccountKey); await account.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
                string id = "ucc_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.TenantId + ":" + request.ExecutionKey))).Substring(0, 48).ToLowerInvariant();
                using (DbCommand exists = Command(connection, transaction, "SELECT COUNT(*) FROM usage_account_cooldowns WHERE tenant_id=@tenant AND id=@id;"))
                { Add(exists, "@tenant", request.TenantId); Add(exists, "@id", id); if (Convert.ToInt32(await exists.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0) return false; }
                await InsertUsageAccountCooldownAsync(connection, transaction, new UsageAccountCooldown { Id = id, TenantId = request.TenantId,
                    HostId = source.HostId, Runtime = source.Runtime, AccountKey = source.AccountKey, WindowKey = "account", ExecutionKey = request.ExecutionKey, MissionId = request.MissionId, DecisionId = source.DecisionId,
                    ObservedUtc = request.ObservedUtc, RetryAfterUtc = request.UntilUtc, Classification = "ProviderUsageLimit" }, token).ConfigureAwait(false);
                if (source.DecisionId != null)
                {
                    CaptainSelectionDecision? decision = await ReadAttemptAsync(connection, transaction, request.TenantId, request.ExecutionKey, token).ConfigureAwait(false);
                    await InsertCaptainSelectionExecutionEventAsync(connection, transaction, new CaptainSelectionExecutionEvent { TenantId = request.TenantId, UserId = decision?.UserId,
                        DecisionId = source.DecisionId, ExecutionAttemptId = request.ExecutionKey, EventKind = "ProviderCooldownObserved", OccurredUtc = request.ObservedUtc,
                        FailureClassification = "UsageLimit", ReportingSource = "StructuredRuntimeExit" }, token).ConfigureAwait(false);
                }
                await transaction.CommitAsync(token).ConfigureAwait(false); return true;
            }, token, write: true);
        }
    }
}
