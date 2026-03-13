namespace Armada.Core.Database.Sqlite.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Static class containing all CREATE TABLE and CREATE INDEX DDL statements for the Armada SQLite schema.
    /// </summary>
    public static class TableQueries
    {
        #region Public-Methods

        /// <summary>
        /// Get all schema migrations for the Armada database.
        /// </summary>
        /// <returns>List of schema migrations.</returns>
        public static List<SchemaMigration> GetMigrations()
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
                ),
                new SchemaMigration(5, "Add commit_hash to missions for tracking completed work",
                    @"ALTER TABLE missions ADD COLUMN commit_hash TEXT;"
                ),
                new SchemaMigration(6, "Add project_context and style_guide to vessels",
                    @"ALTER TABLE vessels ADD COLUMN project_context TEXT;",
                    @"ALTER TABLE vessels ADD COLUMN style_guide TEXT;"
                ),
                new SchemaMigration(7, "Add diff_snapshot to missions for persisting diffs before worktree reclamation",
                    @"ALTER TABLE missions ADD COLUMN diff_snapshot TEXT;"
                ),
                new SchemaMigration(8, "Remove max_parallelism from captains",
                    @"ALTER TABLE captains DROP COLUMN max_parallelism;"
                ),
                new SchemaMigration(9, "Add merge_entries table for persistent merge queue",
                    @"CREATE TABLE IF NOT EXISTS merge_entries (
                        id TEXT PRIMARY KEY,
                        mission_id TEXT,
                        vessel_id TEXT,
                        branch_name TEXT NOT NULL,
                        target_branch TEXT NOT NULL DEFAULT 'main',
                        status TEXT NOT NULL DEFAULT 'Queued',
                        priority INTEGER NOT NULL DEFAULT 0,
                        batch_id TEXT,
                        test_command TEXT,
                        test_output TEXT,
                        test_exit_code INTEGER,
                        created_utc TEXT NOT NULL,
                        last_update_utc TEXT NOT NULL,
                        test_started_utc TEXT,
                        completed_utc TEXT
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_status ON merge_entries(status);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_status_priority ON merge_entries(status, priority ASC, created_utc ASC);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_vessel ON merge_entries(vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_mission ON merge_entries(mission_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_completed ON merge_entries(completed_utc);"
                ),
                new SchemaMigration(10, "Add landing_mode to vessels and voyages",
                    @"ALTER TABLE vessels ADD COLUMN landing_mode TEXT;",
                    @"ALTER TABLE voyages ADD COLUMN landing_mode TEXT;"
                )
            };
        }

        #endregion
    }
}
