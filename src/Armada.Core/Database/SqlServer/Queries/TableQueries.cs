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
                MergeEntries,
                CoordinationLeases,
                Jobs
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
                ),
                new SchemaMigration(
                    7,
                    "Add prompt_templates table",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'prompt_templates')
                    CREATE TABLE prompt_templates (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX),
                        category NVARCHAR(450) NOT NULL DEFAULT 'mission',
                        content NVARCHAR(MAX) NOT NULL,
                        is_built_in BIT NOT NULL DEFAULT 0,
                        active BIT NOT NULL DEFAULT 1,
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_prompt_templates_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_prompt_templates_tenant_name') CREATE UNIQUE INDEX idx_prompt_templates_tenant_name ON prompt_templates(tenant_id, name);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_prompt_templates_category') CREATE INDEX idx_prompt_templates_category ON prompt_templates(category);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_prompt_templates_active') CREATE INDEX idx_prompt_templates_active ON prompt_templates(active);"
                ),
                new SchemaMigration(
                    8,
                    "Add personas table",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'personas')
                    CREATE TABLE personas (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX),
                        prompt_template_name NVARCHAR(450) NOT NULL,
                        is_built_in BIT NOT NULL DEFAULT 0,
                        active BIT NOT NULL DEFAULT 1,
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_personas_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_personas_tenant_name') CREATE UNIQUE INDEX idx_personas_tenant_name ON personas(tenant_id, name);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_personas_active') CREATE INDEX idx_personas_active ON personas(active);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_personas_prompt_template') CREATE INDEX idx_personas_prompt_template ON personas(prompt_template_name);"
                ),
                new SchemaMigration(
                    9,
                    "Add captain persona fields",
                    @"
                    IF COL_LENGTH('captains', 'allowed_personas') IS NULL
                        ALTER TABLE captains ADD allowed_personas NVARCHAR(MAX);",
                    @"
                    IF COL_LENGTH('captains', 'preferred_persona') IS NULL
                        ALTER TABLE captains ADD preferred_persona NVARCHAR(450);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_captains_preferred_persona') CREATE INDEX idx_captains_preferred_persona ON captains(preferred_persona);"
                ),
                new SchemaMigration(
                    10,
                    "Add mission persona and dependency fields",
                    @"
                    IF COL_LENGTH('missions', 'persona') IS NULL
                        ALTER TABLE missions ADD persona NVARCHAR(450);",
                    @"
                    IF COL_LENGTH('missions', 'depends_on_mission_id') IS NULL
                        ALTER TABLE missions ADD depends_on_mission_id NVARCHAR(450);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_missions_persona') CREATE INDEX idx_missions_persona ON missions(persona);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_missions_depends_on') CREATE INDEX idx_missions_depends_on ON missions(depends_on_mission_id);"
                ),
                new SchemaMigration(
                    11,
                    "Add pipelines and pipeline_stages tables",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'pipelines')
                    CREATE TABLE pipelines (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX),
                        is_built_in BIT NOT NULL DEFAULT 0,
                        active BIT NOT NULL DEFAULT 1,
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_pipelines_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
                    );",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'pipeline_stages')
                    CREATE TABLE pipeline_stages (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        pipeline_id NVARCHAR(450) NOT NULL,
                        stage_order INT NOT NULL,
                        persona_name NVARCHAR(450) NOT NULL,
                        is_optional BIT NOT NULL DEFAULT 0,
                        description NVARCHAR(MAX),
                        CONSTRAINT FK_pipeline_stages_pipeline FOREIGN KEY (pipeline_id) REFERENCES pipelines(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_pipelines_tenant_name') CREATE UNIQUE INDEX idx_pipelines_tenant_name ON pipelines(tenant_id, name);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_pipelines_active') CREATE INDEX idx_pipelines_active ON pipelines(active);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_pipeline_stages_pipeline') CREATE INDEX idx_pipeline_stages_pipeline ON pipeline_stages(pipeline_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_pipeline_stages_order') CREATE UNIQUE INDEX idx_pipeline_stages_order ON pipeline_stages(pipeline_id, stage_order);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_pipeline_stages_persona') CREATE INDEX idx_pipeline_stages_persona ON pipeline_stages(persona_name);",
                    @"
                    IF COL_LENGTH('fleets', 'default_pipeline_id') IS NULL
                        ALTER TABLE fleets ADD default_pipeline_id NVARCHAR(450);",
                    @"
                    IF COL_LENGTH('vessels', 'default_pipeline_id') IS NULL
                        ALTER TABLE vessels ADD default_pipeline_id NVARCHAR(450);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleets_default_pipeline') CREATE INDEX idx_fleets_default_pipeline ON fleets(default_pipeline_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessels_default_pipeline') CREATE INDEX idx_vessels_default_pipeline ON vessels(default_pipeline_id);"
                ),
                new SchemaMigration(
                    12,
                    "Add failure_reason to missions",
                    @"
                    IF COL_LENGTH('missions', 'failure_reason') IS NULL
                        ALTER TABLE missions ADD failure_reason NVARCHAR(MAX);"
                ),
                new SchemaMigration(
                    13,
                    "Add agent_output to missions",
                    @"
                    IF COL_LENGTH('missions', 'agent_output') IS NULL
                        ALTER TABLE missions ADD agent_output NVARCHAR(MAX);"
                ),
                new SchemaMigration(
                    26,
                    "Add model to captains",
                    @"
                    IF COL_LENGTH('captains', 'model') IS NULL
                        ALTER TABLE captains ADD model NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    27,
                    "Add total_runtime_ms to missions",
                    @"
                    IF COL_LENGTH('missions', 'total_runtime_ms') IS NULL
                        ALTER TABLE missions ADD total_runtime_ms BIGINT NULL;"
                ),
                new SchemaMigration(
                    28,
                    "Add playbooks and mission/voyage playbook associations",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'playbooks')
                    CREATE TABLE playbooks (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        file_name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX),
                        content NVARCHAR(MAX) NOT NULL,
                        active BIT NOT NULL DEFAULT 1,
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_playbooks_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        CONSTRAINT FK_playbooks_user FOREIGN KEY (user_id) REFERENCES users(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_playbooks_tenant_file_name') CREATE UNIQUE INDEX idx_playbooks_tenant_file_name ON playbooks(tenant_id, file_name);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_playbooks_tenant') CREATE INDEX idx_playbooks_tenant ON playbooks(tenant_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_playbooks_user') CREATE INDEX idx_playbooks_user ON playbooks(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_playbooks_active') CREATE INDEX idx_playbooks_active ON playbooks(active);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'voyage_playbooks')
                    CREATE TABLE voyage_playbooks (
                        voyage_id NVARCHAR(450) NOT NULL,
                        playbook_id NVARCHAR(450) NOT NULL,
                        selection_order INT NOT NULL,
                        delivery_mode NVARCHAR(450) NOT NULL,
                        CONSTRAINT PK_voyage_playbooks PRIMARY KEY (voyage_id, selection_order),
                        CONSTRAINT FK_voyage_playbooks_voyage FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE CASCADE,
                        CONSTRAINT FK_voyage_playbooks_playbook FOREIGN KEY (playbook_id) REFERENCES playbooks(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_voyage_playbooks_playbook') CREATE INDEX idx_voyage_playbooks_playbook ON voyage_playbooks(playbook_id);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'mission_playbook_snapshots')
                    CREATE TABLE mission_playbook_snapshots (
                        mission_id NVARCHAR(450) NOT NULL,
                        selection_order INT NOT NULL,
                        playbook_id NVARCHAR(450),
                        file_name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX),
                        content NVARCHAR(MAX) NOT NULL,
                        delivery_mode NVARCHAR(450) NOT NULL,
                        resolved_path NVARCHAR(MAX),
                        worktree_relative_path NVARCHAR(MAX),
                        source_last_update_utc NVARCHAR(450),
                        CONSTRAINT PK_mission_playbook_snapshots PRIMARY KEY (mission_id, selection_order),
                        CONSTRAINT FK_mission_playbook_snapshots_mission FOREIGN KEY (mission_id) REFERENCES missions(id) ON DELETE CASCADE,
                        CONSTRAINT FK_mission_playbook_snapshots_playbook FOREIGN KEY (playbook_id) REFERENCES playbooks(id) ON DELETE SET NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_mission_playbook_snapshots_playbook') CREATE INDEX idx_mission_playbook_snapshots_playbook ON mission_playbook_snapshots(playbook_id);"
                ),
                new SchemaMigration(
                    29,
                    "Add runtime_options_json to captains",
                    @"
                    IF COL_LENGTH('captains', 'runtime_options_json') IS NULL
                        ALTER TABLE captains ADD runtime_options_json NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    30,
                    "Add request history tables",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'request_history')
                    CREATE TABLE request_history (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        credential_id NVARCHAR(450),
                        principal_display NVARCHAR(MAX),
                        auth_method NVARCHAR(450),
                        method NVARCHAR(32) NOT NULL,
                        route NVARCHAR(900) NOT NULL,
                        route_template NVARCHAR(900),
                        query_string NVARCHAR(MAX),
                        status_code INT NOT NULL,
                        duration_ms FLOAT NOT NULL,
                        request_size_bytes BIGINT NOT NULL DEFAULT 0,
                        response_size_bytes BIGINT NOT NULL DEFAULT 0,
                        request_content_type NVARCHAR(450),
                        response_content_type NVARCHAR(450),
                        is_success BIT NOT NULL DEFAULT 1,
                        client_ip NVARCHAR(450),
                        correlation_id NVARCHAR(450),
                        created_utc NVARCHAR(450) NOT NULL
                    );",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'request_history_detail')
                    CREATE TABLE request_history_detail (
                        request_history_id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        path_params_json NVARCHAR(MAX),
                        query_params_json NVARCHAR(MAX),
                        request_headers_json NVARCHAR(MAX),
                        response_headers_json NVARCHAR(MAX),
                        request_body_text NVARCHAR(MAX),
                        response_body_text NVARCHAR(MAX),
                        request_body_truncated BIT NOT NULL DEFAULT 0,
                        response_body_truncated BIT NOT NULL DEFAULT 0,
                        CONSTRAINT FK_request_history_detail_request
                            FOREIGN KEY (request_history_id) REFERENCES request_history(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_request_history_created') CREATE INDEX idx_request_history_created ON request_history(created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_request_history_tenant_created') CREATE INDEX idx_request_history_tenant_created ON request_history(tenant_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_request_history_user_created') CREATE INDEX idx_request_history_user_created ON request_history(user_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_request_history_credential_created') CREATE INDEX idx_request_history_credential_created ON request_history(credential_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_request_history_method_created') CREATE INDEX idx_request_history_method_created ON request_history(method, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_request_history_status_created') CREATE INDEX idx_request_history_status_created ON request_history(status_code, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_request_history_success_created') CREATE INDEX idx_request_history_success_created ON request_history(is_success, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_request_history_route_created') CREATE INDEX idx_request_history_route_created ON request_history(route, created_utc DESC);"
                ),
                new SchemaMigration(
                    31,
                    "Add pipeline review gates",
                    @"
                    IF COL_LENGTH('pipeline_stages', 'requires_review') IS NULL
                        ALTER TABLE pipeline_stages ADD requires_review BIT NOT NULL CONSTRAINT DF_pipeline_stages_requires_review DEFAULT 0;",
                    @"
                    IF COL_LENGTH('pipeline_stages', 'review_deny_action') IS NULL
                        ALTER TABLE pipeline_stages ADD review_deny_action NVARCHAR(64) NOT NULL CONSTRAINT DF_pipeline_stages_review_deny_action DEFAULT 'RetryStage';",
                    @"
                    IF COL_LENGTH('missions', 'requires_review') IS NULL
                        ALTER TABLE missions ADD requires_review BIT NOT NULL CONSTRAINT DF_missions_requires_review DEFAULT 0;",
                    @"
                    IF COL_LENGTH('missions', 'review_deny_action') IS NULL
                        ALTER TABLE missions ADD review_deny_action NVARCHAR(64) NOT NULL CONSTRAINT DF_missions_review_deny_action DEFAULT 'RetryStage';",
                    @"
                    IF COL_LENGTH('missions', 'review_comment') IS NULL
                        ALTER TABLE missions ADD review_comment NVARCHAR(MAX) NULL;",
                    @"
                    IF COL_LENGTH('missions', 'reviewed_by_user_id') IS NULL
                        ALTER TABLE missions ADD reviewed_by_user_id NVARCHAR(450) NULL;",
                    @"
                    IF COL_LENGTH('missions', 'review_requested_utc') IS NULL
                        ALTER TABLE missions ADD review_requested_utc NVARCHAR(450) NULL;",
                    @"
                    IF COL_LENGTH('missions', 'reviewed_utc') IS NULL
                        ALTER TABLE missions ADD reviewed_utc NVARCHAR(450) NULL;",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_missions_requires_review') CREATE INDEX idx_missions_requires_review ON missions(requires_review);"
                ),
                new SchemaMigration(
                    32,
                    "Add workflow profiles",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'workflow_profiles')
                    CREATE TABLE workflow_profiles (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX),
                        scope NVARCHAR(64) NOT NULL CONSTRAINT DF_workflow_profiles_scope DEFAULT 'Global',
                        fleet_id NVARCHAR(450),
                        vessel_id NVARCHAR(450),
                        is_default BIT NOT NULL CONSTRAINT DF_workflow_profiles_is_default DEFAULT 0,
                        active BIT NOT NULL CONSTRAINT DF_workflow_profiles_active DEFAULT 1,
                        language_hints_json NVARCHAR(MAX),
                        lint_command NVARCHAR(MAX),
                        build_command NVARCHAR(MAX),
                        unit_test_command NVARCHAR(MAX),
                        integration_test_command NVARCHAR(MAX),
                        e2e_test_command NVARCHAR(MAX),
                        package_command NVARCHAR(MAX),
                        publish_artifact_command NVARCHAR(MAX),
                        release_versioning_command NVARCHAR(MAX),
                        changelog_generation_command NVARCHAR(MAX),
                        required_secrets_json NVARCHAR(MAX),
                        expected_artifacts_json NVARCHAR(MAX),
                        environments_json NVARCHAR(MAX),
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_workflow_profiles_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        CONSTRAINT FK_workflow_profiles_user FOREIGN KEY (user_id) REFERENCES users(id),
                        CONSTRAINT FK_workflow_profiles_fleet FOREIGN KEY (fleet_id) REFERENCES fleets(id),
                        CONSTRAINT FK_workflow_profiles_vessel FOREIGN KEY (vessel_id) REFERENCES vessels(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_workflow_profiles_tenant') CREATE INDEX idx_workflow_profiles_tenant ON workflow_profiles(tenant_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_workflow_profiles_user') CREATE INDEX idx_workflow_profiles_user ON workflow_profiles(user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_workflow_profiles_scope') CREATE INDEX idx_workflow_profiles_scope ON workflow_profiles(scope);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_workflow_profiles_fleet') CREATE INDEX idx_workflow_profiles_fleet ON workflow_profiles(fleet_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_workflow_profiles_vessel') CREATE INDEX idx_workflow_profiles_vessel ON workflow_profiles(vessel_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_workflow_profiles_default_scope') CREATE INDEX idx_workflow_profiles_default_scope ON workflow_profiles(scope, is_default, active);"
                ),
                new SchemaMigration(
                    33,
                    "Add check runs",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'check_runs')
                    CREATE TABLE check_runs (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        workflow_profile_id NVARCHAR(450),
                        vessel_id NVARCHAR(450),
                        mission_id NVARCHAR(450),
                        voyage_id NVARCHAR(450),
                        label NVARCHAR(450),
                        check_type NVARCHAR(64) NOT NULL,
                        status NVARCHAR(64) NOT NULL CONSTRAINT DF_check_runs_status DEFAULT 'Pending',
                        environment_name NVARCHAR(450),
                        command NVARCHAR(MAX) NOT NULL,
                        working_directory NVARCHAR(MAX),
                        branch_name NVARCHAR(450),
                        commit_hash NVARCHAR(450),
                        exit_code INT NULL,
                        output NVARCHAR(MAX),
                        summary NVARCHAR(MAX),
                        artifacts_json NVARCHAR(MAX),
                        duration_ms BIGINT NULL,
                        started_utc NVARCHAR(450) NULL,
                        completed_utc NVARCHAR(450) NULL,
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_check_runs_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        CONSTRAINT FK_check_runs_user FOREIGN KEY (user_id) REFERENCES users(id),
                        CONSTRAINT FK_check_runs_workflow_profile FOREIGN KEY (workflow_profile_id) REFERENCES workflow_profiles(id),
                        CONSTRAINT FK_check_runs_vessel FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
                        CONSTRAINT FK_check_runs_mission FOREIGN KEY (mission_id) REFERENCES missions(id) ON DELETE SET NULL,
                        CONSTRAINT FK_check_runs_voyage FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE SET NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_tenant_created') CREATE INDEX idx_check_runs_tenant_created ON check_runs(tenant_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_user_created') CREATE INDEX idx_check_runs_user_created ON check_runs(user_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_vessel_created') CREATE INDEX idx_check_runs_vessel_created ON check_runs(vessel_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_mission_created') CREATE INDEX idx_check_runs_mission_created ON check_runs(mission_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_voyage_created') CREATE INDEX idx_check_runs_voyage_created ON check_runs(voyage_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_profile_created') CREATE INDEX idx_check_runs_profile_created ON check_runs(workflow_profile_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_type_created') CREATE INDEX idx_check_runs_type_created ON check_runs(check_type, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_status_created') CREATE INDEX idx_check_runs_status_created ON check_runs(status, created_utc DESC);"
                ),
                new SchemaMigration(
                    34,
                    "Add structured parsing summaries to check runs",
                    @"IF COL_LENGTH('check_runs', 'test_summary_json') IS NULL ALTER TABLE check_runs ADD test_summary_json NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('check_runs', 'coverage_summary_json') IS NULL ALTER TABLE check_runs ADD coverage_summary_json NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    35,
                    "Add workflow check expansion and landing readiness fields",
                    @"IF COL_LENGTH('workflow_profiles', 'migration_command') IS NULL ALTER TABLE workflow_profiles ADD migration_command NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('workflow_profiles', 'security_scan_command') IS NULL ALTER TABLE workflow_profiles ADD security_scan_command NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('workflow_profiles', 'performance_command') IS NULL ALTER TABLE workflow_profiles ADD performance_command NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('workflow_profiles', 'deployment_verification_command') IS NULL ALTER TABLE workflow_profiles ADD deployment_verification_command NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('workflow_profiles', 'rollback_verification_command') IS NULL ALTER TABLE workflow_profiles ADD rollback_verification_command NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessels', 'require_passing_checks_to_land') IS NULL ALTER TABLE vessels ADD require_passing_checks_to_land BIT NOT NULL CONSTRAINT DF_vessels_require_passing_checks_to_land DEFAULT 0;"
                ),
                new SchemaMigration(
                    36,
                    "Add external check metadata and landing branch policy fields",
                    @"IF COL_LENGTH('check_runs', 'source') IS NULL ALTER TABLE check_runs ADD source NVARCHAR(64) NOT NULL CONSTRAINT DF_check_runs_source DEFAULT 'Armada';",
                    @"IF COL_LENGTH('check_runs', 'provider_name') IS NULL ALTER TABLE check_runs ADD provider_name NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('check_runs', 'external_id') IS NULL ALTER TABLE check_runs ADD external_id NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('check_runs', 'external_url') IS NULL ALTER TABLE check_runs ADD external_url NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessels', 'protected_branch_patterns_json') IS NULL ALTER TABLE vessels ADD protected_branch_patterns_json NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessels', 'release_branch_prefix') IS NULL ALTER TABLE vessels ADD release_branch_prefix NVARCHAR(450) NOT NULL CONSTRAINT DF_vessels_release_branch_prefix DEFAULT 'release/';",
                    @"IF COL_LENGTH('vessels', 'hotfix_branch_prefix') IS NULL ALTER TABLE vessels ADD hotfix_branch_prefix NVARCHAR(450) NOT NULL CONSTRAINT DF_vessels_hotfix_branch_prefix DEFAULT 'hotfix/';",
                    @"IF COL_LENGTH('vessels', 'require_pull_request_for_protected_branches') IS NULL ALTER TABLE vessels ADD require_pull_request_for_protected_branches BIT NOT NULL CONSTRAINT DF_vessels_require_pull_request_for_protected_branches DEFAULT 0;",
                    @"IF COL_LENGTH('vessels', 'require_merge_queue_for_release_branches') IS NULL ALTER TABLE vessels ADD require_merge_queue_for_release_branches BIT NOT NULL CONSTRAINT DF_vessels_require_merge_queue_for_release_branches DEFAULT 0;"
                ),
                new SchemaMigration(
                    37,
                    "Add releases",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'releases')
                    CREATE TABLE releases (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        vessel_id NVARCHAR(450),
                        workflow_profile_id NVARCHAR(450),
                        title NVARCHAR(450) NOT NULL,
                        version NVARCHAR(450),
                        tag_name NVARCHAR(450),
                        summary NVARCHAR(MAX),
                        notes NVARCHAR(MAX),
                        status NVARCHAR(64) NOT NULL CONSTRAINT DF_releases_status DEFAULT 'Draft',
                        voyage_ids_json NVARCHAR(MAX),
                        mission_ids_json NVARCHAR(MAX),
                        check_run_ids_json NVARCHAR(MAX),
                        artifacts_json NVARCHAR(MAX),
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        published_utc NVARCHAR(450),
                        CONSTRAINT FK_releases_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        CONSTRAINT FK_releases_user FOREIGN KEY (user_id) REFERENCES users(id),
                        CONSTRAINT FK_releases_vessel FOREIGN KEY (vessel_id) REFERENCES vessels(id),
                        CONSTRAINT FK_releases_workflow_profile FOREIGN KEY (workflow_profile_id) REFERENCES workflow_profiles(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_releases_tenant_created') CREATE INDEX idx_releases_tenant_created ON releases(tenant_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_releases_user_created') CREATE INDEX idx_releases_user_created ON releases(user_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_releases_vessel_created') CREATE INDEX idx_releases_vessel_created ON releases(vessel_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_releases_profile_created') CREATE INDEX idx_releases_profile_created ON releases(workflow_profile_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_releases_status_created') CREATE INDEX idx_releases_status_created ON releases(status, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_releases_published') CREATE INDEX idx_releases_published ON releases(published_utc DESC);"
                ),
                new SchemaMigration(
                    38,
                    "Add deployment environments",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'environments')
                    CREATE TABLE environments (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        vessel_id NVARCHAR(450),
                        name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX),
                        kind NVARCHAR(64) NOT NULL CONSTRAINT DF_environments_kind DEFAULT 'Development',
                        configuration_source NVARCHAR(MAX),
                        base_url NVARCHAR(MAX),
                        health_endpoint NVARCHAR(MAX),
                        access_notes NVARCHAR(MAX),
                        deployment_rules NVARCHAR(MAX),
                        requires_approval BIT NOT NULL CONSTRAINT DF_environments_requires_approval DEFAULT 0,
                        is_default BIT NOT NULL CONSTRAINT DF_environments_is_default DEFAULT 0,
                        active BIT NOT NULL CONSTRAINT DF_environments_active DEFAULT 1,
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_environments_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        CONSTRAINT FK_environments_user FOREIGN KEY (user_id) REFERENCES users(id),
                        CONSTRAINT FK_environments_vessel FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_environments_tenant_created') CREATE INDEX idx_environments_tenant_created ON environments(tenant_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_environments_user_created') CREATE INDEX idx_environments_user_created ON environments(user_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_environments_vessel_name') CREATE INDEX idx_environments_vessel_name ON environments(vessel_id, name);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_environments_kind') CREATE INDEX idx_environments_kind ON environments(kind);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_environments_default') CREATE INDEX idx_environments_default ON environments(is_default);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_environments_active') CREATE INDEX idx_environments_active ON environments(active);"
                ),
                new SchemaMigration(
                    39,
                    "Add deployments",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'deployments')
                    CREATE TABLE deployments (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        vessel_id NVARCHAR(450),
                        workflow_profile_id NVARCHAR(450),
                        environment_id NVARCHAR(450),
                        environment_name NVARCHAR(450),
                        release_id NVARCHAR(450),
                        mission_id NVARCHAR(450),
                        voyage_id NVARCHAR(450),
                        title NVARCHAR(450) NOT NULL,
                        source_ref NVARCHAR(MAX),
                        summary NVARCHAR(MAX),
                        notes NVARCHAR(MAX),
                        status NVARCHAR(64) NOT NULL CONSTRAINT DF_deployments_status DEFAULT 'PendingApproval',
                        verification_status NVARCHAR(64) NOT NULL CONSTRAINT DF_deployments_verification_status DEFAULT 'NotRun',
                        approval_required BIT NOT NULL CONSTRAINT DF_deployments_approval_required DEFAULT 0,
                        approved_by_user_id NVARCHAR(450),
                        approved_utc NVARCHAR(450),
                        approval_comment NVARCHAR(MAX),
                        deploy_check_run_id NVARCHAR(450),
                        smoke_test_check_run_id NVARCHAR(450),
                        health_check_run_id NVARCHAR(450),
                        deployment_verification_check_run_id NVARCHAR(450),
                        rollback_check_run_id NVARCHAR(450),
                        rollback_verification_check_run_id NVARCHAR(450),
                        check_run_ids_json NVARCHAR(MAX),
                        request_history_summary_json NVARCHAR(MAX),
                        created_utc NVARCHAR(450) NOT NULL,
                        started_utc NVARCHAR(450),
                        completed_utc NVARCHAR(450),
                        verified_utc NVARCHAR(450),
                        rolled_back_utc NVARCHAR(450),
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_deployments_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        CONSTRAINT FK_deployments_user FOREIGN KEY (user_id) REFERENCES users(id),
                        CONSTRAINT FK_deployments_vessel FOREIGN KEY (vessel_id) REFERENCES vessels(id),
                        CONSTRAINT FK_deployments_workflow_profile FOREIGN KEY (workflow_profile_id) REFERENCES workflow_profiles(id),
                        CONSTRAINT FK_deployments_environment FOREIGN KEY (environment_id) REFERENCES environments(id),
                        CONSTRAINT FK_deployments_release FOREIGN KEY (release_id) REFERENCES releases(id),
                        CONSTRAINT FK_deployments_mission FOREIGN KEY (mission_id) REFERENCES missions(id),
                        CONSTRAINT FK_deployments_voyage FOREIGN KEY (voyage_id) REFERENCES voyages(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_deployments_tenant_created') CREATE INDEX idx_deployments_tenant_created ON deployments(tenant_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_deployments_user_created') CREATE INDEX idx_deployments_user_created ON deployments(user_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_deployments_vessel_created') CREATE INDEX idx_deployments_vessel_created ON deployments(vessel_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_deployments_profile_created') CREATE INDEX idx_deployments_profile_created ON deployments(workflow_profile_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_deployments_environment_created') CREATE INDEX idx_deployments_environment_created ON deployments(environment_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_deployments_release_created') CREATE INDEX idx_deployments_release_created ON deployments(release_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_deployments_status_created') CREATE INDEX idx_deployments_status_created ON deployments(status, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_deployments_verification_created') CREATE INDEX idx_deployments_verification_created ON deployments(verification_status, created_utc DESC);"
                ),
                new SchemaMigration(
                    40,
                    "Add deployment-linked checks and rollout monitoring",
                    @"IF COL_LENGTH('check_runs', 'deployment_id') IS NULL ALTER TABLE check_runs ADD deployment_id NVARCHAR(450) NULL;",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_check_runs_deployment_created') CREATE INDEX idx_check_runs_deployment_created ON check_runs(deployment_id, created_utc DESC);",
                    @"IF COL_LENGTH('environments', 'verification_definitions_json') IS NULL ALTER TABLE environments ADD verification_definitions_json NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('environments', 'rollout_monitoring_window_minutes') IS NULL ALTER TABLE environments ADD rollout_monitoring_window_minutes INT NOT NULL CONSTRAINT DF_environments_rollout_monitoring_window_minutes DEFAULT 0;",
                    @"IF COL_LENGTH('environments', 'rollout_monitoring_interval_seconds') IS NULL ALTER TABLE environments ADD rollout_monitoring_interval_seconds INT NOT NULL CONSTRAINT DF_environments_rollout_monitoring_interval_seconds DEFAULT 300;",
                    @"IF COL_LENGTH('environments', 'alert_on_regression') IS NULL ALTER TABLE environments ADD alert_on_regression BIT NOT NULL CONSTRAINT DF_environments_alert_on_regression DEFAULT 1;",
                    @"IF COL_LENGTH('deployments', 'monitoring_window_ends_utc') IS NULL ALTER TABLE deployments ADD monitoring_window_ends_utc NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('deployments', 'last_monitored_utc') IS NULL ALTER TABLE deployments ADD last_monitored_utc NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('deployments', 'last_regression_alert_utc') IS NULL ALTER TABLE deployments ADD last_regression_alert_utc NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('deployments', 'latest_monitoring_summary') IS NULL ALTER TABLE deployments ADD latest_monitoring_summary NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('deployments', 'monitoring_failure_count') IS NULL ALTER TABLE deployments ADD monitoring_failure_count INT NOT NULL CONSTRAINT DF_deployments_monitoring_failure_count DEFAULT 0;"
                ),
                new SchemaMigration(
                    41,
                    "Add vessel GitHub token overrides",
                    @"IF COL_LENGTH('vessels', 'github_token_override') IS NULL ALTER TABLE vessels ADD github_token_override NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    42,
                    "Add normalized objectives backlog tables",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'objectives')
                    CREATE TABLE objectives (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        title NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX),
                        status NVARCHAR(64) NOT NULL CONSTRAINT DF_objectives_status DEFAULT 'Draft',
                        kind NVARCHAR(64) NOT NULL CONSTRAINT DF_objectives_kind DEFAULT 'Feature',
                        category NVARCHAR(450),
                        priority NVARCHAR(64) NOT NULL CONSTRAINT DF_objectives_priority DEFAULT 'P2',
                        rank INT NOT NULL CONSTRAINT DF_objectives_rank DEFAULT 0,
                        backlog_state NVARCHAR(64) NOT NULL CONSTRAINT DF_objectives_backlog_state DEFAULT 'Inbox',
                        effort NVARCHAR(64) NOT NULL CONSTRAINT DF_objectives_effort DEFAULT 'M',
                        owner NVARCHAR(450),
                        target_version NVARCHAR(450),
                        due_utc NVARCHAR(450),
                        parent_objective_id NVARCHAR(450),
                        blocked_by_objective_ids_json NVARCHAR(MAX),
                        refinement_summary NVARCHAR(MAX),
                        suggested_pipeline_id NVARCHAR(450),
                        suggested_playbooks_json NVARCHAR(MAX),
                        tags_json NVARCHAR(MAX),
                        acceptance_criteria_json NVARCHAR(MAX),
                        non_goals_json NVARCHAR(MAX),
                        rollout_constraints_json NVARCHAR(MAX),
                        evidence_links_json NVARCHAR(MAX),
                        fleet_ids_json NVARCHAR(MAX),
                        vessel_ids_json NVARCHAR(MAX),
                        planning_session_ids_json NVARCHAR(MAX),
                        refinement_session_ids_json NVARCHAR(MAX),
                        voyage_ids_json NVARCHAR(MAX),
                        mission_ids_json NVARCHAR(MAX),
                        check_run_ids_json NVARCHAR(MAX),
                        release_ids_json NVARCHAR(MAX),
                        deployment_ids_json NVARCHAR(MAX),
                        incident_ids_json NVARCHAR(MAX),
                        source_provider NVARCHAR(450),
                        source_type NVARCHAR(450),
                        source_id NVARCHAR(450),
                        source_url NVARCHAR(MAX),
                        source_updated_utc NVARCHAR(450),
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        completed_utc NVARCHAR(450),
                        CONSTRAINT FK_objectives_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
                        CONSTRAINT FK_objectives_user FOREIGN KEY (user_id) REFERENCES users(id),
                        CONSTRAINT FK_objectives_parent FOREIGN KEY (parent_objective_id) REFERENCES objectives(id),
                        CONSTRAINT FK_objectives_pipeline FOREIGN KEY (suggested_pipeline_id) REFERENCES pipelines(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objectives_tenant_status_updated') CREATE INDEX idx_objectives_tenant_status_updated ON objectives(tenant_id, status, last_update_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objectives_tenant_backlog_priority_rank') CREATE INDEX idx_objectives_tenant_backlog_priority_rank ON objectives(tenant_id, backlog_state, priority, rank);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objectives_tenant_kind_priority') CREATE INDEX idx_objectives_tenant_kind_priority ON objectives(tenant_id, kind, priority);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objectives_tenant_owner') CREATE INDEX idx_objectives_tenant_owner ON objectives(tenant_id, owner);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objectives_tenant_due') CREATE INDEX idx_objectives_tenant_due ON objectives(tenant_id, due_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objectives_tenant_target_version') CREATE INDEX idx_objectives_tenant_target_version ON objectives(tenant_id, target_version);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objectives_tenant_parent') CREATE INDEX idx_objectives_tenant_parent ON objectives(tenant_id, parent_objective_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objectives_tenant_source') CREATE INDEX idx_objectives_tenant_source ON objectives(tenant_id, source_provider, source_type, source_id);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'objective_refinement_sessions')
                    CREATE TABLE objective_refinement_sessions (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        objective_id NVARCHAR(450) NOT NULL,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        captain_id NVARCHAR(450) NOT NULL,
                        fleet_id NVARCHAR(450),
                        vessel_id NVARCHAR(450),
                        title NVARCHAR(450) NOT NULL,
                        status NVARCHAR(64) NOT NULL CONSTRAINT DF_objective_refinement_sessions_status DEFAULT 'Created',
                        process_id INT NULL,
                        failure_reason NVARCHAR(MAX),
                        created_utc NVARCHAR(450) NOT NULL,
                        started_utc NVARCHAR(450),
                        completed_utc NVARCHAR(450),
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_objective_refinement_sessions_objective FOREIGN KEY (objective_id) REFERENCES objectives(id) ON DELETE CASCADE,
                        CONSTRAINT FK_objective_refinement_sessions_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
                        CONSTRAINT FK_objective_refinement_sessions_user FOREIGN KEY (user_id) REFERENCES users(id),
                        CONSTRAINT FK_objective_refinement_sessions_captain FOREIGN KEY (captain_id) REFERENCES captains(id),
                        CONSTRAINT FK_objective_refinement_sessions_fleet FOREIGN KEY (fleet_id) REFERENCES fleets(id),
                        CONSTRAINT FK_objective_refinement_sessions_vessel FOREIGN KEY (vessel_id) REFERENCES vessels(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objective_refinement_sessions_tenant_objective_created') CREATE INDEX idx_objective_refinement_sessions_tenant_objective_created ON objective_refinement_sessions(tenant_id, objective_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objective_refinement_sessions_tenant_captain_status') CREATE INDEX idx_objective_refinement_sessions_tenant_captain_status ON objective_refinement_sessions(tenant_id, captain_id, status);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'objective_refinement_messages')
                    CREATE TABLE objective_refinement_messages (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        objective_refinement_session_id NVARCHAR(450) NOT NULL,
                        objective_id NVARCHAR(450) NOT NULL,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        role NVARCHAR(64) NOT NULL,
                        sequence INT NOT NULL,
                        content NVARCHAR(MAX) NOT NULL,
                        is_selected BIT NOT NULL CONSTRAINT DF_objective_refinement_messages_is_selected DEFAULT 0,
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL,
                        CONSTRAINT FK_objective_refinement_messages_session FOREIGN KEY (objective_refinement_session_id) REFERENCES objective_refinement_sessions(id) ON DELETE CASCADE,
                        CONSTRAINT FK_objective_refinement_messages_objective FOREIGN KEY (objective_id) REFERENCES objectives(id),
                        CONSTRAINT FK_objective_refinement_messages_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
                        CONSTRAINT FK_objective_refinement_messages_user FOREIGN KEY (user_id) REFERENCES users(id)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objective_refinement_messages_session_sequence') CREATE INDEX idx_objective_refinement_messages_session_sequence ON objective_refinement_messages(objective_refinement_session_id, sequence);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_objective_refinement_messages_objective_created') CREATE INDEX idx_objective_refinement_messages_objective_created ON objective_refinement_messages(objective_id, created_utc DESC);"
                ),
                new SchemaMigration(
                    44,
                    "Reliability release: dock leases, process liveness, review deadline, merge retry, coordination leases",
                    @"IF COL_LENGTH('docks', 'state') IS NULL ALTER TABLE docks ADD state NVARCHAR(32) NOT NULL CONSTRAINT DF_docks_state DEFAULT 'Available';",
                    @"IF COL_LENGTH('docks', 'lease_expires_utc') IS NULL ALTER TABLE docks ADD lease_expires_utc DATETIME2 NULL;",
                    @"IF COL_LENGTH('docks', 'owner_token') IS NULL ALTER TABLE docks ADD owner_token NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('captains', 'last_process_alive_utc') IS NULL ALTER TABLE captains ADD last_process_alive_utc DATETIME2 NULL;",
                    @"IF COL_LENGTH('missions', 'review_deadline_utc') IS NULL ALTER TABLE missions ADD review_deadline_utc DATETIME2 NULL;",
                    @"IF COL_LENGTH('merge_entries', 'retry_count') IS NULL ALTER TABLE merge_entries ADD retry_count INT NOT NULL CONSTRAINT DF_merge_entries_retry_count DEFAULT 0;",
                    @"IF COL_LENGTH('merge_entries', 'lease_expires_utc') IS NULL ALTER TABLE merge_entries ADD lease_expires_utc DATETIME2 NULL;",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'coordination_leases')
                    CREATE TABLE coordination_leases (
                        name NVARCHAR(255) NOT NULL PRIMARY KEY,
                        holder NVARCHAR(MAX) NOT NULL,
                        tenant_id NVARCHAR(255) NULL,
                        acquired_utc DATETIME2 NOT NULL,
                        expires_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_coordination_leases_expires') CREATE INDEX idx_coordination_leases_expires ON coordination_leases(expires_utc);"
                ),
                new SchemaMigration(
                    45,
                    "Add project_profiles for per-project persona/pipeline/skill customization",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'project_profiles')
                    CREATE TABLE project_profiles (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450) NULL,
                        user_id NVARCHAR(450) NULL,
                        name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX) NULL,
                        scope NVARCHAR(64) NOT NULL CONSTRAINT DF_project_profiles_scope DEFAULT 'Global',
                        fleet_id NVARCHAR(450) NULL,
                        vessel_id NVARCHAR(450) NULL,
                        is_default BIT NOT NULL CONSTRAINT DF_project_profiles_is_default DEFAULT 0,
                        active BIT NOT NULL CONSTRAINT DF_project_profiles_active DEFAULT 1,
                        default_pipeline_id NVARCHAR(450) NULL,
                        workflow_profile_id NVARCHAR(450) NULL,
                        persona_overrides_json NVARCHAR(MAX) NULL,
                        skills_json NVARCHAR(MAX) NULL,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_project_profiles_tenant') CREATE INDEX idx_project_profiles_tenant ON project_profiles(tenant_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_project_profiles_scope') CREATE INDEX idx_project_profiles_scope ON project_profiles(scope);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_project_profiles_fleet') CREATE INDEX idx_project_profiles_fleet ON project_profiles(fleet_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_project_profiles_vessel') CREATE INDEX idx_project_profiles_vessel ON project_profiles(vessel_id);"
                ),
                new SchemaMigration(
                    46,
                    "Add skills directory",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'skills')
                    CREATE TABLE skills (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450) NULL,
                        user_id NVARCHAR(450) NULL,
                        name NVARCHAR(450) NOT NULL,
                        description NVARCHAR(MAX) NULL,
                        category NVARCHAR(255) NULL,
                        content NVARCHAR(MAX) NULL,
                        is_built_in BIT NOT NULL CONSTRAINT DF_skills_is_built_in DEFAULT 0,
                        active BIT NOT NULL CONSTRAINT DF_skills_active DEFAULT 1,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_skills_tenant') CREATE INDEX idx_skills_tenant ON skills(tenant_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_skills_category') CREATE INDEX idx_skills_category ON skills(category);"
                ),
                new SchemaMigration(
                    47,
                    "Add captain reasoning_effort/tier, mission redispatch_attempts/tier, vessel secret scan and privacy fields",
                    @"IF COL_LENGTH('captains', 'reasoning_effort') IS NULL ALTER TABLE captains ADD reasoning_effort NVARCHAR(64) NULL;",
                    @"IF COL_LENGTH('captains', 'tier') IS NULL ALTER TABLE captains ADD tier NVARCHAR(32) NULL;",
                    @"IF COL_LENGTH('missions', 'redispatch_attempts') IS NULL ALTER TABLE missions ADD redispatch_attempts INT NOT NULL CONSTRAINT DF_missions_redispatch_attempts DEFAULT 0;",
                    @"IF COL_LENGTH('missions', 'tier') IS NULL ALTER TABLE missions ADD tier NVARCHAR(32) NULL;",
                    @"IF COL_LENGTH('vessels', 'secret_scan_enabled') IS NULL ALTER TABLE vessels ADD secret_scan_enabled BIT NOT NULL CONSTRAINT DF_vessels_secret_scan_enabled DEFAULT 0;",
                    @"IF COL_LENGTH('vessels', 'protected_path_patterns_json') IS NULL ALTER TABLE vessels ADD protected_path_patterns_json NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessels', 'private_identifier_denylist_json') IS NULL ALTER TABLE vessels ADD private_identifier_denylist_json NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    48,
                    "Add vessel auto-land policy fields and captain quarantine fields",
                    @"IF COL_LENGTH('vessels', 'auto_land_enabled') IS NULL ALTER TABLE vessels ADD auto_land_enabled BIT NOT NULL CONSTRAINT DF_vessels_auto_land_enabled DEFAULT 0;",
                    @"IF COL_LENGTH('vessels', 'auto_land_max_files') IS NULL ALTER TABLE vessels ADD auto_land_max_files INT NOT NULL CONSTRAINT DF_vessels_auto_land_max_files DEFAULT 0;",
                    @"IF COL_LENGTH('vessels', 'auto_land_max_lines') IS NULL ALTER TABLE vessels ADD auto_land_max_lines INT NOT NULL CONSTRAINT DF_vessels_auto_land_max_lines DEFAULT 0;",
                    @"IF COL_LENGTH('vessels', 'auto_land_path_allow_globs_json') IS NULL ALTER TABLE vessels ADD auto_land_path_allow_globs_json NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessels', 'auto_land_path_deny_globs_json') IS NULL ALTER TABLE vessels ADD auto_land_path_deny_globs_json NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('captains', 'quarantine_until_utc') IS NULL ALTER TABLE captains ADD quarantine_until_utc NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('captains', 'quarantine_reason') IS NULL ALTER TABLE captains ADD quarantine_reason NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    49,
                    "Add jobs table for request-independent background jobs",
                    Jobs
                ),
                new SchemaMigration(
                    55,
                    "Add per-step captain selection (persona default captain, mission requested captain, voyage captain overrides)",
                    @"IF COL_LENGTH('personas', 'default_captain_id') IS NULL ALTER TABLE personas ADD default_captain_id NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('missions', 'requested_captain_id') IS NULL ALTER TABLE missions ADD requested_captain_id NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('voyages', 'captain_overrides_json') IS NULL ALTER TABLE voyages ADD captain_overrides_json NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    56,
                    "Add token_usage table for per-model token accounting",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'token_usage')
                    CREATE TABLE token_usage (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        model NVARCHAR(255) NOT NULL CONSTRAINT DF_token_usage_model DEFAULT '',
                        runtime NVARCHAR(64),
                        source NVARCHAR(32) NOT NULL,
                        source_id NVARCHAR(450),
                        vessel_id NVARCHAR(450),
                        captain_id NVARCHAR(450),
                        input_tokens BIGINT NOT NULL CONSTRAINT DF_token_usage_input_tokens DEFAULT 0,
                        output_tokens BIGINT NOT NULL CONSTRAINT DF_token_usage_output_tokens DEFAULT 0,
                        cached_tokens BIGINT NOT NULL CONSTRAINT DF_token_usage_cached_tokens DEFAULT 0,
                        total_tokens BIGINT NOT NULL CONSTRAINT DF_token_usage_total_tokens DEFAULT 0,
                        estimated BIT NOT NULL CONSTRAINT DF_token_usage_estimated DEFAULT 0,
                        created_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_token_usage_created') CREATE INDEX idx_token_usage_created ON token_usage(created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_token_usage_tenant_created') CREATE INDEX idx_token_usage_tenant_created ON token_usage(tenant_id, created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_token_usage_model') CREATE INDEX idx_token_usage_model ON token_usage(model);"
                ),
                new SchemaMigration(
                    57,
                    "Add mission execution mode (Implementation/Audit/Research)",
                    @"IF COL_LENGTH('missions', 'mode') IS NULL ALTER TABLE missions ADD mode NVARCHAR(32) NOT NULL CONSTRAINT DF_missions_mode DEFAULT 'Implementation';"
                ),
                new SchemaMigration(
                    58,
                    "Add in-dock Definition-of-Done gate config to vessels",
                    @"IF COL_LENGTH('vessels', 'definition_of_done_enabled') IS NULL ALTER TABLE vessels ADD definition_of_done_enabled BIT NOT NULL CONSTRAINT DF_vessels_dod_enabled DEFAULT 0;",
                    @"IF COL_LENGTH('vessels', 'definition_of_done_build_command') IS NULL ALTER TABLE vessels ADD definition_of_done_build_command NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessels', 'definition_of_done_test_command') IS NULL ALTER TABLE vessels ADD definition_of_done_test_command NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessels', 'definition_of_done_timeout_seconds') IS NULL ALTER TABLE vessels ADD definition_of_done_timeout_seconds INT NOT NULL CONSTRAINT DF_vessels_dod_timeout DEFAULT 1800;"
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
                github_token_override NVARCHAR(MAX),
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
                runtime_options_json NVARCHAR(MAX),
                state NVARCHAR(450) NOT NULL DEFAULT 'Idle',
                current_mission_id NVARCHAR(450),
                current_dock_id NVARCHAR(450),
                process_id INT,
                recovery_attempts INT NOT NULL DEFAULT 0,
                last_heartbeat_utc NVARCHAR(450),
                last_process_alive_utc DATETIME2 NULL,
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
                agent_output NVARCHAR(MAX),
                review_deadline_utc DATETIME2 NULL,
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
                state NVARCHAR(32) NOT NULL DEFAULT 'Available',
                lease_expires_utc DATETIME2 NULL,
                owner_token NVARCHAR(MAX) NULL,
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
                retry_count INT NOT NULL DEFAULT 0,
                lease_expires_utc DATETIME2 NULL,
                created_utc NVARCHAR(450) NOT NULL,
                last_update_utc NVARCHAR(450) NOT NULL,
                test_started_utc NVARCHAR(450),
                completed_utc NVARCHAR(450)
            );";

        /// <summary>
        /// Coordination leases table. Backs restart-safe, multi-instance-safe mutual exclusion via
        /// atomic compare-and-swap on the lease name with TTL-based takeover.
        /// </summary>
        public static readonly string CoordinationLeases = @"
            CREATE TABLE coordination_leases (
                name NVARCHAR(255) NOT NULL PRIMARY KEY,
                holder NVARCHAR(MAX) NOT NULL,
                tenant_id NVARCHAR(255) NULL,
                acquired_utc DATETIME2 NOT NULL,
                expires_utc DATETIME2 NOT NULL
            );";

        /// <summary>
        /// Jobs table. Backs request-independent background jobs with a time-ordered id and pollable status.
        /// </summary>
        public static readonly string Jobs = @"
            IF OBJECT_ID(N'jobs') IS NULL
            CREATE TABLE jobs (
                id NVARCHAR(450) NOT NULL PRIMARY KEY,
                tenant_id NVARCHAR(450),
                user_id NVARCHAR(450),
                name NVARCHAR(MAX) NOT NULL,
                kind NVARCHAR(64) NOT NULL CONSTRAINT DF_jobs_kind DEFAULT 'Generic',
                status NVARCHAR(64) NOT NULL CONSTRAINT DF_jobs_status DEFAULT 'Queued',
                progress INT NOT NULL CONSTRAINT DF_jobs_progress DEFAULT 0,
                result_json NVARCHAR(MAX),
                error_reason NVARCHAR(MAX),
                created_utc NVARCHAR(450) NOT NULL,
                started_utc NVARCHAR(450),
                completed_utc NVARCHAR(450),
                last_update_utc NVARCHAR(450) NOT NULL
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
            "CREATE INDEX idx_merge_entries_tenant_mission ON merge_entries(tenant_id, mission_id);",

            // Coordination leases
            "CREATE INDEX idx_coordination_leases_expires ON coordination_leases(expires_utc);"
        };

        #endregion
    }
}
