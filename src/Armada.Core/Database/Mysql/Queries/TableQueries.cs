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
            enable_model_context TINYINT(1) NOT NULL DEFAULT 1,
            model_context LONGTEXT,
            github_token_override LONGTEXT,
            landing_mode TEXT,
            branch_cleanup_policy TEXT,
            allow_concurrent_missions TINYINT(1) NOT NULL DEFAULT 0,
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
            system_instructions LONGTEXT,
            runtime_options_json LONGTEXT,
            state VARCHAR(450) NOT NULL DEFAULT 'Idle',
            current_mission_id VARCHAR(450),
            current_dock_id VARCHAR(450),
            process_id INT,
            recovery_attempts INT NOT NULL DEFAULT 0,
            last_heartbeat_utc DATETIME(6),
            last_process_alive_utc DATETIME(6) NULL,
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
            landing_mode TEXT,
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
            agent_output LONGTEXT,
            review_deadline_utc DATETIME(6) NULL,
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
            state VARCHAR(32) NOT NULL DEFAULT 'Available',
            lease_expires_utc DATETIME(6) NULL,
            owner_token TEXT NULL,
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
            retry_count INT NOT NULL DEFAULT 0,
            lease_expires_utc DATETIME(6) NULL,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            test_started_utc DATETIME(6),
            completed_utc DATETIME(6)
        );";

        /// <summary>
        /// DDL for the tenants table.
        /// </summary>
        public static readonly string Tenants = @"CREATE TABLE IF NOT EXISTS tenants (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            name VARCHAR(450) NOT NULL,
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL
        );";

        /// <summary>
        /// DDL for the users table.
        /// </summary>
        public static readonly string Users = @"CREATE TABLE IF NOT EXISTS users (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            tenant_id VARCHAR(450) NOT NULL,
            email VARCHAR(450) NOT NULL,
            password_sha256 VARCHAR(450) NOT NULL,
            first_name VARCHAR(450),
            last_name VARCHAR(450),
            is_admin TINYINT(1) NOT NULL DEFAULT 0,
            is_tenant_admin TINYINT(1) NOT NULL DEFAULT 0,
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
        );";

        /// <summary>
        /// DDL for the credentials table.
        /// </summary>
        public static readonly string Credentials = @"CREATE TABLE IF NOT EXISTS credentials (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            tenant_id VARCHAR(450) NOT NULL,
            user_id VARCHAR(450) NOT NULL,
            name VARCHAR(450),
            bearer_token VARCHAR(450) NOT NULL UNIQUE,
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
            FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
        );";

        /// <summary>
        /// Migration v3 statements for multi-tenant support: ALTER TABLE, backfill, seed, and indexes.
        /// </summary>
        public static readonly string[] MigrationV3Statements = new string[]
        {
            // New tables
            Tenants,
            Users,
            @"CREATE UNIQUE INDEX idx_users_tenant_email ON users(tenant_id(191), email(191));",
            @"CREATE INDEX idx_users_tenant ON users(tenant_id);",
            @"CREATE INDEX idx_users_email ON users(email);",
            Credentials,
            @"CREATE INDEX idx_credentials_tenant ON credentials(tenant_id);",
            @"CREATE INDEX idx_credentials_user ON credentials(user_id);",
            @"CREATE INDEX idx_credentials_bearer ON credentials(bearer_token);",
            // Seed default tenant so FK constraints are satisfied for backfill
            @"INSERT IGNORE INTO tenants (id, name, active, created_utc, last_update_utc)
              VALUES ('default', 'Default Tenant', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));",
            // Add tenant_id to existing tables
            @"ALTER TABLE fleets ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            @"ALTER TABLE voyages ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            @"ALTER TABLE docks ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            @"ALTER TABLE signals ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            @"ALTER TABLE events ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            @"ALTER TABLE merge_entries ADD COLUMN IF NOT EXISTS tenant_id VARCHAR(450);",
            // Backfill existing rows with default tenant
            @"UPDATE fleets SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            @"UPDATE vessels SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            @"UPDATE captains SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            @"UPDATE voyages SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            @"UPDATE missions SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            @"UPDATE docks SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            @"UPDATE signals SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            @"UPDATE events SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            @"UPDATE merge_entries SET tenant_id = 'default' WHERE tenant_id IS NULL;",
            // Indexes on new tables
            @"CREATE INDEX idx_tenants_active ON tenants(active);",
            @"CREATE INDEX idx_credentials_tenant_user ON credentials(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_credentials_active ON credentials(active);",
            // Tenant indexes on operational tables
            @"CREATE INDEX idx_fleets_tenant ON fleets(tenant_id);",
            @"CREATE INDEX idx_fleets_tenant_name ON fleets(tenant_id(191), name(191));",
            @"CREATE INDEX idx_fleets_created_utc ON fleets(created_utc);",
            @"CREATE INDEX idx_vessels_tenant ON vessels(tenant_id);",
            @"CREATE INDEX idx_vessels_tenant_fleet ON vessels(tenant_id(191), fleet_id(191));",
            @"CREATE INDEX idx_vessels_tenant_name ON vessels(tenant_id(191), name(191));",
            @"CREATE INDEX idx_vessels_created_utc ON vessels(created_utc);",
            @"CREATE INDEX idx_captains_tenant ON captains(tenant_id);",
            @"CREATE INDEX idx_captains_tenant_state ON captains(tenant_id(191), state(191));",
            @"CREATE INDEX idx_captains_created_utc ON captains(created_utc);",
            @"CREATE INDEX idx_missions_tenant ON missions(tenant_id);",
            @"CREATE INDEX idx_missions_tenant_status ON missions(tenant_id(191), status(191));",
            @"CREATE INDEX idx_missions_tenant_vessel ON missions(tenant_id(191), vessel_id(191));",
            @"CREATE INDEX idx_missions_tenant_voyage ON missions(tenant_id(191), voyage_id(191));",
            @"CREATE INDEX idx_missions_tenant_captain ON missions(tenant_id(191), captain_id(191));",
            @"CREATE INDEX idx_missions_tenant_status_priority ON missions(tenant_id(191), status(191), priority ASC, created_utc ASC);",
            @"CREATE INDEX idx_voyages_tenant ON voyages(tenant_id);",
            @"CREATE INDEX idx_voyages_tenant_status ON voyages(tenant_id(191), status(191));",
            @"CREATE INDEX idx_voyages_created_utc ON voyages(created_utc);",
            @"CREATE INDEX idx_docks_tenant ON docks(tenant_id);",
            @"CREATE INDEX idx_docks_tenant_vessel ON docks(tenant_id(191), vessel_id(191));",
            @"CREATE INDEX idx_docks_tenant_vessel_available ON docks(tenant_id(191), vessel_id(191), active, captain_id(191));",
            @"CREATE INDEX idx_docks_tenant_captain ON docks(tenant_id(191), captain_id(191));",
            @"CREATE INDEX idx_docks_created_utc ON docks(created_utc);",
            @"CREATE INDEX idx_signals_tenant ON signals(tenant_id);",
            @"CREATE INDEX idx_signals_tenant_to_captain ON signals(tenant_id(191), to_captain_id(191));",
            @"CREATE INDEX idx_signals_tenant_to_captain_read ON signals(tenant_id(191), to_captain_id(191), `read`);",
            @"CREATE INDEX idx_signals_tenant_created ON signals(tenant_id, created_utc DESC);",
            @"CREATE INDEX idx_events_tenant ON events(tenant_id);",
            @"CREATE INDEX idx_events_tenant_type ON events(tenant_id(191), event_type(191));",
            @"CREATE INDEX idx_events_tenant_entity ON events(tenant_id(191), entity_type(191), entity_id(191));",
            @"CREATE INDEX idx_events_tenant_vessel ON events(tenant_id(191), vessel_id(191));",
            @"CREATE INDEX idx_events_tenant_voyage ON events(tenant_id(191), voyage_id(191));",
            @"CREATE INDEX idx_events_tenant_captain ON events(tenant_id(191), captain_id(191));",
            @"CREATE INDEX idx_events_tenant_mission ON events(tenant_id(191), mission_id(191));",
            @"CREATE INDEX idx_events_tenant_created ON events(tenant_id, created_utc DESC);",
            @"CREATE INDEX idx_merge_entries_tenant ON merge_entries(tenant_id);",
            @"CREATE INDEX idx_merge_entries_tenant_status ON merge_entries(tenant_id(191), status(191));",
            @"CREATE INDEX idx_merge_entries_tenant_status_priority ON merge_entries(tenant_id(191), status(191), priority ASC, created_utc ASC);",
            @"CREATE INDEX idx_merge_entries_tenant_vessel ON merge_entries(tenant_id(191), vessel_id(191));",
            @"CREATE INDEX idx_merge_entries_tenant_mission ON merge_entries(tenant_id(191), mission_id(191));"
        };

        /// <summary>
        /// Migration v4 statements for protected resources and user ownership.
        /// </summary>
        public static readonly string[] MigrationV4Statements = new string[]
        {
            @"ALTER TABLE tenants ADD COLUMN IF NOT EXISTS is_protected TINYINT(1) NOT NULL DEFAULT 0;",
            @"ALTER TABLE users ADD COLUMN IF NOT EXISTS is_protected TINYINT(1) NOT NULL DEFAULT 0;",
            @"ALTER TABLE credentials ADD COLUMN IF NOT EXISTS is_protected TINYINT(1) NOT NULL DEFAULT 0;",
            @"UPDATE tenants SET is_protected = 1 WHERE id IN ('default', 'ten_system');",
            @"UPDATE users SET is_protected = 1 WHERE id IN ('default', 'usr_system');",
            @"UPDATE credentials SET is_protected = 1 WHERE user_id IN ('default', 'usr_system');",
            @"ALTER TABLE fleets ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"ALTER TABLE voyages ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"ALTER TABLE docks ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"ALTER TABLE signals ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"ALTER TABLE events ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"ALTER TABLE merge_entries ADD COLUMN IF NOT EXISTS user_id VARCHAR(450);",
            @"UPDATE fleets f SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = f.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"UPDATE vessels v SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = v.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"UPDATE captains c SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = c.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"UPDATE voyages v SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = v.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"UPDATE missions m SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = m.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"UPDATE docks d SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = d.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"UPDATE signals s SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = s.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"UPDATE events e SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = e.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"UPDATE merge_entries m SET user_id = COALESCE((SELECT u.id FROM users u WHERE u.tenant_id = m.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;",
            @"ALTER TABLE fleets ADD CONSTRAINT fk_fleets_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"ALTER TABLE vessels ADD CONSTRAINT fk_vessels_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"ALTER TABLE captains ADD CONSTRAINT fk_captains_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"ALTER TABLE voyages ADD CONSTRAINT fk_voyages_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"ALTER TABLE missions ADD CONSTRAINT fk_missions_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"ALTER TABLE docks ADD CONSTRAINT fk_docks_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"ALTER TABLE signals ADD CONSTRAINT fk_signals_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"ALTER TABLE events ADD CONSTRAINT fk_events_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"ALTER TABLE merge_entries ADD CONSTRAINT fk_merge_entries_user FOREIGN KEY (user_id) REFERENCES users(id);",
            @"CREATE INDEX idx_fleets_user ON fleets(user_id);",
            @"CREATE INDEX idx_fleets_tenant_user ON fleets(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_vessels_user ON vessels(user_id);",
            @"CREATE INDEX idx_vessels_tenant_user ON vessels(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_captains_user ON captains(user_id);",
            @"CREATE INDEX idx_captains_tenant_user ON captains(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_voyages_user ON voyages(user_id);",
            @"CREATE INDEX idx_voyages_tenant_user ON voyages(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_missions_user ON missions(user_id);",
            @"CREATE INDEX idx_missions_tenant_user ON missions(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_docks_user ON docks(user_id);",
            @"CREATE INDEX idx_docks_tenant_user ON docks(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_signals_user ON signals(user_id);",
            @"CREATE INDEX idx_signals_tenant_user ON signals(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_events_user ON events(user_id);",
            @"CREATE INDEX idx_events_tenant_user ON events(tenant_id(191), user_id(191));",
            @"CREATE INDEX idx_merge_entries_user ON merge_entries(user_id);",
            @"CREATE INDEX idx_merge_entries_tenant_user ON merge_entries(tenant_id(191), user_id(191));"
        };

        /// <summary>
        /// Migration v5 statements for operational tenant foreign keys.
        /// </summary>
        public static readonly string[] MigrationV5Statements = new string[]
        {
            @"ALTER TABLE fleets ADD CONSTRAINT fk_fleets_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
            @"ALTER TABLE vessels ADD CONSTRAINT fk_vessels_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
            @"ALTER TABLE captains ADD CONSTRAINT fk_captains_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
            @"ALTER TABLE voyages ADD CONSTRAINT fk_voyages_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
            @"ALTER TABLE missions ADD CONSTRAINT fk_missions_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
            @"ALTER TABLE docks ADD CONSTRAINT fk_docks_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
            @"ALTER TABLE signals ADD CONSTRAINT fk_signals_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
            @"ALTER TABLE events ADD CONSTRAINT fk_events_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);",
            @"ALTER TABLE merge_entries ADD CONSTRAINT fk_merge_entries_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);"
        };

        /// <summary>
        /// Migration v6 statements for tenant-scoped admin role support.
        /// </summary>
        public static readonly string[] MigrationV6Statements = new string[]
        {
            @"ALTER TABLE users ADD COLUMN IF NOT EXISTS is_tenant_admin TINYINT(1) NOT NULL DEFAULT 0;",
            @"UPDATE users SET is_tenant_admin = 1 WHERE is_admin = 1;"
        };

        /// <summary>
        /// Migration v7 statements for adding model context fields to vessels.
        /// </summary>
        public static readonly string[] MigrationV7Statements = new string[]
        {
            @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS enable_model_context TINYINT(1) NOT NULL DEFAULT 1;",
            @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS model_context LONGTEXT;"
        };

        /// <summary>
        /// Migration v8 statements for adding system_instructions to captains.
        /// </summary>
        public static readonly string[] MigrationV8Statements = new string[]
        {
            @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS system_instructions LONGTEXT;"
        };

        /// <summary>
        /// DDL for the prompt_templates table.
        /// </summary>
        public static readonly string PromptTemplates = @"CREATE TABLE IF NOT EXISTS prompt_templates (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            tenant_id VARCHAR(450),
            name VARCHAR(450) NOT NULL,
            description LONGTEXT,
            category VARCHAR(450) NOT NULL DEFAULT 'mission',
            content LONGTEXT NOT NULL,
            is_built_in TINYINT(1) NOT NULL DEFAULT 0,
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (tenant_id) REFERENCES tenants(id)
        );";

        /// <summary>
        /// DDL for the personas table.
        /// </summary>
        public static readonly string Personas = @"CREATE TABLE IF NOT EXISTS personas (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            tenant_id VARCHAR(450),
            name VARCHAR(450) NOT NULL,
            description LONGTEXT,
            prompt_template_name VARCHAR(450) NOT NULL,
            is_built_in TINYINT(1) NOT NULL DEFAULT 0,
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (tenant_id) REFERENCES tenants(id)
        );";

        /// <summary>
        /// DDL for the pipelines table.
        /// </summary>
        public static readonly string Pipelines = @"CREATE TABLE IF NOT EXISTS pipelines (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            tenant_id VARCHAR(450),
            name VARCHAR(450) NOT NULL,
            description LONGTEXT,
            is_built_in TINYINT(1) NOT NULL DEFAULT 0,
            active TINYINT(1) NOT NULL DEFAULT 1,
            created_utc DATETIME(6) NOT NULL,
            last_update_utc DATETIME(6) NOT NULL,
            FOREIGN KEY (tenant_id) REFERENCES tenants(id)
        );";

        /// <summary>
        /// DDL for the pipeline_stages table.
        /// </summary>
        public static readonly string PipelineStages = @"CREATE TABLE IF NOT EXISTS pipeline_stages (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            pipeline_id VARCHAR(450) NOT NULL,
            stage_order INT NOT NULL,
            persona_name VARCHAR(450) NOT NULL,
            is_optional TINYINT(1) NOT NULL DEFAULT 0,
            description LONGTEXT,
            FOREIGN KEY (pipeline_id) REFERENCES pipelines(id) ON DELETE CASCADE
        );";

        /// <summary>
        /// Migration v9 statements for adding the prompt_templates table.
        /// </summary>
        public static readonly string[] MigrationV9Statements = new string[]
        {
            PromptTemplates,
            @"CREATE UNIQUE INDEX idx_prompt_templates_tenant_name ON prompt_templates(tenant_id(191), name(191));",
            @"CREATE INDEX idx_prompt_templates_category ON prompt_templates(category);",
            @"CREATE INDEX idx_prompt_templates_active ON prompt_templates(active);"
        };

        /// <summary>
        /// Migration v10 statements for adding the personas table.
        /// </summary>
        public static readonly string[] MigrationV10Statements = new string[]
        {
            Personas,
            @"CREATE UNIQUE INDEX idx_personas_tenant_name ON personas(tenant_id(191), name(191));",
            @"CREATE INDEX idx_personas_active ON personas(active);",
            @"CREATE INDEX idx_personas_prompt_template ON personas(prompt_template_name);"
        };

        /// <summary>
        /// Migration v11 statements for adding captain persona fields.
        /// </summary>
        public static readonly string[] MigrationV11Statements = new string[]
        {
            @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS allowed_personas LONGTEXT;",
            @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS preferred_persona VARCHAR(450);",
            @"CREATE INDEX idx_captains_preferred_persona ON captains(preferred_persona);"
        };

        /// <summary>
        /// Migration v12 statements for adding mission persona and dependency fields.
        /// </summary>
        public static readonly string[] MigrationV12Statements = new string[]
        {
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS persona VARCHAR(450);",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS depends_on_mission_id VARCHAR(450);",
            @"CREATE INDEX idx_missions_persona ON missions(persona);",
            @"CREATE INDEX idx_missions_depends_on ON missions(depends_on_mission_id);"
        };

        /// <summary>
        /// Migration v13 statements for adding pipelines and pipeline_stages tables.
        /// </summary>
        public static readonly string[] MigrationV13Statements = new string[]
        {
            Pipelines,
            PipelineStages,
            @"CREATE UNIQUE INDEX idx_pipelines_tenant_name ON pipelines(tenant_id(191), name(191));",
            @"CREATE INDEX idx_pipelines_active ON pipelines(active);",
            @"CREATE INDEX idx_pipeline_stages_pipeline ON pipeline_stages(pipeline_id);",
            @"CREATE UNIQUE INDEX idx_pipeline_stages_order ON pipeline_stages(pipeline_id, stage_order);",
            @"CREATE INDEX idx_pipeline_stages_persona ON pipeline_stages(persona_name);",
            @"ALTER TABLE fleets ADD COLUMN IF NOT EXISTS default_pipeline_id VARCHAR(450);",
            @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS default_pipeline_id VARCHAR(450);",
            @"CREATE INDEX idx_fleets_default_pipeline ON fleets(default_pipeline_id);",
            @"CREATE INDEX idx_vessels_default_pipeline ON vessels(default_pipeline_id);"
        };

        /// <summary>
        /// Migration v14 statements for adding failure_reason to missions.
        /// </summary>
        public static readonly string[] MigrationV14Statements = new string[]
        {
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS failure_reason LONGTEXT;"
        };

        /// <summary>
        /// Migration v15 statements for adding agent_output to missions.
        /// </summary>
        public static readonly string[] MigrationV15Statements = new string[]
        {
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS agent_output LONGTEXT;"
        };

        /// <summary>
        /// Migration v26 statements for adding model to captains.
        /// </summary>
        public static readonly string[] MigrationV26Statements = new string[]
        {
            @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS model TEXT NULL;"
        };

        /// <summary>
        /// Migration v27 statements for adding total_runtime_ms to missions.
        /// </summary>
        public static readonly string[] MigrationV27Statements = new string[]
        {
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS total_runtime_ms BIGINT NULL;"
        };

        /// <summary>
        /// Migration v28 statements for adding playbooks and mission/voyage associations.
        /// </summary>
        public static readonly string[] MigrationV28Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS playbooks (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                file_name VARCHAR(450) NOT NULL,
                description LONGTEXT,
                content LONGTEXT NOT NULL,
                active TINYINT(1) NOT NULL DEFAULT 1,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
            );",
            @"CREATE UNIQUE INDEX idx_playbooks_tenant_file_name ON playbooks(tenant_id(191), file_name(191));",
            @"CREATE INDEX idx_playbooks_tenant ON playbooks(tenant_id);",
            @"CREATE INDEX idx_playbooks_user ON playbooks(user_id);",
            @"CREATE INDEX idx_playbooks_active ON playbooks(active);",
            @"CREATE TABLE IF NOT EXISTS voyage_playbooks (
                voyage_id VARCHAR(450) NOT NULL,
                playbook_id VARCHAR(450) NOT NULL,
                selection_order INT NOT NULL,
                delivery_mode VARCHAR(450) NOT NULL,
                PRIMARY KEY (voyage_id, selection_order),
                FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE CASCADE,
                FOREIGN KEY (playbook_id) REFERENCES playbooks(id) ON DELETE CASCADE
            );",
            @"CREATE INDEX idx_voyage_playbooks_playbook ON voyage_playbooks(playbook_id);",
            @"CREATE TABLE IF NOT EXISTS mission_playbook_snapshots (
                mission_id VARCHAR(450) NOT NULL,
                selection_order INT NOT NULL,
                playbook_id VARCHAR(450),
                file_name VARCHAR(450) NOT NULL,
                description LONGTEXT,
                content LONGTEXT NOT NULL,
                delivery_mode VARCHAR(450) NOT NULL,
                resolved_path LONGTEXT,
                worktree_relative_path LONGTEXT,
                source_last_update_utc DATETIME(6),
                PRIMARY KEY (mission_id, selection_order),
                FOREIGN KEY (mission_id) REFERENCES missions(id) ON DELETE CASCADE,
                FOREIGN KEY (playbook_id) REFERENCES playbooks(id) ON DELETE SET NULL
            );",
            @"CREATE INDEX idx_mission_playbook_snapshots_playbook ON mission_playbook_snapshots(playbook_id);"
        };

        /// <summary>
        /// Migration v29 statements for adding runtime options to captains.
        /// </summary>
        public static readonly string[] MigrationV29Statements = new string[]
        {
            @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS runtime_options_json LONGTEXT NULL;"
        };

        /// <summary>
        /// Migration v30 statements for adding request history tables.
        /// </summary>
        public static readonly string[] MigrationV30Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS request_history (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                credential_id VARCHAR(450),
                principal_display LONGTEXT,
                auth_method VARCHAR(450),
                method VARCHAR(32) NOT NULL,
                route LONGTEXT NOT NULL,
                route_template LONGTEXT,
                query_string LONGTEXT,
                status_code INT NOT NULL,
                duration_ms DOUBLE NOT NULL,
                request_size_bytes BIGINT NOT NULL DEFAULT 0,
                response_size_bytes BIGINT NOT NULL DEFAULT 0,
                request_content_type VARCHAR(450),
                response_content_type VARCHAR(450),
                is_success TINYINT(1) NOT NULL DEFAULT 1,
                client_ip VARCHAR(450),
                correlation_id VARCHAR(450),
                created_utc DATETIME(6) NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS request_history_detail (
                request_history_id VARCHAR(450) NOT NULL PRIMARY KEY,
                path_params_json LONGTEXT,
                query_params_json LONGTEXT,
                request_headers_json LONGTEXT,
                response_headers_json LONGTEXT,
                request_body_text LONGTEXT,
                response_body_text LONGTEXT,
                request_body_truncated TINYINT(1) NOT NULL DEFAULT 0,
                response_body_truncated TINYINT(1) NOT NULL DEFAULT 0,
                FOREIGN KEY (request_history_id) REFERENCES request_history(id) ON DELETE CASCADE
            );",
            @"CREATE INDEX idx_request_history_created ON request_history(created_utc DESC);",
            @"CREATE INDEX idx_request_history_tenant_created ON request_history(tenant_id, created_utc DESC);",
            @"CREATE INDEX idx_request_history_user_created ON request_history(user_id, created_utc DESC);",
            @"CREATE INDEX idx_request_history_credential_created ON request_history(credential_id, created_utc DESC);",
            @"CREATE INDEX idx_request_history_method_created ON request_history(method, created_utc DESC);",
            @"CREATE INDEX idx_request_history_status_created ON request_history(status_code, created_utc DESC);",
            @"CREATE INDEX idx_request_history_success_created ON request_history(is_success, created_utc DESC);",
            @"CREATE INDEX idx_request_history_route_created ON request_history(route(255), created_utc DESC);"
        };

        /// <summary>
        /// Migration v31 statements for adding pipeline review gates.
        /// </summary>
        public static readonly string[] MigrationV31Statements = new string[]
        {
            @"ALTER TABLE pipeline_stages ADD COLUMN IF NOT EXISTS requires_review TINYINT(1) NOT NULL DEFAULT 0;",
            @"ALTER TABLE pipeline_stages ADD COLUMN IF NOT EXISTS review_deny_action VARCHAR(64) NOT NULL DEFAULT 'RetryStage';",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS requires_review TINYINT(1) NOT NULL DEFAULT 0;",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS review_deny_action VARCHAR(64) NOT NULL DEFAULT 'RetryStage';",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS review_comment LONGTEXT NULL;",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS reviewed_by_user_id VARCHAR(450) NULL;",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS review_requested_utc DATETIME(6) NULL;",
            @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS reviewed_utc DATETIME(6) NULL;",
            @"CREATE INDEX idx_missions_requires_review ON missions(requires_review);"
        };

        /// <summary>
        /// Migration v32 statements for adding workflow profiles.
        /// </summary>
        public static readonly string[] MigrationV32Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS workflow_profiles (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                name VARCHAR(450) NOT NULL,
                description LONGTEXT,
                scope VARCHAR(64) NOT NULL DEFAULT 'Global',
                fleet_id VARCHAR(450),
                vessel_id VARCHAR(450),
                is_default TINYINT(1) NOT NULL DEFAULT 0,
                active TINYINT(1) NOT NULL DEFAULT 1,
                language_hints_json LONGTEXT,
                lint_command LONGTEXT,
                build_command LONGTEXT,
                unit_test_command LONGTEXT,
                integration_test_command LONGTEXT,
                e2e_test_command LONGTEXT,
                package_command LONGTEXT,
                publish_artifact_command LONGTEXT,
                release_versioning_command LONGTEXT,
                changelog_generation_command LONGTEXT,
                required_secrets_json LONGTEXT,
                expected_artifacts_json LONGTEXT,
                environments_json LONGTEXT,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (fleet_id) REFERENCES fleets(id) ON DELETE SET NULL,
                FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL
            );",
            @"CREATE INDEX idx_workflow_profiles_tenant ON workflow_profiles(tenant_id);",
            @"CREATE INDEX idx_workflow_profiles_user ON workflow_profiles(user_id);",
            @"CREATE INDEX idx_workflow_profiles_scope ON workflow_profiles(scope);",
            @"CREATE INDEX idx_workflow_profiles_fleet ON workflow_profiles(fleet_id);",
            @"CREATE INDEX idx_workflow_profiles_vessel ON workflow_profiles(vessel_id);",
            @"CREATE INDEX idx_workflow_profiles_default_scope ON workflow_profiles(scope, is_default, active);"
        };

        /// <summary>
        /// Migration v33 statements for adding check runs.
        /// </summary>
        public static readonly string[] MigrationV33Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS check_runs (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                workflow_profile_id VARCHAR(450),
                vessel_id VARCHAR(450),
                mission_id VARCHAR(450),
                voyage_id VARCHAR(450),
                label VARCHAR(450),
                check_type VARCHAR(64) NOT NULL,
                status VARCHAR(64) NOT NULL DEFAULT 'Pending',
                environment_name VARCHAR(450),
                command LONGTEXT NOT NULL,
                working_directory LONGTEXT,
                branch_name VARCHAR(450),
                commit_hash VARCHAR(450),
                exit_code INT NULL,
                output LONGTEXT,
                summary LONGTEXT,
                artifacts_json LONGTEXT,
                duration_ms BIGINT NULL,
                started_utc DATETIME(6) NULL,
                completed_utc DATETIME(6) NULL,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (workflow_profile_id) REFERENCES workflow_profiles(id) ON DELETE SET NULL,
                FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
                FOREIGN KEY (mission_id) REFERENCES missions(id) ON DELETE SET NULL,
                FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE SET NULL
            );",
            @"CREATE INDEX idx_check_runs_tenant_created ON check_runs(tenant_id, created_utc DESC);",
            @"CREATE INDEX idx_check_runs_user_created ON check_runs(user_id, created_utc DESC);",
            @"CREATE INDEX idx_check_runs_vessel_created ON check_runs(vessel_id, created_utc DESC);",
            @"CREATE INDEX idx_check_runs_mission_created ON check_runs(mission_id, created_utc DESC);",
            @"CREATE INDEX idx_check_runs_voyage_created ON check_runs(voyage_id, created_utc DESC);",
            @"CREATE INDEX idx_check_runs_profile_created ON check_runs(workflow_profile_id, created_utc DESC);",
            @"CREATE INDEX idx_check_runs_type_created ON check_runs(check_type, created_utc DESC);",
            @"CREATE INDEX idx_check_runs_status_created ON check_runs(status, created_utc DESC);"
        };

        /// <summary>
        /// Migration v34 statements for adding structured parsing summaries to check runs.
        /// </summary>
        public static readonly string[] MigrationV34Statements = new string[]
        {
            @"ALTER TABLE check_runs
                ADD COLUMN IF NOT EXISTS test_summary_json LONGTEXT NULL,
                ADD COLUMN IF NOT EXISTS coverage_summary_json LONGTEXT NULL;"
        };

        /// <summary>
        /// Migration v35 statements for workflow check expansion and landing readiness fields.
        /// </summary>
        public static readonly string[] MigrationV35Statements = new string[]
        {
            @"ALTER TABLE workflow_profiles
                ADD COLUMN IF NOT EXISTS migration_command LONGTEXT NULL,
                ADD COLUMN IF NOT EXISTS security_scan_command LONGTEXT NULL,
                ADD COLUMN IF NOT EXISTS performance_command LONGTEXT NULL,
                ADD COLUMN IF NOT EXISTS deployment_verification_command LONGTEXT NULL,
                ADD COLUMN IF NOT EXISTS rollback_verification_command LONGTEXT NULL;",
            @"ALTER TABLE vessels
                ADD COLUMN IF NOT EXISTS require_passing_checks_to_land TINYINT(1) NOT NULL DEFAULT 0;"
        };

        /// <summary>
        /// Migration v36 statements for external check metadata and landing branch policy fields.
        /// </summary>
        public static readonly string[] MigrationV36Statements = new string[]
        {
            @"ALTER TABLE check_runs
                ADD COLUMN IF NOT EXISTS source VARCHAR(64) NOT NULL DEFAULT 'Armada',
                ADD COLUMN IF NOT EXISTS provider_name VARCHAR(450) NULL,
                ADD COLUMN IF NOT EXISTS external_id VARCHAR(450) NULL,
                ADD COLUMN IF NOT EXISTS external_url LONGTEXT NULL;",
            @"ALTER TABLE vessels
                ADD COLUMN IF NOT EXISTS protected_branch_patterns_json LONGTEXT NULL,
                ADD COLUMN IF NOT EXISTS release_branch_prefix VARCHAR(450) NOT NULL DEFAULT 'release/',
                ADD COLUMN IF NOT EXISTS hotfix_branch_prefix VARCHAR(450) NOT NULL DEFAULT 'hotfix/',
                ADD COLUMN IF NOT EXISTS require_pull_request_for_protected_branches TINYINT(1) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS require_merge_queue_for_release_branches TINYINT(1) NOT NULL DEFAULT 0;"
        };

        /// <summary>
        /// Migration v37 statements for adding releases.
        /// </summary>
        public static readonly string[] MigrationV37Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS releases (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                vessel_id VARCHAR(450),
                workflow_profile_id VARCHAR(450),
                title VARCHAR(450) NOT NULL,
                version VARCHAR(450) NULL,
                tag_name VARCHAR(450) NULL,
                summary LONGTEXT,
                notes LONGTEXT,
                status VARCHAR(64) NOT NULL DEFAULT 'Draft',
                voyage_ids_json LONGTEXT,
                mission_ids_json LONGTEXT,
                check_run_ids_json LONGTEXT,
                artifacts_json LONGTEXT,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                published_utc DATETIME(6) NULL,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
                FOREIGN KEY (workflow_profile_id) REFERENCES workflow_profiles(id) ON DELETE SET NULL
            );",
            @"CREATE INDEX idx_releases_tenant_created ON releases(tenant_id, created_utc DESC);",
            @"CREATE INDEX idx_releases_user_created ON releases(user_id, created_utc DESC);",
            @"CREATE INDEX idx_releases_vessel_created ON releases(vessel_id, created_utc DESC);",
            @"CREATE INDEX idx_releases_profile_created ON releases(workflow_profile_id, created_utc DESC);",
            @"CREATE INDEX idx_releases_status_created ON releases(status, created_utc DESC);",
            @"CREATE INDEX idx_releases_published ON releases(published_utc DESC);"
        };

        /// <summary>
        /// Migration v38 statements for adding deployment environments.
        /// </summary>
        public static readonly string[] MigrationV38Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS environments (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                vessel_id VARCHAR(450),
                name VARCHAR(450) NOT NULL,
                description LONGTEXT,
                kind VARCHAR(64) NOT NULL DEFAULT 'Development',
                configuration_source LONGTEXT,
                base_url LONGTEXT,
                health_endpoint LONGTEXT,
                access_notes LONGTEXT,
                deployment_rules LONGTEXT,
                requires_approval TINYINT(1) NOT NULL DEFAULT 0,
                is_default TINYINT(1) NOT NULL DEFAULT 0,
                active TINYINT(1) NOT NULL DEFAULT 1,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE
            );",
            @"CREATE INDEX idx_environments_tenant_created ON environments(tenant_id, created_utc DESC);",
            @"CREATE INDEX idx_environments_user_created ON environments(user_id, created_utc DESC);",
            @"CREATE INDEX idx_environments_vessel_name ON environments(vessel_id(191), name(191));",
            @"CREATE INDEX idx_environments_kind ON environments(kind);",
            @"CREATE INDEX idx_environments_default ON environments(is_default);",
            @"CREATE INDEX idx_environments_active ON environments(active);"
        };

        /// <summary>
        /// Migration v39 statements for adding deployments.
        /// </summary>
        public static readonly string[] MigrationV39Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS deployments (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                vessel_id VARCHAR(450),
                workflow_profile_id VARCHAR(450),
                environment_id VARCHAR(450),
                environment_name VARCHAR(450),
                release_id VARCHAR(450),
                mission_id VARCHAR(450),
                voyage_id VARCHAR(450),
                title VARCHAR(450) NOT NULL,
                source_ref LONGTEXT,
                summary LONGTEXT,
                notes LONGTEXT,
                status VARCHAR(64) NOT NULL DEFAULT 'PendingApproval',
                verification_status VARCHAR(64) NOT NULL DEFAULT 'NotRun',
                approval_required TINYINT(1) NOT NULL DEFAULT 0,
                approved_by_user_id VARCHAR(450),
                approved_utc DATETIME(6),
                approval_comment LONGTEXT,
                deploy_check_run_id VARCHAR(450),
                smoke_test_check_run_id VARCHAR(450),
                health_check_run_id VARCHAR(450),
                deployment_verification_check_run_id VARCHAR(450),
                rollback_check_run_id VARCHAR(450),
                rollback_verification_check_run_id VARCHAR(450),
                check_run_ids_json LONGTEXT,
                request_history_summary_json LONGTEXT,
                created_utc DATETIME(6) NOT NULL,
                started_utc DATETIME(6),
                completed_utc DATETIME(6),
                verified_utc DATETIME(6),
                rolled_back_utc DATETIME(6),
                last_update_utc DATETIME(6) NOT NULL,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
                FOREIGN KEY (workflow_profile_id) REFERENCES workflow_profiles(id) ON DELETE SET NULL,
                FOREIGN KEY (environment_id) REFERENCES environments(id) ON DELETE SET NULL,
                FOREIGN KEY (release_id) REFERENCES releases(id) ON DELETE SET NULL,
                FOREIGN KEY (mission_id) REFERENCES missions(id) ON DELETE SET NULL,
                FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE SET NULL
            );",
            @"CREATE INDEX idx_deployments_tenant_created ON deployments(tenant_id, created_utc DESC);",
            @"CREATE INDEX idx_deployments_user_created ON deployments(user_id, created_utc DESC);",
            @"CREATE INDEX idx_deployments_vessel_created ON deployments(vessel_id, created_utc DESC);",
            @"CREATE INDEX idx_deployments_profile_created ON deployments(workflow_profile_id, created_utc DESC);",
            @"CREATE INDEX idx_deployments_environment_created ON deployments(environment_id, created_utc DESC);",
            @"CREATE INDEX idx_deployments_release_created ON deployments(release_id, created_utc DESC);",
            @"CREATE INDEX idx_deployments_status_created ON deployments(status, created_utc DESC);",
            @"CREATE INDEX idx_deployments_verification_created ON deployments(verification_status, created_utc DESC);"
        };

        /// <summary>
        /// Migration v40 statements for deployment-linked checks and rollout monitoring.
        /// </summary>
        public static readonly string[] MigrationV40Statements = new string[]
        {
            @"ALTER TABLE check_runs ADD COLUMN IF NOT EXISTS deployment_id VARCHAR(450) NULL;",
            @"CREATE INDEX idx_check_runs_deployment_created ON check_runs(deployment_id, created_utc DESC);",
            @"ALTER TABLE environments ADD COLUMN IF NOT EXISTS verification_definitions_json LONGTEXT NULL;",
            @"ALTER TABLE environments ADD COLUMN IF NOT EXISTS rollout_monitoring_window_minutes INT NOT NULL DEFAULT 0;",
            @"ALTER TABLE environments ADD COLUMN IF NOT EXISTS rollout_monitoring_interval_seconds INT NOT NULL DEFAULT 300;",
            @"ALTER TABLE environments ADD COLUMN IF NOT EXISTS alert_on_regression TINYINT(1) NOT NULL DEFAULT 1;",
            @"ALTER TABLE deployments ADD COLUMN IF NOT EXISTS monitoring_window_ends_utc DATETIME(6) NULL;",
            @"ALTER TABLE deployments ADD COLUMN IF NOT EXISTS last_monitored_utc DATETIME(6) NULL;",
            @"ALTER TABLE deployments ADD COLUMN IF NOT EXISTS last_regression_alert_utc DATETIME(6) NULL;",
            @"ALTER TABLE deployments ADD COLUMN IF NOT EXISTS latest_monitoring_summary LONGTEXT NULL;",
            @"ALTER TABLE deployments ADD COLUMN IF NOT EXISTS monitoring_failure_count INT NOT NULL DEFAULT 0;"
        };

        /// <summary>
        /// Migration v41 statements for vessel GitHub token overrides.
        /// </summary>
        public static readonly string[] MigrationV41Statements = new string[]
        {
            @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS github_token_override LONGTEXT NULL;"
        };

        /// <summary>
        /// Migration v42 statements for normalized objectives backlog tables.
        /// </summary>
        public static readonly string[] MigrationV42Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS objectives (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                title VARCHAR(450) NOT NULL,
                description LONGTEXT,
                status VARCHAR(64) NOT NULL DEFAULT 'Draft',
                kind VARCHAR(64) NOT NULL DEFAULT 'Feature',
                category VARCHAR(450),
                priority VARCHAR(64) NOT NULL DEFAULT 'P2',
                `rank` INT NOT NULL DEFAULT 0,
                backlog_state VARCHAR(64) NOT NULL DEFAULT 'Inbox',
                effort VARCHAR(64) NOT NULL DEFAULT 'M',
                owner VARCHAR(450),
                target_version VARCHAR(450),
                due_utc DATETIME(6) NULL,
                parent_objective_id VARCHAR(450),
                blocked_by_objective_ids_json LONGTEXT,
                refinement_summary LONGTEXT,
                suggested_pipeline_id VARCHAR(450),
                suggested_playbooks_json LONGTEXT,
                tags_json LONGTEXT,
                acceptance_criteria_json LONGTEXT,
                non_goals_json LONGTEXT,
                rollout_constraints_json LONGTEXT,
                evidence_links_json LONGTEXT,
                fleet_ids_json LONGTEXT,
                vessel_ids_json LONGTEXT,
                planning_session_ids_json LONGTEXT,
                refinement_session_ids_json LONGTEXT,
                voyage_ids_json LONGTEXT,
                mission_ids_json LONGTEXT,
                check_run_ids_json LONGTEXT,
                release_ids_json LONGTEXT,
                deployment_ids_json LONGTEXT,
                incident_ids_json LONGTEXT,
                source_provider VARCHAR(450),
                source_type VARCHAR(450),
                source_id VARCHAR(450),
                source_url LONGTEXT,
                source_updated_utc DATETIME(6) NULL,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                completed_utc DATETIME(6) NULL,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (parent_objective_id) REFERENCES objectives(id) ON DELETE SET NULL,
                FOREIGN KEY (suggested_pipeline_id) REFERENCES pipelines(id) ON DELETE SET NULL
            );",
            @"CREATE INDEX idx_objectives_tenant_status_updated ON objectives(tenant_id, status, last_update_utc DESC);",
            @"CREATE INDEX idx_objectives_tenant_backlog_priority_rank ON objectives(tenant_id, backlog_state, priority, `rank`);",
            @"CREATE INDEX idx_objectives_tenant_kind_priority ON objectives(tenant_id, kind, priority);",
            @"CREATE INDEX idx_objectives_tenant_owner ON objectives(tenant_id(128), owner(128));",
            @"CREATE INDEX idx_objectives_tenant_due ON objectives(tenant_id, due_utc);",
            @"CREATE INDEX idx_objectives_tenant_target_version ON objectives(tenant_id(128), target_version(128));",
            @"CREATE INDEX idx_objectives_tenant_parent ON objectives(tenant_id(128), parent_objective_id(128));",
            @"CREATE INDEX idx_objectives_tenant_source ON objectives(tenant_id(128), source_provider(64), source_type(64), source_id(128));",
            @"CREATE TABLE IF NOT EXISTS objective_refinement_sessions (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                objective_id VARCHAR(450) NOT NULL,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                captain_id VARCHAR(450),
                fleet_id VARCHAR(450),
                vessel_id VARCHAR(450),
                title VARCHAR(450) NOT NULL,
                status VARCHAR(64) NOT NULL DEFAULT 'Created',
                process_id INT NULL,
                failure_reason LONGTEXT,
                created_utc DATETIME(6) NOT NULL,
                started_utc DATETIME(6) NULL,
                completed_utc DATETIME(6) NULL,
                last_update_utc DATETIME(6) NOT NULL,
                FOREIGN KEY (objective_id) REFERENCES objectives(id) ON DELETE CASCADE,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL,
                FOREIGN KEY (fleet_id) REFERENCES fleets(id) ON DELETE SET NULL,
                FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL
            );",
            @"CREATE INDEX idx_objective_refinement_sessions_tenant_objective_created ON objective_refinement_sessions(tenant_id(128), objective_id(128), created_utc DESC);",
            @"CREATE INDEX idx_objective_refinement_sessions_tenant_captain_status ON objective_refinement_sessions(tenant_id(128), captain_id(128), status);",
            @"CREATE TABLE IF NOT EXISTS objective_refinement_messages (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                objective_refinement_session_id VARCHAR(450) NOT NULL,
                objective_id VARCHAR(450) NOT NULL,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                role VARCHAR(64) NOT NULL,
                sequence INT NOT NULL,
                content LONGTEXT NOT NULL,
                is_selected TINYINT(1) NOT NULL DEFAULT 0,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                FOREIGN KEY (objective_refinement_session_id) REFERENCES objective_refinement_sessions(id) ON DELETE CASCADE,
                FOREIGN KEY (objective_id) REFERENCES objectives(id) ON DELETE CASCADE,
                FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
            );",
            @"CREATE INDEX idx_objective_refinement_messages_session_sequence ON objective_refinement_messages(objective_refinement_session_id, sequence);",
            @"CREATE INDEX idx_objective_refinement_messages_objective_created ON objective_refinement_messages(objective_id, created_utc DESC);"
        };

        /// <summary>
        /// DDL for the coordination_leases table. Provides durable, restart-safe, multi-instance-safe
        /// mutual exclusion via atomic compare-and-swap on the lease name, with TTL-based takeover.
        /// </summary>
        public static readonly string CoordinationLeases = @"CREATE TABLE IF NOT EXISTS coordination_leases (
            name VARCHAR(255) NOT NULL PRIMARY KEY,
            holder TEXT NOT NULL,
            tenant_id VARCHAR(255) NULL,
            acquired_utc DATETIME(6) NOT NULL,
            expires_utc DATETIME(6) NOT NULL,
            INDEX idx_coordination_leases_expires (expires_utc)
        );";

        /// <summary>
        /// Migration v44 statements for the reliability release: dock leases, process liveness,
        /// review deadline, merge retry, and coordination leases.
        /// </summary>
        public static readonly string[] MigrationV44Statements = new string[]
        {
            @"ALTER TABLE docks ADD COLUMN state VARCHAR(32) NOT NULL DEFAULT 'Available';",
            @"ALTER TABLE docks ADD COLUMN lease_expires_utc DATETIME(6) NULL;",
            @"ALTER TABLE docks ADD COLUMN owner_token TEXT NULL;",
            @"ALTER TABLE captains ADD COLUMN last_process_alive_utc DATETIME(6) NULL;",
            @"ALTER TABLE missions ADD COLUMN review_deadline_utc DATETIME(6) NULL;",
            @"ALTER TABLE merge_entries ADD COLUMN retry_count INT NOT NULL DEFAULT 0;",
            @"ALTER TABLE merge_entries ADD COLUMN lease_expires_utc DATETIME(6) NULL;",
            CoordinationLeases
        };

        /// <summary>
        /// Migration v45 statements: project_profiles for per-project persona/pipeline/skill customization.
        /// </summary>
        public static readonly string[] MigrationV45Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS project_profiles (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                name VARCHAR(450) NOT NULL,
                description LONGTEXT,
                scope VARCHAR(64) NOT NULL DEFAULT 'Global',
                fleet_id VARCHAR(450),
                vessel_id VARCHAR(450),
                is_default TINYINT(1) NOT NULL DEFAULT 0,
                active TINYINT(1) NOT NULL DEFAULT 1,
                default_pipeline_id VARCHAR(450),
                workflow_profile_id VARCHAR(450),
                persona_overrides_json LONGTEXT,
                skills_json LONGTEXT,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                INDEX idx_project_profiles_tenant (tenant_id),
                INDEX idx_project_profiles_scope (scope),
                INDEX idx_project_profiles_fleet (fleet_id),
                INDEX idx_project_profiles_vessel (vessel_id)
            );"
        };

        /// <summary>
        /// Migration v46 statements: skills directory.
        /// </summary>
        public static readonly string[] MigrationV46Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS skills (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                name VARCHAR(450) NOT NULL,
                description LONGTEXT,
                category VARCHAR(255),
                content LONGTEXT,
                is_built_in TINYINT(1) NOT NULL DEFAULT 0,
                active TINYINT(1) NOT NULL DEFAULT 1,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL,
                INDEX idx_skills_tenant (tenant_id),
                INDEX idx_skills_category (category),
                INDEX idx_skills_active (active)
            );"
        };

        /// <summary>
        /// Migration v47 statements: reasoning_effort/tier on captains, redispatch_attempts/tier on missions, and vessel dock-boundary columns.
        /// </summary>
        public static readonly string[] MigrationV47Statements = new string[]
        {
            @"ALTER TABLE captains ADD COLUMN reasoning_effort VARCHAR(64) NULL;",
            @"ALTER TABLE captains ADD COLUMN tier VARCHAR(32) NULL;",
            @"ALTER TABLE missions ADD COLUMN redispatch_attempts INT NOT NULL DEFAULT 0;",
            @"ALTER TABLE missions ADD COLUMN tier VARCHAR(32) NULL;",
            @"ALTER TABLE vessels ADD COLUMN secret_scan_enabled TINYINT(1) NOT NULL DEFAULT 0;",
            @"ALTER TABLE vessels ADD COLUMN protected_path_patterns_json LONGTEXT NULL;",
            @"ALTER TABLE vessels ADD COLUMN private_identifier_denylist_json LONGTEXT NULL;"
        };

        /// <summary>
        /// Migration v48 statements: auto-land policy columns on vessels and quarantine columns on captains.
        /// </summary>
        public static readonly string[] MigrationV48Statements = new string[]
        {
            @"ALTER TABLE vessels ADD COLUMN auto_land_enabled TINYINT(1) NOT NULL DEFAULT 0;",
            @"ALTER TABLE vessels ADD COLUMN auto_land_max_files INT NOT NULL DEFAULT 0;",
            @"ALTER TABLE vessels ADD COLUMN auto_land_max_lines INT NOT NULL DEFAULT 0;",
            @"ALTER TABLE vessels ADD COLUMN auto_land_path_allow_globs_json LONGTEXT NULL;",
            @"ALTER TABLE vessels ADD COLUMN auto_land_path_deny_globs_json LONGTEXT NULL;",
            @"ALTER TABLE captains ADD COLUMN quarantine_until_utc DATETIME(6) NULL;",
            @"ALTER TABLE captains ADD COLUMN quarantine_reason LONGTEXT NULL;"
        };

        /// <summary>
        /// DDL for the jobs table.
        /// </summary>
        public static readonly string Jobs = @"CREATE TABLE IF NOT EXISTS jobs (
            id VARCHAR(450) NOT NULL PRIMARY KEY,
            tenant_id VARCHAR(450),
            user_id VARCHAR(450),
            name VARCHAR(450) NOT NULL,
            kind VARCHAR(64) NOT NULL DEFAULT 'Generic',
            status VARCHAR(64) NOT NULL DEFAULT 'Queued',
            progress INT NOT NULL DEFAULT 0,
            result_json LONGTEXT,
            error_reason LONGTEXT,
            created_utc DATETIME(6) NOT NULL,
            started_utc DATETIME(6) NULL,
            completed_utc DATETIME(6) NULL,
            last_update_utc DATETIME(6) NOT NULL
        );";

        /// <summary>
        /// Migration v49 statements: add the jobs table.
        /// </summary>
        public static readonly string[] MigrationV49Statements = new string[]
        {
            Jobs
        };

        /// <summary>
        /// Migration v55 statements: per-step captain selection columns (persona default captain, mission requested captain, voyage captain overrides).
        /// </summary>
        public static readonly string[] MigrationV55Statements = new string[]
        {
            @"ALTER TABLE personas ADD COLUMN default_captain_id TEXT NULL;",
            @"ALTER TABLE missions ADD COLUMN requested_captain_id TEXT NULL;",
            @"ALTER TABLE voyages ADD COLUMN captain_overrides_json TEXT NULL;"
        };

        /// <summary>
        /// Migration v56 statements: add the token_usage table for per-model token accounting.
        /// </summary>
        public static readonly string[] MigrationV56Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS token_usage (
                id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64),
                user_id VARCHAR(64),
                model VARCHAR(255) NOT NULL DEFAULT '',
                runtime VARCHAR(64),
                source VARCHAR(32) NOT NULL,
                source_id VARCHAR(64),
                vessel_id VARCHAR(64),
                captain_id VARCHAR(64),
                input_tokens BIGINT NOT NULL DEFAULT 0,
                output_tokens BIGINT NOT NULL DEFAULT 0,
                cached_tokens BIGINT NOT NULL DEFAULT 0,
                total_tokens BIGINT NOT NULL DEFAULT 0,
                estimated TINYINT(1) NOT NULL DEFAULT 0,
                created_utc DATETIME(6) NOT NULL
            );",
            "CREATE INDEX idx_token_usage_created ON token_usage(created_utc DESC);",
            "CREATE INDEX idx_token_usage_tenant_created ON token_usage(tenant_id, created_utc);",
            "CREATE INDEX idx_token_usage_model ON token_usage(model);"
        };

        /// <summary>
        /// Migration v57 statements: add the mission execution mode column (Implementation/Audit/Research).
        /// </summary>
        public static readonly string[] MigrationV57Statements = new string[]
        {
            @"ALTER TABLE missions ADD COLUMN mode VARCHAR(32) NOT NULL DEFAULT 'Implementation';"
        };

        /// <summary>
        /// Migration statements for schema version 58.
        /// </summary>
        public static readonly string[] MigrationV58Statements = new string[]
        {
            @"ALTER TABLE vessels ADD COLUMN definition_of_done_enabled TINYINT(1) NOT NULL DEFAULT 0;",
            @"ALTER TABLE vessels ADD COLUMN definition_of_done_build_command TEXT;",
            @"ALTER TABLE vessels ADD COLUMN definition_of_done_test_command TEXT;",
            @"ALTER TABLE vessels ADD COLUMN definition_of_done_timeout_seconds INT NOT NULL DEFAULT 1800;"
        };

        /// <summary>
        /// Migration statements for schema version 59.
        /// </summary>
        public static readonly string[] MigrationV59Statements = new string[]
        {
            @"ALTER TABLE docks ADD COLUMN git_anchors_json TEXT;"
        };

        /// <summary>
        /// Migration v60 statements: add the model_endpoints table for managed embedding/inference endpoints.
        /// </summary>
        public static readonly string[] MigrationV60Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS model_endpoints (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                name VARCHAR(450) NOT NULL,
                kind VARCHAR(64) NOT NULL,
                provider VARCHAR(64) NOT NULL,
                base_url VARCHAR(1024) NOT NULL,
                api_key TEXT,
                model VARCHAR(255),
                dimensionality INT NOT NULL DEFAULT 0,
                timeout_ms INT NOT NULL DEFAULT 120000,
                enabled TINYINT(1) NOT NULL DEFAULT 1,
                health_status VARCHAR(64) NOT NULL DEFAULT 'Unknown',
                last_health_check_utc DATETIME(6) NULL,
                last_health_error TEXT,
                last_latency_ms BIGINT NULL,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL
            );",
            "CREATE INDEX idx_model_endpoints_created ON model_endpoints(created_utc DESC);",
            "CREATE INDEX idx_model_endpoints_tenant ON model_endpoints(tenant_id);"
        };

        /// <summary>
        /// Migration v61 statements: add rolling health-check history to model_endpoints.
        /// </summary>
        public static readonly string[] MigrationV61Statements = new string[]
        {
            @"ALTER TABLE model_endpoints ADD COLUMN health_history_json LONGTEXT;"
        };

        /// <summary>
        /// Migration v62 statements: add the harbors and harbor_capabilities tables for host runners.
        /// </summary>
        public static readonly string[] MigrationV62Statements = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS harbors (
                id VARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(450),
                user_id VARCHAR(450),
                name VARCHAR(450) NOT NULL,
                connection_status VARCHAR(64) NOT NULL DEFAULT 'Unknown',
                max_concurrent_jobs INT NOT NULL DEFAULT 4,
                enabled TINYINT(1) NOT NULL DEFAULT 1,
                protocol_version VARCHAR(64),
                os_platform VARCHAR(64),
                architecture VARCHAR(64),
                last_seen_utc DATETIME(6) NULL,
                last_connected_utc DATETIME(6) NULL,
                created_utc DATETIME(6) NOT NULL,
                last_update_utc DATETIME(6) NOT NULL
            );",
            "CREATE INDEX idx_harbors_created ON harbors(created_utc DESC);",
            "CREATE INDEX idx_harbors_tenant ON harbors(tenant_id);",
            @"CREATE TABLE IF NOT EXISTS harbor_capabilities (
                harbor_id VARCHAR(191) NOT NULL,
                name VARCHAR(191) NOT NULL,
                available TINYINT(1) NOT NULL DEFAULT 1,
                detail TEXT,
                PRIMARY KEY (harbor_id, name),
                FOREIGN KEY (harbor_id) REFERENCES harbors(id) ON DELETE CASCADE
            );",
            "CREATE INDEX idx_harbor_capabilities_harbor ON harbor_capabilities(harbor_id);"
        };

        /// <summary>
        /// Migration v63 statements: add Harbor routing and affinity columns to docks, missions, and vessels.
        /// </summary>
        public static readonly string[] MigrationV63Statements = new string[]
        {
            "ALTER TABLE docks ADD COLUMN harbor_id VARCHAR(191);",
            "ALTER TABLE missions ADD COLUMN assigned_harbor_id VARCHAR(191);",
            "ALTER TABLE vessels ADD COLUMN preferred_harbor_id VARCHAR(191);",
            "ALTER TABLE vessels ADD COLUMN required_capabilities VARCHAR(1024);"
        };

        /// <summary>
        /// Migration v64 statements: add model_endpoint_id to captains for API-endpoint captains.
        /// </summary>
        public static readonly string[] MigrationV64Statements = new string[]
        {
            "ALTER TABLE captains ADD COLUMN model_endpoint_id VARCHAR(191);"
        };

        /// <summary>
        /// Migration v65 statements: add scope to model_endpoints (tenant-wide vs user-specific).
        /// </summary>
        public static readonly string[] MigrationV65Statements = new string[]
        {
            "ALTER TABLE model_endpoints ADD COLUMN scope VARCHAR(32) NOT NULL DEFAULT 'TenantWide';"
        };

        /// <summary>
        /// Migration v66 statements: add ownership scope to playbooks and skills.
        /// </summary>
        public static readonly string[] MigrationV66Statements = new string[]
        {
            "ALTER TABLE playbooks ADD COLUMN scope VARCHAR(32) NOT NULL DEFAULT 'TenantWide';",
            "ALTER TABLE skills ADD COLUMN scope VARCHAR(32) NOT NULL DEFAULT 'TenantWide';"
        };

        /// <summary>
        /// Migration v67 statements: add ownership (user_id + scope) to personas, pipelines, prompt_templates.
        /// </summary>
        public static readonly string[] MigrationV67Statements = new string[]
        {
            "ALTER TABLE personas ADD COLUMN user_id VARCHAR(191) NULL;",
            "ALTER TABLE personas ADD COLUMN scope VARCHAR(32) NOT NULL DEFAULT 'TenantWide';",
            "ALTER TABLE pipelines ADD COLUMN user_id VARCHAR(191) NULL;",
            "ALTER TABLE pipelines ADD COLUMN scope VARCHAR(32) NOT NULL DEFAULT 'TenantWide';",
            "ALTER TABLE prompt_templates ADD COLUMN user_id VARCHAR(191) NULL;",
            "ALTER TABLE prompt_templates ADD COLUMN scope VARCHAR(32) NOT NULL DEFAULT 'TenantWide';"
        };

        /// <summary>
        /// Migration v68 statements: add ownership_scope to workflow_profiles and project_profiles.
        /// </summary>
        public static readonly string[] MigrationV68Statements = new string[]
        {
            "ALTER TABLE workflow_profiles ADD COLUMN ownership_scope VARCHAR(32) NOT NULL DEFAULT 'TenantWide';",
            "ALTER TABLE project_profiles ADD COLUMN ownership_scope VARCHAR(32) NOT NULL DEFAULT 'TenantWide';"
        };

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
            "CREATE INDEX idx_missions_vessel_status ON missions(vessel_id(191), status(191));",
            "CREATE INDEX idx_voyages_status ON voyages(status);",
            "CREATE INDEX idx_docks_vessel ON docks(vessel_id);",
            "CREATE INDEX idx_docks_vessel_available ON docks(vessel_id(191), active, captain_id(191));",
            "CREATE INDEX idx_signals_to_captain ON signals(to_captain_id);",
            "CREATE INDEX idx_signals_to_captain_read ON signals(to_captain_id, `read`);",
            "CREATE INDEX idx_signals_created ON signals(created_utc DESC);",
            "CREATE INDEX idx_events_type ON events(event_type);",
            "CREATE INDEX idx_events_captain ON events(captain_id);",
            "CREATE INDEX idx_events_mission ON events(mission_id);",
            "CREATE INDEX idx_events_vessel ON events(vessel_id);",
            "CREATE INDEX idx_events_voyage ON events(voyage_id);",
            "CREATE INDEX idx_events_entity ON events(entity_type(191), entity_id(191));",
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
