namespace Armada.Core.Database
{
    using System;
    using System.Data;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;

    internal sealed partial class CaptainSelectionMethods
    {
        /// <inheritdoc />
        public Task<UsageSelectionRecovery?> ReadDecisionRecoveryAsync(string tenantId, string decisionId, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, @"SELECT r.* FROM usage_selection_recovery r JOIN captain_selection_execution_events e ON e.tenant_id=r.tenant_id AND e.reporting_source=r.id
                    WHERE e.tenant_id=@tenant AND e.decision_id=@decision AND e.event_kind='RecoveryAuthorizationApplied' ORDER BY r.revision DESC" + PageSql(connection));
                Add(command, "@tenant", tenantId); Add(command, "@decision", decisionId); Add(command, "@skip", 0); Add(command, "@take", 1);
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false) ? MapRecovery(reader) : null;
            }, token);
        }

        private static UsageSelectionRecovery MapRecovery(DbDataReader reader)
            => new UsageSelectionRecovery { Id = reader["id"].ToString()!, MissionId = reader["mission_id"].ToString()!, ActorUserId = reader["actor_user_id"].ToString()!, Reason = reader["reason"].ToString()!,
                AllowFallback = Convert.ToInt32(reader["allow_fallback"], CultureInfo.InvariantCulture) != 0, ReserveException = Convert.ToInt32(reader["reserve_exception"], CultureInfo.InvariantCulture) != 0,
                Revision = Convert.ToInt32(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = ParseUtc(reader["created_utc"]) };

        /// <inheritdoc />
        public Task<UsageSelectionRecovery?> ReadRecoveryAsync(string tenantId, string missionId, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(missionId, nameof(missionId));
            return RunAsync(connection => ReadRecoveryAsync(connection, null, tenantId, missionId, token), token);
        }

        private static async Task<UsageSelectionRecovery?> ReadRecoveryAsync(DbConnection connection, DbTransaction? transaction, string tenantId, string missionId, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "SELECT * FROM usage_selection_recovery WHERE tenant_id=@tenant AND mission_id=@mission ORDER BY revision DESC" + PageSql(connection));
            Add(command, "@tenant", tenantId); Add(command, "@mission", missionId); Add(command, "@skip", 0); Add(command, "@take", 1);
            using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
            return MapRecovery(reader);
        }

        /// <inheritdoc />
        public Task<UsageSelectionRecovery?> TryAuthorizeRecoveryAsync(string tenantId, string missionId, string actorUserId, UsageSelectionRecoveryRequest request, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(missionId, nameof(missionId)); Required(actorUserId, nameof(actorUserId)); ArgumentNullException.ThrowIfNull(request);
            if (request.ExpectedRevision < 0 || request.ExpectedRevision == Int32.MaxValue || String.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 512
                || UsageSelectionEvidenceSanitizer.Label(request.Reason) != request.Reason || System.Linq.Enumerable.Any(request.Reason, Char.IsControl))
                throw new ArgumentException("Recovery requires a nonnegative revision and a plain explanation of 1..512 characters, without credentials.", nameof(request));
            return RunAsync(async connection =>
            {
                using DbTransaction transaction = await connection.BeginTransactionAsync(connection.GetType().Name == "SqliteConnection" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
                using DbCommand locked = Command(connection, transaction, "UPDATE missions SET last_update_utc=@at WHERE tenant_id=@tenant AND id=@mission AND status='Pending';");
                DateTime now = DateTime.UtcNow;
                Add(locked, "@at", connection.GetType().Name == "NpgsqlConnection" || connection.GetType().Name == "MySqlConnection" ? (object)now : Utc(now)); Add(locked, "@tenant", tenantId); Add(locked, "@mission", missionId);
                if (await locked.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) return null;
                UsageSelectionRecovery? previous = await ReadRecoveryAsync(connection, transaction, tenantId, missionId, token).ConfigureAwait(false);
                if ((previous?.Revision ?? 0) != request.ExpectedRevision) return null;
                UsageSelectionRecovery row = new UsageSelectionRecovery { Id = Constants.IdGenerator.GenerateKSortable("ura_", 24), MissionId = missionId,
                    ActorUserId = actorUserId, Reason = request.Reason.Trim(), AllowFallback = request.AllowFallback, ReserveException = request.ReserveException,
                    Revision = request.ExpectedRevision + 1, CreatedUtc = now };
                using DbCommand insert = Command(connection, transaction, @"INSERT INTO usage_selection_recovery(id,tenant_id,mission_id,actor_user_id,reason,allow_fallback,reserve_exception,revision,created_utc)
                    VALUES(@id,@tenant,@mission,@actor,@reason,@fallback,@reserve,@revision,@at);");
                Add(insert, "@id", row.Id); Add(insert, "@tenant", tenantId); Add(insert, "@mission", missionId); Add(insert, "@actor", actorUserId); Add(insert, "@reason", row.Reason);
                Add(insert, "@fallback", row.AllowFallback ? 1 : 0); Add(insert, "@reserve", row.ReserveException ? 1 : 0); Add(insert, "@revision", row.Revision); Add(insert, "@at", Utc(now));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false); return row;
            }, token, write: true);
        }
    }
}
