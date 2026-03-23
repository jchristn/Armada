namespace Armada.Core.Database.SqlServer.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Static class containing all CREATE TABLE and CREATE INDEX DDL statements for the Armada SQL Server schema.
    /// </summary>
    public static class TableQueries
    {
        #region Public-Methods

        /// <summary>
        /// Get all schema migrations for the Armada SQL Server database.
        /// </summary>
        /// <returns>List of schema migrations.</returns>
        public static List<SchemaMigration> GetMigrations()
        {
            List<string> initialStatements = new List<string>
            {
                Tenants,
                Users,
                Credentials,
                Fleets,
                Vessels,
                Captains,
                Voyages,
                Missions,
                Docks,
                Signals,
                Events,
                MergeEntries
            };

            foreach (string index in Indexes)
            {
                initialStatements.Add(index);
            }

            return new List<SchemaMigration>
            {
                new SchemaMigration(
                    1,
                    "Initial schema: tenants, users, credentials, fleets, vessels, captains, voyages, missions, docks, signals, events, merge_entries with full multi-tenant support",
                    initialStatements.ToArray()
                ),
                new SchemaMigration(
                    2,
                    "Protected resources and user ownership",
                    @"
                    IF COL_LENGTH('tenants', 'is_protected') IS NULL
                        ALTER TABLE tenants ADD is_protected BIT NOT NULL CONSTRAINT DF_tenants_is_protected DEFAULT 0;
                    IF COL_LENGTH('users', 'is_protected') IS NULL
                        ALTER TABLE users ADD is_protected BIT NOT NULL CONSTRAINT DF_users_is_protected DEFAULT 0;
                    IF COL_LENGTH('credentials', 'is_protected') IS NULL
                        ALTER TABLE credentials ADD is_protected BIT NOT NULL CONSTRAINT DF_credentials_is_protected DEFAULT 0;",
                    @"UPDATE tenants SET is_protected = 1 WHERE id IN ('default', 'ten_system');",
                    @"UPDATE users SET is_protected = 1 WHERE id IN ('default', 'usr_system');",
                    @"UPDATE credentials SET is_protected = 1 WHERE user_id IN ('default', 'usr_system');",
                    @"
                    IF COL_LENGTH('fleets', 'user_id') IS NULL ALTER TABLE fleets ADD user_id NVARCHAR(450);
                    IF COL_LENGTH('vessels', 'user_id') IS NULL ALTER TABLE vessels ADD user_id NVARCHAR(450);
                    IF COL_LENGTH('captains', 'user_id') IS NULL ALTER TABLE captains ADD user_id NVARCHAR(450);
                    IF COL_LENGTH('voyages', 'user_id') IS NULL ALTER TABLE voyages ADD user_id NVARCHAR(450);
                    IF COL_LENGTH('missions', 'user_id') IS NULL ALTER TABLE missions ADD user_id NVARCHAR(450);
                    IF COL_LENGTH('docks', 'user_id') IS NULL ALTER TABLE docks ADD user_id NVARCHAR(450);
                    IF COL_LENGTH('signals', 'user_id') IS NULL ALTER TABLE signals ADD user_id NVARCHAR(450);
                    IF COL_LENGTH('events', 'user_id') IS NULL ALTER TABLE events ADD user_id NVARCHAR(450);
                    IF COL_LENGTH('merge_entries', 'user_id') IS NULL ALTER TABLE merge_entries ADD user_id NVARCHAR(450);",
                    @"UPDATE fleets SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = fleets.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"UPDATE vessels SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = vessels.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"UPDATE captains SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = captains.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"UPDATE voyages SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = voyages.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"UPDATE missions SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = missions.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"UPDATE docks SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = docks.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"UPDATE signals SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = signals.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"UPDATE events SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = events.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"UPDATE merge_entries SET user_id = COALESCE((SELECT TOP 1 u.id FROM users u WHERE u.tenant_id = merge_entries.tenant_id ORDER BY u.created_utc), 'default') WHERE user_id IS NULL;",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_fleets_user') ALTER TABLE fleets ADD CONSTRAINT FK_fleets_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_vessels_user') ALTER TABLE vessels ADD CONSTRAINT FK_vessels_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_captains_user') ALTER TABLE captains ADD CONSTRAINT FK_captains_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_voyages_user') ALTER TABLE voyages ADD CONSTRAINT FK_voyages_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_missions_user') ALTER TABLE missions ADD CONSTRAINT FK_missions_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_docks_user') ALTER TABLE docks ADD CONSTRAINT FK_docks_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_signals_user') ALTER TABLE signals ADD CONSTRAINT FK_signals_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_events_user') ALTER TABLE events ADD CONSTRAINT FK_events_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_merge_entries_user') ALTER TABLE merge_entries ADD CONSTRAINT FK_merge_entries_user FOREIGN KEY (user_id) REFERENCES users(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleets_user') CREATE INDEX idx_fleets_user ON fleets(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleets_tenant_user') CREATE INDEX idx_fleets_tenant_user ON fleets(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessels_user') CREATE INDEX idx_vessels_user ON vessels(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessels_tenant_user') CREATE INDEX idx_vessels_tenant_user ON vessels(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_captains_user') CREATE INDEX idx_captains_user ON captains(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_captains_tenant_user') CREATE INDEX idx_captains_tenant_user ON captains(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_voyages_user') CREATE INDEX idx_voyages_user ON voyages(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_voyages_tenant_user') CREATE INDEX idx_voyages_tenant_user ON voyages(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_missions_user') CREATE INDEX idx_missions_user ON missions(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_missions_tenant_user') CREATE INDEX idx_missions_tenant_user ON missions(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_docks_user') CREATE INDEX idx_docks_user ON docks(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_docks_tenant_user') CREATE INDEX idx_docks_tenant_user ON docks(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_signals_user') CREATE INDEX idx_signals_user ON signals(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_signals_tenant_user') CREATE INDEX idx_signals_tenant_user ON signals(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_events_user') CREATE INDEX idx_events_user ON events(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_events_tenant_user') CREATE INDEX idx_events_tenant_user ON events(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_merge_entries_user') CREATE INDEX idx_merge_entries_user ON merge_entries(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_merge_entries_tenant_user') CREATE INDEX idx_merge_entries_tenant_user ON merge_entries(tenant_id, user_id);"
                ),
                new SchemaMigration(
                    3,
                    "Operational tenant foreign keys",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_fleets_tenant') ALTER TABLE fleets ADD CONSTRAINT FK_fleets_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_vessels_tenant') ALTER TABLE vessels ADD CONSTRAINT FK_vessels_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_captains_tenant') ALTER TABLE captains ADD CONSTRAINT FK_captains_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_voyages_tenant') ALTER TABLE voyages ADD CONSTRAINT FK_voyages_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_missions_tenant') ALTER TABLE missions ADD CONSTRAINT FK_missions_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_docks_tenant') ALTER TABLE docks ADD CONSTRAINT FK_docks_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_signals_tenant') ALTER TABLE signals ADD CONSTRAINT FK_signals_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_events_tenant') ALTER TABLE events ADD CONSTRAINT FK_events_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_merge_entries_tenant') ALTER TABLE merge_entries ADD CONSTRAINT FK_merge_entries_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);"
                ),
                new SchemaMigration(
                    4,
                    "Add tenant admin role to users",
                    @"
                    IF COL_LENGTH('users', 'is_tenant_admin') IS NULL
                        ALTER TABLE users ADD is_tenant_admin BIT NOT NULL CONSTRAINT DF_users_is_tenant_admin DEFAULT 0;",
                    @"UPDATE users SET is_tenant_admin = 1 WHERE is_admin = 1;"
                ),
                new SchemaMigration(
                    5,
                    "Add enable_model_context and model_context to vessels",
                    @"
                    IF COL_LENGTH('vessels', 'enable_model_context') IS NULL
                        ALTER TABLE vessels ADD enable_model_context BIT NOT NULL CONSTRAINT DF_vessels_enable_model_context DEFAULT 1;",
                    @"
                    IF COL_LENGTH('vessels', 'model_context') IS NULL
                        ALTER TABLE vessels ADD model_context NVARCHAR(MAX);"
                ),
                new SchemaMigration(
                    6,
                    "Add system_instructions to captains",
                    @"
                    IF COL_LENGTH('captains', 'system_instructions') IS NULL
                        ALTER TABLE captains ADD system_instructions NVARCHAR(MAX);"
                )
            };
        }

        #endregion

        #region Table-Definitions

        /// <summary>
        /// SQL Server schema_migrations table DDL.
        /// </summary>
        public static readonly string SchemaMigrations = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'schema_migrations')
            CREATE TABLE schema_migrations (
                version INT PRIMARY KEY,
                description NVARCHAR(450) NOT NULL,
                applied_utc DATETIME2 NOT NULL
            );";

        /// <summary>
        /// Tenants table.
        /// </summary>
        public static readonly string Tenants = @"
            CREATE TABLE tenants (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                name NVARCHAR(450) NOT NULL,
                active BIT NOT NULL DEFAULT 1,
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL
            );";

        /// <summary>
        /// Users table.
        /// </summary>
        public static readonly string Users = @"
            CREATE TABLE users (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450) NOT NULL,
                email NVARCHAR(450) NOT NULL,
                password_sha256 NVARCHAR(450) NOT NULL,
                first_name NVARCHAR(450),
                last_name NVARCHAR(450),
                is_admin BIT NOT NULL DEFAULT 0,
                is_tenant_admin BIT NOT NULL DEFAULT 0,
                active BIT NOT NULL DEFAULT 1,
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL,
                CONSTRAINT FK_users_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
            );";

        /// <summary>
        /// Credentials table.
        /// </summary>
        public static readonly string Credentials = @"
            CREATE TABLE credentials (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450) NOT NULL,
                user_id NVARCHAR(450) NOT NULL,
                name NVARCHAR(450),
                bearer_token NVARCHAR(450) NOT NULL,
                active BIT NOT NULL DEFAULT 1,
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL,
                CONSTRAINT FK_credentials_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                CONSTRAINT FK_credentials_user FOREIGN KEY (user_id) REFERENCES users(id)
            );";

        /// <summary>
        /// Fleets table.
        /// </summary>
        public static readonly string Fleets = @"
            CREATE TABLE fleets (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                name NVARCHAR(450) NOT NULL,
                description NVARCHAR(MAX),
                active BIT NOT NULL DEFAULT 1,
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL
            );";

        /// <summary>
        /// Vessels table.
        /// </summary>
        public static readonly string Vessels = @"
            CREATE TABLE vessels (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                fleet_id NVARCHAR(450),
                name NVARCHAR(450) NOT NULL,
                repo_url NVARCHAR(450),
                local_path NVARCHAR(450),
                working_directory NVARCHAR(450),
                project_context NVARCHAR(MAX),
                style_guide NVARCHAR(MAX),
                enable_model_context BIT NOT NULL DEFAULT 1,
                model_context NVARCHAR(MAX),
                landing_mode NVARCHAR(450),
                branch_cleanup_policy NVARCHAR(450),
                allow_concurrent_missions BIT NOT NULL DEFAULT 0,
                default_branch NVARCHAR(450) NOT NULL DEFAULT 'main',
                active BIT NOT NULL DEFAULT 1,
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL,
                CONSTRAINT FK_vessels_fleet FOREIGN KEY (fleet_id) REFERENCES fleets(id) ON DELETE SET NULL
            );";

        /// <summary>
        /// Captains table.
        /// </summary>
        public static readonly string Captains = @"
            CREATE TABLE captains (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                name NVARCHAR(450) NOT NULL,
                runtime NVARCHAR(450) NOT NULL DEFAULT 'ClaudeCode',
                system_instructions NVARCHAR(MAX),
                state NVARCHAR(450) NOT NULL DEFAULT 'Idle',
                current_mission_id NVARCHAR(450),
                current_dock_id NVARCHAR(450),
                process_id INT,
                recovery_attempts INT NOT NULL DEFAULT 0,
                last_heartbeat_utc NVARCHAR(450),
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL
            );";

        /// <summary>
        /// Voyages table.
        /// </summary>
        public static readonly string Voyages = @"
            CREATE TABLE voyages (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                title NVARCHAR(450) NOT NULL,
                description NVARCHAR(MAX),
                status NVARCHAR(450) NOT NULL DEFAULT 'Open',
                created_utc NVARCHAR(450) NOT NULL,
                completed_utc NVARCHAR(450),
                last_update_utc NVARCHAR(450) NOT NULL,
                auto_push BIT,
                auto_create_pull_requests BIT,
                auto_merge_pull_requests BIT,
                landing_mode NVARCHAR(450)
            );";

        /// <summary>
        /// Missions table.
        /// </summary>
        public static readonly string Missions = @"
            CREATE TABLE missions (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                voyage_id NVARCHAR(450),
                vessel_id NVARCHAR(450),
                captain_id NVARCHAR(450),
                title NVARCHAR(450) NOT NULL,
                description NVARCHAR(MAX),
                status NVARCHAR(450) NOT NULL DEFAULT 'Pending',
                priority INT NOT NULL DEFAULT 100,
                parent_mission_id NVARCHAR(450),
                branch_name NVARCHAR(450),
                dock_id NVARCHAR(450),
                process_id INT,
                pr_url NVARCHAR(450),
                commit_hash NVARCHAR(450),
                diff_snapshot NVARCHAR(MAX),
                created_utc NVARCHAR(450) NOT NULL,
                started_utc NVARCHAR(450),
                completed_utc NVARCHAR(450),
                last_update_utc NVARCHAR(450) NOT NULL,
                CONSTRAINT FK_missions_voyage FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE SET NULL,
                CONSTRAINT FK_missions_vessel FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
                CONSTRAINT FK_missions_captain FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL,
                CONSTRAINT FK_missions_parent FOREIGN KEY (parent_mission_id) REFERENCES missions(id)
            );";

        /// <summary>
        /// Docks table.
        /// </summary>
        public static readonly string Docks = @"
            CREATE TABLE docks (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                vessel_id NVARCHAR(450) NOT NULL,
                captain_id NVARCHAR(450),
                worktree_path NVARCHAR(450),
                branch_name NVARCHAR(450),
                active BIT NOT NULL DEFAULT 1,
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL,
                CONSTRAINT FK_docks_vessel FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE,
                CONSTRAINT FK_docks_captain FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL
            );";

        /// <summary>
        /// Signals table.
        /// </summary>
        public static readonly string Signals = @"
            CREATE TABLE signals (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                from_captain_id NVARCHAR(450),
                to_captain_id NVARCHAR(450),
                type NVARCHAR(450) NOT NULL DEFAULT 'Nudge',
                payload NVARCHAR(MAX),
                [read] BIT NOT NULL DEFAULT 0,
                created_utc NVARCHAR(450) NOT NULL,
                CONSTRAINT FK_signals_from_captain FOREIGN KEY (from_captain_id) REFERENCES captains(id) ON DELETE NO ACTION,
                CONSTRAINT FK_signals_to_captain FOREIGN KEY (to_captain_id) REFERENCES captains(id) ON DELETE NO ACTION
            );";

        /// <summary>
        /// Events table.
        /// </summary>
        public static readonly string Events = @"
            CREATE TABLE events (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                event_type NVARCHAR(450) NOT NULL,
                entity_type NVARCHAR(450),
                entity_id NVARCHAR(450),
                captain_id NVARCHAR(450),
                mission_id NVARCHAR(450),
                vessel_id NVARCHAR(450),
                voyage_id NVARCHAR(450),
                message NVARCHAR(MAX) NOT NULL,
                payload NVARCHAR(MAX),
                created_utc NVARCHAR(450) NOT NULL
            );";

        /// <summary>
        /// Merge entries table.
        /// </summary>
        public static readonly string MergeEntries = @"
            CREATE TABLE merge_entries (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                mission_id NVARCHAR(450),
                vessel_id NVARCHAR(450),
                branch_name NVARCHAR(450) NOT NULL,
                target_branch NVARCHAR(450) NOT NULL DEFAULT 'main',
                status NVARCHAR(450) NOT NULL DEFAULT 'Queued',
                priority INT NOT NULL DEFAULT 0,
                batch_id NVARCHAR(450),
                test_command NVARCHAR(MAX),
                test_output NVARCHAR(MAX),
                test_exit_code INT,
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL,
                test_started_utc NVARCHAR(450),
                completed_utc NVARCHAR(450)
            );";

        #endregion

        #region Indexes

        /// <summary>
        /// All index creation statements.
        /// </summary>
        public static readonly string[] Indexes = new string[]
        {
            // Tenants
            "CREATE INDEX idx_tenants_active ON tenants(active);",

            // Users
            "CREATE UNIQUE INDEX idx_users_tenant_email ON users(tenant_id, email);",
            "CREATE INDEX idx_users_tenant ON users(tenant_id);",
            "CREATE INDEX idx_users_email ON users(email);",

            // Credentials
            "CREATE UNIQUE INDEX idx_credentials_bearer ON credentials(bearer_token);",
            "CREATE INDEX idx_credentials_tenant ON credentials(tenant_id);",
            "CREATE INDEX idx_credentials_user ON credentials(user_id);",
            "CREATE INDEX idx_credentials_tenant_user ON credentials(tenant_id, user_id);",
            "CREATE INDEX idx_credentials_active ON credentials(active);",

            // Fleets
            "CREATE INDEX idx_fleets_tenant ON fleets(tenant_id);",
            "CREATE INDEX idx_fleets_tenant_name ON fleets(tenant_id, name);",
            "CREATE INDEX idx_fleets_created_utc ON fleets(created_utc);",

            // Vessels
            "CREATE INDEX idx_vessels_fleet ON vessels(fleet_id);",
            "CREATE INDEX idx_vessels_tenant ON vessels(tenant_id);",
            "CREATE INDEX idx_vessels_tenant_fleet ON vessels(tenant_id, fleet_id);",
            "CREATE INDEX idx_vessels_tenant_name ON vessels(tenant_id, name);",
            "CREATE INDEX idx_vessels_created_utc ON vessels(created_utc);",

            // Captains
            "CREATE INDEX idx_captains_state ON captains(state);",
            "CREATE INDEX idx_captains_tenant ON captains(tenant_id);",
            "CREATE INDEX idx_captains_tenant_state ON captains(tenant_id, state);",
            "CREATE INDEX idx_captains_created_utc ON captains(created_utc);",

            // Voyages
            "CREATE INDEX idx_voyages_status ON voyages(status);",
            "CREATE INDEX idx_voyages_tenant ON voyages(tenant_id);",
            "CREATE INDEX idx_voyages_tenant_status ON voyages(tenant_id, status);",
            "CREATE INDEX idx_voyages_created_utc ON voyages(created_utc);",

            // Missions
            "CREATE INDEX idx_missions_voyage ON missions(voyage_id);",
            "CREATE INDEX idx_missions_vessel ON missions(vessel_id);",
            "CREATE INDEX idx_missions_captain ON missions(captain_id);",
            "CREATE INDEX idx_missions_status ON missions(status);",
            "CREATE INDEX idx_missions_status_priority ON missions(status, priority ASC, created_utc ASC);",
            "CREATE INDEX idx_missions_vessel_status ON missions(vessel_id, status);",
            "CREATE INDEX idx_missions_tenant ON missions(tenant_id);",
            "CREATE INDEX idx_missions_tenant_status ON missions(tenant_id, status);",
            "CREATE INDEX idx_missions_tenant_vessel ON missions(tenant_id, vessel_id);",
            "CREATE INDEX idx_missions_tenant_voyage ON missions(tenant_id, voyage_id);",
            "CREATE INDEX idx_missions_tenant_captain ON missions(tenant_id, captain_id);",
            "CREATE INDEX idx_missions_tenant_status_priority ON missions(tenant_id, status, priority ASC, created_utc ASC);",

            // Docks
            "CREATE INDEX idx_docks_vessel ON docks(vessel_id);",
            "CREATE INDEX idx_docks_vessel_available ON docks(vessel_id, active, captain_id);",
            "CREATE INDEX idx_docks_tenant ON docks(tenant_id);",
            "CREATE INDEX idx_docks_tenant_vessel ON docks(tenant_id, vessel_id);",
            "CREATE INDEX idx_docks_tenant_vessel_available ON docks(tenant_id, vessel_id, active, captain_id);",
            "CREATE INDEX idx_docks_tenant_captain ON docks(tenant_id, captain_id);",
            "CREATE INDEX idx_docks_created_utc ON docks(created_utc);",

            // Signals
            "CREATE INDEX idx_signals_to_captain ON signals(to_captain_id);",
            "CREATE INDEX idx_signals_to_captain_read ON signals(to_captain_id, [read]);",
            "CREATE INDEX idx_signals_created ON signals(created_utc DESC);",
            "CREATE INDEX idx_signals_tenant ON signals(tenant_id);",
            "CREATE INDEX idx_signals_tenant_to_captain ON signals(tenant_id, to_captain_id);",
            "CREATE INDEX idx_signals_tenant_to_captain_read ON signals(tenant_id, to_captain_id, [read]);",
            "CREATE INDEX idx_signals_tenant_created ON signals(tenant_id, created_utc DESC);",

            // Events
            "CREATE INDEX idx_events_type ON events(event_type);",
            "CREATE INDEX idx_events_captain ON events(captain_id);",
            "CREATE INDEX idx_events_mission ON events(mission_id);",
            "CREATE INDEX idx_events_vessel ON events(vessel_id);",
            "CREATE INDEX idx_events_voyage ON events(voyage_id);",
            "CREATE INDEX idx_events_entity ON events(entity_type, entity_id);",
            "CREATE INDEX idx_events_created ON events(created_utc DESC);",
            "CREATE INDEX idx_events_tenant ON events(tenant_id);",
            "CREATE INDEX idx_events_tenant_type ON events(tenant_id, event_type);",
            "CREATE INDEX idx_events_tenant_entity ON events(tenant_id, entity_type, entity_id);",
            "CREATE INDEX idx_events_tenant_vessel ON events(tenant_id, vessel_id);",
            "CREATE INDEX idx_events_tenant_voyage ON events(tenant_id, voyage_id);",
            "CREATE INDEX idx_events_tenant_captain ON events(tenant_id, captain_id);",
            "CREATE INDEX idx_events_tenant_mission ON events(tenant_id, mission_id);",
            "CREATE INDEX idx_events_tenant_created ON events(tenant_id, created_utc DESC);",

            // Merge entries
            "CREATE INDEX idx_merge_entries_status ON merge_entries(status);",
            "CREATE INDEX idx_merge_entries_status_priority ON merge_entries(status, priority ASC, created_utc ASC);",
            "CREATE INDEX idx_merge_entries_vessel ON merge_entries(vessel_id);",
            "CREATE INDEX idx_merge_entries_mission ON merge_entries(mission_id);",
            "CREATE INDEX idx_merge_entries_completed ON merge_entries(completed_utc);",
            "CREATE INDEX idx_merge_entries_tenant ON merge_entries(tenant_id);",
            "CREATE INDEX idx_merge_entries_tenant_status ON merge_entries(tenant_id, status);",
            "CREATE INDEX idx_merge_entries_tenant_status_priority ON merge_entries(tenant_id, status, priority ASC, created_utc ASC);",
            "CREATE INDEX idx_merge_entries_tenant_vessel ON merge_entries(tenant_id, vessel_id);",
            "CREATE INDEX idx_merge_entries_tenant_mission ON merge_entries(tenant_id, mission_id);"
        };

        #endregion
    }
}
