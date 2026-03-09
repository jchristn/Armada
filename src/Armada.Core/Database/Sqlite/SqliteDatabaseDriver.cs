namespace Armada.Core.Database.Sqlite
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// SQLite implementation of the Armada database driver.
    /// </summary>
    public class SqliteDatabaseDriver : DatabaseDriver
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[SqliteDatabaseDriver] ";
        private string _ConnectionString;
        private LoggingModule _Logging;
        private bool _Disposed = false;

        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the SQLite database driver.
        /// </summary>
        /// <param name="connectionString">SQLite connection string.</param>
        /// <param name="logging">Logging module.</param>
        public SqliteDatabaseDriver(string connectionString, LoggingModule logging)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));

            Fleets = new SqliteFleetMethods(_ConnectionString);
            Vessels = new SqliteVesselMethods(_ConnectionString);
            Captains = new SqliteCaptainMethods(_ConnectionString);
            Missions = new SqliteMissionMethods(_ConnectionString);
            Voyages = new SqliteVoyageMethods(_ConnectionString);
            Docks = new SqliteDockMethods(_ConnectionString);
            Signals = new SqliteSignalMethods(_ConnectionString);
            Events = new SqliteEventMethods(_ConnectionString);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the database, running any pending schema migrations.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            _Logging.Info(_Header + "initializing database");

            using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                using (SqliteCommand walCmd = conn.CreateCommand())
                {
                    walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                    await walCmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                using (SqliteCommand fkCmd = conn.CreateCommand())
                {
                    fkCmd.CommandText = "PRAGMA foreign_keys=ON;";
                    await fkCmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Create migration tracking table
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS schema_migrations (
                        version INTEGER PRIMARY KEY,
                        description TEXT NOT NULL,
                        applied_utc TEXT NOT NULL
                    );";
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Get current schema version
                int currentVersion = 0;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result != null && result != DBNull.Value) currentVersion = Convert.ToInt32(result);
                }

                // Apply pending migrations
                List<SchemaMigration> migrations = GetMigrations();
                int applied = 0;

                foreach (SchemaMigration migration in migrations)
                {
                    if (migration.Version <= currentVersion) continue;

                    _Logging.Info(_Header + "applying migration v" + migration.Version + ": " + migration.Description);

                    using (SqliteTransaction tx = conn.BeginTransaction())
                    {
                        foreach (string sql in migration.Statements)
                        {
                            using (SqliteCommand cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = sql;
                                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                            }
                        }

                        // Record migration
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "INSERT INTO schema_migrations (version, description, applied_utc) VALUES (@v, @d, @t);";
                            cmd.Parameters.AddWithValue("@v", migration.Version);
                            cmd.Parameters.AddWithValue("@d", migration.Description);
                            cmd.Parameters.AddWithValue("@t", ToIso8601(DateTime.UtcNow));
                            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }

                        tx.Commit();
                        applied++;
                    }
                }

                if (applied > 0)
                    _Logging.Info(_Header + "applied " + applied + " migration(s), schema now at v" + migrations[migrations.Count - 1].Version);
                else
                    _Logging.Info(_Header + "schema is up to date at v" + currentVersion);
            }

            _Logging.Info(_Header + "database initialized successfully");
        }

        /// <summary>
        /// Get the current schema version.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Current schema version number, or 0 if no migrations have been applied.</returns>
        public async Task<int> GetSchemaVersionAsync(CancellationToken token = default)
        {
            using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                // Check if schema_migrations table exists
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result == null || result == DBNull.Value) return 0;
                }

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result != null && result != DBNull.Value) return Convert.ToInt32(result);
                    return 0;
                }
            }
        }

        /// <summary>
        /// Dispose of resources.
        /// </summary>
        public override void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Logging.Info(_Header + "disposed");
        }

        #endregion

        #region Private-Methods

        private static List<SchemaMigration> GetMigrations()
        {
            return new List<SchemaMigration>
            {
                new SchemaMigration(1, "Initial schema: fleets, vessels, captains, voyages, missions, docks, signals, events",
                    @"CREATE TABLE IF NOT EXISTS fleets (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL UNIQUE,
                        description TEXT,
                        active INTEGER NOT NULL DEFAULT 1,
                        created_utc TEXT NOT NULL,
                        last_update_utc TEXT NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS vessels (
                        id TEXT PRIMARY KEY,
                        fleet_id TEXT,
                        name TEXT NOT NULL UNIQUE,
                        repo_url TEXT NOT NULL,
                        local_path TEXT,
                        default_branch TEXT NOT NULL DEFAULT 'main',
                        active INTEGER NOT NULL DEFAULT 1,
                        created_utc TEXT NOT NULL,
                        last_update_utc TEXT NOT NULL,
                        FOREIGN KEY (fleet_id) REFERENCES fleets(id) ON DELETE SET NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS captains (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL UNIQUE,
                        runtime TEXT NOT NULL DEFAULT 'ClaudeCode',
                        state TEXT NOT NULL DEFAULT 'Idle',
                        current_mission_id TEXT,
                        current_dock_id TEXT,
                        process_id INTEGER,
                        recovery_attempts INTEGER NOT NULL DEFAULT 0,
                        last_heartbeat_utc TEXT,
                        created_utc TEXT NOT NULL,
                        last_update_utc TEXT NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS voyages (
                        id TEXT PRIMARY KEY,
                        title TEXT NOT NULL,
                        description TEXT,
                        status TEXT NOT NULL DEFAULT 'Open',
                        created_utc TEXT NOT NULL,
                        completed_utc TEXT,
                        last_update_utc TEXT NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS missions (
                        id TEXT PRIMARY KEY,
                        voyage_id TEXT,
                        vessel_id TEXT,
                        captain_id TEXT,
                        title TEXT NOT NULL,
                        description TEXT,
                        status TEXT NOT NULL DEFAULT 'Pending',
                        priority INTEGER NOT NULL DEFAULT 100,
                        parent_mission_id TEXT,
                        branch_name TEXT,
                        pr_url TEXT,
                        created_utc TEXT NOT NULL,
                        started_utc TEXT,
                        completed_utc TEXT,
                        last_update_utc TEXT NOT NULL,
                        FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE SET NULL,
                        FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
                        FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL,
                        FOREIGN KEY (parent_mission_id) REFERENCES missions(id) ON DELETE SET NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS docks (
                        id TEXT PRIMARY KEY,
                        vessel_id TEXT NOT NULL,
                        captain_id TEXT,
                        worktree_path TEXT,
                        branch_name TEXT,
                        active INTEGER NOT NULL DEFAULT 1,
                        created_utc TEXT NOT NULL,
                        last_update_utc TEXT NOT NULL,
                        FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE,
                        FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS signals (
                        id TEXT PRIMARY KEY,
                        from_captain_id TEXT,
                        to_captain_id TEXT,
                        type TEXT NOT NULL DEFAULT 'Nudge',
                        payload TEXT,
                        read INTEGER NOT NULL DEFAULT 0,
                        created_utc TEXT NOT NULL,
                        FOREIGN KEY (from_captain_id) REFERENCES captains(id) ON DELETE SET NULL,
                        FOREIGN KEY (to_captain_id) REFERENCES captains(id) ON DELETE SET NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS events (
                        id TEXT PRIMARY KEY,
                        event_type TEXT NOT NULL,
                        entity_type TEXT,
                        entity_id TEXT,
                        captain_id TEXT,
                        mission_id TEXT,
                        vessel_id TEXT,
                        voyage_id TEXT,
                        message TEXT NOT NULL,
                        payload TEXT,
                        created_utc TEXT NOT NULL
                    );",
                    // Indexes
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_fleet ON vessels(fleet_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_captains_state ON captains(state);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_voyage ON missions(voyage_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_vessel ON missions(vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_captain ON missions(captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_status ON missions(status);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_status_priority ON missions(status, priority ASC, created_utc ASC);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_vessel_status ON missions(vessel_id, status);",
                    @"CREATE INDEX IF NOT EXISTS idx_voyages_status ON voyages(status);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_vessel ON docks(vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_vessel_available ON docks(vessel_id, active, captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_to_captain ON signals(to_captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_to_captain_read ON signals(to_captain_id, read);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_created ON signals(created_utc DESC);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_type ON events(event_type);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_captain ON events(captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_mission ON events(mission_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_vessel ON events(vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_voyage ON events(voyage_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_entity ON events(entity_type, entity_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_created ON events(created_utc DESC);"
                ),
                new SchemaMigration(2, "Add working_directory to vessels for local merge support",
                    @"ALTER TABLE vessels ADD COLUMN working_directory TEXT;"
                ),
                new SchemaMigration(3, "Add per-voyage push/PR/merge override columns",
                    @"ALTER TABLE voyages ADD COLUMN auto_push INTEGER;",
                    @"ALTER TABLE voyages ADD COLUMN auto_create_pull_requests INTEGER;",
                    @"ALTER TABLE voyages ADD COLUMN auto_merge_pull_requests INTEGER;"
                ),
                new SchemaMigration(4, "Add captain MaxParallelism and per-mission process/dock tracking",
                    @"ALTER TABLE captains ADD COLUMN max_parallelism INTEGER NOT NULL DEFAULT 1;",
                    @"ALTER TABLE missions ADD COLUMN dock_id TEXT;",
                    @"ALTER TABLE missions ADD COLUMN process_id INTEGER;"
                )
            };
        }

        private static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        private static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }

        private static DateTime? FromIso8601Nullable(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            if (string.IsNullOrEmpty(str)) return null;
            return FromIso8601(str);
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        private static bool? NullableBool(SqliteDataReader reader, string column)
        {
            try
            {
                object value = reader[column];
                if (value == null || value == DBNull.Value) return null;
                return Convert.ToInt64(value) == 1;
            }
            catch
            {
                return null;
            }
        }

        private static int? NullableInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value);
        }

        private static Fleet FleetFromReader(SqliteDataReader reader)
        {
            Fleet fleet = new Fleet();
            fleet.Id = reader["id"].ToString()!;
            fleet.Name = reader["name"].ToString()!;
            fleet.Description = NullableString(reader["description"]);
            fleet.Active = Convert.ToInt64(reader["active"]) == 1;
            fleet.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            fleet.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return fleet;
        }

        private static Vessel VesselFromReader(SqliteDataReader reader)
        {
            Vessel vessel = new Vessel();
            vessel.Id = reader["id"].ToString()!;
            vessel.FleetId = NullableString(reader["fleet_id"]);
            vessel.Name = reader["name"].ToString()!;
            vessel.RepoUrl = NullableString(reader["repo_url"]);
            vessel.LocalPath = NullableString(reader["local_path"]);
            vessel.WorkingDirectory = NullableString(reader["working_directory"]);
            vessel.DefaultBranch = reader["default_branch"].ToString()!;
            vessel.Active = Convert.ToInt64(reader["active"]) == 1;
            vessel.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            vessel.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return vessel;
        }

        private static Captain CaptainFromReader(SqliteDataReader reader)
        {
            Captain captain = new Captain();
            captain.Id = reader["id"].ToString()!;
            captain.Name = reader["name"].ToString()!;
            captain.Runtime = Enum.Parse<AgentRuntimeEnum>(reader["runtime"].ToString()!);
            captain.MaxParallelism = Convert.ToInt32(reader["max_parallelism"]);
            captain.State = Enum.Parse<CaptainStateEnum>(reader["state"].ToString()!);
            captain.CurrentMissionId = NullableString(reader["current_mission_id"]);
            captain.CurrentDockId = NullableString(reader["current_dock_id"]);
            captain.ProcessId = NullableInt(reader["process_id"]);
            captain.RecoveryAttempts = Convert.ToInt32(reader["recovery_attempts"]);
            captain.LastHeartbeatUtc = FromIso8601Nullable(reader["last_heartbeat_utc"]);
            captain.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            captain.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return captain;
        }

        private static Mission MissionFromReader(SqliteDataReader reader)
        {
            Mission mission = new Mission();
            mission.Id = reader["id"].ToString()!;
            mission.VoyageId = NullableString(reader["voyage_id"]);
            mission.VesselId = NullableString(reader["vessel_id"]);
            mission.CaptainId = NullableString(reader["captain_id"]);
            mission.Title = reader["title"].ToString()!;
            mission.Description = NullableString(reader["description"]);
            mission.Status = Enum.Parse<MissionStatusEnum>(reader["status"].ToString()!);
            mission.Priority = Convert.ToInt32(reader["priority"]);
            mission.ParentMissionId = NullableString(reader["parent_mission_id"]);
            mission.BranchName = NullableString(reader["branch_name"]);
            mission.DockId = NullableString(reader["dock_id"]);
            mission.ProcessId = NullableInt(reader["process_id"]);
            mission.PrUrl = NullableString(reader["pr_url"]);
            mission.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            mission.StartedUtc = FromIso8601Nullable(reader["started_utc"]);
            mission.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            mission.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return mission;
        }

        private static Voyage VoyageFromReader(SqliteDataReader reader)
        {
            Voyage voyage = new Voyage();
            voyage.Id = reader["id"].ToString()!;
            voyage.Title = reader["title"].ToString()!;
            voyage.Description = NullableString(reader["description"]);
            voyage.Status = Enum.Parse<VoyageStatusEnum>(reader["status"].ToString()!);
            voyage.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            voyage.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            voyage.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            voyage.AutoPush = NullableBool(reader, "auto_push");
            voyage.AutoCreatePullRequests = NullableBool(reader, "auto_create_pull_requests");
            voyage.AutoMergePullRequests = NullableBool(reader, "auto_merge_pull_requests");
            return voyage;
        }

        private static Dock DockFromReader(SqliteDataReader reader)
        {
            Dock dock = new Dock();
            dock.Id = reader["id"].ToString()!;
            dock.VesselId = reader["vessel_id"].ToString()!;
            dock.CaptainId = NullableString(reader["captain_id"]);
            dock.WorktreePath = NullableString(reader["worktree_path"]);
            dock.BranchName = NullableString(reader["branch_name"]);
            dock.Active = Convert.ToInt64(reader["active"]) == 1;
            dock.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            dock.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return dock;
        }

        private static Signal SignalFromReader(SqliteDataReader reader)
        {
            Signal signal = new Signal();
            signal.Id = reader["id"].ToString()!;
            signal.FromCaptainId = NullableString(reader["from_captain_id"]);
            signal.ToCaptainId = NullableString(reader["to_captain_id"]);
            signal.Type = Enum.Parse<SignalTypeEnum>(reader["type"].ToString()!);
            signal.Payload = NullableString(reader["payload"]);
            signal.Read = Convert.ToInt64(reader["read"]) == 1;
            signal.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            return signal;
        }

        private static ArmadaEvent EventFromReader(SqliteDataReader reader)
        {
            ArmadaEvent evt = new ArmadaEvent();
            evt.Id = reader["id"].ToString()!;
            evt.EventType = reader["event_type"].ToString()!;
            evt.EntityType = NullableString(reader["entity_type"]);
            evt.EntityId = NullableString(reader["entity_id"]);
            evt.CaptainId = NullableString(reader["captain_id"]);
            evt.MissionId = NullableString(reader["mission_id"]);
            evt.VesselId = NullableString(reader["vessel_id"]);
            evt.VoyageId = NullableString(reader["voyage_id"]);
            evt.Message = reader["message"].ToString()!;
            evt.Payload = NullableString(reader["payload"]);
            evt.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            return evt;
        }

        #endregion

        #region Private-Classes

        private class SqliteFleetMethods : IFleetMethods
        {
            private string _ConnectionString;

            internal SqliteFleetMethods(string connectionString)
            {
                _ConnectionString = connectionString;
            }

            public async Task<Fleet> CreateAsync(Fleet fleet, CancellationToken token = default)
            {
                if (fleet == null) throw new ArgumentNullException(nameof(fleet));
                fleet.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO fleets (id, name, description, active, created_utc, last_update_utc)
                            VALUES (@id, @name, @description, @active, @created_utc, @last_update_utc);";
                        cmd.Parameters.AddWithValue("@id", fleet.Id);
                        cmd.Parameters.AddWithValue("@name", fleet.Name);
                        cmd.Parameters.AddWithValue("@description", (object?)fleet.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@active", fleet.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(fleet.CreatedUtc));
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(fleet.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return fleet;
            }

            public async Task<Fleet?> ReadAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM fleets WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return FleetFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Fleet?> ReadByNameAsync(string name, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM fleets WHERE name = @name;";
                        cmd.Parameters.AddWithValue("@name", name);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return FleetFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Fleet> UpdateAsync(Fleet fleet, CancellationToken token = default)
            {
                if (fleet == null) throw new ArgumentNullException(nameof(fleet));
                fleet.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"UPDATE fleets SET
                            name = @name,
                            description = @description,
                            active = @active,
                            last_update_utc = @last_update_utc
                            WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", fleet.Id);
                        cmd.Parameters.AddWithValue("@name", fleet.Name);
                        cmd.Parameters.AddWithValue("@description", (object?)fleet.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@active", fleet.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(fleet.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return fleet;
            }

            public async Task DeleteAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM fleets WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }

            public async Task<List<Fleet>> EnumerateAsync(CancellationToken token = default)
            {
                List<Fleet> results = new List<Fleet>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM fleets ORDER BY name;";
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(FleetFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<EnumerationResult<Fleet>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
            {
                if (query == null) query = new EnumerationQuery();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);

                    List<string> conditions = new List<string>();
                    List<SqliteParameter> parameters = new List<SqliteParameter>();

                    if (query.CreatedAfter.HasValue)
                    {
                        conditions.Add("created_utc > @created_after");
                        parameters.Add(new SqliteParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                    }
                    if (query.CreatedBefore.HasValue)
                    {
                        conditions.Add("created_utc < @created_before");
                        parameters.Add(new SqliteParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                    }

                    string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                    string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                    // Count
                    long totalCount = 0;
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM fleets" + whereClause + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    }

                    // Query
                    List<Fleet> results = new List<Fleet>();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM fleets" + whereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(FleetFromReader(reader));
                        }
                    }

                    return EnumerationResult<Fleet>.Create(query, results, totalCount);
                }
            }

            public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM fleets WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                        return count > 0;
                    }
                }
            }
        }

        private class SqliteVesselMethods : IVesselMethods
        {
            private string _ConnectionString;

            internal SqliteVesselMethods(string connectionString)
            {
                _ConnectionString = connectionString;
            }

            public async Task<Vessel> CreateAsync(Vessel vessel, CancellationToken token = default)
            {
                if (vessel == null) throw new ArgumentNullException(nameof(vessel));
                vessel.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO vessels (id, fleet_id, name, repo_url, local_path, working_directory, default_branch, active, created_utc, last_update_utc)
                            VALUES (@id, @fleet_id, @name, @repo_url, @local_path, @working_directory, @default_branch, @active, @created_utc, @last_update_utc);";
                        cmd.Parameters.AddWithValue("@id", vessel.Id);
                        cmd.Parameters.AddWithValue("@fleet_id", (object?)vessel.FleetId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@name", vessel.Name);
                        cmd.Parameters.AddWithValue("@repo_url", (object?)vessel.RepoUrl ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@local_path", (object?)vessel.LocalPath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@working_directory", (object?)vessel.WorkingDirectory ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@default_branch", vessel.DefaultBranch);
                        cmd.Parameters.AddWithValue("@active", vessel.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(vessel.CreatedUtc));
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(vessel.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return vessel;
            }

            public async Task<Vessel?> ReadAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM vessels WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return VesselFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Vessel?> ReadByNameAsync(string name, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM vessels WHERE name = @name;";
                        cmd.Parameters.AddWithValue("@name", name);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return VesselFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Vessel> UpdateAsync(Vessel vessel, CancellationToken token = default)
            {
                if (vessel == null) throw new ArgumentNullException(nameof(vessel));
                vessel.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"UPDATE vessels SET
                            fleet_id = @fleet_id,
                            name = @name,
                            repo_url = @repo_url,
                            local_path = @local_path,
                            working_directory = @working_directory,
                            default_branch = @default_branch,
                            active = @active,
                            last_update_utc = @last_update_utc
                            WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", vessel.Id);
                        cmd.Parameters.AddWithValue("@fleet_id", (object?)vessel.FleetId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@name", vessel.Name);
                        cmd.Parameters.AddWithValue("@repo_url", (object?)vessel.RepoUrl ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@local_path", (object?)vessel.LocalPath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@working_directory", (object?)vessel.WorkingDirectory ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@default_branch", vessel.DefaultBranch);
                        cmd.Parameters.AddWithValue("@active", vessel.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(vessel.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return vessel;
            }

            public async Task DeleteAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM vessels WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }

            public async Task<List<Vessel>> EnumerateAsync(CancellationToken token = default)
            {
                List<Vessel> results = new List<Vessel>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM vessels ORDER BY name;";
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(VesselFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Vessel>> EnumerateByFleetAsync(string fleetId, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(fleetId)) throw new ArgumentNullException(nameof(fleetId));
                List<Vessel> results = new List<Vessel>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM vessels WHERE fleet_id = @fleet_id ORDER BY name;";
                        cmd.Parameters.AddWithValue("@fleet_id", fleetId);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(VesselFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<EnumerationResult<Vessel>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
            {
                if (query == null) query = new EnumerationQuery();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);

                    List<string> conditions = new List<string>();
                    List<SqliteParameter> parameters = new List<SqliteParameter>();

                    if (query.CreatedAfter.HasValue)
                    {
                        conditions.Add("created_utc > @created_after");
                        parameters.Add(new SqliteParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                    }
                    if (query.CreatedBefore.HasValue)
                    {
                        conditions.Add("created_utc < @created_before");
                        parameters.Add(new SqliteParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                    }
                    if (!string.IsNullOrEmpty(query.FleetId))
                    {
                        conditions.Add("fleet_id = @fleet_id");
                        parameters.Add(new SqliteParameter("@fleet_id", query.FleetId));
                    }

                    string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                    string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                    // Count
                    long totalCount = 0;
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM vessels" + whereClause + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    }

                    // Query
                    List<Vessel> results = new List<Vessel>();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM vessels" + whereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(VesselFromReader(reader));
                        }
                    }

                    return EnumerationResult<Vessel>.Create(query, results, totalCount);
                }
            }

            public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM vessels WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                        return count > 0;
                    }
                }
            }
        }

        private class SqliteCaptainMethods : ICaptainMethods
        {
            private string _ConnectionString;

            internal SqliteCaptainMethods(string connectionString)
            {
                _ConnectionString = connectionString;
            }

            public async Task<Captain> CreateAsync(Captain captain, CancellationToken token = default)
            {
                if (captain == null) throw new ArgumentNullException(nameof(captain));
                captain.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO captains (id, name, runtime, max_parallelism, state, current_mission_id, current_dock_id, process_id, recovery_attempts, last_heartbeat_utc, created_utc, last_update_utc)
                            VALUES (@id, @name, @runtime, @max_parallelism, @state, @current_mission_id, @current_dock_id, @process_id, @recovery_attempts, @last_heartbeat_utc, @created_utc, @last_update_utc);";
                        cmd.Parameters.AddWithValue("@id", captain.Id);
                        cmd.Parameters.AddWithValue("@name", captain.Name);
                        cmd.Parameters.AddWithValue("@runtime", captain.Runtime.ToString());
                        cmd.Parameters.AddWithValue("@max_parallelism", captain.MaxParallelism);
                        cmd.Parameters.AddWithValue("@state", captain.State.ToString());
                        cmd.Parameters.AddWithValue("@current_mission_id", (object?)captain.CurrentMissionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@current_dock_id", (object?)captain.CurrentDockId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@process_id", captain.ProcessId.HasValue ? (object)captain.ProcessId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@recovery_attempts", captain.RecoveryAttempts);
                        cmd.Parameters.AddWithValue("@last_heartbeat_utc", captain.LastHeartbeatUtc.HasValue ? (object)ToIso8601(captain.LastHeartbeatUtc.Value) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(captain.CreatedUtc));
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(captain.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return captain;
            }

            public async Task<Captain?> ReadAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM captains WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return CaptainFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Captain?> ReadByNameAsync(string name, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM captains WHERE name = @name;";
                        cmd.Parameters.AddWithValue("@name", name);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return CaptainFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Captain> UpdateAsync(Captain captain, CancellationToken token = default)
            {
                if (captain == null) throw new ArgumentNullException(nameof(captain));
                captain.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"UPDATE captains SET
                            name = @name,
                            runtime = @runtime,
                            max_parallelism = @max_parallelism,
                            state = @state,
                            current_mission_id = @current_mission_id,
                            current_dock_id = @current_dock_id,
                            process_id = @process_id,
                            recovery_attempts = @recovery_attempts,
                            last_heartbeat_utc = @last_heartbeat_utc,
                            last_update_utc = @last_update_utc
                            WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", captain.Id);
                        cmd.Parameters.AddWithValue("@name", captain.Name);
                        cmd.Parameters.AddWithValue("@runtime", captain.Runtime.ToString());
                        cmd.Parameters.AddWithValue("@max_parallelism", captain.MaxParallelism);
                        cmd.Parameters.AddWithValue("@state", captain.State.ToString());
                        cmd.Parameters.AddWithValue("@current_mission_id", (object?)captain.CurrentMissionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@current_dock_id", (object?)captain.CurrentDockId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@process_id", captain.ProcessId.HasValue ? (object)captain.ProcessId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@recovery_attempts", captain.RecoveryAttempts);
                        cmd.Parameters.AddWithValue("@last_heartbeat_utc", captain.LastHeartbeatUtc.HasValue ? (object)ToIso8601(captain.LastHeartbeatUtc.Value) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(captain.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return captain;
            }

            public async Task DeleteAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM captains WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }

            public async Task<List<Captain>> EnumerateAsync(CancellationToken token = default)
            {
                List<Captain> results = new List<Captain>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM captains ORDER BY name;";
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(CaptainFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Captain>> EnumerateByStateAsync(CaptainStateEnum state, CancellationToken token = default)
            {
                List<Captain> results = new List<Captain>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM captains WHERE state = @state ORDER BY name;";
                        cmd.Parameters.AddWithValue("@state", state.ToString());
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(CaptainFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task UpdateStateAsync(string id, CaptainStateEnum state, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"UPDATE captains SET state = @state, last_update_utc = @last_update_utc WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@state", state.ToString());
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(DateTime.UtcNow));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }

            public async Task UpdateHeartbeatAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                DateTime now = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"UPDATE captains SET last_heartbeat_utc = @last_heartbeat_utc, last_update_utc = @last_update_utc WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@last_heartbeat_utc", ToIso8601(now));
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(now));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }

            public async Task<EnumerationResult<Captain>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
            {
                if (query == null) query = new EnumerationQuery();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);

                    List<string> conditions = new List<string>();
                    List<SqliteParameter> parameters = new List<SqliteParameter>();

                    if (query.CreatedAfter.HasValue)
                    {
                        conditions.Add("created_utc > @created_after");
                        parameters.Add(new SqliteParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                    }
                    if (query.CreatedBefore.HasValue)
                    {
                        conditions.Add("created_utc < @created_before");
                        parameters.Add(new SqliteParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                    }
                    if (!string.IsNullOrEmpty(query.Status))
                    {
                        conditions.Add("state = @state");
                        parameters.Add(new SqliteParameter("@state", query.Status));
                    }

                    string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                    string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                    // Count
                    long totalCount = 0;
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM captains" + whereClause + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    }

                    // Query
                    List<Captain> results = new List<Captain>();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM captains" + whereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(CaptainFromReader(reader));
                        }
                    }

                    return EnumerationResult<Captain>.Create(query, results, totalCount);
                }
            }

            public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM captains WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                        return count > 0;
                    }
                }
            }
        }

        private class SqliteMissionMethods : IMissionMethods
        {
            private string _ConnectionString;

            internal SqliteMissionMethods(string connectionString)
            {
                _ConnectionString = connectionString;
            }

            public async Task<Mission> CreateAsync(Mission mission, CancellationToken token = default)
            {
                if (mission == null) throw new ArgumentNullException(nameof(mission));
                mission.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO missions (id, voyage_id, vessel_id, captain_id, title, description, status, priority, parent_mission_id, branch_name, dock_id, process_id, pr_url, created_utc, started_utc, completed_utc, last_update_utc)
                            VALUES (@id, @voyage_id, @vessel_id, @captain_id, @title, @description, @status, @priority, @parent_mission_id, @branch_name, @dock_id, @process_id, @pr_url, @created_utc, @started_utc, @completed_utc, @last_update_utc);";
                        cmd.Parameters.AddWithValue("@id", mission.Id);
                        cmd.Parameters.AddWithValue("@voyage_id", (object?)mission.VoyageId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@vessel_id", (object?)mission.VesselId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@captain_id", (object?)mission.CaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@title", mission.Title);
                        cmd.Parameters.AddWithValue("@description", (object?)mission.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@status", mission.Status.ToString());
                        cmd.Parameters.AddWithValue("@priority", mission.Priority);
                        cmd.Parameters.AddWithValue("@parent_mission_id", (object?)mission.ParentMissionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@branch_name", (object?)mission.BranchName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@dock_id", (object?)mission.DockId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@process_id", mission.ProcessId.HasValue ? (object)mission.ProcessId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@pr_url", (object?)mission.PrUrl ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(mission.CreatedUtc));
                        cmd.Parameters.AddWithValue("@started_utc", mission.StartedUtc.HasValue ? (object)ToIso8601(mission.StartedUtc.Value) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@completed_utc", mission.CompletedUtc.HasValue ? (object)ToIso8601(mission.CompletedUtc.Value) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(mission.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return mission;
            }

            public async Task<Mission?> ReadAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM missions WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return MissionFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Mission> UpdateAsync(Mission mission, CancellationToken token = default)
            {
                if (mission == null) throw new ArgumentNullException(nameof(mission));
                mission.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"UPDATE missions SET
                            voyage_id = @voyage_id,
                            vessel_id = @vessel_id,
                            captain_id = @captain_id,
                            title = @title,
                            description = @description,
                            status = @status,
                            priority = @priority,
                            parent_mission_id = @parent_mission_id,
                            branch_name = @branch_name,
                            dock_id = @dock_id,
                            process_id = @process_id,
                            pr_url = @pr_url,
                            started_utc = @started_utc,
                            completed_utc = @completed_utc,
                            last_update_utc = @last_update_utc
                            WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", mission.Id);
                        cmd.Parameters.AddWithValue("@voyage_id", (object?)mission.VoyageId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@vessel_id", (object?)mission.VesselId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@captain_id", (object?)mission.CaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@title", mission.Title);
                        cmd.Parameters.AddWithValue("@description", (object?)mission.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@status", mission.Status.ToString());
                        cmd.Parameters.AddWithValue("@priority", mission.Priority);
                        cmd.Parameters.AddWithValue("@parent_mission_id", (object?)mission.ParentMissionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@branch_name", (object?)mission.BranchName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@dock_id", (object?)mission.DockId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@process_id", mission.ProcessId.HasValue ? (object)mission.ProcessId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@pr_url", (object?)mission.PrUrl ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@started_utc", mission.StartedUtc.HasValue ? (object)ToIso8601(mission.StartedUtc.Value) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@completed_utc", mission.CompletedUtc.HasValue ? (object)ToIso8601(mission.CompletedUtc.Value) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(mission.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return mission;
            }

            public async Task DeleteAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM missions WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }

            public async Task<List<Mission>> EnumerateAsync(CancellationToken token = default)
            {
                List<Mission> results = new List<Mission>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM missions ORDER BY priority ASC, created_utc ASC;";
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(MissionFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Mission>> EnumerateByVoyageAsync(string voyageId, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));
                List<Mission> results = new List<Mission>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM missions WHERE voyage_id = @voyage_id ORDER BY priority ASC, created_utc ASC;";
                        cmd.Parameters.AddWithValue("@voyage_id", voyageId);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(MissionFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Mission>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
                List<Mission> results = new List<Mission>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM missions WHERE vessel_id = @vessel_id ORDER BY priority ASC, created_utc ASC;";
                        cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(MissionFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Mission>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
                List<Mission> results = new List<Mission>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM missions WHERE captain_id = @captain_id ORDER BY priority ASC, created_utc ASC;";
                        cmd.Parameters.AddWithValue("@captain_id", captainId);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(MissionFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Mission>> EnumerateByStatusAsync(MissionStatusEnum status, CancellationToken token = default)
            {
                List<Mission> results = new List<Mission>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM missions WHERE status = @status ORDER BY priority ASC, created_utc ASC;";
                        cmd.Parameters.AddWithValue("@status", status.ToString());
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(MissionFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<EnumerationResult<Mission>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
            {
                if (query == null) query = new EnumerationQuery();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);

                    List<string> conditions = new List<string>();
                    List<SqliteParameter> parameters = new List<SqliteParameter>();

                    if (query.CreatedAfter.HasValue)
                    {
                        conditions.Add("created_utc > @created_after");
                        parameters.Add(new SqliteParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                    }
                    if (query.CreatedBefore.HasValue)
                    {
                        conditions.Add("created_utc < @created_before");
                        parameters.Add(new SqliteParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                    }
                    if (!string.IsNullOrEmpty(query.Status))
                    {
                        conditions.Add("status = @status");
                        parameters.Add(new SqliteParameter("@status", query.Status));
                    }
                    if (!string.IsNullOrEmpty(query.VoyageId))
                    {
                        conditions.Add("voyage_id = @voyage_id");
                        parameters.Add(new SqliteParameter("@voyage_id", query.VoyageId));
                    }
                    if (!string.IsNullOrEmpty(query.VesselId))
                    {
                        conditions.Add("vessel_id = @vessel_id");
                        parameters.Add(new SqliteParameter("@vessel_id", query.VesselId));
                    }
                    if (!string.IsNullOrEmpty(query.CaptainId))
                    {
                        conditions.Add("captain_id = @captain_id");
                        parameters.Add(new SqliteParameter("@captain_id", query.CaptainId));
                    }

                    string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                    string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                    // Count
                    long totalCount = 0;
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM missions" + whereClause + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    }

                    // Query
                    List<Mission> results = new List<Mission>();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM missions" + whereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(MissionFromReader(reader));
                        }
                    }

                    return EnumerationResult<Mission>.Create(query, results, totalCount);
                }
            }

            public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM missions WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                        return count > 0;
                    }
                }
            }
        }

        private class SqliteVoyageMethods : IVoyageMethods
        {
            private string _ConnectionString;

            internal SqliteVoyageMethods(string connectionString)
            {
                _ConnectionString = connectionString;
            }

            public async Task<Voyage> CreateAsync(Voyage voyage, CancellationToken token = default)
            {
                if (voyage == null) throw new ArgumentNullException(nameof(voyage));
                voyage.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO voyages (id, title, description, status, created_utc, completed_utc, last_update_utc, auto_push, auto_create_pull_requests, auto_merge_pull_requests)
                            VALUES (@id, @title, @description, @status, @created_utc, @completed_utc, @last_update_utc, @auto_push, @auto_create_pull_requests, @auto_merge_pull_requests);";
                        cmd.Parameters.AddWithValue("@id", voyage.Id);
                        cmd.Parameters.AddWithValue("@title", voyage.Title);
                        cmd.Parameters.AddWithValue("@description", (object?)voyage.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@status", voyage.Status.ToString());
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(voyage.CreatedUtc));
                        cmd.Parameters.AddWithValue("@completed_utc", voyage.CompletedUtc.HasValue ? (object)ToIso8601(voyage.CompletedUtc.Value) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(voyage.LastUpdateUtc));
                        cmd.Parameters.AddWithValue("@auto_push", voyage.AutoPush.HasValue ? (object)(voyage.AutoPush.Value ? 1 : 0) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@auto_create_pull_requests", voyage.AutoCreatePullRequests.HasValue ? (object)(voyage.AutoCreatePullRequests.Value ? 1 : 0) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@auto_merge_pull_requests", voyage.AutoMergePullRequests.HasValue ? (object)(voyage.AutoMergePullRequests.Value ? 1 : 0) : DBNull.Value);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return voyage;
            }

            public async Task<Voyage?> ReadAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM voyages WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return VoyageFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Voyage> UpdateAsync(Voyage voyage, CancellationToken token = default)
            {
                if (voyage == null) throw new ArgumentNullException(nameof(voyage));
                voyage.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"UPDATE voyages SET
                            title = @title,
                            description = @description,
                            status = @status,
                            completed_utc = @completed_utc,
                            last_update_utc = @last_update_utc,
                            auto_push = @auto_push,
                            auto_create_pull_requests = @auto_create_pull_requests,
                            auto_merge_pull_requests = @auto_merge_pull_requests
                            WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", voyage.Id);
                        cmd.Parameters.AddWithValue("@title", voyage.Title);
                        cmd.Parameters.AddWithValue("@description", (object?)voyage.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@status", voyage.Status.ToString());
                        cmd.Parameters.AddWithValue("@completed_utc", voyage.CompletedUtc.HasValue ? (object)ToIso8601(voyage.CompletedUtc.Value) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(voyage.LastUpdateUtc));
                        cmd.Parameters.AddWithValue("@auto_push", voyage.AutoPush.HasValue ? (object)(voyage.AutoPush.Value ? 1 : 0) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@auto_create_pull_requests", voyage.AutoCreatePullRequests.HasValue ? (object)(voyage.AutoCreatePullRequests.Value ? 1 : 0) : DBNull.Value);
                        cmd.Parameters.AddWithValue("@auto_merge_pull_requests", voyage.AutoMergePullRequests.HasValue ? (object)(voyage.AutoMergePullRequests.Value ? 1 : 0) : DBNull.Value);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return voyage;
            }

            public async Task DeleteAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM voyages WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }

            public async Task<List<Voyage>> EnumerateAsync(CancellationToken token = default)
            {
                List<Voyage> results = new List<Voyage>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM voyages ORDER BY created_utc DESC;";
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(VoyageFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Voyage>> EnumerateByStatusAsync(VoyageStatusEnum status, CancellationToken token = default)
            {
                List<Voyage> results = new List<Voyage>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM voyages WHERE status = @status ORDER BY created_utc DESC;";
                        cmd.Parameters.AddWithValue("@status", status.ToString());
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(VoyageFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<EnumerationResult<Voyage>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
            {
                if (query == null) query = new EnumerationQuery();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);

                    List<string> conditions = new List<string>();
                    List<SqliteParameter> parameters = new List<SqliteParameter>();

                    if (query.CreatedAfter.HasValue)
                    {
                        conditions.Add("created_utc > @created_after");
                        parameters.Add(new SqliteParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                    }
                    if (query.CreatedBefore.HasValue)
                    {
                        conditions.Add("created_utc < @created_before");
                        parameters.Add(new SqliteParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                    }
                    if (!string.IsNullOrEmpty(query.Status))
                    {
                        conditions.Add("status = @status");
                        parameters.Add(new SqliteParameter("@status", query.Status));
                    }

                    string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                    string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                    // Count
                    long totalCount = 0;
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM voyages" + whereClause + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    }

                    // Query
                    List<Voyage> results = new List<Voyage>();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM voyages" + whereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(VoyageFromReader(reader));
                        }
                    }

                    return EnumerationResult<Voyage>.Create(query, results, totalCount);
                }
            }

            public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM voyages WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                        return count > 0;
                    }
                }
            }
        }

        private class SqliteDockMethods : IDockMethods
        {
            private string _ConnectionString;

            internal SqliteDockMethods(string connectionString)
            {
                _ConnectionString = connectionString;
            }

            public async Task<Dock> CreateAsync(Dock dock, CancellationToken token = default)
            {
                if (dock == null) throw new ArgumentNullException(nameof(dock));
                dock.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO docks (id, vessel_id, captain_id, worktree_path, branch_name, active, created_utc, last_update_utc)
                            VALUES (@id, @vessel_id, @captain_id, @worktree_path, @branch_name, @active, @created_utc, @last_update_utc);";
                        cmd.Parameters.AddWithValue("@id", dock.Id);
                        cmd.Parameters.AddWithValue("@vessel_id", dock.VesselId);
                        cmd.Parameters.AddWithValue("@captain_id", (object?)dock.CaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@worktree_path", (object?)dock.WorktreePath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@branch_name", (object?)dock.BranchName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@active", dock.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(dock.CreatedUtc));
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(dock.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return dock;
            }

            public async Task<Dock?> ReadAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM docks WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return DockFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<Dock> UpdateAsync(Dock dock, CancellationToken token = default)
            {
                if (dock == null) throw new ArgumentNullException(nameof(dock));
                dock.LastUpdateUtc = DateTime.UtcNow;

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"UPDATE docks SET
                            vessel_id = @vessel_id,
                            captain_id = @captain_id,
                            worktree_path = @worktree_path,
                            branch_name = @branch_name,
                            active = @active,
                            last_update_utc = @last_update_utc
                            WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", dock.Id);
                        cmd.Parameters.AddWithValue("@vessel_id", dock.VesselId);
                        cmd.Parameters.AddWithValue("@captain_id", (object?)dock.CaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@worktree_path", (object?)dock.WorktreePath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@branch_name", (object?)dock.BranchName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@active", dock.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(dock.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return dock;
            }

            public async Task DeleteAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM docks WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }

            public async Task<List<Dock>> EnumerateAsync(CancellationToken token = default)
            {
                List<Dock> results = new List<Dock>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM docks ORDER BY created_utc;";
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(DockFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Dock>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
                List<Dock> results = new List<Dock>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM docks WHERE vessel_id = @vessel_id ORDER BY created_utc;";
                        cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(DockFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<Dock?> FindAvailableAsync(string vesselId, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM docks WHERE vessel_id = @vessel_id AND active = 1 AND captain_id IS NULL LIMIT 1;";
                        cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return DockFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<EnumerationResult<Dock>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
            {
                if (query == null) query = new EnumerationQuery();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);

                    List<string> conditions = new List<string>();
                    List<SqliteParameter> parameters = new List<SqliteParameter>();

                    if (query.CreatedAfter.HasValue)
                    {
                        conditions.Add("created_utc > @created_after");
                        parameters.Add(new SqliteParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                    }
                    if (query.CreatedBefore.HasValue)
                    {
                        conditions.Add("created_utc < @created_before");
                        parameters.Add(new SqliteParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                    }
                    if (!string.IsNullOrEmpty(query.VesselId))
                    {
                        conditions.Add("vessel_id = @vessel_id");
                        parameters.Add(new SqliteParameter("@vessel_id", query.VesselId));
                    }
                    if (!string.IsNullOrEmpty(query.CaptainId))
                    {
                        conditions.Add("captain_id = @captain_id");
                        parameters.Add(new SqliteParameter("@captain_id", query.CaptainId));
                    }

                    string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                    string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                    // Count
                    long totalCount = 0;
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM docks" + whereClause + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    }

                    // Query
                    List<Dock> results = new List<Dock>();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM docks" + whereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(DockFromReader(reader));
                        }
                    }

                    return EnumerationResult<Dock>.Create(query, results, totalCount);
                }
            }

            public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM docks WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                        return count > 0;
                    }
                }
            }
        }

        private class SqliteSignalMethods : ISignalMethods
        {
            private string _ConnectionString;

            internal SqliteSignalMethods(string connectionString)
            {
                _ConnectionString = connectionString;
            }

            public async Task<Signal> CreateAsync(Signal signal, CancellationToken token = default)
            {
                if (signal == null) throw new ArgumentNullException(nameof(signal));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO signals (id, from_captain_id, to_captain_id, type, payload, read, created_utc)
                            VALUES (@id, @from_captain_id, @to_captain_id, @type, @payload, @read, @created_utc);";
                        cmd.Parameters.AddWithValue("@id", signal.Id);
                        cmd.Parameters.AddWithValue("@from_captain_id", (object?)signal.FromCaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@to_captain_id", (object?)signal.ToCaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@type", signal.Type.ToString());
                        cmd.Parameters.AddWithValue("@payload", (object?)signal.Payload ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@read", signal.Read ? 1 : 0);
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(signal.CreatedUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return signal;
            }

            public async Task<Signal?> ReadAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM signals WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                                return SignalFromReader(reader);
                        }
                    }
                }

                return null;
            }

            public async Task<List<Signal>> EnumerateByRecipientAsync(string captainId, bool unreadOnly = true, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
                List<Signal> results = new List<Signal>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = unreadOnly
                            ? "SELECT * FROM signals WHERE to_captain_id = @to_captain_id AND read = 0 ORDER BY created_utc DESC;"
                            : "SELECT * FROM signals WHERE to_captain_id = @to_captain_id ORDER BY created_utc DESC;";
                        cmd.Parameters.AddWithValue("@to_captain_id", captainId);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(SignalFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<List<Signal>> EnumerateRecentAsync(int count = 50, CancellationToken token = default)
            {
                List<Signal> results = new List<Signal>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM signals ORDER BY created_utc DESC LIMIT @count;";
                        cmd.Parameters.AddWithValue("@count", count);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(SignalFromReader(reader));
                        }
                    }
                }

                return results;
            }

            public async Task<EnumerationResult<Signal>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
            {
                if (query == null) query = new EnumerationQuery();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);

                    List<string> conditions = new List<string>();
                    List<SqliteParameter> parameters = new List<SqliteParameter>();

                    if (query.CreatedAfter.HasValue)
                    {
                        conditions.Add("created_utc > @created_after");
                        parameters.Add(new SqliteParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                    }
                    if (query.CreatedBefore.HasValue)
                    {
                        conditions.Add("created_utc < @created_before");
                        parameters.Add(new SqliteParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                    }
                    if (!string.IsNullOrEmpty(query.ToCaptainId))
                    {
                        conditions.Add("to_captain_id = @to_captain_id");
                        parameters.Add(new SqliteParameter("@to_captain_id", query.ToCaptainId));
                    }
                    if (!string.IsNullOrEmpty(query.SignalType))
                    {
                        conditions.Add("type = @type");
                        parameters.Add(new SqliteParameter("@type", query.SignalType));
                    }
                    if (query.UnreadOnly.HasValue && query.UnreadOnly.Value)
                    {
                        conditions.Add("read = 0");
                    }

                    string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                    string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                    // Count
                    long totalCount = 0;
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM signals" + whereClause + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    }

                    // Query
                    List<Signal> results = new List<Signal>();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM signals" + whereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(SignalFromReader(reader));
                        }
                    }

                    return EnumerationResult<Signal>.Create(query, results, totalCount);
                }
            }

            public async Task MarkReadAsync(string id, CancellationToken token = default)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE signals SET read = 1 WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }
        }

        private class SqliteEventMethods : IEventMethods
        {
            private string _ConnectionString;

            internal SqliteEventMethods(string connectionString)
            {
                _ConnectionString = connectionString;
            }

            public async Task<ArmadaEvent> CreateAsync(ArmadaEvent armadaEvent, CancellationToken token = default)
            {
                if (armadaEvent == null) throw new ArgumentNullException(nameof(armadaEvent));

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO events (id, event_type, entity_type, entity_id, captain_id, mission_id, vessel_id, voyage_id, message, payload, created_utc)
                            VALUES (@id, @event_type, @entity_type, @entity_id, @captain_id, @mission_id, @vessel_id, @voyage_id, @message, @payload, @created_utc);";
                        cmd.Parameters.AddWithValue("@id", armadaEvent.Id);
                        cmd.Parameters.AddWithValue("@event_type", armadaEvent.EventType);
                        cmd.Parameters.AddWithValue("@entity_type", (object?)armadaEvent.EntityType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@entity_id", (object?)armadaEvent.EntityId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@captain_id", (object?)armadaEvent.CaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@mission_id", (object?)armadaEvent.MissionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@vessel_id", (object?)armadaEvent.VesselId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@voyage_id", (object?)armadaEvent.VoyageId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@message", armadaEvent.Message);
                        cmd.Parameters.AddWithValue("@payload", (object?)armadaEvent.Payload ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(armadaEvent.CreatedUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                return armadaEvent;
            }

            public async Task<List<ArmadaEvent>> EnumerateRecentAsync(int limit = 50, CancellationToken token = default)
            {
                return await QueryEventsAsync("SELECT * FROM events ORDER BY created_utc DESC LIMIT @limit;",
                    cmd => cmd.Parameters.AddWithValue("@limit", limit), token).ConfigureAwait(false);
            }

            public async Task<List<ArmadaEvent>> EnumerateByTypeAsync(string eventType, int limit = 50, CancellationToken token = default)
            {
                return await QueryEventsAsync("SELECT * FROM events WHERE event_type = @event_type ORDER BY created_utc DESC LIMIT @limit;",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@event_type", eventType);
                        cmd.Parameters.AddWithValue("@limit", limit);
                    }, token).ConfigureAwait(false);
            }

            public async Task<List<ArmadaEvent>> EnumerateByEntityAsync(string entityType, string entityId, int limit = 50, CancellationToken token = default)
            {
                return await QueryEventsAsync("SELECT * FROM events WHERE entity_type = @entity_type AND entity_id = @entity_id ORDER BY created_utc DESC LIMIT @limit;",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@entity_type", entityType);
                        cmd.Parameters.AddWithValue("@entity_id", entityId);
                        cmd.Parameters.AddWithValue("@limit", limit);
                    }, token).ConfigureAwait(false);
            }

            public async Task<List<ArmadaEvent>> EnumerateByCaptainAsync(string captainId, int limit = 50, CancellationToken token = default)
            {
                return await QueryEventsAsync("SELECT * FROM events WHERE captain_id = @captain_id ORDER BY created_utc DESC LIMIT @limit;",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@captain_id", captainId);
                        cmd.Parameters.AddWithValue("@limit", limit);
                    }, token).ConfigureAwait(false);
            }

            public async Task<List<ArmadaEvent>> EnumerateByMissionAsync(string missionId, int limit = 50, CancellationToken token = default)
            {
                return await QueryEventsAsync("SELECT * FROM events WHERE mission_id = @mission_id ORDER BY created_utc DESC LIMIT @limit;",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@mission_id", missionId);
                        cmd.Parameters.AddWithValue("@limit", limit);
                    }, token).ConfigureAwait(false);
            }

            public async Task<List<ArmadaEvent>> EnumerateByVesselAsync(string vesselId, int limit = 50, CancellationToken token = default)
            {
                return await QueryEventsAsync("SELECT * FROM events WHERE vessel_id = @vessel_id ORDER BY created_utc DESC LIMIT @limit;",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                        cmd.Parameters.AddWithValue("@limit", limit);
                    }, token).ConfigureAwait(false);
            }

            public async Task<List<ArmadaEvent>> EnumerateByVoyageAsync(string voyageId, int limit = 50, CancellationToken token = default)
            {
                return await QueryEventsAsync("SELECT * FROM events WHERE voyage_id = @voyage_id ORDER BY created_utc DESC LIMIT @limit;",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@voyage_id", voyageId);
                        cmd.Parameters.AddWithValue("@limit", limit);
                    }, token).ConfigureAwait(false);
            }

            public async Task<EnumerationResult<ArmadaEvent>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
            {
                if (query == null) query = new EnumerationQuery();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);

                    List<string> conditions = new List<string>();
                    List<SqliteParameter> parameters = new List<SqliteParameter>();

                    if (query.CreatedAfter.HasValue)
                    {
                        conditions.Add("created_utc > @created_after");
                        parameters.Add(new SqliteParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                    }
                    if (query.CreatedBefore.HasValue)
                    {
                        conditions.Add("created_utc < @created_before");
                        parameters.Add(new SqliteParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                    }
                    if (!string.IsNullOrEmpty(query.EventType))
                    {
                        conditions.Add("event_type = @event_type");
                        parameters.Add(new SqliteParameter("@event_type", query.EventType));
                    }
                    if (!string.IsNullOrEmpty(query.CaptainId))
                    {
                        conditions.Add("captain_id = @captain_id");
                        parameters.Add(new SqliteParameter("@captain_id", query.CaptainId));
                    }
                    if (!string.IsNullOrEmpty(query.MissionId))
                    {
                        conditions.Add("mission_id = @mission_id");
                        parameters.Add(new SqliteParameter("@mission_id", query.MissionId));
                    }
                    if (!string.IsNullOrEmpty(query.VesselId))
                    {
                        conditions.Add("vessel_id = @vessel_id");
                        parameters.Add(new SqliteParameter("@vessel_id", query.VesselId));
                    }
                    if (!string.IsNullOrEmpty(query.VoyageId))
                    {
                        conditions.Add("voyage_id = @voyage_id");
                        parameters.Add(new SqliteParameter("@voyage_id", query.VoyageId));
                    }

                    string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                    string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                    // Count
                    long totalCount = 0;
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM events" + whereClause + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    }

                    // Query
                    List<ArmadaEvent> results = new List<ArmadaEvent>();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM events" + whereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(EventFromReader(reader));
                        }
                    }

                    return EnumerationResult<ArmadaEvent>.Create(query, results, totalCount);
                }
            }

            private async Task<List<ArmadaEvent>> QueryEventsAsync(string sql, Action<SqliteCommand> addParams, CancellationToken token)
            {
                List<ArmadaEvent> results = new List<ArmadaEvent>();

                using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        addParams(cmd);
                        using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(EventFromReader(reader));
                        }
                    }
                }

                return results;
            }
        }

        #endregion
    }
}
