namespace Armada.Core.Database
{
    using Armada.Core.Enums;
    using System.Data;
    using System.Data.Common;
    using System.Globalization;

    /// <summary>Transactional mission-slot ownership shared by all four database providers.</summary>
    internal sealed class CaptainSlotStore
    {
        private readonly DbConnection _Connection;
        private readonly bool _Postgres;
        private readonly bool _Mysql;
        internal CaptainSlotStore(DbConnection connection)
        {
            _Connection = connection;
            _Postgres = connection.GetType().Name.Contains("Npgsql", StringComparison.Ordinal);
            _Mysql = connection.GetType().Name.Contains("MySql", StringComparison.Ordinal);
        }

        private DbCommand Command(DbTransaction transaction, string sql, string? captainId = null, string? missionId = null, object? value = null)
        {
            DbCommand cmd = _Connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            void Add(string name, object? data) { DbParameter p = cmd.CreateParameter(); p.ParameterName = name; p.Value = data ?? DBNull.Value; cmd.Parameters.Add(p); }
            if (captainId != null) Add("@id", captainId);
            if (missionId != null) Add("@mission", missionId);
            Add("@value", value);
            Add("@now", _Postgres || _Mysql ? DateTime.UtcNow : DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            return cmd;
        }

        private static void ValidateExclusive(string captainId, string sessionId, CaptainStateEnum? state = null)
        {
            if (String.IsNullOrWhiteSpace(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (String.IsNullOrWhiteSpace(sessionId)) throw new ArgumentNullException(nameof(sessionId));
            if (state.HasValue && state != CaptainStateEnum.Planning && state != CaptainStateEnum.Refining && state != CaptainStateEnum.Analyzing)
                throw new ArgumentOutOfRangeException(nameof(state), "Exclusive ownership requires Planning, Refining or Analyzing.");
        }

        internal async Task<bool> ClaimExclusiveAsync(string captainId, string sessionId, CaptainStateEnum state, bool recover, CancellationToken token)
        {
            ValidateExclusive(captainId, sessionId, state);
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            using DbCommand cmd = Command(tx, @"UPDATE captains SET process_id=CASE WHEN current_session_id=@mission THEN process_id ELSE NULL END,
                current_session_id=@mission,state=@value,last_heartbeat_utc=@now,last_update_utc=@now
                WHERE id=@id AND active_mission_count=0 AND
                ((state='Idle' AND current_session_id IS NULL AND active=" + (_Postgres ? "TRUE" : "1") + @"
                  AND (quarantine_until_utc IS NULL OR quarantine_until_utc<=@now))
                 OR (state=@value AND (current_session_id=@mission" + (recover ? " OR current_session_id IS NULL" : "") + ")));", captainId, sessionId, state.ToString());
            bool changed = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            await tx.CommitAsync(token).ConfigureAwait(false);
            return changed;
        }

        internal async Task<bool> UpdateExclusiveAsync(string captainId, string sessionId, CaptainStateEnum state, int? processId, CancellationToken token)
        {
            ValidateExclusive(captainId, sessionId, state);
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            using DbCommand cmd = Command(tx, @"UPDATE captains SET process_id=@process,
                last_heartbeat_utc=@now,last_update_utc=@now
                WHERE id=@id AND current_session_id=@mission AND state=@value AND active_mission_count=0;", captainId, sessionId, state.ToString());
            DbParameter process = cmd.CreateParameter(); process.ParameterName = "@process"; process.Value = (object?)processId ?? DBNull.Value; cmd.Parameters.Add(process);
            bool changed = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            await tx.CommitAsync(token).ConfigureAwait(false);
            return changed;
        }

        internal async Task<bool> ReleaseExclusiveAsync(string captainId, string sessionId, CancellationToken token)
        {
            ValidateExclusive(captainId, sessionId);
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            using DbCommand cmd = Command(tx, @"UPDATE captains SET current_session_id=NULL,process_id=NULL,state=CASE WHEN state IN ('Planning','Refining','Analyzing','Stopping') THEN 'Idle' ELSE state END,
                last_heartbeat_utc=@now,last_update_utc=@now
                WHERE id=@id AND current_session_id=@mission AND active_mission_count=0;", captainId, sessionId);
            bool changed = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            await tx.CommitAsync(token).ConfigureAwait(false);
            return changed;
        }

        internal async Task<bool> ClaimAsync(string captainId, string missionId, string? dockId, CancellationToken token, int ceiling = 256, Armada.Core.Models.CaptainSelectionRecordSet? selection = null)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            try
            {
                if (selection != null)
                {
                    if (selection.Decision.Mode != UsageSelectionModeEnum.Observe || selection.Decision.SelectedCaptainId != captainId || selection.Decision.MissionId != missionId)
                        throw new ArgumentException("Observed admission must match its selected captain, mission and Observe mode.", nameof(selection));
                    using DbCommand scope = Command(tx, "SELECT COUNT(*) FROM captains c JOIN missions m ON m.id=@mission WHERE c.id=@id AND c.tenant_id=@tenant AND m.tenant_id=@tenant AND ((m.user_id=@owner) OR (m.user_id IS NULL AND @owner IS NULL)) AND m.status='Pending'", captainId, missionId);
                    DbParameter owner = scope.CreateParameter(); owner.ParameterName = "@owner"; owner.Value = (object?)selection.Decision.UserId ?? DBNull.Value; scope.Parameters.Add(owner);
                    DbParameter tenant = scope.CreateParameter(); tenant.ParameterName = "@tenant"; tenant.Value = selection.Decision.TenantId; scope.Parameters.Add(tenant);
                    if (Convert.ToInt32(await scope.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1) return false;
                }
                using DbCommand row = Command(tx, "UPDATE captains SET active_mission_count=active_mission_count WHERE id=@id;", captainId);
                await row.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                // Updating the captain row serializes claims before the unique mission-slot insertion.
                using DbCommand claim = Command(tx, @"UPDATE captains SET state='Working',
                    last_heartbeat_utc=@now,last_update_utc=@now,active_mission_count=active_mission_count+1
                    WHERE id=@id AND active=" + (_Postgres ? "TRUE" : "1") + @" AND current_session_id IS NULL
                    AND state IN ('Idle','Working') AND active_mission_count < CASE WHEN max_concurrency<1 THEN 1 WHEN max_concurrency>256 THEN 256 ELSE max_concurrency END AND active_mission_count < @ceiling
                    AND (state='Idle' OR active_mission_count>0)
                    AND NOT EXISTS (SELECT 1 FROM captain_mission_slots s WHERE s.mission_id=@mission)
                    AND NOT EXISTS (SELECT 1 FROM missions m WHERE m.id=@mission AND m.status <> 'Pending');", captainId, missionId, dockId);
                DbParameter maximum = claim.CreateParameter(); maximum.ParameterName = "@ceiling"; maximum.Value = Math.Clamp(ceiling, 1, 256); claim.Parameters.Add(maximum);
                if (await claim.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) return false;
                using DbCommand insert = Command(tx, "INSERT INTO captain_mission_slots(mission_id,captain_id,dock_id) VALUES(@mission,@id,@value);", captainId, missionId, dockId);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                using DbCommand assign = Command(tx, "UPDATE missions SET captain_id=@id,status='Assigned',dock_id=COALESCE(@value,dock_id),assigned_utc=@now,last_update_utc=@now,selection_decision_id=NULL WHERE id=@mission AND status='Pending';", captainId, missionId, dockId);
                int assigned = await assign.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                if (selection != null)
                {
                    if (assigned != 1) return false;
                    await CaptainSelectionMethods.WriteAggregateAsync(_Connection, tx, selection, token).ConfigureAwait(false);
                    await SetSelectionDecisionAsync(tx, missionId, captainId, selection.Decision.Id, token).ConfigureAwait(false);
                }
                await tx.CommitAsync(token).ConfigureAwait(false);
                return true;
            }
            catch (DbException ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return false;
            }
        }

        private async Task SetSelectionDecisionAsync(DbTransaction transaction, string missionId, string captainId, string decisionId, CancellationToken token)
        {
            using DbCommand command = Command(transaction, "UPDATE missions SET selection_decision_id=@value WHERE id=@mission AND captain_id=@id;", captainId, missionId, decisionId);
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Mission ownership changed before its selection decision could be recorded.");
        }

        private const string Count = "(SELECT COUNT(*) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id)))";
        private static readonly string Refresh = @"UPDATE captains SET
            state=CASE WHEN " + Count + @"=0 AND state IN ('Working','Stalled','Stopping') THEN 'Idle'
                       WHEN " + Count + @">0 AND state IN ('Idle','Stalled') THEN 'Working' ELSE state END,
            active_mission_count=" + Count + @",
            process_id=CASE WHEN current_session_id IS NULL THEN NULL ELSE process_id END,
            recovery_attempts=CASE WHEN " + Count + @"=0 AND current_session_id IS NULL THEN 0 ELSE recovery_attempts END,
            last_update_utc=@now";

        internal async Task<bool> ReleaseAsync(string captainId, string missionId, CancellationToken token, CaptainStateEnum? emptyState = null)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            // Take the same captain row lock as Claim before touching its slot rows.
            using DbCommand row = Command(tx, "UPDATE captains SET active_mission_count=active_mission_count WHERE id=@id;", captainId);
            await row.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand remove = Command(tx, "DELETE FROM captain_mission_slots WHERE captain_id=@id AND mission_id=@mission;", captainId, missionId);
            bool released = await remove.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
            if (released)
            {
                string refreshSql = emptyState.HasValue
                    ? Refresh.Replace("THEN 'Idle'", "THEN '" + emptyState.Value.ToString() + "'", StringComparison.Ordinal) : Refresh;
                using DbCommand refresh = Command(tx, refreshSql + " WHERE id=@id;", captainId);
                await refresh.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            await tx.CommitAsync(token).ConfigureAwait(false);
            return released;
        }

        internal async Task<bool> ReleaseVerificationCapacityAsync(string captainId, string missionId, int processId, DateTime version, CancellationToken token)
        {
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            using DbCommand row = Command(tx, "UPDATE captains SET active_mission_count=active_mission_count WHERE id=@id;", captainId);
            await row.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand current = Command(tx, "SELECT COUNT(*) FROM missions WHERE id=@mission AND captain_id=@id AND status='Verifying' AND last_update_utc=@value;", captainId, missionId,
                _Postgres || _Mysql ? version : version.ToString("o", CultureInfo.InvariantCulture));
            if (Convert.ToInt32(await current.ExecuteScalarAsync(token).ConfigureAwait(false)) != 1) return false;
            using DbCommand insert = Command(tx, @"INSERT INTO captain_mission_slots(mission_id,captain_id,process_id,dock_id)
                SELECT m.id,m.captain_id,@value,m.dock_id FROM missions m WHERE m.id=@mission AND m.captain_id=@id AND m.status='Verifying'
                AND NOT EXISTS (SELECT 1 FROM captain_mission_slots s WHERE s.mission_id=@mission);", captainId, missionId, processId);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand retain = Command(tx, @"UPDATE captain_mission_slots SET process_id=@value WHERE captain_id=@id AND mission_id=@mission;", captainId, missionId, processId);
            await retain.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand refresh = Command(tx, Refresh + " WHERE id=@id;", captainId);
            await refresh.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return true;
        }

        internal async Task<bool> ResumeVerificationAsync(string captainId, string missionId, DateTime version, int ceiling, CancellationToken token, Armada.Core.Models.CaptainSelectionRecordSet? selection = null)
        {
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            if (selection != null)
            {
                if (selection.Decision.Mode != UsageSelectionModeEnum.Observe || selection.Decision.SelectedCaptainId != captainId || selection.Decision.MissionId != missionId)
                    throw new ArgumentException("Observed repair must match its selected captain, mission and mode.", nameof(selection));
                using DbCommand scope = Command(tx, "SELECT COUNT(*) FROM captains c JOIN missions m ON m.id=@mission WHERE c.id=@id AND c.tenant_id=@tenant AND m.tenant_id=@tenant AND ((m.user_id=@owner) OR (m.user_id IS NULL AND @owner IS NULL));", captainId, missionId);
                DbParameter tenant = scope.CreateParameter(); tenant.ParameterName = "@tenant"; tenant.Value = selection.Decision.TenantId; scope.Parameters.Add(tenant);
                DbParameter owner = scope.CreateParameter(); owner.ParameterName = "@owner"; owner.Value = (object?)selection.Decision.UserId ?? DBNull.Value; scope.Parameters.Add(owner);
                if (Convert.ToInt32(await scope.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1) return false;
            }
            using DbCommand claim = Command(tx, @"UPDATE captains SET active_mission_count=active_mission_count+1,state='Working',last_update_utc=@now
                WHERE id=@id AND active=" + (_Postgres ? "TRUE" : "1") + @" AND current_session_id IS NULL AND state IN ('Idle','Working')
                AND active_mission_count < max_concurrency AND active_mission_count < @ceiling
                AND EXISTS (SELECT 1 FROM captain_mission_slots s JOIN missions m ON m.id=s.mission_id
                    WHERE s.captain_id=@id AND s.mission_id=@mission AND m.captain_id=@id AND m.status='Verifying'
                    AND m.process_id IS NULL AND m.last_update_utc=@value);", captainId, missionId,
                _Postgres || _Mysql ? version : version.ToString("o", CultureInfo.InvariantCulture));
            DbParameter maximum = claim.CreateParameter(); maximum.ParameterName = "@ceiling"; maximum.Value = Math.Clamp(ceiling, 1, 256); claim.Parameters.Add(maximum);
            if (await claim.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) return false;
            using DbCommand mission = Command(tx, @"UPDATE missions SET status='InProgress',last_update_utc=@now,selection_decision_id=NULL
                WHERE id=@mission AND captain_id=@id AND status='Verifying' AND process_id IS NULL AND last_update_utc=@value;", captainId, missionId,
                _Postgres || _Mysql ? version : version.ToString("o", CultureInfo.InvariantCulture));
            if (await mission.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) return false;
            using DbCommand refresh = Command(tx, Refresh + " WHERE id=@id;", captainId);
            await refresh.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (selection != null)
            {
                await CaptainSelectionMethods.WriteAggregateAsync(_Connection, tx, selection, token).ConfigureAwait(false);
                await SetSelectionDecisionAsync(tx, missionId, captainId, selection.Decision.Id, token).ConfigureAwait(false);
            }
            await tx.CommitAsync(token).ConfigureAwait(false);
            return true;
        }

        internal async Task<bool> SetAssignmentAsync(string captainId, string missionId, string column, object value, CancellationToken token)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (value is string text && String.IsNullOrEmpty(text)) throw new ArgumentNullException(nameof(value));
            if (column != "process_id" && column != "dock_id") throw new ArgumentException("Unsupported assignment field", nameof(column));
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            using DbCommand row = Command(tx, "UPDATE captains SET active_mission_count=active_mission_count WHERE id=@id;", captainId);
            await row.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand slot = Command(tx, "UPDATE captain_mission_slots SET " + column + "=@value WHERE captain_id=@id AND mission_id=@mission;", captainId, missionId, value);
            bool result = await slot.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
            if (result)
            {
                using DbCommand mission = Command(tx, "UPDATE missions SET " + column + "=@value WHERE id=@mission AND captain_id=@id AND status IN ('Assigned','InProgress','Verifying');", captainId, missionId, value);
                await mission.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            await tx.CommitAsync(token).ConfigureAwait(false);
            return result;
        }

        internal async Task<int?> ReadProcessAsync(string captainId, string missionId, CancellationToken token)
        {
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            using DbCommand read = Command(tx, "SELECT process_id FROM captain_mission_slots WHERE captain_id=@id AND mission_id=@mission;", captainId, missionId);
            object? result = await read.ExecuteScalarAsync(token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        internal async Task ReconcileAsync(CancellationToken token)
        {
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            using DbCommand row = Command(tx, "UPDATE captains SET active_mission_count=active_mission_count;");
            await row.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand delete = Command(tx, @"DELETE FROM captain_mission_slots WHERE NOT EXISTS
                (SELECT 1 FROM missions m WHERE m.id=captain_mission_slots.mission_id AND m.captain_id=captain_mission_slots.captain_id AND m.status IN ('Assigned','InProgress','Verifying'));");
            await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand insert = Command(tx, @"INSERT INTO captain_mission_slots(mission_id,captain_id,process_id,dock_id)
                SELECT m.id,m.captain_id,m.process_id,m.dock_id FROM missions m JOIN captains c ON c.id=m.captain_id WHERE m.status IN ('Assigned','InProgress','Verifying')
                AND NOT EXISTS (SELECT 1 FROM captain_mission_slots s WHERE s.mission_id=m.id);");
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand refresh = Command(tx, Refresh + " WHERE active_mission_count>0 OR " + Count + ">0;");
            await refresh.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }
    }
}
