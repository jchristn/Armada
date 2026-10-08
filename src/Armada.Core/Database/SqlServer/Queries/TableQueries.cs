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
                )
                {
                    // The initial CREATE TABLE / CREATE INDEX statements are not re-runnable. SQL Server DDL is
                    // transactional, so the tenants table existing means the whole migration was applied.
                    AlreadyAppliedCheckSql = @"SELECT CASE WHEN OBJECT_ID(N'tenants', N'U') IS NOT NULL THEN 1 ELSE 0 END;"
                },
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
                ),
                new SchemaMigration(
                    59,
                    "Add git_anchors_json to docks",
                    @"IF COL_LENGTH('docks','git_anchors_json') IS NULL ALTER TABLE docks ADD git_anchors_json NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    60,
                    "Add model_endpoints table for managed embedding/inference endpoints",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'model_endpoints')
                    CREATE TABLE model_endpoints (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        name NVARCHAR(450) NOT NULL,
                        kind NVARCHAR(64) NOT NULL,
                        provider NVARCHAR(64) NOT NULL,
                        base_url NVARCHAR(2048) NOT NULL,
                        api_key NVARCHAR(MAX),
                        model NVARCHAR(450),
                        dimensionality INT NOT NULL CONSTRAINT DF_model_endpoints_dimensionality DEFAULT 0,
                        timeout_ms INT NOT NULL CONSTRAINT DF_model_endpoints_timeout_ms DEFAULT 120000,
                        enabled BIT NOT NULL CONSTRAINT DF_model_endpoints_enabled DEFAULT 1,
                        health_status NVARCHAR(64) NOT NULL CONSTRAINT DF_model_endpoints_health_status DEFAULT 'Unknown',
                        last_health_check_utc NVARCHAR(450),
                        last_health_error NVARCHAR(MAX),
                        last_latency_ms BIGINT,
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_model_endpoints_created') CREATE INDEX idx_model_endpoints_created ON model_endpoints(created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_model_endpoints_tenant') CREATE INDEX idx_model_endpoints_tenant ON model_endpoints(tenant_id);"
                ),
                new SchemaMigration(
                    61,
                    "Add rolling health-check history to model_endpoints",
                    @"IF COL_LENGTH('model_endpoints', 'health_history_json') IS NULL ALTER TABLE model_endpoints ADD health_history_json NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    62,
                    "Add harbors and harbor_capabilities tables for host runners",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'harbors')
                    CREATE TABLE harbors (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        name NVARCHAR(450) NOT NULL,
                        connection_status NVARCHAR(64) NOT NULL CONSTRAINT DF_harbors_connection_status DEFAULT 'Unknown',
                        max_concurrent_jobs INT NOT NULL CONSTRAINT DF_harbors_max_concurrent_jobs DEFAULT 4,
                        enabled BIT NOT NULL CONSTRAINT DF_harbors_enabled DEFAULT 1,
                        protocol_version NVARCHAR(450),
                        os_platform NVARCHAR(450),
                        architecture NVARCHAR(450),
                        last_seen_utc NVARCHAR(450),
                        last_connected_utc NVARCHAR(450),
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbors_created') CREATE INDEX idx_harbors_created ON harbors(created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbors_tenant') CREATE INDEX idx_harbors_tenant ON harbors(tenant_id);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'harbor_capabilities')
                    CREATE TABLE harbor_capabilities (
                        harbor_id NVARCHAR(450) NOT NULL,
                        name NVARCHAR(450) NOT NULL,
                        available BIT NOT NULL CONSTRAINT DF_harbor_capabilities_available DEFAULT 1,
                        detail NVARCHAR(MAX),
                        CONSTRAINT PK_harbor_capabilities PRIMARY KEY (harbor_id, name),
                        CONSTRAINT FK_harbor_capabilities_harbor FOREIGN KEY (harbor_id) REFERENCES harbors(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbor_capabilities_harbor') CREATE INDEX idx_harbor_capabilities_harbor ON harbor_capabilities(harbor_id);"
                ),
                new SchemaMigration(
                    63,
                    "Add Harbor routing and affinity columns",
                    @"IF COL_LENGTH('docks','harbor_id') IS NULL ALTER TABLE docks ADD harbor_id NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('missions','assigned_harbor_id') IS NULL ALTER TABLE missions ADD assigned_harbor_id NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('vessels','preferred_harbor_id') IS NULL ALTER TABLE vessels ADD preferred_harbor_id NVARCHAR(450) NULL;",
                    @"IF COL_LENGTH('vessels','required_capabilities') IS NULL ALTER TABLE vessels ADD required_capabilities NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    64,
                    "Add model_endpoint_id to captains for API-endpoint captains",
                    @"IF COL_LENGTH('captains','model_endpoint_id') IS NULL ALTER TABLE captains ADD model_endpoint_id NVARCHAR(450) NULL;"
                ),
                new SchemaMigration(
                    65,
                    "Add scope to model_endpoints (tenant-wide vs user-specific)",
                    @"IF COL_LENGTH('model_endpoints','scope') IS NULL ALTER TABLE model_endpoints ADD scope NVARCHAR(32) NOT NULL DEFAULT 'TenantWide';"
                ),
                new SchemaMigration(
                    66,
                    "Add ownership scope to playbooks and skills",
                    @"IF COL_LENGTH('playbooks','scope') IS NULL ALTER TABLE playbooks ADD scope NVARCHAR(32) NOT NULL DEFAULT 'TenantWide';",
                    @"IF COL_LENGTH('skills','scope') IS NULL ALTER TABLE skills ADD scope NVARCHAR(32) NOT NULL DEFAULT 'TenantWide';"
                ),
                new SchemaMigration(
                    67,
                    "Add ownership (user_id + scope) to personas, pipelines, prompt_templates",
                    @"IF COL_LENGTH('personas','user_id') IS NULL ALTER TABLE personas ADD user_id NVARCHAR(450);",
                    @"IF COL_LENGTH('personas','scope') IS NULL ALTER TABLE personas ADD scope NVARCHAR(32) NOT NULL DEFAULT 'TenantWide';",
                    @"IF COL_LENGTH('pipelines','user_id') IS NULL ALTER TABLE pipelines ADD user_id NVARCHAR(450);",
                    @"IF COL_LENGTH('pipelines','scope') IS NULL ALTER TABLE pipelines ADD scope NVARCHAR(32) NOT NULL DEFAULT 'TenantWide';",
                    @"IF COL_LENGTH('prompt_templates','user_id') IS NULL ALTER TABLE prompt_templates ADD user_id NVARCHAR(450);",
                    @"IF COL_LENGTH('prompt_templates','scope') IS NULL ALTER TABLE prompt_templates ADD scope NVARCHAR(32) NOT NULL DEFAULT 'TenantWide';"
                ),
                new SchemaMigration(
                    68,
                    "Add ownership_scope to workflow_profiles and project_profiles",
                    @"IF COL_LENGTH('workflow_profiles','ownership_scope') IS NULL ALTER TABLE workflow_profiles ADD ownership_scope NVARCHAR(32) NOT NULL DEFAULT 'TenantWide';",
                    @"IF COL_LENGTH('project_profiles','ownership_scope') IS NULL ALTER TABLE project_profiles ADD ownership_scope NVARCHAR(32) NOT NULL DEFAULT 'TenantWide';"
                ),
                new SchemaMigration(
                    69,
                    "Add cloud-provider fields (region, project, api_version, access_key_id) to model_endpoints",
                    @"IF COL_LENGTH('model_endpoints','region') IS NULL ALTER TABLE model_endpoints ADD region NVARCHAR(256) NULL;",
                    @"IF COL_LENGTH('model_endpoints','project') IS NULL ALTER TABLE model_endpoints ADD project NVARCHAR(256) NULL;",
                    @"IF COL_LENGTH('model_endpoints','api_version') IS NULL ALTER TABLE model_endpoints ADD api_version NVARCHAR(64) NULL;",
                    @"IF COL_LENGTH('model_endpoints','access_key_id') IS NULL ALTER TABLE model_endpoints ADD access_key_id NVARCHAR(256) NULL;"
                ),
                new SchemaMigration(
                    70,
                    "Add memories and memory_tags tables for durable agent memory",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'memories')
                    CREATE TABLE memories (
                        id NVARCHAR(450) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(450),
                        user_id NVARCHAR(450),
                        scope NVARCHAR(64) NOT NULL CONSTRAINT DF_memories_scope DEFAULT 'TenantWide',
                        type NVARCHAR(64) NOT NULL CONSTRAINT DF_memories_type DEFAULT 'Semantic',
                        topic NVARCHAR(450),
                        memory_key NVARCHAR(450),
                        summary NVARCHAR(MAX),
                        content NVARCHAR(MAX) NOT NULL CONSTRAINT DF_memories_content DEFAULT '',
                        salience FLOAT NOT NULL CONSTRAINT DF_memories_salience DEFAULT 0.5,
                        version INT NOT NULL CONSTRAINT DF_memories_version DEFAULT 1,
                        source_kind NVARCHAR(64) NOT NULL CONSTRAINT DF_memories_source_kind DEFAULT 'Manual',
                        source_voyage_id NVARCHAR(450),
                        source_mission_id NVARCHAR(450),
                        source_vessel_id NVARCHAR(450),
                        source_detail NVARCHAR(MAX),
                        vessel_id NVARCHAR(450),
                        created_utc NVARCHAR(450) NOT NULL,
                        last_update_utc NVARCHAR(450) NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_memories_created') CREATE INDEX idx_memories_created ON memories(created_utc DESC);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_memories_tenant') CREATE INDEX idx_memories_tenant ON memories(tenant_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_memories_tenant_user') CREATE INDEX idx_memories_tenant_user ON memories(tenant_id, user_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_memories_type') CREATE INDEX idx_memories_type ON memories(type);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_memories_vessel') CREATE INDEX idx_memories_vessel ON memories(vessel_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_memories_key') CREATE INDEX idx_memories_key ON memories(tenant_id, memory_key);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'memory_tags')
                    CREATE TABLE memory_tags (
                        memory_id NVARCHAR(450) NOT NULL,
                        tag NVARCHAR(450) NOT NULL,
                        CONSTRAINT PK_memory_tags PRIMARY KEY (memory_id, tag),
                        CONSTRAINT FK_memory_tags_memory FOREIGN KEY (memory_id) REFERENCES memories(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_memory_tags_memory') CREATE INDEX idx_memory_tags_memory ON memory_tags(memory_id);"
                ),
                new SchemaMigration(
                    71,
                    "Add vessel_import_batches and vessel_import_items tables for bulk vessel import",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vessel_import_batches')
                    CREATE TABLE vessel_import_batches (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        user_id NVARCHAR(64),
                        status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_import_batches_status DEFAULT 'Discovered',
                        harbor_id NVARCHAR(64),
                        fleet_id NVARCHAR(64),
                        job_id NVARCHAR(64),
                        requested_path_count INT NOT NULL CONSTRAINT DF_vessel_import_batches_requested_path_count DEFAULT 0,
                        candidate_count INT NOT NULL CONSTRAINT DF_vessel_import_batches_candidate_count DEFAULT 0,
                        created_count INT NOT NULL CONSTRAINT DF_vessel_import_batches_created_count DEFAULT 0,
                        skipped_count INT NOT NULL CONSTRAINT DF_vessel_import_batches_skipped_count DEFAULT 0,
                        failed_count INT NOT NULL CONSTRAINT DF_vessel_import_batches_failed_count DEFAULT 0,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        completed_utc DATETIME2
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_batches_tenant_created') CREATE INDEX idx_vessel_import_batches_tenant_created ON vessel_import_batches(tenant_id, created_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_batches_tenant_status') CREATE INDEX idx_vessel_import_batches_tenant_status ON vessel_import_batches(tenant_id, status);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vessel_import_items')
                    CREATE TABLE vessel_import_items (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        batch_id NVARCHAR(64) NOT NULL,
                        path NVARCHAR(700) NOT NULL,
                        proposed_name NVARCHAR(256) NOT NULL,
                        remote_url NVARCHAR(MAX),
                        default_branch NVARCHAR(256),
                        candidate_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_import_items_candidate_status DEFAULT 'New',
                        existing_vessel_id NVARCHAR(64),
                        outcome NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_import_items_outcome DEFAULT 'Pending',
                        outcome_reason NVARCHAR(256),
                        outcome_message NVARCHAR(MAX),
                        vessel_id NVARCHAR(64),
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_vessel_import_items_batch_id FOREIGN KEY (batch_id) REFERENCES vessel_import_batches(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_items_tenant_batch_path') CREATE UNIQUE INDEX idx_vessel_import_items_tenant_batch_path ON vessel_import_items(tenant_id, batch_id, path);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_items_batch') CREATE INDEX idx_vessel_import_items_batch ON vessel_import_items(batch_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_items_tenant_outcome') CREATE INDEX idx_vessel_import_items_tenant_outcome ON vessel_import_items(tenant_id, outcome);"
                ),
                new SchemaMigration(
                    72,
                    "Add fleet_actions, fleet_action_runs, and fleet_action_run_targets tables",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'fleet_actions')
                    CREATE TABLE fleet_actions (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        user_id NVARCHAR(64),
                        name NVARCHAR(256) NOT NULL,
                        description NVARCHAR(MAX),
                        kind NVARCHAR(32) NOT NULL CONSTRAINT DF_fleet_actions_kind DEFAULT 'Command',
                        command_text NVARCHAR(MAX),
                        prompt_template NVARCHAR(MAX),
                        pipeline_id NVARCHAR(64),
                        persona NVARCHAR(256),
                        timeout_seconds INT NOT NULL CONSTRAINT DF_fleet_actions_timeout_seconds DEFAULT 300,
                        default_concurrency INT NOT NULL CONSTRAINT DF_fleet_actions_default_concurrency DEFAULT 4,
                        requires_clean_working_tree BIT NOT NULL CONSTRAINT DF_fleet_actions_requires_clean_working_tree DEFAULT 1,
                        is_built_in BIT NOT NULL CONSTRAINT DF_fleet_actions_is_built_in DEFAULT 0,
                        built_in_key NVARCHAR(256),
                        active BIT NOT NULL CONSTRAINT DF_fleet_actions_active DEFAULT 1,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_actions_tenant_created') CREATE INDEX idx_fleet_actions_tenant_created ON fleet_actions(tenant_id, created_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_actions_tenant_name') CREATE INDEX idx_fleet_actions_tenant_name ON fleet_actions(tenant_id, name);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_actions_tenant_builtin') CREATE INDEX idx_fleet_actions_tenant_builtin ON fleet_actions(tenant_id, built_in_key);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_actions_tenant_active') CREATE INDEX idx_fleet_actions_tenant_active ON fleet_actions(tenant_id, active);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'fleet_action_runs')
                    CREATE TABLE fleet_action_runs (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        user_id NVARCHAR(64),
                        action_id NVARCHAR(64),
                        action_name NVARCHAR(256) NOT NULL,
                        kind NVARCHAR(32) NOT NULL CONSTRAINT DF_fleet_action_runs_kind DEFAULT 'Command',
                        command_text NVARCHAR(MAX),
                        prompt_template NVARCHAR(MAX),
                        pipeline_id NVARCHAR(64),
                        persona NVARCHAR(256),
                        timeout_seconds INT NOT NULL CONSTRAINT DF_fleet_action_runs_timeout_seconds DEFAULT 300,
                        requires_clean_working_tree BIT NOT NULL CONSTRAINT DF_fleet_action_runs_requires_clean_working_tree DEFAULT 1,
                        concurrency INT NOT NULL CONSTRAINT DF_fleet_action_runs_concurrency DEFAULT 4,
                        status NVARCHAR(32) NOT NULL CONSTRAINT DF_fleet_action_runs_status DEFAULT 'Pending',
                        target_count INT NOT NULL CONSTRAINT DF_fleet_action_runs_target_count DEFAULT 0,
                        succeeded_count INT NOT NULL CONSTRAINT DF_fleet_action_runs_succeeded_count DEFAULT 0,
                        failed_count INT NOT NULL CONSTRAINT DF_fleet_action_runs_failed_count DEFAULT 0,
                        skipped_count INT NOT NULL CONSTRAINT DF_fleet_action_runs_skipped_count DEFAULT 0,
                        cancelled_count INT NOT NULL CONSTRAINT DF_fleet_action_runs_cancelled_count DEFAULT 0,
                        started_utc DATETIME2,
                        completed_utc DATETIME2,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_action_runs_tenant_created') CREATE INDEX idx_fleet_action_runs_tenant_created ON fleet_action_runs(tenant_id, created_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_action_runs_tenant_status') CREATE INDEX idx_fleet_action_runs_tenant_status ON fleet_action_runs(tenant_id, status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_action_runs_status') CREATE INDEX idx_fleet_action_runs_status ON fleet_action_runs(status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_action_runs_action') CREATE INDEX idx_fleet_action_runs_action ON fleet_action_runs(action_id);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'fleet_action_run_targets')
                    CREATE TABLE fleet_action_run_targets (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        run_id NVARCHAR(64) NOT NULL,
                        vessel_id NVARCHAR(64) NOT NULL,
                        vessel_name NVARCHAR(256) NOT NULL,
                        status NVARCHAR(32) NOT NULL CONSTRAINT DF_fleet_action_run_targets_status DEFAULT 'Pending',
                        skip_reason NVARCHAR(256),
                        failure_reason NVARCHAR(256),
                        rendered_text NVARCHAR(MAX),
                        exit_code INT,
                        output_text NVARCHAR(MAX),
                        error_text NVARCHAR(MAX),
                        output_truncated BIT NOT NULL CONSTRAINT DF_fleet_action_run_targets_output_truncated DEFAULT 0,
                        voyage_id NVARCHAR(64),
                        started_utc DATETIME2,
                        completed_utc DATETIME2,
                        duration_ms BIGINT,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_fleet_action_run_targets_run_id FOREIGN KEY (run_id) REFERENCES fleet_action_runs(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_action_run_targets_tenant_run_status') CREATE INDEX idx_fleet_action_run_targets_tenant_run_status ON fleet_action_run_targets(tenant_id, run_id, status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_action_run_targets_run_vessel_name') CREATE INDEX idx_fleet_action_run_targets_run_vessel_name ON fleet_action_run_targets(run_id, vessel_name);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_action_run_targets_vessel') CREATE INDEX idx_fleet_action_run_targets_vessel ON fleet_action_run_targets(vessel_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_fleet_action_run_targets_voyage') CREATE INDEX idx_fleet_action_run_targets_voyage ON fleet_action_run_targets(voyage_id);"
                ),
                new SchemaMigration(
                    73,
                    "Add vessel_health, vessel_health_findings, vessel_dependencies, and vessel_health_overrides tables",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vessel_health')
                    CREATE TABLE vessel_health (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        vessel_id NVARCHAR(450) NOT NULL,
                        overall_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_overall_status DEFAULT 'Unknown',
                        evaluated_utc DATETIME2,
                        evaluation_duration_ms BIGINT,
                        error_code NVARCHAR(256),
                        evaluated_path NVARCHAR(MAX),
                        current_branch NVARCHAR(256),
                        is_dirty BIT,
                        untracked_count INT,
                        ahead_of_default INT,
                        behind_default INT,
                        ahead_of_upstream INT,
                        behind_upstream INT,
                        last_commit_utc DATETIME2,
                        branch_count INT,
                        stale_branch_count INT,
                        armada_branch_count INT,
                        primary_language NVARCHAR(256),
                        project_count INT,
                        outdated_count INT,
                        outdated_major_count INT,
                        vulnerable_count INT,
                        max_vulnerability_severity NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_max_vulnerability_severity DEFAULT 'None',
                        dependency_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_dependency_status DEFAULT 'Unknown',
                        vulnerability_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_vulnerability_status DEFAULT 'Unknown',
                        test_infra_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_test_infra_status DEFAULT 'Unknown',
                        ci_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_ci_status DEFAULT 'Unknown',
                        divergence_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_divergence_status DEFAULT 'Unknown',
                        working_tree_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_working_tree_status DEFAULT 'Unknown',
                        branch_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_branch_status DEFAULT 'Unknown',
                        readiness_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_readiness_status DEFAULT 'Unknown',
                        mission_outcome_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_mission_outcome_status DEFAULT 'Unknown',
                        last_check_run_status NVARCHAR(256),
                        has_ci_config BIT,
                        has_license BIT,
                        has_readme BIT,
                        readiness_error_count INT,
                        recent_mission_failure_count INT,
                        manifest_hash NVARCHAR(256),
                        dependencies_evaluated_utc DATETIME2,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_vessel_health_vessel_id FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_vessel') CREATE UNIQUE INDEX idx_vessel_health_vessel ON vessel_health(vessel_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_overall') CREATE INDEX idx_vessel_health_tenant_overall ON vessel_health(tenant_id, overall_status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_ahead_of_def') CREATE INDEX idx_vessel_health_tenant_ahead_of_def ON vessel_health(tenant_id, ahead_of_default);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_behind_def') CREATE INDEX idx_vessel_health_tenant_behind_def ON vessel_health(tenant_id, behind_default);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_is_dirty') CREATE INDEX idx_vessel_health_tenant_is_dirty ON vessel_health(tenant_id, is_dirty);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_branch') CREATE INDEX idx_vessel_health_tenant_branch ON vessel_health(tenant_id, branch_count);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_stale_branch') CREATE INDEX idx_vessel_health_tenant_stale_branch ON vessel_health(tenant_id, stale_branch_count);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_outdated') CREATE INDEX idx_vessel_health_tenant_outdated ON vessel_health(tenant_id, outdated_count);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_outdated_major') CREATE INDEX idx_vessel_health_tenant_outdated_major ON vessel_health(tenant_id, outdated_major_count);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_vulnerable') CREATE INDEX idx_vessel_health_tenant_vulnerable ON vessel_health(tenant_id, vulnerable_count);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_dependency') CREATE INDEX idx_vessel_health_tenant_dependency ON vessel_health(tenant_id, dependency_status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_test_infra') CREATE INDEX idx_vessel_health_tenant_test_infra ON vessel_health(tenant_id, test_infra_status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_ci') CREATE INDEX idx_vessel_health_tenant_ci ON vessel_health(tenant_id, ci_status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_last_commit') CREATE INDEX idx_vessel_health_tenant_last_commit ON vessel_health(tenant_id, last_commit_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_evaluated') CREATE INDEX idx_vessel_health_tenant_evaluated ON vessel_health(tenant_id, evaluated_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_primary_language') CREATE INDEX idx_vessel_health_tenant_primary_language ON vessel_health(tenant_id, primary_language);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_current_branch') CREATE INDEX idx_vessel_health_tenant_current_branch ON vessel_health(tenant_id, current_branch);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_has_ci_config') CREATE INDEX idx_vessel_health_tenant_has_ci_config ON vessel_health(tenant_id, has_ci_config);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_tenant_max_vulnerability_severity') CREATE INDEX idx_vessel_health_tenant_max_vulnerability_severity ON vessel_health(tenant_id, max_vulnerability_severity);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vessel_health_findings')
                    CREATE TABLE vessel_health_findings (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        vessel_id NVARCHAR(450) NOT NULL,
                        criterion NVARCHAR(32) NOT NULL,
                        status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_findings_status DEFAULT 'Unknown',
                        detail_code NVARCHAR(256),
                        value_a BIGINT,
                        value_b BIGINT,
                        evaluated_utc DATETIME2 NOT NULL,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_vessel_health_findings_vessel_id FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_findings_tenant_vessel') CREATE INDEX idx_vessel_health_findings_tenant_vessel ON vessel_health_findings(tenant_id, vessel_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_findings_vessel_criterion') CREATE INDEX idx_vessel_health_findings_vessel_criterion ON vessel_health_findings(vessel_id, criterion);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vessel_dependencies')
                    CREATE TABLE vessel_dependencies (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        vessel_id NVARCHAR(450) NOT NULL,
                        ecosystem NVARCHAR(32) NOT NULL,
                        project_path NVARCHAR(MAX),
                        package_name NVARCHAR(256) NOT NULL,
                        current_version NVARCHAR(256),
                        latest_version NVARCHAR(256),
                        drift NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_dependencies_drift DEFAULT 'None',
                        is_vulnerable BIT NOT NULL CONSTRAINT DF_vessel_dependencies_is_vulnerable DEFAULT 0,
                        severity NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_dependencies_severity DEFAULT 'None',
                        advisory_url NVARCHAR(MAX),
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_vessel_dependencies_vessel_id FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_dependencies_tenant_vessel') CREATE INDEX idx_vessel_dependencies_tenant_vessel ON vessel_dependencies(tenant_id, vessel_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_dependencies_tenant_drift') CREATE INDEX idx_vessel_dependencies_tenant_drift ON vessel_dependencies(tenant_id, drift);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_dependencies_tenant_severity') CREATE INDEX idx_vessel_dependencies_tenant_severity ON vessel_dependencies(tenant_id, severity);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vessel_health_overrides')
                    CREATE TABLE vessel_health_overrides (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        vessel_id NVARCHAR(450) NOT NULL,
                        user_id NVARCHAR(64),
                        criterion NVARCHAR(32) NOT NULL,
                        status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_health_overrides_status DEFAULT 'Unknown',
                        note NVARCHAR(MAX),
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_vessel_health_overrides_vessel_id FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_overrides_tenant_vessel_criterion') CREATE UNIQUE INDEX idx_vessel_health_overrides_tenant_vessel_criterion ON vessel_health_overrides(tenant_id, vessel_id, criterion);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_health_overrides_vessel') CREATE INDEX idx_vessel_health_overrides_vessel ON vessel_health_overrides(vessel_id);"
                ),
                new SchemaMigration(
                    74,
                    "Add background discovery and fleet categorization columns to vessel_import_batches, a selected flag to vessel_import_items, plus vessel_import_fleet_recommendations and vessel_import_fleet_recommendation_vessels tables",
                    @"IF COL_LENGTH('vessel_import_items','selected') IS NULL ALTER TABLE vessel_import_items ADD selected BIT NOT NULL CONSTRAINT DF_vessel_import_items_selected DEFAULT 0;",
                    @"IF COL_LENGTH('vessel_import_batches','discovery_job_id') IS NULL ALTER TABLE vessel_import_batches ADD discovery_job_id NVARCHAR(64) NULL;",
                    @"IF COL_LENGTH('vessel_import_batches','truncated') IS NULL ALTER TABLE vessel_import_batches ADD truncated BIT NOT NULL CONSTRAINT DF_vessel_import_batches_truncated DEFAULT 0;",
                    @"IF COL_LENGTH('vessel_import_batches','error_message') IS NULL ALTER TABLE vessel_import_batches ADD error_message NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessel_import_batches','categorization_status') IS NULL ALTER TABLE vessel_import_batches ADD categorization_status NVARCHAR(32) NOT NULL CONSTRAINT DF_vessel_import_batches_categorization_status DEFAULT 'None';",
                    @"IF COL_LENGTH('vessel_import_batches','categorization_captain_id') IS NULL ALTER TABLE vessel_import_batches ADD categorization_captain_id NVARCHAR(64) NULL;",
                    @"IF COL_LENGTH('vessel_import_batches','categorization_job_id') IS NULL ALTER TABLE vessel_import_batches ADD categorization_job_id NVARCHAR(64) NULL;",
                    @"IF COL_LENGTH('vessel_import_batches','categorization_prompt') IS NULL ALTER TABLE vessel_import_batches ADD categorization_prompt NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessel_import_batches','categorization_apply_automatically') IS NULL ALTER TABLE vessel_import_batches ADD categorization_apply_automatically BIT NOT NULL CONSTRAINT DF_vessel_import_batches_categorization_apply_auto DEFAULT 0;",
                    @"IF COL_LENGTH('vessel_import_batches','categorization_error') IS NULL ALTER TABLE vessel_import_batches ADD categorization_error NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('vessel_import_batches','categorization_started_utc') IS NULL ALTER TABLE vessel_import_batches ADD categorization_started_utc DATETIME2 NULL;",
                    @"IF COL_LENGTH('vessel_import_batches','categorization_completed_utc') IS NULL ALTER TABLE vessel_import_batches ADD categorization_completed_utc DATETIME2 NULL;",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_batches_tenant_categorization') CREATE INDEX idx_vessel_import_batches_tenant_categorization ON vessel_import_batches(tenant_id, categorization_status);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vessel_import_fleet_recommendations')
                    CREATE TABLE vessel_import_fleet_recommendations (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        batch_id NVARCHAR(64) NOT NULL,
                        name NVARCHAR(256) NOT NULL,
                        description NVARCHAR(MAX),
                        rationale NVARCHAR(MAX),
                        sort_order INT NOT NULL CONSTRAINT DF_vessel_import_fleet_recs_sort_order DEFAULT 0,
                        applied_fleet_id NVARCHAR(64),
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_vessel_import_fleet_recs_batch_id FOREIGN KEY (batch_id) REFERENCES vessel_import_batches(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_fleet_recs_tenant_batch') CREATE INDEX idx_vessel_import_fleet_recs_tenant_batch ON vessel_import_fleet_recommendations(tenant_id, batch_id, sort_order);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vessel_import_fleet_recommendation_vessels')
                    CREATE TABLE vessel_import_fleet_recommendation_vessels (
                        recommendation_id NVARCHAR(64) NOT NULL,
                        tenant_id NVARCHAR(64) NOT NULL,
                        batch_id NVARCHAR(64) NOT NULL,
                        vessel_id NVARCHAR(64) NOT NULL,
                        sort_order INT NOT NULL CONSTRAINT DF_vessel_import_fleet_rec_vessels_sort_order DEFAULT 0,
                        CONSTRAINT PK_vessel_import_fleet_rec_vessels PRIMARY KEY (recommendation_id, vessel_id),
                        CONSTRAINT FK_vessel_import_fleet_rec_vessels_rec_id FOREIGN KEY (recommendation_id) REFERENCES vessel_import_fleet_recommendations(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_fleet_rec_vessels_tenant_batch') CREATE INDEX idx_vessel_import_fleet_rec_vessels_tenant_batch ON vessel_import_fleet_recommendation_vessels(tenant_id, batch_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_vessel_import_fleet_rec_vessels_vessel') CREATE INDEX idx_vessel_import_fleet_rec_vessels_vessel ON vessel_import_fleet_recommendation_vessels(vessel_id);"
                ),
                new SchemaMigration(
                    75,
                    "Add ask_threads, ask_messages, ask_message_tool_calls, ask_action_proposals, and ask_tracked_work tables for Ask Armada conversation threads",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ask_threads')
                    CREATE TABLE ask_threads (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        user_id NVARCHAR(64) NOT NULL,
                        title NVARCHAR(256) NOT NULL,
                        captain_id NVARCHAR(64),
                        auto_approve BIT NOT NULL CONSTRAINT DF_ask_threads_auto_approve DEFAULT 0,
                        summary_text NVARCHAR(MAX),
                        summary_utc DATETIME2,
                        pinned BIT NOT NULL CONSTRAINT DF_ask_threads_pinned DEFAULT 0,
                        archived BIT NOT NULL CONSTRAINT DF_ask_threads_archived DEFAULT 0,
                        last_message_utc DATETIME2,
                        message_count INT NOT NULL CONSTRAINT DF_ask_threads_message_count DEFAULT 0,
                        unread_count INT NOT NULL CONSTRAINT DF_ask_threads_unread_count DEFAULT 0,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ask_messages')
                    CREATE TABLE ask_messages (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        user_id NVARCHAR(64),
                        thread_id NVARCHAR(64) NOT NULL,
                        sequence INT NOT NULL,
                        role NVARCHAR(64) NOT NULL,
                        kind NVARCHAR(64) NOT NULL,
                        content_text NVARCHAR(MAX) NOT NULL,
                        thinking_text NVARCHAR(MAX),
                        proposal_id NVARCHAR(64),
                        tracked_work_id NVARCHAR(64),
                        captain_id NVARCHAR(64),
                        duration_ms BIGINT,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_ask_messages_thread_id FOREIGN KEY (thread_id) REFERENCES ask_threads(id) ON DELETE CASCADE
                    );",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ask_message_tool_calls')
                    CREATE TABLE ask_message_tool_calls (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        user_id NVARCHAR(64),
                        message_id NVARCHAR(64) NOT NULL,
                        thread_id NVARCHAR(64) NOT NULL,
                        call_id NVARCHAR(256),
                        tool_name NVARCHAR(256) NOT NULL,
                        arguments_text NVARCHAR(MAX),
                        result_text NVARCHAR(MAX),
                        ok BIT,
                        elapsed_ms BIGINT,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_ask_message_tool_calls_message_id FOREIGN KEY (message_id) REFERENCES ask_messages(id) ON DELETE CASCADE
                    );",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ask_action_proposals')
                    CREATE TABLE ask_action_proposals (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        user_id NVARCHAR(64),
                        thread_id NVARCHAR(64) NOT NULL,
                        message_id NVARCHAR(64),
                        tool_name NVARCHAR(256) NOT NULL,
                        arguments_text NVARCHAR(MAX) NOT NULL,
                        summary_text NVARCHAR(MAX) NOT NULL,
                        source NVARCHAR(64) NOT NULL,
                        status NVARCHAR(64) NOT NULL,
                        result_text NVARCHAR(MAX),
                        error_text NVARCHAR(MAX),
                        decided_by_user_id NVARCHAR(64),
                        decided_utc DATETIME2,
                        executed_utc DATETIME2,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_ask_action_proposals_thread_id FOREIGN KEY (thread_id) REFERENCES ask_threads(id) ON DELETE CASCADE
                    );",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ask_tracked_work')
                    CREATE TABLE ask_tracked_work (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64) NOT NULL,
                        user_id NVARCHAR(64),
                        thread_id NVARCHAR(64) NOT NULL,
                        entity_type NVARCHAR(64) NOT NULL,
                        entity_id NVARCHAR(64) NOT NULL,
                        title NVARCHAR(256) NOT NULL,
                        status NVARCHAR(64),
                        state NVARCHAR(64) NOT NULL,
                        snapshot_hash NVARCHAR(64),
                        last_change_utc DATETIME2,
                        completed_utc DATETIME2,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL,
                        CONSTRAINT FK_ask_tracked_work_thread_id FOREIGN KEY (thread_id) REFERENCES ask_threads(id) ON DELETE CASCADE
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_threads_owner') CREATE INDEX idx_ask_threads_owner ON ask_threads(tenant_id, user_id, archived, pinned);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_threads_tenant_last_message') CREATE INDEX idx_ask_threads_tenant_last_message ON ask_threads(tenant_id, last_message_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_messages_thread_sequence') CREATE UNIQUE INDEX idx_ask_messages_thread_sequence ON ask_messages(thread_id, sequence);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_messages_tenant_thread') CREATE INDEX idx_ask_messages_tenant_thread ON ask_messages(tenant_id, thread_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_message_tool_calls_message') CREATE INDEX idx_ask_message_tool_calls_message ON ask_message_tool_calls(tenant_id, message_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_message_tool_calls_thread') CREATE INDEX idx_ask_message_tool_calls_thread ON ask_message_tool_calls(tenant_id, thread_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_action_proposals_thread_status') CREATE INDEX idx_ask_action_proposals_thread_status ON ask_action_proposals(tenant_id, thread_id, status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_action_proposals_status_created') CREATE INDEX idx_ask_action_proposals_status_created ON ask_action_proposals(status, created_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_tracked_work_thread_entity') CREATE UNIQUE INDEX idx_ask_tracked_work_thread_entity ON ask_tracked_work(thread_id, entity_type, entity_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_tracked_work_tenant_thread') CREATE INDEX idx_ask_tracked_work_tenant_thread ON ask_tracked_work(tenant_id, thread_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_tracked_work_state') CREATE INDEX idx_ask_tracked_work_state ON ask_tracked_work(state);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ask_tracked_work_entity') CREATE INDEX idx_ask_tracked_work_entity ON ask_tracked_work(entity_type, entity_id);"
                ),
                new SchemaMigration(
                    76,
                    "Add nullable auto_approve to vessels: a per-vessel override of the captain auto-approve setting for missions on the vessel",
                    @"IF COL_LENGTH('vessels', 'auto_approve') IS NULL ALTER TABLE vessels ADD auto_approve BIT NULL;"
                ),
                new SchemaMigration(
                    77,
                    "Add failure_kind and wait_for_voyage_workers to missions: a persisted failure classification set where the failure happens, and a structured flag that defers a worker until the other workers in its voyage settle",
                    @"IF COL_LENGTH('missions', 'failure_kind') IS NULL ALTER TABLE missions ADD failure_kind NVARCHAR(64) NULL;",
                    @"IF COL_LENGTH('missions', 'wait_for_voyage_workers') IS NULL ALTER TABLE missions ADD wait_for_voyage_workers BIT NOT NULL CONSTRAINT DF_missions_wait_for_voyage_workers DEFAULT 0;"
                ),
                new SchemaMigration(
                    78,
                    "Add CLI tool permissions: cli_permission_requests and cli_permission_rules tables, cli_permission_policy on captains and ask_threads, and permission_denied on ask_message_tool_calls",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'cli_permission_requests')
                    CREATE TABLE cli_permission_requests (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64),
                        user_id NVARCHAR(64),
                        captain_id NVARCHAR(64),
                        mission_id NVARCHAR(64),
                        voyage_id NVARCHAR(64),
                        vessel_id NVARCHAR(64),
                        thread_id NVARCHAR(64),
                        message_id NVARCHAR(64),
                        runtime NVARCHAR(64) NOT NULL,
                        tool_name NVARCHAR(256) NOT NULL,
                        input_text NVARCHAR(MAX) NOT NULL,
                        summary_text NVARCHAR(MAX) NOT NULL,
                        suggested_rule NVARCHAR(MAX),
                        status NVARCHAR(64) NOT NULL,
                        decision_source NVARCHAR(64),
                        rule_id NVARCHAR(64),
                        decided_by_user_id NVARCHAR(64),
                        decision_message NVARCHAR(MAX),
                        expires_utc DATETIME2 NOT NULL,
                        decided_utc DATETIME2,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'cli_permission_rules')
                    CREATE TABLE cli_permission_rules (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64),
                        scope NVARCHAR(64) NOT NULL,
                        vessel_id NVARCHAR(64),
                        captain_id NVARCHAR(64),
                        pattern NVARCHAR(MAX) NOT NULL,
                        action NVARCHAR(64) NOT NULL,
                        description NVARCHAR(MAX),
                        created_by_user_id NVARCHAR(64),
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_cli_permission_requests_status_created') CREATE INDEX idx_cli_permission_requests_status_created ON cli_permission_requests(status, created_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_cli_permission_requests_tenant_status') CREATE INDEX idx_cli_permission_requests_tenant_status ON cli_permission_requests(tenant_id, status);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_cli_permission_requests_thread') CREATE INDEX idx_cli_permission_requests_thread ON cli_permission_requests(thread_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_cli_permission_requests_mission') CREATE INDEX idx_cli_permission_requests_mission ON cli_permission_requests(mission_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_cli_permission_rules_tenant_scope') CREATE INDEX idx_cli_permission_rules_tenant_scope ON cli_permission_rules(tenant_id, scope);",
                    @"IF COL_LENGTH('captains', 'cli_permission_policy') IS NULL ALTER TABLE captains ADD cli_permission_policy NVARCHAR(64) NULL;",
                    @"IF COL_LENGTH('ask_threads', 'cli_permission_policy') IS NULL ALTER TABLE ask_threads ADD cli_permission_policy NVARCHAR(64) NULL;",
                    @"IF COL_LENGTH('ask_message_tool_calls', 'permission_denied') IS NULL ALTER TABLE ask_message_tool_calls ADD permission_denied BIT NULL;"
                ),
                new SchemaMigration(
                    79,
                    "Add push notification devices: push_devices table (one row per Expo push token, owned by a user)",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'push_devices')
                    CREATE TABLE push_devices (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id NVARCHAR(64),
                        user_id NVARCHAR(64),
                        platform NVARCHAR(32) NOT NULL,
                        expo_push_token NVARCHAR(256) NOT NULL,
                        device_name NVARCHAR(256),
                        app_version NVARCHAR(64),
                        locale NVARCHAR(64),
                        categories NVARCHAR(1024) NOT NULL,
                        active BIT NOT NULL CONSTRAINT DF_push_devices_active DEFAULT 1,
                        created_utc DATETIME2 NOT NULL,
                        last_seen_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_push_devices_token') CREATE UNIQUE INDEX idx_push_devices_token ON push_devices(expo_push_token);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_push_devices_tenant_user') CREATE INDEX idx_push_devices_tenant_user ON push_devices(tenant_id, user_id);"
                ),
                new SchemaMigration(
                    80,
                    "Add repository_path and checkout_path to docks: the repository a Harbor-side dock was created from and the user's checkout on that Harbor host",
                    @"IF COL_LENGTH('docks', 'repository_path') IS NULL ALTER TABLE docks ADD repository_path NVARCHAR(MAX) NULL;",
                    @"IF COL_LENGTH('docks', 'checkout_path') IS NULL ALTER TABLE docks ADD checkout_path NVARCHAR(MAX) NULL;"
                ),
                new SchemaMigration(
                    81,
                    "Add Harbor metrics: harbor_jobs (one row per captain launch delegated to a Harbor), harbor_link_samples (per-minute heartbeat round trips and reconnects), harbor_link_events (link transitions), and harbor_id on token_usage",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'harbor_jobs')
                    CREATE TABLE harbor_jobs (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        job_id NVARCHAR(64) NOT NULL,
                        harbor_id NVARCHAR(64) NOT NULL,
                        tenant_id NVARCHAR(64),
                        kind NVARCHAR(32) NOT NULL,
                        runtime NVARCHAR(64) NOT NULL,
                        model NVARCHAR(256),
                        mission_id NVARCHAR(64),
                        captain_id NVARCHAR(64),
                        launched_utc DATETIME2 NOT NULL,
                        started_utc DATETIME2,
                        first_output_utc DATETIME2,
                        ended_utc DATETIME2,
                        time_to_first_output_ms BIGINT,
                        duration_ms BIGINT,
                        exit_code INT,
                        outcome NVARCHAR(32) NOT NULL,
                        stop_requested BIT NOT NULL CONSTRAINT DF_harbor_jobs_stop_requested DEFAULT 0,
                        created_utc DATETIME2 NOT NULL,
                        last_update_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbor_jobs_job') CREATE INDEX idx_harbor_jobs_job ON harbor_jobs(job_id);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbor_jobs_harbor_launched') CREATE INDEX idx_harbor_jobs_harbor_launched ON harbor_jobs(harbor_id, launched_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbor_jobs_ended') CREATE INDEX idx_harbor_jobs_ended ON harbor_jobs(ended_utc);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'harbor_link_samples')
                    CREATE TABLE harbor_link_samples (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        harbor_id NVARCHAR(64) NOT NULL,
                        bucket_start_utc DATETIME2 NOT NULL,
                        heartbeat_count INT NOT NULL CONSTRAINT DF_harbor_link_samples_heartbeat_count DEFAULT 0,
                        round_trip_count INT NOT NULL CONSTRAINT DF_harbor_link_samples_round_trip_count DEFAULT 0,
                        round_trip_total_ms BIGINT NOT NULL CONSTRAINT DF_harbor_link_samples_round_trip_total_ms DEFAULT 0,
                        round_trip_max_ms BIGINT,
                        reconnect_count INT,
                        last_reconnect_utc DATETIME2,
                        created_utc DATETIME2 NOT NULL
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbor_link_samples_harbor_bucket') CREATE INDEX idx_harbor_link_samples_harbor_bucket ON harbor_link_samples(harbor_id, bucket_start_utc);",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbor_link_samples_bucket') CREATE INDEX idx_harbor_link_samples_bucket ON harbor_link_samples(bucket_start_utc);",
                    @"
                    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'harbor_link_events')
                    CREATE TABLE harbor_link_events (
                        id NVARCHAR(64) NOT NULL PRIMARY KEY,
                        harbor_id NVARCHAR(64) NOT NULL,
                        event_type NVARCHAR(32) NOT NULL,
                        occurred_utc DATETIME2 NOT NULL,
                        detail NVARCHAR(1024)
                    );",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_harbor_link_events_harbor_occurred') CREATE INDEX idx_harbor_link_events_harbor_occurred ON harbor_link_events(harbor_id, occurred_utc);",
                    @"IF COL_LENGTH('token_usage', 'harbor_id') IS NULL ALTER TABLE token_usage ADD harbor_id NVARCHAR(64) NULL;",
                    @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_token_usage_harbor_created') CREATE INDEX idx_token_usage_harbor_created ON token_usage(harbor_id, created_utc);"
                ),
                new SchemaMigration(
                    82,
                    "Add Ask turn telemetry to ask_messages: ttft_ms, first_text_ms, streaming_ms, tokens_per_second, input_tokens, output_tokens, cached_tokens, tokens_estimated, cost_usd, tool_call_count, and tool_time_ms (all nullable)",
                    @"IF COL_LENGTH('ask_messages', 'ttft_ms') IS NULL ALTER TABLE ask_messages ADD ttft_ms FLOAT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'first_text_ms') IS NULL ALTER TABLE ask_messages ADD first_text_ms FLOAT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'streaming_ms') IS NULL ALTER TABLE ask_messages ADD streaming_ms FLOAT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'tokens_per_second') IS NULL ALTER TABLE ask_messages ADD tokens_per_second FLOAT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'input_tokens') IS NULL ALTER TABLE ask_messages ADD input_tokens BIGINT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'output_tokens') IS NULL ALTER TABLE ask_messages ADD output_tokens BIGINT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'cached_tokens') IS NULL ALTER TABLE ask_messages ADD cached_tokens BIGINT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'tokens_estimated') IS NULL ALTER TABLE ask_messages ADD tokens_estimated BIT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'cost_usd') IS NULL ALTER TABLE ask_messages ADD cost_usd FLOAT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'tool_call_count') IS NULL ALTER TABLE ask_messages ADD tool_call_count BIGINT NULL;",
                    @"IF COL_LENGTH('ask_messages', 'tool_time_ms') IS NULL ALTER TABLE ask_messages ADD tool_time_ms FLOAT NULL;"
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
