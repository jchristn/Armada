namespace Armada.Core.Database.Mysql.Queries
{
    /// <summary>
    /// MySQL DDL statements for all Armada tables.
    /// Uses VARCHAR(450) for indexed keys, LONGTEXT for large text,
    /// TINYINT(1) for booleans, DATETIME(6) for microsecond-precision timestamps, INT for integers.
    /// </summary>
    public static class TableQueries
    {
        #region Public-Members

        /// <summary>
        /// DDL for the schema_migrations table.
        /// </summary>
        public static readonly string SchemaMigrations = @"CREATE TABLE IF NOT EXISTS schema_migrations (
            version INT NOT NULL PRIMARY KEY,
            description LONGTEXT NOT NULL,
            applied_utc DATETIME(6) NOT NULL
        );";

        /// <summary>
        /// DDL for the fleets table.
        /// </summary>
        public static readonly string Fleets = @"CREATE TABLE IF NOT EXISTS fleets (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            name VARCHAR(450) NOT NULL UNIQUE,
            description LONGTEXT,
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL
        );";

        /// <summary>
        /// DDL for the vessels table.
        /// </summary>
        public static readonly string Vessels = @"CREATE TABLE IF NOT EXISTS vessels (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            fleet_id VARCHAR(450),
            name VARCHAR(450) NOT NULL UNIQUE,
            repo_url LONGTEXT NOT NULL,
            local_path LONGTEXT,
            default_branch VARCHAR(450) NOT NULL DEFAULT 'main',
            working_directory LONGTEXT,
            project_context LONGTEXT,
            style_guide LONGTEXT,
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (fleet_id) REFERENCES fleets(id) ON DELETE SET NULL
        );";

        /// <summary>
        /// DDL for the captains table.
        /// </summary>
        public static readonly string Captains = @"CREATE TABLE IF NOT EXISTS captains (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            name VARCHAR(450) NOT NULL UNIQUE,
            runtime VARCHAR(450) NOT NULL DEFAULT 'ClaudeCode',
            state VARCHAR(450) NOT NULL DEFAULT 'Idle',
            current_mission_id VARCHAR(450),
            current_dock_id VARCHAR(450),
            process_id INT,
            recovery_attempts INT NOT NULL DEFAULT 0,
            last_heartbeat_utc DATETIME(6),
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL
        );";

        /// <summary>
        /// DDL for the voyages table.
        /// </summary>
        public static readonly string Voyages = @"CREATE TABLE IF NOT EXISTS voyages (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            title LONGTEXT NOT NULL,
            description LONGTEXT,
            status VARCHAR(450) NOT NULL DEFAULT 'Open',
            auto_push TINYINT(1),
            auto_create_pull_requests TINYINT(1),
            auto_merge_pull_requests TINYINT(1),
            created_utc DATETIME(6) NOT NULL,
            completed_utc DATETIME(6),
            last_update_utc DATETIME(6) NOT NULL
        );";

        /// <summary>
        /// DDL for the missions table.
        /// </summary>
        public static readonly string Missions = @"CREATE TABLE IF NOT EXISTS missions (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            voyage_id VARCHAR(450),
            vessel_id VARCHAR(450),
            captain_id VARCHAR(450),
            title LONGTEXT NOT NULL,
            description LONGTEXT,
            status VARCHAR(450) NOT NULL DEFAULT 'Pending',
            priority INT NOT NULL DEFAULT 100,
            parent_mission_id VARCHAR(450),
            branch_name VARCHAR(450),
            dock_id VARCHAR(450),
            process_id INT,
            pr_url LONGTEXT,
            commit_hash VARCHAR(450),
            diff_snapshot LONGTEXT,
            created_utc DATETIME(6) NOT NULL,
            started_utc DATETIME(6),
            completed_utc DATETIME(6),
            last_update_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE SET NULL,
            FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
            FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL,
            FOREIGN KEY (parent_mission_id) REFERENCES missions(id) ON DELETE SET NULL
        );";

        /// <summary>
        /// DDL for the docks table.
        /// </summary>
        public static readonly string Docks = @"CREATE TABLE IF NOT EXISTS docks (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            vessel_id VARCHAR(450) NOT NULL,
            captain_id VARCHAR(450),
            worktree_path LONGTEXT,
            branch_name VARCHAR(450),
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE,
            FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL
        );";

        /// <summary>
        /// DDL for the signals table.
        /// </summary>
        public static readonly string Signals = @"CREATE TABLE IF NOT EXISTS signals (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            from_captain_id VARCHAR(450),
            to_captain_id VARCHAR(450),
            type VARCHAR(450) NOT NULL DEFAULT 'Nudge',
            payload LONGTEXT,
            `read` TINYINT(1) NOT NULL DEFAULT 0,
            created_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (from_captain_id) REFERENCES captains(id) ON DELETE SET NULL,
            FOREIGN KEY (to_captain_id) REFERENCES captains(id) ON DELETE SET NULL
        );";

        /// <summary>
        /// DDL for the events table.
        /// </summary>
        public static readonly string Events = @"CREATE TABLE IF NOT EXISTS events (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            event_type VARCHAR(450) NOT NULL,
            entity_type VARCHAR(450),
            entity_id VARCHAR(450),
            captain_id VARCHAR(450),
            mission_id VARCHAR(450),
            vessel_id VARCHAR(450),
            voyage_id VARCHAR(450),
            message LONGTEXT NOT NULL,
            payload LONGTEXT,
            created_utc DATETIME(6) NOT NULL
        );";

        /// <summary>
        /// DDL for the merge_entries table.
        /// </summary>
        public static readonly string MergeEntries = @"CREATE TABLE IF NOT EXISTS merge_entries (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            mission_id VARCHAR(450),
            vessel_id VARCHAR(450),
            branch_name VARCHAR(450) NOT NULL,
            target_branch VARCHAR(450) NOT NULL DEFAULT 'main',
            status VARCHAR(450) NOT NULL DEFAULT 'Queued',
            priority INT NOT NULL DEFAULT 0,
            batch_id VARCHAR(450),
            test_command LONGTEXT,
            test_output LONGTEXT,
            test_exit_code INT,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            test_started_utc DATETIME(6),
            completed_utc DATETIME(6)
        );";

        /// <summary>
        /// Index DDL statements for all tables.
        /// </summary>
        public static readonly string[] Indexes = new string[]
        {
            "CREATE INDEX idx_vessels_fleet ON vessels(fleet_id);",
            "CREATE INDEX idx_captains_state ON captains(state);",
            "CREATE INDEX idx_missions_voyage ON missions(voyage_id);",
            "CREATE INDEX idx_missions_vessel ON missions(vessel_id);",
            "CREATE INDEX idx_missions_captain ON missions(captain_id);",
            "CREATE INDEX idx_missions_status ON missions(status);",
            "CREATE INDEX idx_missions_status_priority ON missions(status, priority ASC, created_utc ASC);",
            "CREATE INDEX idx_missions_vessel_status ON missions(vessel_id, status);",
            "CREATE INDEX idx_voyages_status ON voyages(status);",
            "CREATE INDEX idx_docks_vessel ON docks(vessel_id);",
            "CREATE INDEX idx_docks_vessel_available ON docks(vessel_id, active, captain_id);",
            "CREATE INDEX idx_signals_to_captain ON signals(to_captain_id);",
            "CREATE INDEX idx_signals_to_captain_read ON signals(to_captain_id, `read`);",
            "CREATE INDEX idx_signals_created ON signals(created_utc DESC);",
            "CREATE INDEX idx_events_type ON events(event_type);",
            "CREATE INDEX idx_events_captain ON events(captain_id);",
            "CREATE INDEX idx_events_mission ON events(mission_id);",
            "CREATE INDEX idx_events_vessel ON events(vessel_id);",
            "CREATE INDEX idx_events_voyage ON events(voyage_id);",
            "CREATE INDEX idx_events_entity ON events(entity_type, entity_id);",
            "CREATE INDEX idx_events_created ON events(created_utc DESC);",
            "CREATE INDEX idx_merge_entries_status ON merge_entries(status);",
            "CREATE INDEX idx_merge_entries_status_priority ON merge_entries(status, priority ASC, created_utc ASC);",
            "CREATE INDEX idx_merge_entries_vessel ON merge_entries(vessel_id);",
            "CREATE INDEX idx_merge_entries_mission ON merge_entries(mission_id);",
            "CREATE INDEX idx_merge_entries_completed ON merge_entries(completed_utc);"
        };

        #endregion
    }
}
