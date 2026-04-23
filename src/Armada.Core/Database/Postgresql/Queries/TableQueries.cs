namespace Armada.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Static class containing all CREATE TABLE and CREATE INDEX DDL statements for the Armada PostgreSQL schema.
    /// </summary>
    public static class TableQueries
    {
        #region Public-Methods

        /// <summary>
        /// Get all schema migrations for the Armada PostgreSQL database.
        /// </summary>
        /// <returns>List of schema migrations.</returns>
        public static List<SchemaMigration> GetMigrations()
        {
            return new List<SchemaMigration>
            {
                new SchemaMigration(1, "Full schema with multi-tenant support: all tables, indexes, and tenant columns",
                    // Tenants
                    @"CREATE TABLE IF NOT EXISTS tenants (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_tenants_active ON tenants(active);",

                    // Users
                    @"CREATE TABLE IF NOT EXISTS users (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT NOT NULL,
                        email TEXT NOT NULL,
                        password_sha256 TEXT NOT NULL,
                        first_name TEXT,
                        last_name TEXT,
                        is_admin BOOLEAN NOT NULL DEFAULT FALSE,
                        is_tenant_admin BOOLEAN NOT NULL DEFAULT FALSE,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
                    );",
                    @"CREATE UNIQUE INDEX IF NOT EXISTS idx_users_tenant_email ON users(tenant_id, email);",
                    @"CREATE INDEX IF NOT EXISTS idx_users_tenant ON users(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);",

                    // Credentials
                    @"CREATE TABLE IF NOT EXISTS credentials (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT NOT NULL,
                        user_id TEXT NOT NULL,
                        name TEXT,
                        bearer_token TEXT NOT NULL UNIQUE,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_credentials_tenant ON credentials(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_credentials_user ON credentials(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_credentials_bearer ON credentials(bearer_token);",
                    @"CREATE INDEX IF NOT EXISTS idx_credentials_tenant_user ON credentials(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_credentials_active ON credentials(active);",

                    // Fleets
                    @"CREATE TABLE IF NOT EXISTS fleets (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        name TEXT NOT NULL,
                        description TEXT,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_fleets_tenant ON fleets(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_fleets_tenant_name ON fleets(tenant_id, name);",
                    @"CREATE INDEX IF NOT EXISTS idx_fleets_created_utc ON fleets(created_utc);",

                    // Vessels
                    @"CREATE TABLE IF NOT EXISTS vessels (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        fleet_id TEXT,
                        name TEXT NOT NULL,
                        repo_url TEXT,
                        local_path TEXT,
                        working_directory TEXT,
                        project_context TEXT,
                        style_guide TEXT,
                        enable_model_context BOOLEAN NOT NULL DEFAULT TRUE,
                        model_context TEXT,
                        landing_mode TEXT,
                        branch_cleanup_policy TEXT,
                        allow_concurrent_missions BOOLEAN NOT NULL DEFAULT FALSE,
                        default_branch TEXT NOT NULL DEFAULT 'main',
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (fleet_id) REFERENCES fleets(id) ON DELETE SET NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_fleet ON vessels(fleet_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_tenant ON vessels(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_tenant_fleet ON vessels(tenant_id, fleet_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_tenant_name ON vessels(tenant_id, name);",
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_created_utc ON vessels(created_utc);",

                    // Captains
                    @"CREATE TABLE IF NOT EXISTS captains (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        name TEXT NOT NULL,
                        runtime TEXT NOT NULL DEFAULT 'ClaudeCode',
                        system_instructions TEXT,
                        state TEXT NOT NULL DEFAULT 'Idle',
                        current_mission_id TEXT,
                        current_dock_id TEXT,
                        process_id INTEGER,
                        recovery_attempts INTEGER NOT NULL DEFAULT 0,
                        last_heartbeat_utc TIMESTAMP,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_captains_state ON captains(state);",
                    @"CREATE INDEX IF NOT EXISTS idx_captains_tenant ON captains(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_captains_tenant_state ON captains(tenant_id, state);",
                    @"CREATE INDEX IF NOT EXISTS idx_captains_created_utc ON captains(created_utc);",

                    // Voyages
                    @"CREATE TABLE IF NOT EXISTS voyages (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        title TEXT NOT NULL,
                        description TEXT,
                        status TEXT NOT NULL DEFAULT 'Open',
                        created_utc TIMESTAMP NOT NULL,
                        completed_utc TIMESTAMP,
                        last_update_utc TIMESTAMP NOT NULL,
                        auto_push BOOLEAN,
                        auto_create_pull_requests BOOLEAN,
                        auto_merge_pull_requests BOOLEAN,
                        landing_mode TEXT
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_voyages_status ON voyages(status);",
                    @"CREATE INDEX IF NOT EXISTS idx_voyages_tenant ON voyages(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_voyages_tenant_status ON voyages(tenant_id, status);",
                    @"CREATE INDEX IF NOT EXISTS idx_voyages_created_utc ON voyages(created_utc);",

                    // Missions
                    @"CREATE TABLE IF NOT EXISTS missions (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        voyage_id TEXT,
                        vessel_id TEXT,
                        captain_id TEXT,
                        title TEXT NOT NULL,
                        description TEXT,
                        status TEXT NOT NULL DEFAULT 'Pending',
                        priority INTEGER NOT NULL DEFAULT 100,
                        parent_mission_id TEXT,
                        branch_name TEXT,
                        dock_id TEXT,
                        process_id INTEGER,
                        pr_url TEXT,
                        commit_hash TEXT,
                        diff_snapshot TEXT,
                        agent_output TEXT,
                        created_utc TIMESTAMP NOT NULL,
                        started_utc TIMESTAMP,
                        completed_utc TIMESTAMP,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE SET NULL,
                        FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
                        FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL,
                        FOREIGN KEY (parent_mission_id) REFERENCES missions(id) ON DELETE SET NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_voyage ON missions(voyage_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_vessel ON missions(vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_captain ON missions(captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_status ON missions(status);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_status_priority ON missions(status, priority ASC, created_utc ASC);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_vessel_status ON missions(vessel_id, status);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_tenant ON missions(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_tenant_status ON missions(tenant_id, status);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_tenant_vessel ON missions(tenant_id, vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_tenant_voyage ON missions(tenant_id, voyage_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_tenant_captain ON missions(tenant_id, captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_tenant_status_priority ON missions(tenant_id, status, priority ASC, created_utc ASC);",

                    // Docks
                    @"CREATE TABLE IF NOT EXISTS docks (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        vessel_id TEXT NOT NULL,
                        captain_id TEXT,
                        worktree_path TEXT,
                        branch_name TEXT,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE,
                        FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_vessel ON docks(vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_vessel_available ON docks(vessel_id, active, captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_tenant ON docks(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_tenant_vessel ON docks(tenant_id, vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_tenant_vessel_available ON docks(tenant_id, vessel_id, active, captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_tenant_captain ON docks(tenant_id, captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_created_utc ON docks(created_utc);",

                    // Signals
                    @"CREATE TABLE IF NOT EXISTS signals (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        from_captain_id TEXT,
                        to_captain_id TEXT,
                        type TEXT NOT NULL DEFAULT 'Nudge',
                        payload TEXT,
                        read BOOLEAN NOT NULL DEFAULT FALSE,
                        created_utc TEXT NOT NULL,
                        FOREIGN KEY (from_captain_id) REFERENCES captains(id) ON DELETE SET NULL,
                        FOREIGN KEY (to_captain_id) REFERENCES captains(id) ON DELETE SET NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_to_captain ON signals(to_captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_to_captain_read ON signals(to_captain_id, read);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_created ON signals(created_utc DESC);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_tenant ON signals(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_tenant_to_captain ON signals(tenant_id, to_captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_tenant_to_captain_read ON signals(tenant_id, to_captain_id, read);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_tenant_created ON signals(tenant_id, created_utc DESC);",

                    // Events
                    @"CREATE TABLE IF NOT EXISTS events (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
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
                    @"CREATE INDEX IF NOT EXISTS idx_events_type ON events(event_type);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_captain ON events(captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_mission ON events(mission_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_vessel ON events(vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_voyage ON events(voyage_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_entity ON events(entity_type, entity_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_created ON events(created_utc DESC);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant ON events(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant_type ON events(tenant_id, event_type);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant_entity ON events(tenant_id, entity_type, entity_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant_vessel ON events(tenant_id, vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant_voyage ON events(tenant_id, voyage_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant_captain ON events(tenant_id, captain_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant_mission ON events(tenant_id, mission_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant_created ON events(tenant_id, created_utc DESC);",

                    // Merge entries
                    @"CREATE TABLE IF NOT EXISTS merge_entries (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
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
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_completed ON merge_entries(completed_utc);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant ON merge_entries(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_status ON merge_entries(tenant_id, status);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_status_priority ON merge_entries(tenant_id, status, priority ASC, created_utc ASC);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_vessel ON merge_entries(tenant_id, vessel_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_mission ON merge_entries(tenant_id, mission_id);"
                ),
                new SchemaMigration(2, "Protected resources and user ownership",
                    @"ALTER TABLE tenants ADD COLUMN IF NOT EXISTS is_protected BOOLEAN NOT NULL DEFAULT FALSE;",
                    @"ALTER TABLE users ADD COLUMN IF NOT EXISTS is_protected BOOLEAN NOT NULL DEFAULT FALSE;",
                    @"ALTER TABLE credentials ADD COLUMN IF NOT EXISTS is_protected BOOLEAN NOT NULL DEFAULT FALSE;",
                    @"UPDATE tenants SET is_protected = TRUE WHERE id IN ('default', 'ten_system');",
                    @"UPDATE users SET is_protected = TRUE WHERE id IN ('default', 'usr_system');",
                    @"UPDATE credentials SET is_protected = TRUE WHERE user_id IN ('default', 'usr_system');",
                    @"ALTER TABLE fleets ADD COLUMN IF NOT EXISTS user_id TEXT;",
                    @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS user_id TEXT;",
                    @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS user_id TEXT;",
                    @"ALTER TABLE voyages ADD COLUMN IF NOT EXISTS user_id TEXT;",
                    @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS user_id TEXT;",
                    @"ALTER TABLE docks ADD COLUMN IF NOT EXISTS user_id TEXT;",
                    @"ALTER TABLE signals ADD COLUMN IF NOT EXISTS user_id TEXT;",
                    @"ALTER TABLE events ADD COLUMN IF NOT EXISTS user_id TEXT;",
                    @"ALTER TABLE merge_entries ADD COLUMN IF NOT EXISTS user_id TEXT;",
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
                    @"CREATE INDEX IF NOT EXISTS idx_fleets_user ON fleets(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_fleets_tenant_user ON fleets(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_user ON vessels(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_tenant_user ON vessels(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_captains_user ON captains(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_captains_tenant_user ON captains(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_voyages_user ON voyages(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_voyages_tenant_user ON voyages(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_user ON missions(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_tenant_user ON missions(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_user ON docks(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_docks_tenant_user ON docks(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_user ON signals(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_signals_tenant_user ON signals(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_user ON events(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_events_tenant_user ON events(tenant_id, user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_user ON merge_entries(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_user ON merge_entries(tenant_id, user_id);"
                ),
                new SchemaMigration(3, "Add operational tenant foreign keys",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_fleets_tenant') THEN
                            ALTER TABLE fleets ADD CONSTRAINT fk_fleets_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_vessels_tenant') THEN
                            ALTER TABLE vessels ADD CONSTRAINT fk_vessels_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_captains_tenant') THEN
                            ALTER TABLE captains ADD CONSTRAINT fk_captains_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_voyages_tenant') THEN
                            ALTER TABLE voyages ADD CONSTRAINT fk_voyages_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_missions_tenant') THEN
                            ALTER TABLE missions ADD CONSTRAINT fk_missions_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_docks_tenant') THEN
                            ALTER TABLE docks ADD CONSTRAINT fk_docks_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_signals_tenant') THEN
                            ALTER TABLE signals ADD CONSTRAINT fk_signals_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_events_tenant') THEN
                            ALTER TABLE events ADD CONSTRAINT fk_events_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;",
                    @"DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_merge_entries_tenant') THEN
                            ALTER TABLE merge_entries ADD CONSTRAINT fk_merge_entries_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id);
                        END IF;
                    END $$;"
                ),
                new SchemaMigration(4, "Add tenant admin role to users",
                    @"ALTER TABLE users ADD COLUMN IF NOT EXISTS is_tenant_admin BOOLEAN NOT NULL DEFAULT FALSE;",
                    @"UPDATE users SET is_tenant_admin = TRUE WHERE is_admin = TRUE;"
                ),
                new SchemaMigration(5, "Add enable_model_context and model_context to vessels",
                    @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS enable_model_context BOOLEAN NOT NULL DEFAULT TRUE;",
                    @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS model_context TEXT;"
                ),
                new SchemaMigration(6, "Add system_instructions to captains",
                    @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS system_instructions TEXT;"
                ),
                new SchemaMigration(7, "Add prompt_templates table",
                    @"CREATE TABLE IF NOT EXISTS prompt_templates (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        name TEXT NOT NULL,
                        description TEXT,
                        category TEXT NOT NULL DEFAULT 'mission',
                        content TEXT NOT NULL,
                        is_built_in BOOLEAN NOT NULL DEFAULT FALSE,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (tenant_id) REFERENCES tenants(id)
                    );",
                    @"CREATE UNIQUE INDEX IF NOT EXISTS idx_prompt_templates_tenant_name ON prompt_templates(tenant_id, name);",
                    @"CREATE INDEX IF NOT EXISTS idx_prompt_templates_category ON prompt_templates(category);",
                    @"CREATE INDEX IF NOT EXISTS idx_prompt_templates_active ON prompt_templates(active);"
                ),
                new SchemaMigration(8, "Add personas table",
                    @"CREATE TABLE IF NOT EXISTS personas (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        name TEXT NOT NULL,
                        description TEXT,
                        prompt_template_name TEXT NOT NULL,
                        is_built_in BOOLEAN NOT NULL DEFAULT FALSE,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (tenant_id) REFERENCES tenants(id)
                    );",
                    @"CREATE UNIQUE INDEX IF NOT EXISTS idx_personas_tenant_name ON personas(tenant_id, name);",
                    @"CREATE INDEX IF NOT EXISTS idx_personas_active ON personas(active);",
                    @"CREATE INDEX IF NOT EXISTS idx_personas_prompt_template ON personas(prompt_template_name);"
                ),
                new SchemaMigration(9, "Add captain persona fields",
                    @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS allowed_personas TEXT;",
                    @"ALTER TABLE captains ADD COLUMN IF NOT EXISTS preferred_persona TEXT;",
                    @"CREATE INDEX IF NOT EXISTS idx_captains_preferred_persona ON captains(preferred_persona);"
                ),
                new SchemaMigration(10, "Add mission persona and dependency fields",
                    @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS persona TEXT;",
                    @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS depends_on_mission_id TEXT;",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_persona ON missions(persona);",
                    @"CREATE INDEX IF NOT EXISTS idx_missions_depends_on ON missions(depends_on_mission_id);"
                ),
                new SchemaMigration(11, "Add pipelines and pipeline_stages tables",
                    @"CREATE TABLE IF NOT EXISTS pipelines (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        name TEXT NOT NULL,
                        description TEXT,
                        is_built_in BOOLEAN NOT NULL DEFAULT FALSE,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (tenant_id) REFERENCES tenants(id)
                    );",
                    @"CREATE TABLE IF NOT EXISTS pipeline_stages (
                        id TEXT PRIMARY KEY,
                        pipeline_id TEXT NOT NULL,
                        stage_order INTEGER NOT NULL,
                        persona_name TEXT NOT NULL,
                        is_optional BOOLEAN NOT NULL DEFAULT FALSE,
                        description TEXT,
                        FOREIGN KEY (pipeline_id) REFERENCES pipelines(id) ON DELETE CASCADE
                    );",
                    @"CREATE UNIQUE INDEX IF NOT EXISTS idx_pipelines_tenant_name ON pipelines(tenant_id, name);",
                    @"CREATE INDEX IF NOT EXISTS idx_pipelines_active ON pipelines(active);",
                    @"CREATE INDEX IF NOT EXISTS idx_pipeline_stages_pipeline ON pipeline_stages(pipeline_id);",
                    @"CREATE UNIQUE INDEX IF NOT EXISTS idx_pipeline_stages_order ON pipeline_stages(pipeline_id, stage_order);",
                    @"CREATE INDEX IF NOT EXISTS idx_pipeline_stages_persona ON pipeline_stages(persona_name);",
                    @"ALTER TABLE fleets ADD COLUMN IF NOT EXISTS default_pipeline_id TEXT;",
                    @"ALTER TABLE vessels ADD COLUMN IF NOT EXISTS default_pipeline_id TEXT;",
                    @"CREATE INDEX IF NOT EXISTS idx_fleets_default_pipeline ON fleets(default_pipeline_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_vessels_default_pipeline ON vessels(default_pipeline_id);"
                ),
                new SchemaMigration(12, "Add failure_reason to missions",
                    @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS failure_reason TEXT;"
                ),
                new SchemaMigration(13, "Add agent_output to missions",
                    @"ALTER TABLE missions ADD COLUMN IF NOT EXISTS agent_output TEXT;"
                ),
                new SchemaMigration(26, "Add model to captains",
                    @"ALTER TABLE captains ADD COLUMN model TEXT NULL;"
                ),
                new SchemaMigration(27, "Add total_runtime_ms to missions",
                    @"ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;"
                ),
                new SchemaMigration(28, "Add playbooks and mission/voyage playbook associations",
                    @"CREATE TABLE IF NOT EXISTS playbooks (
                        id TEXT PRIMARY KEY,
                        tenant_id TEXT,
                        user_id TEXT,
                        file_name TEXT NOT NULL,
                        description TEXT,
                        content TEXT NOT NULL,
                        active BOOLEAN NOT NULL DEFAULT TRUE,
                        created_utc TIMESTAMP NOT NULL,
                        last_update_utc TIMESTAMP NOT NULL,
                        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
                    );",
                    @"CREATE UNIQUE INDEX IF NOT EXISTS idx_playbooks_tenant_file_name ON playbooks(tenant_id, file_name);",
                    @"CREATE INDEX IF NOT EXISTS idx_playbooks_tenant ON playbooks(tenant_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_playbooks_user ON playbooks(user_id);",
                    @"CREATE INDEX IF NOT EXISTS idx_playbooks_active ON playbooks(active);",
                    @"CREATE TABLE IF NOT EXISTS voyage_playbooks (
                        voyage_id TEXT NOT NULL,
                        playbook_id TEXT NOT NULL,
                        selection_order INTEGER NOT NULL,
                        delivery_mode TEXT NOT NULL,
                        PRIMARY KEY (voyage_id, selection_order),
                        FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE CASCADE,
                        FOREIGN KEY (playbook_id) REFERENCES playbooks(id) ON DELETE CASCADE
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_voyage_playbooks_playbook ON voyage_playbooks(playbook_id);",
                    @"CREATE TABLE IF NOT EXISTS mission_playbook_snapshots (
                        mission_id TEXT NOT NULL,
                        selection_order INTEGER NOT NULL,
                        playbook_id TEXT,
                        file_name TEXT NOT NULL,
                        description TEXT,
                        content TEXT NOT NULL,
                        delivery_mode TEXT NOT NULL,
                        resolved_path TEXT,
                        worktree_relative_path TEXT,
                        source_last_update_utc TIMESTAMP,
                        PRIMARY KEY (mission_id, selection_order),
                        FOREIGN KEY (mission_id) REFERENCES missions(id) ON DELETE CASCADE,
                        FOREIGN KEY (playbook_id) REFERENCES playbooks(id) ON DELETE SET NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS idx_mission_playbook_snapshots_playbook ON mission_playbook_snapshots(playbook_id);"
                )
            };
        }

        #endregion
    }
}
