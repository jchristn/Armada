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

        internal async Task<bool> ClaimAsync(string captainId, string missionId, string? dockId, CancellationToken token, int ceiling = 256)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            try
            {
                using DbCommand row = Command(tx, "UPDATE captains SET active_mission_count=active_mission_count WHERE id=@id;", captainId);
                await row.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                // Updating the captain row serializes claims before the unique mission-slot insertion.
                using DbCommand claim = Command(tx, @"UPDATE captains SET state='Working', current_mission_id=CASE WHEN active_mission_count=0 THEN @mission ELSE current_mission_id END,
                    current_dock_id=CASE WHEN active_mission_count=0 THEN @value ELSE current_dock_id END,
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
                using DbCommand assign = Command(tx, "UPDATE missions SET captain_id=@id,status='Assigned',last_update_utc=@now WHERE id=@mission AND status='Pending';", captainId, missionId);
                await assign.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await tx.CommitAsync(token).ConfigureAwait(false);
                return true;
            }
            catch (DbException ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return false;
            }
        }

        private const string Count = "(SELECT COUNT(*) FROM captain_mission_slots s WHERE s.captain_id=captains.id)";
        private const string First = "(SELECT MIN(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id)";
        private static readonly string Refresh = @"UPDATE captains SET
            state=CASE WHEN " + Count + @"=0 AND state IN ('Working','Stalled','Stopping') THEN 'Idle'
                       WHEN " + Count + @">0 AND state IN ('Idle','Stalled') THEN 'Working' ELSE state END,
            active_mission_count=" + Count + @",current_mission_id=" + First + @",
            current_dock_id=(SELECT m.dock_id FROM missions m WHERE m.id=" + First + @"),
            process_id=(SELECT m.process_id FROM missions m WHERE m.id=" + First + @"),
            recovery_attempts=CASE WHEN " + Count + @"=0 THEN 0 ELSE recovery_attempts END,
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
            else
            {
                // Compatibility for a pre-slot operational snapshot; never touch a different assignment.
                using DbCommand legacy = Command(tx, @"UPDATE captains SET state='Idle',current_mission_id=NULL,current_dock_id=NULL,
                    process_id=NULL,recovery_attempts=0,last_update_utc=@now WHERE id=@id AND active_mission_count=0
                    AND current_mission_id=@mission AND state IN ('Working','Stalled','Stopping');", captainId, missionId);
                released = await legacy.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
            }
            await tx.CommitAsync(token).ConfigureAwait(false);
            return released;
        }

        internal async Task<bool> SetAssignmentAsync(string captainId, string missionId, string column, object value, CancellationToken token)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (value is string text && String.IsNullOrEmpty(text)) throw new ArgumentNullException(nameof(value));
            if (column != "process_id" && column != "current_dock_id") throw new ArgumentException("Unsupported assignment field", nameof(column));
            using DbTransaction tx = await _Connection.BeginTransactionAsync(token).ConfigureAwait(false);
            using DbCommand row = Command(tx, "UPDATE captains SET active_mission_count=active_mission_count WHERE id=@id;", captainId);
            await row.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand slot = Command(tx, "UPDATE captain_mission_slots SET " + (column == "process_id" ? "process_id" : "dock_id") + "=@value WHERE captain_id=@id AND mission_id=@mission;", captainId, missionId, value);
            await slot.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand cmd = Command(tx, "UPDATE captains SET " + column + "=CASE WHEN current_mission_id=@mission THEN @value ELSE " + column + @" END,last_update_utc=@now
                WHERE id=@id AND (EXISTS (SELECT 1 FROM captain_mission_slots s WHERE s.captain_id=@id AND s.mission_id=@mission)
                OR (active_mission_count=0 AND current_mission_id=@mission AND state IN ('Working','Stalled','Stopping')));", captainId, missionId, value);
            bool result = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
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
                SELECT m.id,m.captain_id,m.process_id,m.dock_id FROM missions m JOIN captains c ON c.id=m.captain_id WHERE (m.status IN ('InProgress','Verifying') OR (m.status='Assigned' AND (c.current_mission_id=m.id OR c.active_mission_count>0)))
                AND NOT EXISTS (SELECT 1 FROM captain_mission_slots s WHERE s.mission_id=m.id);");
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            using DbCommand refresh = Command(tx, Refresh + " WHERE active_mission_count>0 OR " + Count + ">0;");
            await refresh.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }
    }
}
