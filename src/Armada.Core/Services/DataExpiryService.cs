namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Background service that purges old finished data on every database provider (SQLite, PostgreSQL, MySQL and SQL
    /// Server): terminal missions and finished voyages, finished merge entries, read signals, events (keeping the newest
    /// objective snapshot of each objective) and inactive docks older than the retention period, with the stage outcomes,
    /// handoff artifacts, wait intervals and playbook snapshots of the missions it removes.
    /// <para>
    /// It never deletes an active or recent mission, or anything a remaining record points at. A mission is kept while a
    /// remaining mission names it as parent, rescue original or dependency, while it holds a captain slot or session, has
    /// an open wait interval or an unfinished merge entry, or is referenced by a deployment, an incident or a black box
    /// entry or success that has not expired. A voyage is kept while a mission, deployment, incident or unexpired black
    /// box record points at it, and its missions are kept with it. A dock is kept while a mission, slot, planning session
    /// or unexpired black box entry points at it, and a merge entry while an unexpired black box entry does. Check runs
    /// keep their row with the mission or voyage link cleared, as every provider's ON DELETE SET NULL declares.
    /// </para>
    /// <para>
    /// Rows are removed in an order that satisfies every foreign key (links and child rows before the missions, missions
    /// before their voyages), so the same sweep works where SQL Server declares no ON DELETE action. On SQLite each
    /// statement enters the database's write gate on its own, so other writers interleave between statements. Not
    /// designed for concurrent calls.
    /// </para>
    /// </summary>
    public class DataExpiryService
    {
        #region Public-Members

        /// <summary>Active retention period in days. Zero disables expiry; negative values are rejected.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a negative value.</exception>
        public int RetentionDays
        {
            get { return _RetentionDays; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Retention days must be non-negative.");
                _RetentionDays = value;
            }
        }

        #endregion

        #region Private-Members

        private const int _BatchSize = 200;
        private const string _SnapshotEventType = "objective.snapshot";
        private const string _TerminalMissionStatuses = "'Complete', 'Failed', 'Cancelled'";
        private const string _FinishedVoyageStatuses = "'Complete', 'Cancelled'";
        private const string _FinishedMergeStatuses = "'Landed', 'Cancelled', 'Failed'";

        private string _Header = "[DataExpiryService] ";
        private LoggingModule _Logging;
        private DataExpiryDialect _Dialect;
        private SqliteWriteGate? _WriteGate;
        private int _RetentionDays;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a SQLite database.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="connectionString">SQLite connection string.</param>
        /// <param name="retentionDays">Number of days to retain completed data. Set to 0 to disable.</param>
        /// <exception cref="ArgumentNullException">Thrown when logging or the connection string is null.</exception>
        public DataExpiryService(LoggingModule logging, string connectionString, int retentionDays)
            : this(logging, DatabaseTypeEnum.Sqlite, connectionString, retentionDays)
        {
        }

        /// <summary>
        /// Instantiate for the configured database.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database settings (provider and connection).</param>
        /// <param name="retentionDays">Number of days to retain completed data. Set to 0 to disable.</param>
        /// <exception cref="ArgumentNullException">Thrown when logging or the database settings are null.</exception>
        public DataExpiryService(LoggingModule logging, DatabaseSettings database, int retentionDays)
            : this(logging, (database ?? throw new ArgumentNullException(nameof(database))).Type, database.GetConnectionString(), retentionDays)
        {
        }

        /// <summary>
        /// Instantiate for a database provider and connection string.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="databaseType">Database provider.</param>
        /// <param name="connectionString">Connection string for that provider.</param>
        /// <param name="retentionDays">Number of days to retain completed data. Set to 0 to disable.</param>
        /// <exception cref="ArgumentNullException">Thrown when logging or the connection string is null.</exception>
        public DataExpiryService(LoggingModule logging, DatabaseTypeEnum databaseType, string connectionString, int retentionDays)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));
            _Dialect = new DataExpiryDialect(databaseType, connectionString);
            if (databaseType == DatabaseTypeEnum.Sqlite) _WriteGate = SqliteWriteGate.ForConnectionString(connectionString);
            _RetentionDays = retentionDays;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the data expiry process once (see the class summary for what is deleted and what is kept).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Total number of rows deleted.</returns>
        public async Task<int> PurgeExpiredDataAsync(CancellationToken token = default)
        {
            if (_RetentionDays <= 0)
            {
                return 0;
            }

            DateTime now = DateTime.UtcNow;
            DateTime cutoff = now.AddDays(-_RetentionDays);
            int totalDeleted = 0;

            _Logging.Debug(_Header + "purging data older than " + cutoff.ToString("o", CultureInfo.InvariantCulture));

            using (DbConnection conn = _Dialect.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                await _Dialect.LoadColumnsAsync(conn, token).ConfigureAwait(false);

                DataExpiryReferences keep = await LoadReferencesAsync(conn, now, token).ConfigureAwait(false);

                // Finished merge entries first: a merge entry points at its mission, so a remaining one keeps the mission.
                totalDeleted += await PurgeMergeEntriesAsync(conn, cutoff, keep, token).ConfigureAwait(false);
                totalDeleted += await PurgeMissionsAsync(conn, cutoff, keep, token).ConfigureAwait(false);
                totalDeleted += await PurgeVoyagesAsync(conn, cutoff, keep, token).ConfigureAwait(false);

                // Stage outcomes and handoff artifacts hold nothing that outlives their mission; this also catches rows left
                // by a mission removed before they were cleaned up with it.
                totalDeleted += await ExecuteAsync(conn, "DELETE FROM stage_outcomes WHERE mission_id NOT IN (SELECT id FROM missions);", null, token).ConfigureAwait(false);
                totalDeleted += await ExecuteAsync(conn, "DELETE FROM handoff_artifacts WHERE mission_id NOT IN (SELECT id FROM missions);", null, token).ConfigureAwait(false);

                totalDeleted += await ExecuteAsync(conn,
                    "DELETE FROM signals WHERE " + _Dialect.Quote("read") + " = @read AND created_utc < @cutoff;",
                    cmd =>
                    {
                        _Dialect.AddFlag(cmd, "@read", "signals", "read", true);
                        _Dialect.AddTimestamp(cmd, "@cutoff", "signals", "created_utc", cutoff);
                    }, token).ConfigureAwait(false);

                totalDeleted += await PurgeEventsAsync(conn, cutoff, token).ConfigureAwait(false);
                totalDeleted += await PurgeDocksAsync(conn, cutoff, keep, token).ConfigureAwait(false);
            }

            if (totalDeleted > 0)
            {
                _Logging.Info(_Header + "purged " + totalDeleted + " expired records");
            }

            return totalDeleted;
        }

        #endregion

        #region Private-Methods

        private async Task<DataExpiryReferences> LoadReferencesAsync(DbConnection conn, DateTime now, CancellationToken token)
        {
            DataExpiryReferences keep = new DataExpiryReferences();

            // Slot ownership: a slot holds its mission and dock.
            foreach (string?[] row in await QueryAsync(conn, "SELECT mission_id, dock_id FROM captain_mission_slots;", null, token).ConfigureAwait(false))
            {
                DataExpiryReferences.AddId(keep.MissionIds, row[0]);
                DataExpiryReferences.AddId(keep.DockIds, row[1]);
            }

            // Session ownership: an exclusive session id can be a mission id (migrated from the old current mission).
            foreach (string?[] row in await QueryAsync(conn, "SELECT current_session_id FROM captains WHERE current_session_id IS NOT NULL;", null, token).ConfigureAwait(false))
                DataExpiryReferences.AddId(keep.MissionIds, row[0]);

            // An open wait interval means the mission is still being scheduled.
            foreach (string?[] row in await QueryAsync(conn, "SELECT mission_id FROM mission_wait_intervals WHERE ended_utc IS NULL;", null, token).ConfigureAwait(false))
                DataExpiryReferences.AddId(keep.MissionIds, row[0]);

            // Deployments and incidents are never expired here (SQL Server declares no ON DELETE action for deployments).
            foreach (string?[] row in await QueryAsync(conn, "SELECT mission_id, voyage_id FROM deployments WHERE mission_id IS NOT NULL OR voyage_id IS NOT NULL;", null, token).ConfigureAwait(false))
            {
                DataExpiryReferences.AddId(keep.MissionIds, row[0]);
                DataExpiryReferences.AddId(keep.VoyageIds, row[1]);
            }

            foreach (string?[] row in await QueryAsync(conn, "SELECT mission_id, voyage_id FROM incidents WHERE mission_id IS NOT NULL OR voyage_id IS NOT NULL;", null, token).ConfigureAwait(false))
            {
                DataExpiryReferences.AddId(keep.MissionIds, row[0]);
                DataExpiryReferences.AddId(keep.VoyageIds, row[1]);
            }

            // Black box evidence that has not expired (pinned, no expiry, or expiring later) keeps what it describes.
            foreach (string?[] row in await QueryAsync(conn,
                "SELECT mission_id, parent_mission_id, voyage_id, dock_id, merge_entry_id FROM blackbox_entries WHERE pinned = @pinned OR expires_utc IS NULL OR expires_utc >= @now;",
                cmd =>
                {
                    _Dialect.AddFlag(cmd, "@pinned", "blackbox_entries", "pinned", true);
                    _Dialect.AddTimestamp(cmd, "@now", "blackbox_entries", "expires_utc", now);
                }, token).ConfigureAwait(false))
            {
                DataExpiryReferences.AddId(keep.MissionIds, row[0]);
                DataExpiryReferences.AddId(keep.MissionIds, row[1]);
                DataExpiryReferences.AddId(keep.VoyageIds, row[2]);
                DataExpiryReferences.AddId(keep.DockIds, row[3]);
                DataExpiryReferences.AddId(keep.MergeEntryIds, row[4]);
            }

            foreach (string?[] row in await QueryAsync(conn,
                "SELECT mission_id, voyage_id FROM blackbox_successes WHERE expires_utc IS NULL OR expires_utc >= @now;",
                cmd => _Dialect.AddTimestamp(cmd, "@now", "blackbox_successes", "expires_utc", now), token).ConfigureAwait(false))
            {
                DataExpiryReferences.AddId(keep.MissionIds, row[0]);
                DataExpiryReferences.AddId(keep.VoyageIds, row[1]);
            }

            // Planning sessions own their dock (the table exists only where planning sessions are stored relationally).
            if (_Dialect.HasColumn("planning_sessions", "dock_id"))
            {
                foreach (string?[] row in await QueryAsync(conn, "SELECT dock_id FROM planning_sessions WHERE dock_id IS NOT NULL;", null, token).ConfigureAwait(false))
                    DataExpiryReferences.AddId(keep.DockIds, row[0]);
            }

            return keep;
        }

        private async Task<int> PurgeMergeEntriesAsync(DbConnection conn, DateTime cutoff, DataExpiryReferences keep, CancellationToken token)
        {
            List<string> expired = (await QueryAsync(conn,
                "SELECT id FROM merge_entries WHERE status IN (" + _FinishedMergeStatuses + ") AND completed_utc IS NOT NULL AND completed_utc < @cutoff;",
                cmd => _Dialect.AddTimestamp(cmd, "@cutoff", "merge_entries", "completed_utc", cutoff), token).ConfigureAwait(false))
                .Select(row => row[0])
                .Where(id => !String.IsNullOrEmpty(id) && !keep.MergeEntryIds.Contains(id!))
                .Select(id => id!)
                .ToList();

            int deleted = 0;
            foreach (List<string> batch in Batches(expired))
                deleted += await ExecuteIdsAsync(conn, "DELETE FROM merge_entries WHERE id IN ({ids});", batch, token).ConfigureAwait(false);
            return deleted;
        }

        private async Task<int> PurgeMissionsAsync(DbConnection conn, DateTime cutoff, DataExpiryReferences keep, CancellationToken token)
        {
            // Voyages that are finished and old, less those something else keeps; a kept voyage keeps its missions.
            HashSet<string> finishedVoyages = await SelectFinishedVoyagesAsync(conn, cutoff, keep, token).ConfigureAwait(false);

            // Terminal missions that finished (or, with no completion time, last changed) before the cutoff, standalone
            // or in a finished voyage.
            List<string?[]> rows = await QueryAsync(conn,
                "SELECT id, voyage_id FROM missions WHERE status IN (" + _TerminalMissionStatuses + ")"
                + " AND ((completed_utc IS NOT NULL AND completed_utc < @completed) OR (completed_utc IS NULL AND last_update_utc < @updated));",
                cmd =>
                {
                    _Dialect.AddTimestamp(cmd, "@completed", "missions", "completed_utc", cutoff);
                    _Dialect.AddTimestamp(cmd, "@updated", "missions", "last_update_utc", cutoff);
                }, token).ConfigureAwait(false);

            HashSet<string> deletable = new HashSet<string>(StringComparer.Ordinal);
            foreach (string?[] row in rows)
            {
                string? id = row[0];
                if (String.IsNullOrEmpty(id) || keep.MissionIds.Contains(id)) continue;
                if (!String.IsNullOrEmpty(row[1]) && !finishedVoyages.Contains(row[1]!)) continue;
                deletable.Add(id);
            }

            if (deletable.Count == 0) return 0;

            // A remaining merge entry keeps its mission.
            foreach (List<string> batch in Batches(deletable.ToList()))
            {
                foreach (string?[] row in await QueryIdsAsync(conn, "SELECT mission_id FROM merge_entries WHERE mission_id IN ({ids});", batch, token).ConfigureAwait(false))
                    if (!String.IsNullOrEmpty(row[0])) deletable.Remove(row[0]!);
            }

            // Every mission that points at a deletable one (as parent, rescue original or dependency).
            List<DataExpiryMissionLink> links = new List<DataExpiryMissionLink>();
            foreach (List<string> batch in Batches(deletable.ToList()))
            {
                string sql = "SELECT id, parent_mission_id, rescue_of_mission_id, depends_on_mission_id FROM missions"
                    + " WHERE parent_mission_id IN ({ids}) OR rescue_of_mission_id IN ({ids}) OR depends_on_mission_id IN ({ids});";
                foreach (string?[] row in await QueryIdsAsync(conn, sql, batch, token).ConfigureAwait(false))
                {
                    links.Add(new DataExpiryMissionLink
                    {
                        Id = row[0] ?? "",
                        ParentMissionId = row[1],
                        RescueOfMissionId = row[2],
                        DependsOnMissionId = row[3]
                    });
                }
            }

            // Keep a mission while any mission that stays points at it, until nothing changes (a kept child keeps its
            // parent, which keeps the rescue that names it, and so on).
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (DataExpiryMissionLink link in links)
                {
                    if (deletable.Contains(link.Id)) continue;
                    changed |= RemoveId(deletable, link.ParentMissionId);
                    changed |= RemoveId(deletable, link.RescueOfMissionId);
                    changed |= RemoveId(deletable, link.DependsOnMissionId);
                }
            }

            int deleted = 0;
            List<string> ordered = deletable.OrderBy(id => id, StringComparer.Ordinal).ToList();
            foreach (List<string> batch in Batches(ordered))
            {
                // Links first: SQL Server declares no ON DELETE action for parent_mission_id, and rescue and dependency
                // links have no foreign key. Only missions in this sweep can still point at the batch.
                await ExecuteIdsAsync(conn, "UPDATE missions SET parent_mission_id = NULL WHERE parent_mission_id IN ({ids});", batch, token).ConfigureAwait(false);
                await ExecuteIdsAsync(conn, "UPDATE missions SET rescue_of_mission_id = NULL WHERE rescue_of_mission_id IN ({ids});", batch, token).ConfigureAwait(false);
                await ExecuteIdsAsync(conn, "UPDATE missions SET depends_on_mission_id = NULL WHERE depends_on_mission_id IN ({ids});", batch, token).ConfigureAwait(false);
                await ExecuteIdsAsync(conn, "UPDATE check_runs SET mission_id = NULL WHERE mission_id IN ({ids});", batch, token).ConfigureAwait(false);

                // Child rows that cascade on most providers are removed explicitly so the order never depends on it.
                await ExecuteIdsAsync(conn, "DELETE FROM mission_wait_intervals WHERE mission_id IN ({ids});", batch, token).ConfigureAwait(false);
                await ExecuteIdsAsync(conn, "DELETE FROM mission_playbook_snapshots WHERE mission_id IN ({ids});", batch, token).ConfigureAwait(false);
                deleted += await ExecuteIdsAsync(conn, "DELETE FROM stage_outcomes WHERE mission_id IN ({ids});", batch, token).ConfigureAwait(false);
                deleted += await ExecuteIdsAsync(conn, "DELETE FROM handoff_artifacts WHERE mission_id IN ({ids});", batch, token).ConfigureAwait(false);
                deleted += await ExecuteIdsAsync(conn, "DELETE FROM missions WHERE id IN ({ids});", batch, token).ConfigureAwait(false);
            }

            return deleted;
        }

        private async Task<int> PurgeVoyagesAsync(DbConnection conn, DateTime cutoff, DataExpiryReferences keep, CancellationToken token)
        {
            List<string> candidates = (await SelectFinishedVoyagesAsync(conn, cutoff, keep, token).ConfigureAwait(false))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            int deleted = 0;
            foreach (List<string> batch in Batches(candidates))
            {
                // A mission that remains (kept, or not finished) keeps its voyage.
                HashSet<string> held = new HashSet<string>(StringComparer.Ordinal);
                foreach (string?[] row in await QueryIdsAsync(conn, "SELECT voyage_id FROM missions WHERE voyage_id IN ({ids});", batch, token).ConfigureAwait(false))
                    DataExpiryReferences.AddId(held, row[0]);

                List<string> free = batch.Where(id => !held.Contains(id)).ToList();
                if (free.Count == 0) continue;

                await ExecuteIdsAsync(conn, "UPDATE check_runs SET voyage_id = NULL WHERE voyage_id IN ({ids});", free, token).ConfigureAwait(false);
                await ExecuteIdsAsync(conn, "DELETE FROM voyage_playbooks WHERE voyage_id IN ({ids});", free, token).ConfigureAwait(false);
                deleted += await ExecuteIdsAsync(conn, "DELETE FROM voyages WHERE id IN ({ids});", free, token).ConfigureAwait(false);
            }

            return deleted;
        }

        private async Task<HashSet<string>> SelectFinishedVoyagesAsync(DbConnection conn, DateTime cutoff, DataExpiryReferences keep, CancellationToken token)
        {
            HashSet<string> voyages = new HashSet<string>(StringComparer.Ordinal);
            foreach (string?[] row in await QueryAsync(conn,
                "SELECT id FROM voyages WHERE status IN (" + _FinishedVoyageStatuses + ") AND completed_utc IS NOT NULL AND completed_utc < @cutoff;",
                cmd => _Dialect.AddTimestamp(cmd, "@cutoff", "voyages", "completed_utc", cutoff), token).ConfigureAwait(false))
            {
                if (!String.IsNullOrEmpty(row[0]) && !keep.VoyageIds.Contains(row[0]!)) voyages.Add(row[0]!);
            }

            return voyages;
        }

        private async Task<int> PurgeEventsAsync(DbConnection conn, DateTime cutoff, CancellationToken token)
        {
            int deleted = await ExecuteAsync(conn,
                "DELETE FROM events WHERE created_utc < @cutoff AND (event_type IS NULL OR event_type <> @snapshot);",
                cmd =>
                {
                    _Dialect.AddTimestamp(cmd, "@cutoff", "events", "created_utc", cutoff);
                    DataExpiryDialect.Add(cmd, "@snapshot", _SnapshotEventType);
                }, token).ConfigureAwait(false);

            // An objective is rebuilt from its newest snapshot, so keep that one even when it is older than the cutoff:
            // an objective with a recent snapshot loses every old one, any other keeps its newest old one.
            HashSet<string> recent = new HashSet<string>(StringComparer.Ordinal);
            foreach (string?[] row in await QueryAsync(conn,
                "SELECT DISTINCT entity_id FROM events WHERE event_type = @snapshot AND created_utc >= @cutoff AND entity_id IS NOT NULL;",
                cmd =>
                {
                    _Dialect.AddTimestamp(cmd, "@cutoff", "events", "created_utc", cutoff);
                    DataExpiryDialect.Add(cmd, "@snapshot", _SnapshotEventType);
                }, token).ConfigureAwait(false))
            {
                DataExpiryReferences.AddId(recent, row[0]);
            }

            List<string?[]> old = await QueryAsync(conn,
                "SELECT id, entity_id FROM events WHERE event_type = @snapshot AND created_utc < @cutoff ORDER BY created_utc DESC, id DESC;",
                cmd =>
                {
                    _Dialect.AddTimestamp(cmd, "@cutoff", "events", "created_utc", cutoff);
                    DataExpiryDialect.Add(cmd, "@snapshot", _SnapshotEventType);
                }, token).ConfigureAwait(false);

            HashSet<string> keptEntities = new HashSet<string>(StringComparer.Ordinal);
            List<string> expired = new List<string>();
            foreach (string?[] row in old)
            {
                if (String.IsNullOrEmpty(row[0])) continue;
                string? entity = row[1];
                bool newest = !String.IsNullOrEmpty(entity) && !recent.Contains(entity) && keptEntities.Add(entity);
                if (!newest) expired.Add(row[0]!);
            }

            foreach (List<string> batch in Batches(expired))
                deleted += await ExecuteIdsAsync(conn, "DELETE FROM events WHERE id IN ({ids});", batch, token).ConfigureAwait(false);
            return deleted;
        }

        private async Task<int> PurgeDocksAsync(DbConnection conn, DateTime cutoff, DataExpiryReferences keep, CancellationToken token)
        {
            List<string> candidates = (await QueryAsync(conn,
                "SELECT id FROM docks WHERE active = @active AND captain_id IS NULL AND created_utc < @cutoff;",
                cmd =>
                {
                    _Dialect.AddFlag(cmd, "@active", "docks", "active", false);
                    _Dialect.AddTimestamp(cmd, "@cutoff", "docks", "created_utc", cutoff);
                }, token).ConfigureAwait(false))
                .Select(row => row[0])
                .Where(id => !String.IsNullOrEmpty(id) && !keep.DockIds.Contains(id!))
                .Select(id => id!)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            int deleted = 0;
            foreach (List<string> batch in Batches(candidates))
            {
                // A mission that remains keeps the dock it owns.
                HashSet<string> held = new HashSet<string>(StringComparer.Ordinal);
                foreach (string?[] row in await QueryIdsAsync(conn, "SELECT dock_id FROM missions WHERE dock_id IN ({ids});", batch, token).ConfigureAwait(false))
                    DataExpiryReferences.AddId(held, row[0]);

                List<string> free = batch.Where(id => !held.Contains(id)).ToList();
                if (free.Count == 0) continue;
                deleted += await ExecuteIdsAsync(conn, "DELETE FROM docks WHERE id IN ({ids});", free, token).ConfigureAwait(false);
            }

            return deleted;
        }

        private static bool RemoveId(HashSet<string> set, string? id)
        {
            return !String.IsNullOrEmpty(id) && set.Remove(id);
        }

        private static IEnumerable<List<string>> Batches(List<string> ids)
        {
            for (int i = 0; i < ids.Count; i += _BatchSize)
                yield return ids.GetRange(i, Math.Min(_BatchSize, ids.Count - i));
        }

        private async Task<int> ExecuteIdsAsync(DbConnection conn, string sql, List<string> ids, CancellationToken token)
        {
            if (ids.Count == 0) return 0;
            int affected = 0;
            await WithCommandAsync(conn, async cmd =>
            {
                cmd.CommandText = sql.Replace("{ids}", DataExpiryDialect.AddIdList(cmd, "id", ids), StringComparison.Ordinal);
                affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }, true, token).ConfigureAwait(false);
            return affected;
        }

        private async Task<int> ExecuteAsync(DbConnection conn, string sql, Action<DbCommand>? bind, CancellationToken token)
        {
            int affected = 0;
            await WithCommandAsync(conn, async cmd =>
            {
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }, true, token).ConfigureAwait(false);
            return affected;
        }

        private async Task<List<string?[]>> QueryIdsAsync(DbConnection conn, string sql, List<string> ids, CancellationToken token)
        {
            if (ids.Count == 0) return new List<string?[]>();
            List<string?[]> rows = new List<string?[]>();
            await WithCommandAsync(conn, async cmd =>
            {
                cmd.CommandText = sql.Replace("{ids}", DataExpiryDialect.AddIdList(cmd, "id", ids), StringComparison.Ordinal);
                rows = await ReadRowsAsync(cmd, token).ConfigureAwait(false);
            }, false, token).ConfigureAwait(false);
            return rows;
        }

        private async Task<List<string?[]>> QueryAsync(DbConnection conn, string sql, Action<DbCommand>? bind, CancellationToken token)
        {
            List<string?[]> rows = new List<string?[]>();
            await WithCommandAsync(conn, async cmd =>
            {
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                rows = await ReadRowsAsync(cmd, token).ConfigureAwait(false);
            }, false, token).ConfigureAwait(false);
            return rows;
        }

        private async Task WithCommandAsync(DbConnection conn, Func<DbCommand, Task> body, bool write, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (write && _WriteGate != null)
            {
                using (SqliteWriteLease writeLease = await _WriteGate.EnterAsync(token).ConfigureAwait(false))
                using (DbCommand cmd = conn.CreateCommand())
                {
                    await body(cmd).ConfigureAwait(false);
                }

                return;
            }

            using (DbCommand cmd = conn.CreateCommand())
            {
                await body(cmd).ConfigureAwait(false);
            }
        }

        private static async Task<List<string?[]>> ReadRowsAsync(DbCommand cmd, CancellationToken token)
        {
            List<string?[]> rows = new List<string?[]>();
            using (DbDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    string?[] row = new string?[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                    rows.Add(row);
                }
            }

            return rows;
        }

        #endregion
    }
}
