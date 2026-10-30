namespace Armada.Core.Database.Mysql
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Mysql.Implementations;
    using Armada.Core.Database.Mysql.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using MySqlConnector;
    using SyslogLogging;

    /// <summary>
    /// MySQL implementation of the Armada database driver.
    /// Uses MySqlConnector with connection pooling configured from DatabaseSettings.
    /// </summary>
    public class MysqlDatabaseDriver : DatabaseDriver
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[MysqlDatabaseDriver] ";
        private DatabaseSettings _Settings;
        private string _ConnectionString;
        private LoggingModule _Logging;
        private bool _Disposed = false;

        private static readonly string _Iso8601Format = "yyyy-MM-dd HH:mm:ss.ffffff";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the MySQL database driver.
        /// </summary>
        /// <param name="settings">Database settings including connection pooling parameters.</param>
        /// <param name="logging">Logging module.</param>
        public MysqlDatabaseDriver(DatabaseSettings settings, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _ConnectionString = settings.GetConnectionString();
            Storage = new StorageMethods(_ConnectionString);

            Fleets = new FleetMethods(_ConnectionString);
            Vessels = new VesselMethods(_ConnectionString);
            Captains = new CaptainMethods(_ConnectionString);
            Missions = new MissionMethods(_ConnectionString);
            Voyages = new VoyageMethods(_ConnectionString);
            PlanningSessions = new PlanningSessionMethods(_ConnectionString);
            PlanningSessionMessages = new PlanningSessionMessageMethods(_ConnectionString);
            Objectives = new ObjectiveMethods(_ConnectionString);
            Jobs = new JobMethods(_ConnectionString);
            ModelEndpoints = new ModelEndpointMethods(_ConnectionString);
            Harbors = new HarborMethods(_ConnectionString);
            Memories = new MemoryMethods(_ConnectionString);
            VesselImportBatches = new VesselImportBatchMethods(_ConnectionString);
            VesselImportItems = new VesselImportItemMethods(_ConnectionString);
            VesselImportFleetRecommendations = new VesselImportFleetRecommendationMethods(_ConnectionString);
            AskThreads = new AskThreadMethods(_ConnectionString);
            AskMessages = new AskMessageMethods(_ConnectionString);
            AskMessageToolCalls = new AskMessageToolCallMethods(_ConnectionString);
            AskActionProposals = new AskActionProposalMethods(_ConnectionString);
            AskTrackedWork = new AskTrackedWorkMethods(_ConnectionString);
            CliPermissionRequests = new CliPermissionRequestMethods(_ConnectionString);
            CliPermissionRules = new CliPermissionRuleMethods(_ConnectionString);
            StageOutcomes = new StageOutcomeMethods(_ConnectionString);
            BlackBoxEntries = new BlackBoxEntryMethods(_ConnectionString);
            BlackBoxObjects = new BlackBoxObjectMethods(_ConnectionString);
            BlackBoxFiles = new BlackBoxFileMethods(_ConnectionString);
            BlackBoxLinks = new BlackBoxLinkMethods(_ConnectionString);
            BlackBoxNotes = new BlackBoxNoteMethods(_ConnectionString);
            BlackBoxMigrations = new BlackBoxMigrationMethods(_ConnectionString);
            BlackBoxSuccesses = new BlackBoxSuccessMethods(_ConnectionString);
            BlackBoxRecoveryOutcomes = new BlackBoxRecoveryOutcomeMethods(_ConnectionString);
            HandoffArtifacts = new HandoffArtifactMethods(_ConnectionString);
            FailureCauseRules = new FailureCauseRuleMethods(_ConnectionString);
            ModelProbeRuns = new ModelProbeRunMethods(_ConnectionString);
            ModelProfiles = new ModelProfileMethods(_ConnectionString);
            ModelAlerts = new ModelAlertMethods(_ConnectionString);
            ModelProbeResults = new ModelProbeResultMethods(_ConnectionString);
            BatteryRuns = new BatteryRunMethods(_ConnectionString);
            BatteryTaskResults = new BatteryTaskResultMethods(_ConnectionString);
            PushDevices = new PushDeviceMethods(_ConnectionString);
            Incidents = new IncidentMethods(_ConnectionString);
            RunbookExecutions = new RunbookExecutionMethods(_ConnectionString);
            Runbooks = new RunbookMethods(_ConnectionString);
            HarborJobs = new HarborJobMethods(_ConnectionString);
            HarborLinkSamples = new HarborLinkSampleMethods(_ConnectionString);
            HarborLinkEvents = new HarborLinkEventMethods(_ConnectionString);
            FleetActions = new FleetActionMethods(_ConnectionString);
            FleetActionRuns = new FleetActionRunMethods(_ConnectionString);
            FleetActionRunTargets = new FleetActionRunTargetMethods(_ConnectionString);
            VesselHealth = new VesselHealthMethods(_ConnectionString);
            VesselHealthFindings = new VesselHealthFindingMethods(_ConnectionString);
            VesselDependencies = new VesselDependencyMethods(_ConnectionString);
            VesselHealthOverrides = new VesselHealthOverrideMethods(_ConnectionString);
            ObjectiveRefinementSessions = new ObjectiveRefinementSessionMethods(_ConnectionString);
            ObjectiveRefinementMessages = new ObjectiveRefinementMessageMethods(_ConnectionString);
            Docks = new DockMethods(_ConnectionString);
            Signals = new SignalMethods(_ConnectionString);
            Events = new EventMethods(_ConnectionString);
            RequestHistory = new RequestHistoryMethods(_ConnectionString);
            TokenUsage = new TokenUsageMethods(_ConnectionString);
            MergeEntries = new MergeEntryMethods(_ConnectionString);
            Tenants = new TenantMethods(_ConnectionString);
            Users = new UserMethods(_ConnectionString);
            Credentials = new CredentialMethods(_ConnectionString);
            PromptTemplates = new PromptTemplateMethods(_ConnectionString);
            Playbooks = new PlaybookMethods(_ConnectionString);
            Personas = new PersonaMethods(_ConnectionString);
            Pipelines = new PipelineMethods(_ConnectionString);
            WorkflowProfiles = new WorkflowProfileMethods(_ConnectionString);
            ProjectProfiles = new ProjectProfileMethods(_ConnectionString);
            Skills = new SkillMethods(_ConnectionString);
            Environments = new DeploymentEnvironmentMethods(_ConnectionString);
            CheckRuns = new CheckRunMethods(_ConnectionString);
            Releases = new ReleaseMethods(_ConnectionString);
            Deployments = new DeploymentMethods(_ConnectionString);
            CoordinationLeases = new CoordinationLeaseMethods(_ConnectionString);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the database schema by creating the schema_migrations table
        /// and applying all pending migrations using MySQL DDL.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            _Logging.Debug(_Header + "initializing database");

            using (MySqlConnection conn = await GetConnectionAsync(token).ConfigureAwait(false))
            {
                // Create migration tracking table
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = TableQueries.SchemaMigrations;
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Read every applied version. The maximum remains the current schema version.
                List<int> appliedVersions = new List<int>();
                int currentVersion = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version FROM schema_migrations;";
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            int version = Convert.ToInt32(reader.GetValue(0));
                            appliedVersions.Add(version);
                            if (version > currentVersion) currentVersion = version;
                        }
                    }
                }

                // Apply pending migrations
                List<SchemaMigration> migrations = GetMigrations();
                RequireDeclaredMigrationsPresent("MySQL", migrations, appliedVersions);
                int applied = 0;

                foreach (SchemaMigration migration in migrations)
                {
                    if (migration.Version <= currentVersion) continue;

                    _Logging.Info(_Header + "applying migration v" + migration.Version + ": " + migration.Description);

                    using (MySqlTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                    {
                        await ExecuteMigrationAsync(conn, tx, migration, token).ConfigureAwait(false);

                        // Record migration
                        using (MySqlCommand cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "INSERT INTO schema_migrations (version, description, applied_utc) VALUES (@v, @d, @t);";
                            cmd.Parameters.AddWithValue("@v", migration.Version);
                            cmd.Parameters.AddWithValue("@d", migration.Description);
                            cmd.Parameters.AddWithValue("@t", DateTime.UtcNow);
                            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }

                        await tx.CommitAsync(token).ConfigureAwait(false);
                        applied++;
                    }
                }

                if (applied > 0)
                    _Logging.Info(_Header + "applied " + applied + " migration(s), schema now at v" + migrations[migrations.Count - 1].Version);
                else
                    _Logging.Debug(_Header + "schema is up to date at v" + currentVersion);
            }

            _Logging.Debug(_Header + "database initialized successfully");

            // Seed default data on first boot (or after migration that created tenant but not user)
            bool anyTenants = await Tenants.ExistsAnyAsync(token).ConfigureAwait(false);
            if (!anyTenants)
            {
                _Logging.Info(_Header + "first boot detected, seeding default tenant, user, and credential");

                TenantMetadata defaultTenant = new TenantMetadata();
                defaultTenant.Id = Constants.DefaultTenantId;
                defaultTenant.Name = Constants.DefaultTenantName;
                defaultTenant.IsProtected = true;
                await Tenants.CreateAsync(defaultTenant, token).ConfigureAwait(false);
            }

            // Ensure default user and credential exist (migration may have seeded tenant without user)
            UserMaster? existingUser = await Users.ReadByIdAsync(Constants.DefaultUserId, token).ConfigureAwait(false);
            if (existingUser == null)
            {
                _Logging.Info(_Header + "seeding default user and credential");

                UserMaster defaultUser = new UserMaster();
                defaultUser.Id = Constants.DefaultUserId;
                defaultUser.TenantId = Constants.DefaultTenantId;
                defaultUser.Email = Constants.DefaultUserEmail;
                defaultUser.PasswordSha256 = UserMaster.ComputePasswordHash(Constants.DefaultUserPassword);
                defaultUser.IsAdmin = true;
                defaultUser.IsTenantAdmin = true;
                defaultUser.IsProtected = true;
                await Users.CreateAsync(defaultUser, token).ConfigureAwait(false);

                Credential defaultCred = new Credential();
                defaultCred.Id = Constants.DefaultCredentialId;
                defaultCred.Name = Constants.DefaultCredentialName;
                defaultCred.TenantId = Constants.DefaultTenantId;
                defaultCred.UserId = Constants.DefaultUserId;
                defaultCred.BearerToken = Constants.DefaultBearerToken;
                defaultCred.IsProtected = true;
                await Credentials.CreateAsync(defaultCred, token).ConfigureAwait(false);

                _Logging.Info(_Header + "default data seeded successfully");
            }
        }

        /// <summary>
        /// Get an open MySQL connection from the connection pool.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An open MySqlConnection.</returns>
        public async Task<MySqlConnection> GetConnectionAsync(CancellationToken token = default)
        {
            MySqlConnection conn = new MySqlConnection(_ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            return conn;
        }

        /// <summary>
        /// Execute a non-query SQL statement with optional parameters.
        /// </summary>
        /// <param name="sql">SQL statement to execute.</param>
        /// <param name="parameters">Optional parameters as key-value pairs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows affected.</returns>
        public async Task<int> ExecuteQueryAsync(
            string sql,
            Dictionary<string, object?>? parameters = null,
            CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            using (MySqlConnection conn = await GetConnectionAsync(token).ConfigureAwait(false))
            {
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;

                    if (parameters != null)
                    {
                        foreach (KeyValuePair<string, object?> param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                        }
                    }

                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Execute a scalar SQL query with optional parameters.
        /// </summary>
        /// <param name="sql">SQL query to execute.</param>
        /// <param name="parameters">Optional parameters as key-value pairs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The first column of the first row in the result set.</returns>
        public async Task<object?> ExecuteScalarAsync(
            string sql,
            Dictionary<string, object?>? parameters = null,
            CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            using (MySqlConnection conn = await GetConnectionAsync(token).ConfigureAwait(false))
            {
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;

                    if (parameters != null)
                    {
                        foreach (KeyValuePair<string, object?> param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                        }
                    }

                    return await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Get the current schema version.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Current schema version number, or 0 if no migrations have been applied.</returns>
        public override async Task<int> GetSchemaVersionAsync(CancellationToken token = default)
        {
            using (MySqlConnection conn = await GetConnectionAsync(token).ConfigureAwait(false))
            {
                // Check if schema_migrations table exists
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COUNT(*) FROM information_schema.tables
                        WHERE table_schema = DATABASE() AND table_name = 'schema_migrations';";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result == null || Convert.ToInt32(result) == 0) return 0;
                }

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result != null && result != DBNull.Value) return Convert.ToInt32(result);
                    return 0;
                }
            }
        }

        /// <summary>
        /// Sanitize a string value for safe use in SQL by escaping single quotes.
        /// </summary>
        /// <param name="value">The string to sanitize.</param>
        /// <returns>Sanitized string with single quotes escaped.</returns>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Replace("'", "''");
        }

        /// <summary>
        /// Dispose of resources.
        /// </summary>
        public override void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Logging.Debug(_Header + "disposed");
        }

        /// <inheritdoc />
        public override int GetLatestSchemaVersion()
        {
            List<SchemaMigration> migrations = GetMigrations();
            return migrations.Count == 0 ? 0 : migrations[migrations.Count - 1].Version;
        }

        #endregion

        #region Internal-Methods

        /// <inheritdoc />
        internal override IReadOnlyList<SchemaMigration> GetMigrationsForVerification()
        {
            return GetMigrations();
        }

        /// <inheritdoc />
        internal override async Task ReplayMigrationsAsync(CancellationToken token = default)
        {
            using (MySqlConnection conn = await GetConnectionAsync(token).ConfigureAwait(false))
            {
                foreach (SchemaMigration migration in GetMigrations())
                {
                    using (MySqlTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                    {
                        await ExecuteMigrationAsync(conn, tx, migration, token).ConfigureAwait(false);

                        await tx.CommitAsync(token).ConfigureAwait(false);
                    }
                }
            }
        }

        #endregion

        #region Private-Methods

        private static List<SchemaMigration> GetMigrations()
        {
            List<string> initialStatements = new List<string>
            {
                TableQueries.Fleets,
                TableQueries.Vessels,
                TableQueries.Captains,
                TableQueries.Voyages,
                TableQueries.Missions,
                TableQueries.Docks,
                TableQueries.Signals,
                TableQueries.Events,
                TableQueries.MergeEntries,
                TableQueries.CoordinationLeases,
                TableQueries.Jobs
            };

            foreach (string index in TableQueries.Indexes)
            {
                initialStatements.Add(index);
            }

            return new List<SchemaMigration>
            {
                new SchemaMigration(
                    1,
                    "Initial schema: fleets, vessels, captains, voyages, missions, docks, signals, events, merge_entries",
                    initialStatements.ToArray()
                ),
                new SchemaMigration(
                    2,
                    "Add allow_concurrent_missions to vessels",
                    @"ALTER TABLE vessels ADD COLUMN allow_concurrent_missions TINYINT(1) NOT NULL DEFAULT 0;"
                ),
                new SchemaMigration(
                    3,
                    "Multi-tenant: add tenants, users, credentials tables and tenant_id columns",
                    TableQueries.MigrationV3Statements
                ),
                new SchemaMigration(
                    4,
                    "Protected resources and user ownership",
                    TableQueries.MigrationV4Statements
                ),
                new SchemaMigration(
                    5,
                    "Operational tenant foreign keys",
                    TableQueries.MigrationV5Statements
                ),
                new SchemaMigration(
                    6,
                    "Add tenant admin role to users",
                    TableQueries.MigrationV6Statements
                ),
                new SchemaMigration(
                    7,
                    "Add enable_model_context and model_context to vessels",
                    TableQueries.MigrationV7Statements
                ),
                new SchemaMigration(
                    8,
                    "Add system_instructions to captains",
                    TableQueries.MigrationV8Statements
                ),
                new SchemaMigration(
                    9,
                    "Add prompt_templates table",
                    TableQueries.MigrationV9Statements
                ),
                new SchemaMigration(
                    10,
                    "Add personas table",
                    TableQueries.MigrationV10Statements
                ),
                new SchemaMigration(
                    11,
                    "Add captain persona fields",
                    TableQueries.MigrationV11Statements
                ),
                new SchemaMigration(
                    12,
                    "Add mission persona and dependency fields",
                    TableQueries.MigrationV12Statements
                ),
                new SchemaMigration(
                    13,
                    "Add pipelines and pipeline_stages tables",
                    TableQueries.MigrationV13Statements
                ),
                new SchemaMigration(
                    14,
                    "Add failure_reason to missions",
                    TableQueries.MigrationV14Statements
                ),
                new SchemaMigration(
                    15,
                    "Add agent_output to missions",
                    TableQueries.MigrationV15Statements
                ),
                new SchemaMigration(
                    26,
                    "Add model to captains",
                    TableQueries.MigrationV26Statements
                ),
                new SchemaMigration(
                    27,
                    "Add total_runtime_ms to missions",
                    TableQueries.MigrationV27Statements
                ),
                new SchemaMigration(
                    28,
                    "Add playbooks and mission/voyage playbook associations",
                    TableQueries.MigrationV28Statements
                ),
                new SchemaMigration(
                    29,
                    "Add runtime options to captains",
                    TableQueries.MigrationV29Statements
                ),
                new SchemaMigration(
                    30,
                    "Add request history tables",
                    TableQueries.MigrationV30Statements
                ),
                new SchemaMigration(
                    31,
                    "Add pipeline review gates",
                    TableQueries.MigrationV31Statements
                ),
                new SchemaMigration(
                    32,
                    "Add workflow profiles",
                    TableQueries.MigrationV32Statements
                ),
                new SchemaMigration(
                    33,
                    "Add check runs",
                    TableQueries.MigrationV33Statements
                ),
                new SchemaMigration(
                    34,
                    "Add structured parsing summaries to check runs",
                    TableQueries.MigrationV34Statements
                ),
                new SchemaMigration(
                    35,
                    "Add workflow check expansion and landing readiness fields",
                    TableQueries.MigrationV35Statements
                ),
                new SchemaMigration(
                    36,
                    "Add external check metadata and landing branch policy fields",
                    TableQueries.MigrationV36Statements
                ),
                new SchemaMigration(
                    37,
                    "Add releases",
                    TableQueries.MigrationV37Statements
                ),
                new SchemaMigration(
                    38,
                    "Add deployment environments",
                    TableQueries.MigrationV38Statements
                ),
                new SchemaMigration(
                    39,
                    "Add deployments",
                    TableQueries.MigrationV39Statements
                ),
                new SchemaMigration(
                    40,
                    "Add deployment-linked checks and rollout monitoring",
                    TableQueries.MigrationV40Statements
                ),
                new SchemaMigration(
                    41,
                    "Add vessel GitHub token overrides",
                    TableQueries.MigrationV41Statements
                ),
                new SchemaMigration(
                    42,
                    "Add normalized objectives backlog tables",
                    TableQueries.MigrationV42Statements
                ),
                new SchemaMigration(
                    44,
                    "Reliability release: dock leases, process liveness, review deadline, merge retry, coordination leases",
                    TableQueries.MigrationV44Statements
                ),
                new SchemaMigration(
                    45,
                    "Add project_profiles for per-project persona/pipeline/skill customization",
                    TableQueries.MigrationV45Statements
                ),
                new SchemaMigration(
                    46,
                    "Add skills directory",
                    TableQueries.MigrationV46Statements
                ),
                new SchemaMigration(
                    47,
                    "Add reasoning_effort/tier, redispatch_attempts, and vessel dock-boundary columns",
                    TableQueries.MigrationV47Statements
                ),
                new SchemaMigration(
                    48,
                    "Add auto-land + quarantine columns",
                    TableQueries.MigrationV48Statements
                ),
                new SchemaMigration(
                    49,
                    "Add jobs table",
                    TableQueries.MigrationV49Statements
                ),
                new SchemaMigration(
                    55,
                    "Add per-step captain selection (persona default captain, mission requested captain, voyage captain overrides)",
                    TableQueries.MigrationV55Statements
                ),
                new SchemaMigration(
                    56,
                    "Add token_usage table for per-model token accounting",
                    TableQueries.MigrationV56Statements
                ),
                new SchemaMigration(
                    57,
                    "Add mission execution mode (Implementation/Audit/Research)",
                    TableQueries.MigrationV57Statements
                ),
                new SchemaMigration(
                    58,
                    "Add in-dock Definition-of-Done gate config to vessels",
                    TableQueries.MigrationV58Statements
                ),
                new SchemaMigration(
                    59,
                    "Add git_anchors_json to docks",
                    TableQueries.MigrationV59Statements
                ),
                new SchemaMigration(
                    60,
                    "Add model_endpoints table for managed embedding/inference endpoints",
                    TableQueries.MigrationV60Statements
                ),
                new SchemaMigration(
                    61,
                    "Add rolling health-check history to model_endpoints",
                    TableQueries.MigrationV61Statements
                ),
                new SchemaMigration(
                    62,
                    "Add harbors and harbor_capabilities tables for host runners",
                    TableQueries.MigrationV62Statements
                ),
                new SchemaMigration(
                    63,
                    "Add Harbor routing and affinity columns",
                    TableQueries.MigrationV63Statements
                ),
                new SchemaMigration(
                    64,
                    "Add model_endpoint_id to captains for API-endpoint captains",
                    TableQueries.MigrationV64Statements
                ),
                new SchemaMigration(
                    65,
                    "Add scope to model_endpoints (tenant-wide vs user-specific)",
                    TableQueries.MigrationV65Statements
                ),
                new SchemaMigration(
                    66,
                    "Add ownership scope to playbooks and skills",
                    TableQueries.MigrationV66Statements
                ),
                new SchemaMigration(
                    67,
                    "Add ownership (user_id + scope) to personas, pipelines, prompt_templates",
                    TableQueries.MigrationV67Statements
                ),
                new SchemaMigration(
                    68,
                    "Add ownership_scope to workflow_profiles and project_profiles",
                    TableQueries.MigrationV68Statements
                ),
                new SchemaMigration(
                    69,
                    "Add cloud-provider fields (region, project, api_version, access_key_id) to model_endpoints",
                    TableQueries.MigrationV69Statements
                ),
                new SchemaMigration(
                    70,
                    "Add memories and memory_tags tables for durable agent memory",
                    TableQueries.MigrationV70Statements
                ),
                new SchemaMigration(
                    71,
                    "Add vessel_import_batches and vessel_import_items tables for bulk vessel import",
                    TableQueries.MigrationV71Statements
                ),
                new SchemaMigration(
                    72,
                    "Add fleet_actions, fleet_action_runs, and fleet_action_run_targets tables",
                    TableQueries.MigrationV72Statements
                ),
                new SchemaMigration(
                    73,
                    "Add vessel_health, vessel_health_findings, vessel_dependencies, and vessel_health_overrides tables",
                    TableQueries.MigrationV73Statements
                ),
                new SchemaMigration(
                    74,
                    "Add background discovery and fleet categorization columns to vessel_import_batches, a selected flag to vessel_import_items, plus vessel_import_fleet_recommendations and vessel_import_fleet_recommendation_vessels tables",
                    TableQueries.MigrationV74Statements
                ),
                new SchemaMigration(
                    75,
                    "Add ask_threads, ask_messages, ask_message_tool_calls, ask_action_proposals, and ask_tracked_work tables for Ask Armada conversation threads",
                    TableQueries.MigrationV75Statements
                ),
                new SchemaMigration(
                    76,
                    "Add nullable auto_approve to vessels: a per-vessel override of the captain auto-approve setting for missions on the vessel",
                    TableQueries.MigrationV76Statements
                ),
                new SchemaMigration(
                    77,
                    "Add failure_kind and wait_for_voyage_workers to missions: a persisted failure classification set where the failure happens, and a structured flag that defers a worker until the other workers in its voyage settle",
                    TableQueries.MigrationV77Statements
                ),
                new SchemaMigration(
                    78,
                    "Add CLI tool permissions: cli_permission_requests and cli_permission_rules tables, cli_permission_policy on captains and ask_threads, and permission_denied on ask_message_tool_calls",
                    TableQueries.MigrationV78Statements
                ),
                new SchemaMigration(
                    79,
                    "Add push notification devices: push_devices table (one row per Expo push token, owned by a user)",
                    TableQueries.MigrationV79Statements
                ),
                new SchemaMigration(
                    80,
                    "Add repository_path and checkout_path to docks: the repository a Harbor-side dock was created from and the user's checkout on that Harbor host",
                    TableQueries.MigrationV80Statements
                ),
                new SchemaMigration(
                    81,
                    "Add Harbor metrics: harbor_jobs (one row per captain launch delegated to a Harbor), harbor_link_samples (per-minute heartbeat round trips and reconnects), harbor_link_events (link transitions), and harbor_id on token_usage",
                    TableQueries.MigrationV81Statements
                ),
                new SchemaMigration(
                    82,
                    "Add Ask turn telemetry to ask_messages: ttft_ms, first_text_ms, streaming_ms, tokens_per_second, input_tokens, output_tokens, cached_tokens, tokens_estimated, cost_usd, tool_call_count, and tool_time_ms (all nullable)",
                    TableQueries.MigrationV82Statements
                ),
                new SchemaMigration(83, "Add ordered persona captain priorities and availability policies", TableQueries.MigrationV83Statements),
                new SchemaMigration(84, "Add authentication type, settings, and extra headers to model_endpoints", TableQueries.MigrationV84Statements),
                new SchemaMigration(85, "Add source_template to workflow_profiles for profiles Armada creates from its built-in templates", TableQueries.MigrationV85Statements),
                new SchemaMigration(86, "Add inference settings to model endpoints", "ALTER TABLE model_endpoints ADD COLUMN inference_options_json LONGTEXT NULL;"),
                new SchemaMigration(87, "Add per-run approval choices and timeout decisions",
                    @"ALTER TABLE voyages ADD COLUMN approval_options_json TEXT NULL;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN approval_options_json TEXT NULL;",
                    @"ALTER TABLE cli_permission_requests ADD COLUMN timeout_decision VARCHAR(32) NOT NULL DEFAULT 'Deny';"),
                new SchemaMigration(88, "Add output_repair_attempts to missions for bounded output-contract repair turns",
                    @"ALTER TABLE missions ADD COLUMN output_repair_attempts INT NOT NULL DEFAULT 0;"),
                new SchemaMigration(89, "Add noop_redispatch_attempts to missions so the no-op budget is counted separately",
                    @"ALTER TABLE missions ADD COLUMN noop_redispatch_attempts INT NOT NULL DEFAULT 0;"),
                new SchemaMigration(90, "Add is_rescue to missions so a rescue is never rescued again, even after its incident is purged",
                    @"ALTER TABLE missions ADD COLUMN is_rescue TINYINT(1) NOT NULL DEFAULT 0;"),
                new SchemaMigration(91, "Add rework_round and rework_escalated to missions so rework state is not read from titles",
                    @"ALTER TABLE missions ADD COLUMN rework_round INT NOT NULL DEFAULT 0;",
                    @"ALTER TABLE missions ADD COLUMN rework_escalated TINYINT(1) NOT NULL DEFAULT 0;"),
                new SchemaMigration(92, "Add stage_outcomes (one row per stage attempt with provenance, disposition and suspected cause) and failure_cause_rules (amendable rules that map an attempt's signals to a suspected cause)",
                    @"CREATE TABLE IF NOT EXISTS stage_outcomes (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        mission_id VARCHAR(64) NOT NULL,
                        voyage_id VARCHAR(64),
                        vessel_id VARCHAR(64),
                        captain_id VARCHAR(64),
                        captain_name VARCHAR(512),
                        persona VARCHAR(64),
                        attempt INT NOT NULL,
                        rework_round INT NOT NULL,
                        runtime VARCHAR(64),
                        model VARCHAR(512),
                        model_provider VARCHAR(64),
                        model_endpoint_id VARCHAR(64),
                        harbor_id VARCHAR(64),
                        context_tokens INT,
                        temperature DOUBLE,
                        top_p DOUBLE,
                        tools_enabled TINYINT(1),
                        mcp_tools_enabled TINYINT(1),
                        reasoning_effort VARCHAR(64),
                        runtime_version VARCHAR(512),
                        model_digest VARCHAR(512),
                        prompt_version VARCHAR(512),
                        armada_version VARCHAR(64),
                        exit_code INT,
                        runtime_failure_kind VARCHAR(64),
                        stage_check_status VARCHAR(64),
                        stage_check_reason VARCHAR(64),
                        disposition VARCHAR(64) NOT NULL,
                        failure_kind VARCHAR(64),
                        failure_reason LONGTEXT,
                        judge_verdict VARCHAR(64),
                        reviewed_mission_id VARCHAR(64),
                        next_captain_id VARCHAR(64),
                        suspected_cause VARCHAR(64) NOT NULL,
                        cause_source VARCHAR(64) NOT NULL,
                        cause_rule_id VARCHAR(64),
                        cause_note LONGTEXT,
                        classified_by_user_id VARCHAR(64),
                        classified_utc DATETIME(6),
                        is_evaluation TINYINT(1) NOT NULL,
                        started_utc DATETIME(6),
                        ended_utc DATETIME(6) NOT NULL,
                        duration_ms BIGINT,
                        created_utc DATETIME(6) NOT NULL,
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS failure_cause_rules (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        name VARCHAR(512) NOT NULL,
                        description LONGTEXT,
                        priority INT NOT NULL,
                        enabled TINYINT(1) NOT NULL,
                        is_built_in TINYINT(1) NOT NULL,
                        match_json LONGTEXT NOT NULL,
                        cause VARCHAR(64) NOT NULL,
                        created_by_user_id VARCHAR(64),
                        created_utc DATETIME(6) NOT NULL,
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE INDEX idx_stage_outcomes_mission ON stage_outcomes(mission_id);",
                    @"CREATE INDEX idx_stage_outcomes_tenant_ended ON stage_outcomes(tenant_id, ended_utc);",
                    @"CREATE INDEX idx_stage_outcomes_captain_ended ON stage_outcomes(captain_id, ended_utc);",
                    @"CREATE INDEX idx_stage_outcomes_cause ON stage_outcomes(suspected_cause, ended_utc);",
                    @"CREATE INDEX idx_failure_cause_rules_tenant_priority ON failure_cause_rules(tenant_id, priority);"),
                new SchemaMigration(93, "Add commit_hash, finished_by_outcome_id, finished_by_captain_id and prior_work_kept to stage_outcomes (the escalation record: which captain finished an escalated attempt and whether it kept or replaced its work)",
                    @"ALTER TABLE stage_outcomes ADD COLUMN commit_hash VARCHAR(64) NULL;",
                    @"ALTER TABLE stage_outcomes ADD COLUMN finished_by_outcome_id VARCHAR(64) NULL;",
                    @"ALTER TABLE stage_outcomes ADD COLUMN finished_by_captain_id VARCHAR(64) NULL;",
                    @"ALTER TABLE stage_outcomes ADD COLUMN prior_work_kept TINYINT(1) NULL;"),
                new SchemaMigration(94, "Add model_probe_runs (one row per probe-only Evaluate of a captain or model endpoint, tagged as evaluation) and model_probe_results (one row per probe of a run)",
                    @"CREATE TABLE IF NOT EXISTS model_probe_runs (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        target_kind VARCHAR(64) NOT NULL,
                        captain_id VARCHAR(64),
                        captain_name VARCHAR(512),
                        model_endpoint_id VARCHAR(64),
                        depth VARCHAR(64) NOT NULL,
                        status VARCHAR(64) NOT NULL,
                        is_evaluation TINYINT(1) NOT NULL,
                        probe_set_version VARCHAR(64) NOT NULL,
                        runtime VARCHAR(64),
                        runtime_version VARCHAR(512),
                        model_provider VARCHAR(64),
                        model VARCHAR(512),
                        model_digest VARCHAR(512),
                        configured_context_tokens INT,
                        served_context_tokens INT,
                        harbor_id VARCHAR(64),
                        inference_host VARCHAR(512),
                        token_budget BIGINT NOT NULL,
                        tokens_used BIGINT NOT NULL,
                        passed_count INT NOT NULL,
                        failed_count INT NOT NULL,
                        could_not_run_count INT NOT NULL,
                        failure_reason LONGTEXT,
                        created_utc DATETIME(6) NOT NULL,
                        started_utc DATETIME(6),
                        completed_utc DATETIME(6),
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS model_probe_results (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        run_id VARCHAR(64) NOT NULL,
                        tenant_id VARCHAR(64),
                        probe VARCHAR(64) NOT NULL,
                        outcome VARCHAR(64) NOT NULL,
                        summary LONGTEXT,
                        reason LONGTEXT,
                        attempts INT NOT NULL,
                        tokens_used BIGINT NOT NULL,
                        duration_ms BIGINT NOT NULL,
                        metrics_json LONGTEXT,
                        output_excerpt LONGTEXT,
                        created_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE INDEX idx_model_probe_runs_tenant ON model_probe_runs(tenant_id);",
                    @"CREATE INDEX idx_model_probe_runs_captain ON model_probe_runs(captain_id);",
                    @"CREATE INDEX idx_model_probe_runs_endpoint ON model_probe_runs(model_endpoint_id);",
                    @"CREATE INDEX idx_model_probe_runs_created ON model_probe_runs(created_utc);",
                    @"CREATE INDEX idx_model_probe_runs_host ON model_probe_runs(inference_host);",
                    @"CREATE INDEX idx_model_probe_results_run ON model_probe_results(run_id);"),
                new SchemaMigration(95, "Add battery_runs (one row per fixture battery run against a captain, tagged as evaluation, with seed, sampling settings, battery version and configuration revision) and battery_task_results (one row per graded task attempt)",
                    @"CREATE TABLE IF NOT EXISTS battery_runs (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        captain_id VARCHAR(64),
                        captain_name VARCHAR(512),
                        packs LONGTEXT NOT NULL,
                        battery_version VARCHAR(64) NOT NULL,
                        run_count INT NOT NULL,
                        status VARCHAR(64) NOT NULL,
                        is_evaluation TINYINT(1) NOT NULL,
                        pipeline VARCHAR(64) NOT NULL,
                        seed INT,
                        sampling_json LONGTEXT,
                        configuration_revision VARCHAR(512),
                        configuration_json LONGTEXT,
                        runtime VARCHAR(64),
                        model VARCHAR(512),
                        model_endpoint_id VARCHAR(64),
                        inference_host VARCHAR(512),
                        total_tasks INT NOT NULL,
                        passed_count INT NOT NULL,
                        failed_count INT NOT NULL,
                        could_not_run_count INT NOT NULL,
                        pending_evaluation_count INT NOT NULL,
                        failure_reason LONGTEXT,
                        created_utc DATETIME(6) NOT NULL,
                        started_utc DATETIME(6),
                        completed_utc DATETIME(6),
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS battery_task_results (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        run_id VARCHAR(64) NOT NULL,
                        tenant_id VARCHAR(64),
                        pack_id VARCHAR(64) NOT NULL,
                        pack_version INT NOT NULL,
                        task_id VARCHAR(64) NOT NULL,
                        task_title VARCHAR(512),
                        persona VARCHAR(64),
                        grader VARCHAR(64) NOT NULL,
                        attempt INT NOT NULL,
                        outcome VARCHAR(64) NOT NULL,
                        score DOUBLE,
                        summary LONGTEXT,
                        reason LONGTEXT,
                        details_json LONGTEXT,
                        output LONGTEXT,
                        vessel_id VARCHAR(64),
                        voyage_id VARCHAR(64),
                        mission_id VARCHAR(64),
                        mission_status VARCHAR(64),
                        fixture_commit VARCHAR(64),
                        duration_ms BIGINT NOT NULL,
                        created_utc DATETIME(6) NOT NULL,
                        completed_utc DATETIME(6)
                    );",
                    @"CREATE INDEX idx_battery_runs_tenant ON battery_runs(tenant_id);",
                    @"CREATE INDEX idx_battery_runs_captain ON battery_runs(captain_id);",
                    @"CREATE INDEX idx_battery_runs_status ON battery_runs(status);",
                    @"CREATE INDEX idx_battery_runs_created ON battery_runs(created_utc);",
                    @"CREATE INDEX idx_battery_task_results_run ON battery_task_results(run_id);",
                    @"CREATE INDEX idx_battery_task_results_mission ON battery_task_results(mission_id);"),
                new SchemaMigration(96, "Add is_evaluation to voyages and missions so evaluation work (fixture battery and model evaluation) is flagged first-class instead of inferred from the vessel pipeline",
                    @"ALTER TABLE voyages ADD COLUMN is_evaluation TINYINT(1) NOT NULL DEFAULT 0;",
                    @"ALTER TABLE missions ADD COLUMN is_evaluation TINYINT(1) NOT NULL DEFAULT 0;"),
                new SchemaMigration(97, "Add handoff_artifacts (one durable, versioned handoff artifact per stage attempt: findings, acceptance criteria, check results, commit references, the full stage output and diff) for budgeted delivery to the next stage",
                    @"CREATE TABLE IF NOT EXISTS handoff_artifacts (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        mission_id VARCHAR(64) NOT NULL,
                        voyage_id VARCHAR(64),
                        vessel_id VARCHAR(64),
                        stage_outcome_id VARCHAR(64),
                        persona VARCHAR(256),
                        captain_id VARCHAR(64),
                        captain_name VARCHAR(256),
                        attempt INT NOT NULL,
                        rework_round INT NOT NULL,
                        schema_version INT NOT NULL,
                        verdict VARCHAR(256),
                        branch_name VARCHAR(256),
                        commit_hash VARCHAR(64),
                        findings_json LONGTEXT,
                        acceptance_criteria_json LONGTEXT,
                        required_actions_json LONGTEXT,
                        check_results_json LONGTEXT,
                        commit_references_json LONGTEXT,
                        completion_lines_json LONGTEXT,
                        output LONGTEXT,
                        output_length INT NOT NULL,
                        output_complete TINYINT(1) NOT NULL,
                        diff LONGTEXT,
                        diff_length INT NOT NULL,
                        diff_complete TINYINT(1) NOT NULL,
                        diff_pointer LONGTEXT,
                        created_utc DATETIME(6) NOT NULL,
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE INDEX idx_handoff_artifacts_mission_created ON handoff_artifacts(mission_id, created_utc);",
                    @"CREATE INDEX idx_handoff_artifacts_tenant_created ON handoff_artifacts(tenant_id, created_utc);"),
                new SchemaMigration(98, "Add contract_repair_kind to stage_outcomes so a deterministic contract repair shows on the stage outcome as well as in the mission.contract_repaired event",
                    @"ALTER TABLE stage_outcomes ADD COLUMN contract_repair_kind VARCHAR(64) NULL;"),

                new SchemaMigration(99, "Add test_command, lint_command and audit_command to vessels so a vessel can override the ecosystem default command for the test, lint and audit intents",
                    @"ALTER TABLE vessels ADD COLUMN test_command TEXT;",
                    @"ALTER TABLE vessels ADD COLUMN lint_command TEXT;",
                    @"ALTER TABLE vessels ADD COLUMN audit_command TEXT;"),
                new SchemaMigration(100, "Add abbreviated_unit_test_command to workflow_profiles so a profile can carry a quick unit-test command next to the full one",
                    @"ALTER TABLE workflow_profiles ADD COLUMN abbreviated_unit_test_command LONGTEXT NULL;"),
                new SchemaMigration(101, "Add fleet action runtime inputs, parser and preflight snapshots and findings counts",
                    @"ALTER TABLE fleet_actions ADD COLUMN command_windows LONGTEXT NULL;",
                    @"ALTER TABLE fleet_actions ADD COLUMN output_parser LONGTEXT NULL;",
                    @"ALTER TABLE fleet_actions ADD COLUMN scan_source_action_key LONGTEXT NULL;",
                    @"ALTER TABLE fleet_actions ADD COLUMN required_tools_json LONGTEXT NULL;",
                    @"ALTER TABLE fleet_actions ADD COLUMN requires_network TINYINT(1) NOT NULL DEFAULT 0;",
                    @"ALTER TABLE fleet_actions ADD COLUMN inputs_json LONGTEXT NULL;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN command_windows LONGTEXT NULL;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN output_parser LONGTEXT NULL;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN scan_source_action_key LONGTEXT NULL;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN required_tools_json LONGTEXT NULL;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN requires_network TINYINT(1) NOT NULL DEFAULT 0;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN inputs_json LONGTEXT NULL;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN input_values_json LONGTEXT NULL;",
                    @"ALTER TABLE fleet_action_runs ADD COLUMN findings_count INT NOT NULL DEFAULT 0;",
                    @"ALTER TABLE fleet_action_run_targets ADD COLUMN scan_summary LONGTEXT NULL;"),
                new SchemaMigration(102, "Add incidents and runbook_executions: one row per incident and per runbook execution holding its current state, so their lists page and count in the database (the snapshot events stay as history)",
                    @"CREATE TABLE IF NOT EXISTS incidents (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        title LONGTEXT NOT NULL,
                        status VARCHAR(32) NOT NULL,
                        severity VARCHAR(32) NOT NULL,
                        vessel_id VARCHAR(64),
                        environment_id VARCHAR(64),
                        deployment_id VARCHAR(64),
                        release_id VARCHAR(64),
                        mission_id VARCHAR(64),
                        voyage_id VARCHAR(64),
                        failure_kind VARCHAR(64),
                        search_text LONGTEXT,
                        payload LONGTEXT NOT NULL,
                        detected_utc DATETIME(6) NOT NULL,
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE INDEX idx_incidents_tenant_user ON incidents(tenant_id, user_id);",
                    @"CREATE INDEX idx_incidents_last_update ON incidents(last_update_utc, id);",
                    @"CREATE TABLE IF NOT EXISTS runbook_executions (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        runbook_id VARCHAR(64) NOT NULL,
                        deployment_id VARCHAR(64),
                        incident_id VARCHAR(64),
                        status VARCHAR(32) NOT NULL,
                        search_text LONGTEXT,
                        payload LONGTEXT NOT NULL,
                        started_utc DATETIME(6) NOT NULL,
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE INDEX idx_runbook_executions_tenant_user ON runbook_executions(tenant_id, user_id);",
                    @"CREATE INDEX idx_runbook_executions_runbook ON runbook_executions(runbook_id);",
                    @"CREATE INDEX idx_runbook_executions_last_update ON runbook_executions(last_update_utc, id);"),
                new SchemaMigration(103, "Add the mission and voyage black box: blackbox_entries, blackbox_objects (object bytes in a BLOB column for the Database target), blackbox_files, blackbox_links and blackbox_notes, plus missions.last_blackbox_entry_id, missions.start_commit, voyages.last_blackbox_entry_id and vessels.blackbox_retention_days",
                    @"CREATE TABLE IF NOT EXISTS blackbox_entries (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        subject_type VARCHAR(64) NOT NULL,
                        mission_id VARCHAR(64),
                        voyage_id VARCHAR(64),
                        vessel_id VARCHAR(64),
                        fleet_id VARCHAR(64),
                        captain_id VARCHAR(64),
                        harbor_id VARCHAR(64),
                        dock_id VARCHAR(64),
                        merge_entry_id VARCHAR(64),
                        incident_id VARCHAR(64),
                        stage_outcome_id VARCHAR(64),
                        parent_mission_id VARCHAR(64),
                        mission_title LONGTEXT,
                        vessel_name LONGTEXT,
                        attempt INT NOT NULL DEFAULT 1,
                        is_current TINYINT(1) NOT NULL DEFAULT 0,
                        supersedes_id VARCHAR(64),
                        phase VARCHAR(64) NOT NULL,
                        step VARCHAR(64) NOT NULL,
                        failure_kind VARCHAR(64),
                        status_before VARCHAR(64),
                        status_after VARCHAR(64),
                        triage VARCHAR(64) NOT NULL,
                        fingerprint VARCHAR(64),
                        capture_mode VARCHAR(64) NOT NULL,
                        state VARCHAR(64) NOT NULL,
                        resolution VARCHAR(64) NOT NULL,
                        resolution_note LONGTEXT,
                        resolution_link LONGTEXT,
                        pinned TINYINT(1) NOT NULL DEFAULT 0,
                        storage_target VARCHAR(64) NOT NULL,
                        storage_prefix LONGTEXT,
                        total_bytes BIGINT NOT NULL DEFAULT 0,
                        object_count INT NOT NULL DEFAULT 0,
                        schema_version INT NOT NULL DEFAULT 1,
                        rules_version INT NOT NULL DEFAULT 0,
                        armada_version LONGTEXT,
                        armada_commit VARCHAR(64),
                        reason_excerpt LONGTEXT,
                        summary LONGTEXT,
                        enrichment_attempts INT NOT NULL DEFAULT 0,
                        last_error LONGTEXT,
                        next_attempt_utc DATETIME(6),
                        expires_utc DATETIME(6),
                        created_utc DATETIME(6) NOT NULL,
                        enriched_utc DATETIME(6),
                        analyzed_utc DATETIME(6),
                        resolved_utc DATETIME(6),
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS blackbox_objects (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        entry_id VARCHAR(64) NOT NULL,
                        tenant_id VARCHAR(64),
                        name VARCHAR(255) NOT NULL,
                        content_type VARCHAR(255) NOT NULL,
                        content_encoding VARCHAR(64) NOT NULL,
                        size_bytes BIGINT NOT NULL DEFAULT 0,
                        uncompressed_bytes BIGINT NOT NULL DEFAULT 0,
                        sha256 VARCHAR(64),
                        storage_key LONGTEXT,
                        missing TINYINT(1) NOT NULL DEFAULT 0,
                        data LONGBLOB,
                        created_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS blackbox_files (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        entry_id VARCHAR(64) NOT NULL,
                        tenant_id VARCHAR(64),
                        vessel_id VARCHAR(64),
                        path LONGTEXT NOT NULL,
                        directory VARCHAR(512) NOT NULL,
                        role VARCHAR(64) NOT NULL,
                        conflicting_mission_id VARCHAR(64),
                        created_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS blackbox_links (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        entry_id VARCHAR(64) NOT NULL,
                        tenant_id VARCHAR(64),
                        mission_id VARCHAR(64) NOT NULL,
                        relation VARCHAR(64) NOT NULL,
                        commit_hash VARCHAR(64),
                        overlap_file_count INT NOT NULL DEFAULT 0,
                        created_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS blackbox_notes (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        entry_id VARCHAR(64) NOT NULL,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        author_kind VARCHAR(64) NOT NULL,
                        body LONGTEXT NOT NULL,
                        created_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE INDEX idx_blackbox_entries_tenant_mission_current ON blackbox_entries(tenant_id, mission_id, is_current);",
                    @"CREATE INDEX idx_blackbox_entries_mission_current ON blackbox_entries(mission_id, is_current);",
                    @"CREATE INDEX idx_blackbox_entries_tenant_voyage ON blackbox_entries(tenant_id, voyage_id);",
                    @"CREATE INDEX idx_blackbox_entries_voyage ON blackbox_entries(voyage_id);",
                    @"CREATE INDEX idx_blackbox_entries_tenant_vessel_created ON blackbox_entries(tenant_id, vessel_id, created_utc);",
                    @"CREATE INDEX idx_blackbox_entries_tenant_fingerprint_created ON blackbox_entries(tenant_id, fingerprint, created_utc);",
                    @"CREATE INDEX idx_blackbox_entries_expires ON blackbox_entries(expires_utc);",
                    @"CREATE INDEX idx_blackbox_entries_tenant_resolution_triage ON blackbox_entries(tenant_id, resolution, triage);",
                    @"CREATE INDEX idx_blackbox_entries_created ON blackbox_entries(created_utc, id);",
                    @"CREATE INDEX idx_blackbox_entries_state ON blackbox_entries(state);",
                    @"CREATE UNIQUE INDEX idx_blackbox_objects_entry_name ON blackbox_objects(entry_id, name);",
                    @"CREATE INDEX idx_blackbox_files_tenant_vessel_directory ON blackbox_files(tenant_id, vessel_id, directory);",
                    @"CREATE INDEX idx_blackbox_files_entry ON blackbox_files(entry_id);",
                    @"CREATE INDEX idx_blackbox_links_entry ON blackbox_links(entry_id);",
                    @"CREATE INDEX idx_blackbox_links_mission ON blackbox_links(mission_id);",
                    @"CREATE INDEX idx_blackbox_notes_entry_created ON blackbox_notes(entry_id, created_utc);",
                    @"ALTER TABLE missions ADD COLUMN last_blackbox_entry_id VARCHAR(64) NULL;",
                    @"ALTER TABLE missions ADD COLUMN start_commit VARCHAR(64) NULL;",
                    @"ALTER TABLE voyages ADD COLUMN last_blackbox_entry_id VARCHAR(64) NULL;",
                    @"ALTER TABLE vessels ADD COLUMN blackbox_retention_days INT NULL;"),

                new SchemaMigration(104, "Add blackbox_migrations: one row per black box storage migration job (copy, verify, switch, delete between storage targets) so a job is resumable after a restart and its progress can be read back",
                    @"CREATE TABLE IF NOT EXISTS blackbox_migrations (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        state VARCHAR(64) NOT NULL,
                        from_target VARCHAR(64),
                        to_target VARCHAR(64) NOT NULL,
                        delete_source TINYINT(1) NOT NULL DEFAULT 1,
                        batch_size INT NOT NULL DEFAULT 50,
                        total_entries BIGINT NOT NULL DEFAULT 0,
                        migrated_entries BIGINT NOT NULL DEFAULT 0,
                        failed_entries BIGINT NOT NULL DEFAULT 0,
                        bytes_copied BIGINT NOT NULL DEFAULT 0,
                        last_entry_id VARCHAR(64),
                        last_error LONGTEXT,
                        created_utc DATETIME(6) NOT NULL,
                        started_utc DATETIME(6),
                        finished_utc DATETIME(6),
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE INDEX idx_blackbox_migrations_state_created ON blackbox_migrations(state, created_utc);"),

                new SchemaMigration(105, "Add vessels.serialize_overlapping_missions: when true, a mission dispatched to the vessel that overlaps an active mission's files or a reserved hotspot directory is chained after it with DependsOnMissionId",
                    @"ALTER TABLE vessels ADD COLUMN serialize_overlapping_missions TINYINT(1) NOT NULL DEFAULT 0;"),

                new SchemaMigration(106, "Add the black box success flight recorder (blackbox_successes: minimal records of successful landings kept for blackBox.successRetentionDays) and recovery outcome tracking (blackbox_recovery_outcomes: one row per confirmed recovery run with the mission's next terminal status)",
                    @"CREATE TABLE IF NOT EXISTS blackbox_successes (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        vessel_id VARCHAR(64),
                        mission_id VARCHAR(64) NOT NULL,
                        voyage_id VARCHAR(64),
                        captain_id VARCHAR(64),
                        mission_title LONGTEXT,
                        base_commit VARCHAR(64),
                        target_commit VARCHAR(64),
                        target_branch VARCHAR(256),
                        files_changed LONGTEXT,
                        file_count INT NOT NULL DEFAULT 0,
                        mission_created_utc DATETIME(6),
                        started_utc DATETIME(6),
                        landed_utc DATETIME(6) NOT NULL,
                        queue_ms BIGINT,
                        run_ms BIGINT,
                        total_ms BIGINT,
                        created_utc DATETIME(6) NOT NULL,
                        expires_utc DATETIME(6)
                    );",
                    @"CREATE TABLE IF NOT EXISTS blackbox_recovery_outcomes (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        user_id VARCHAR(64),
                        entry_id VARCHAR(64) NOT NULL,
                        mission_id VARCHAR(64),
                        voyage_id VARCHAR(64),
                        vessel_id VARCHAR(64),
                        rule_id VARCHAR(64),
                        path_id VARCHAR(256) NOT NULL,
                        runbook_id VARCHAR(64),
                        run_succeeded TINYINT(1) NOT NULL DEFAULT 0,
                        outcome VARCHAR(64) NOT NULL,
                        outcome_status VARCHAR(64),
                        created_utc DATETIME(6) NOT NULL,
                        resolved_utc DATETIME(6)
                    );",
                    @"CREATE INDEX idx_blackbox_successes_tenant_vessel_landed ON blackbox_successes(tenant_id, vessel_id, landed_utc);",
                    @"CREATE INDEX idx_blackbox_successes_mission ON blackbox_successes(mission_id);",
                    @"CREATE INDEX idx_blackbox_successes_expires ON blackbox_successes(expires_utc);",
                    @"CREATE INDEX idx_blackbox_recovery_outcomes_outcome_created ON blackbox_recovery_outcomes(outcome, created_utc);",
                    @"CREATE INDEX idx_blackbox_recovery_outcomes_tenant_rule_path ON blackbox_recovery_outcomes(tenant_id, rule_id, path_id);",
                    @"CREATE INDEX idx_blackbox_recovery_outcomes_entry ON blackbox_recovery_outcomes(entry_id);"),

                new SchemaMigration(107, "Add model profiles: model_profiles (the model as served, keyed by a unique identity of runtime, provider, model and served context, with editable capabilities, rendering overrides and notes), model_profile_revisions (immutable runtime version, model digest, sampling and tool configuration), model_profile_revision_compat (administrator compatibility marks between revisions) and captains.model_profile_id",
                    @"CREATE TABLE IF NOT EXISTS model_profiles (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        identity_key VARCHAR(64) NOT NULL,
                        runtime VARCHAR(256) NOT NULL,
                        provider VARCHAR(256),
                        model VARCHAR(512),
                        served_context_tokens INT,
                        tools_available TINYINT(1),
                        mcp_available TINYINT(1),
                        rendering_template VARCHAR(256),
                        rendering_repeat_contract_at_end TINYINT(1),
                        rendering_max_files_per_mission INT,
                        notes LONGTEXT,
                        created_utc DATETIME(6) NOT NULL,
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS model_profile_revisions (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        profile_id VARCHAR(64) NOT NULL,
                        tenant_id VARCHAR(64),
                        runtime_version VARCHAR(256),
                        model_digest VARCHAR(256),
                        sampling_temperature DOUBLE,
                        sampling_top_p DOUBLE,
                        tool_tools TINYINT(1),
                        tool_mcp TINYINT(1),
                        first_seen_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE TABLE IF NOT EXISTS model_profile_revision_compat (
                        revision_id VARCHAR(64) NOT NULL,
                        compatible_revision_id VARCHAR(64) NOT NULL,
                        created_utc DATETIME(6) NOT NULL,
                        PRIMARY KEY (revision_id, compatible_revision_id)
                    );",
                    @"CREATE UNIQUE INDEX idx_model_profiles_identity_key ON model_profiles(identity_key);",
                    @"CREATE INDEX idx_model_profiles_tenant_created ON model_profiles(tenant_id, created_utc);",
                    @"CREATE INDEX idx_model_profile_revisions_profile_seen ON model_profile_revisions(profile_id, first_seen_utc);",
                    @"CREATE INDEX idx_model_profile_revision_compat_other ON model_profile_revision_compat(compatible_revision_id);",
                    @"ALTER TABLE captains ADD COLUMN model_profile_id VARCHAR(64) NULL;"),

                new SchemaMigration(108, "Add model_alerts: one row per scorecard alert condition (tenant, model profile, revision group, persona, vessel scope, rule) with a unique alert key, open/resolved status, the counts, ignore-until and the applied-action audit record",
                    @"CREATE TABLE IF NOT EXISTS model_alerts (
                        id VARCHAR(64) NOT NULL PRIMARY KEY,
                        tenant_id VARCHAR(64),
                        alert_key VARCHAR(64) NOT NULL,
                        profile_id VARCHAR(64) NOT NULL,
                        model VARCHAR(512),
                        revision_group LONGTEXT NOT NULL,
                        persona VARCHAR(256) NOT NULL,
                        vessel_id VARCHAR(64),
                        vessel_name VARCHAR(512),
                        rule VARCHAR(64) NOT NULL,
                        status VARCHAR(32) NOT NULL,
                        title LONGTEXT NOT NULL,
                        detail LONGTEXT,
                        metric_count INT,
                        metric_of INT,
                        metric_value DOUBLE,
                        threshold_value DOUBLE,
                        created_utc DATETIME(6) NOT NULL,
                        last_raised_utc DATETIME(6) NOT NULL,
                        last_evaluated_utc DATETIME(6) NOT NULL,
                        resolved_utc DATETIME(6),
                        ignored_until_utc DATETIME(6),
                        actions_json LONGTEXT,
                        last_update_utc DATETIME(6) NOT NULL
                    );",
                    @"CREATE UNIQUE INDEX idx_model_alerts_alert_key ON model_alerts(alert_key);",
                    @"CREATE INDEX idx_model_alerts_tenant_status_raised ON model_alerts(tenant_id, status, last_raised_utc);",
                    @"CREATE INDEX idx_model_alerts_profile ON model_alerts(profile_id);")


            };
        }

        internal static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        internal static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        internal static DateTime? FromIso8601Nullable(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            string str = value.ToString()!;
            if (string.IsNullOrEmpty(str)) return null;
            return FromIso8601(str);
        }

        internal static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        internal static int? NullableInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value);
        }

        internal static TenantMetadata TenantFromReader(MySqlDataReader reader)
        {
            TenantMetadata tenant = new TenantMetadata();
            tenant.Id = reader["id"].ToString()!;
            tenant.Name = reader["name"].ToString()!;
            tenant.Active = Convert.ToInt64(reader["active"]) == 1;
            tenant.IsProtected = Convert.ToInt64(reader["is_protected"]) == 1;
            tenant.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            tenant.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            return tenant;
        }

        internal static UserMaster UserFromReader(MySqlDataReader reader)
        {
            UserMaster user = new UserMaster();
            user.Id = reader["id"].ToString()!;
            user.TenantId = reader["tenant_id"].ToString()!;
            user.Email = reader["email"].ToString()!;
            user.PasswordSha256 = reader["password_sha256"].ToString()!;
            user.FirstName = NullableString(reader["first_name"]);
            user.LastName = NullableString(reader["last_name"]);
            user.IsAdmin = Convert.ToInt64(reader["is_admin"]) == 1;
            user.IsTenantAdmin = Convert.ToInt64(reader["is_tenant_admin"]) == 1;
            user.IsProtected = Convert.ToInt64(reader["is_protected"]) == 1;
            user.Active = Convert.ToInt64(reader["active"]) == 1;
            user.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            user.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            return user;
        }

        internal static Credential CredentialFromReader(MySqlDataReader reader)
        {
            Credential cred = new Credential();
            cred.Id = reader["id"].ToString()!;
            cred.TenantId = reader["tenant_id"].ToString()!;
            cred.UserId = reader["user_id"].ToString()!;
            cred.Name = NullableString(reader["name"]);
            cred.BearerToken = reader["bearer_token"].ToString()!;
            cred.Active = Convert.ToInt64(reader["active"]) == 1;
            cred.IsProtected = Convert.ToInt64(reader["is_protected"]) == 1;
            cred.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            cred.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            return cred;
        }

        internal static Fleet FleetFromReader(MySqlDataReader reader)
        {
            Fleet fleet = new Fleet();
            fleet.Id = reader["id"].ToString()!;
            fleet.TenantId = NullableString(reader["tenant_id"]);
            fleet.UserId = NullableString(reader["user_id"]);
            fleet.Name = reader["name"].ToString()!;
            fleet.Description = NullableString(reader["description"]);
            fleet.Active = Convert.ToInt64(reader["active"]) == 1;
            fleet.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            fleet.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            return fleet;
        }

        internal static Vessel VesselFromReader(MySqlDataReader reader)
        {
            Vessel vessel = new Vessel();
            vessel.Id = reader["id"].ToString()!;
            vessel.TenantId = NullableString(reader["tenant_id"]);
            vessel.UserId = NullableString(reader["user_id"]);
            vessel.FleetId = NullableString(reader["fleet_id"]);
            vessel.Name = reader["name"].ToString()!;
            vessel.RepoUrl = NullableString(reader["repo_url"]);
            vessel.LocalPath = NullableString(reader["local_path"]);
            vessel.WorkingDirectory = NullableString(reader["working_directory"]);
            vessel.ProjectContext = NullableString(reader["project_context"]);
            vessel.StyleGuide = NullableString(reader["style_guide"]);
            try { vessel.EnableModelContext = Convert.ToInt64(reader["enable_model_context"]) == 1; }
            catch { vessel.EnableModelContext = true; }
            vessel.ModelContext = NullableString(reader["model_context"]);
            string? landingModeStr = NullableString(reader["landing_mode"]);
            if (!String.IsNullOrEmpty(landingModeStr) && Enum.TryParse<LandingModeEnum>(landingModeStr, out LandingModeEnum lm))
                vessel.LandingMode = lm;
            string? branchCleanupStr = NullableString(reader["branch_cleanup_policy"]);
            if (!String.IsNullOrEmpty(branchCleanupStr) && Enum.TryParse<BranchCleanupPolicyEnum>(branchCleanupStr, out BranchCleanupPolicyEnum bcp))
                vessel.BranchCleanupPolicy = bcp;
            try { vessel.RequirePassingChecksToLand = Convert.ToInt64(reader["require_passing_checks_to_land"]) == 1; }
            catch { vessel.RequirePassingChecksToLand = false; }
            try { object autoApproveValue = reader["auto_approve"]; vessel.AutoApprove = autoApproveValue == null || autoApproveValue == DBNull.Value ? (bool?)null : Convert.ToInt64(autoApproveValue) == 1; }
            catch { vessel.AutoApprove = null; }
            try { vessel.AllowConcurrentMissions = Convert.ToInt64(reader["allow_concurrent_missions"]) == 1; }
            catch { vessel.AllowConcurrentMissions = false; }
            try
            {
                string? protectedPatternsJson = NullableString(reader["protected_branch_patterns_json"]);
                if (!String.IsNullOrWhiteSpace(protectedPatternsJson))
                    vessel.ProtectedBranchPatterns = JsonSerializer.Deserialize<List<string>>(protectedPatternsJson) ?? new List<string>();
            }
            catch { }
            try { vessel.ReleaseBranchPrefix = NullableString(reader["release_branch_prefix"]) ?? "release/"; }
            catch { vessel.ReleaseBranchPrefix = "release/"; }
            try { vessel.HotfixBranchPrefix = NullableString(reader["hotfix_branch_prefix"]) ?? "hotfix/"; }
            catch { vessel.HotfixBranchPrefix = "hotfix/"; }
            try { vessel.RequirePullRequestForProtectedBranches = Convert.ToInt64(reader["require_pull_request_for_protected_branches"]) == 1; }
            catch { vessel.RequirePullRequestForProtectedBranches = false; }
            try { vessel.RequireMergeQueueForReleaseBranches = Convert.ToInt64(reader["require_merge_queue_for_release_branches"]) == 1; }
            catch { vessel.RequireMergeQueueForReleaseBranches = false; }
            try { vessel.AutoLandEnabled = Convert.ToInt64(reader["auto_land_enabled"]) == 1; } catch { }
            try { vessel.AutoLandMaxFiles = Convert.ToInt32(reader["auto_land_max_files"]); } catch { }
            try { vessel.AutoLandMaxLines = Convert.ToInt32(reader["auto_land_max_lines"]); } catch { }
            try
            {
                string? allowGlobsJson = NullableString(reader["auto_land_path_allow_globs_json"]);
                if (!String.IsNullOrWhiteSpace(allowGlobsJson))
                    vessel.AutoLandPathAllowGlobs = JsonSerializer.Deserialize<List<string>>(allowGlobsJson) ?? new List<string>();
            }
            catch { }
            try
            {
                string? denyGlobsJson = NullableString(reader["auto_land_path_deny_globs_json"]);
                if (!String.IsNullOrWhiteSpace(denyGlobsJson))
                    vessel.AutoLandPathDenyGlobs = JsonSerializer.Deserialize<List<string>>(denyGlobsJson) ?? new List<string>();
            }
            catch { }
            vessel.DefaultBranch = reader["default_branch"].ToString()!;
            vessel.Active = Convert.ToInt64(reader["active"]) == 1;
            vessel.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            vessel.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            return vessel;
        }

        internal static Captain CaptainFromReader(MySqlDataReader reader)
        {
            Captain captain = new Captain();
            captain.Id = reader["id"].ToString()!;
            captain.TenantId = NullableString(reader["tenant_id"]);
            captain.UserId = NullableString(reader["user_id"]);
            captain.Name = reader["name"].ToString()!;
            captain.Runtime = Enum.Parse<AgentRuntimeEnum>(reader["runtime"].ToString()!);
            try { captain.Model = NullableString(reader["model"]); } catch { }
            try { captain.ModelEndpointId = NullableString(reader["model_endpoint_id"]); } catch { }
            captain.SystemInstructions = NullableString(reader["system_instructions"]);
            try { captain.RuntimeOptionsJson = NullableString(reader["runtime_options_json"]); } catch { }
            try
            {
                string? cliPolicy = NullableString(reader["cli_permission_policy"]);
                captain.CliPermissionPolicy = !String.IsNullOrEmpty(cliPolicy) && Enum.TryParse<CliPermissionPolicyEnum>(cliPolicy, out CliPermissionPolicyEnum parsedPolicy) ? parsedPolicy : (CliPermissionPolicyEnum?)null;
            }
            catch { captain.CliPermissionPolicy = null; }
            captain.State = Enum.Parse<CaptainStateEnum>(reader["state"].ToString()!);
            captain.CurrentMissionId = NullableString(reader["current_mission_id"]);
            captain.CurrentDockId = NullableString(reader["current_dock_id"]);
            captain.ProcessId = NullableInt(reader["process_id"]);
            captain.RecoveryAttempts = Convert.ToInt32(reader["recovery_attempts"]);
            captain.LastHeartbeatUtc = FromIso8601Nullable(reader["last_heartbeat_utc"]);
            try { captain.LastProcessAliveUtc = FromIso8601Nullable(reader["last_process_alive_utc"]); } catch { }
            try { captain.QuarantineUntilUtc = FromIso8601Nullable(reader["quarantine_until_utc"]); } catch { }
            try { captain.QuarantineReason = NullableString(reader["quarantine_reason"]); } catch { }
            captain.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            captain.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            return captain;
        }

        internal static Signal SignalFromReader(MySqlDataReader reader)
        {
            Signal signal = new Signal();
            signal.Id = reader["id"].ToString()!;
            signal.TenantId = NullableString(reader["tenant_id"]);
            signal.UserId = NullableString(reader["user_id"]);
            signal.FromCaptainId = NullableString(reader["from_captain_id"]);
            signal.ToCaptainId = NullableString(reader["to_captain_id"]);
            signal.Type = Enum.Parse<SignalTypeEnum>(reader["type"].ToString()!);
            signal.Payload = NullableString(reader["payload"]);
            signal.Read = Convert.ToBoolean(reader["read"]);
            signal.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            return signal;
        }

        internal static ArmadaEvent EventFromReader(MySqlDataReader reader)
        {
            ArmadaEvent evt = new ArmadaEvent();
            evt.Id = reader["id"].ToString()!;
            evt.TenantId = NullableString(reader["tenant_id"]);
            evt.UserId = NullableString(reader["user_id"]);
            evt.EventType = reader["event_type"].ToString()!;
            evt.EntityType = NullableString(reader["entity_type"]);
            evt.EntityId = NullableString(reader["entity_id"]);
            evt.CaptainId = NullableString(reader["captain_id"]);
            evt.MissionId = NullableString(reader["mission_id"]);
            evt.VesselId = NullableString(reader["vessel_id"]);
            evt.VoyageId = NullableString(reader["voyage_id"]);
            evt.Message = reader["message"].ToString()!;
            evt.Payload = NullableString(reader["payload"]);
            evt.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            return evt;
        }

        internal static MergeEntry MergeEntryFromReader(MySqlDataReader reader)
        {
            MergeEntry entry = new MergeEntry();
            entry.Id = reader["id"].ToString()!;
            entry.TenantId = NullableString(reader["tenant_id"]);
            entry.UserId = NullableString(reader["user_id"]);
            entry.MissionId = NullableString(reader["mission_id"]);
            entry.VesselId = NullableString(reader["vessel_id"]);
            entry.BranchName = reader["branch_name"].ToString()!;
            entry.TargetBranch = reader["target_branch"].ToString()!;
            entry.Status = Enum.Parse<MergeStatusEnum>(reader["status"].ToString()!);
            entry.Priority = Convert.ToInt32(reader["priority"]);
            entry.BatchId = NullableString(reader["batch_id"]);
            entry.TestCommand = NullableString(reader["test_command"]);
            entry.TestOutput = NullableString(reader["test_output"]);
            entry.TestExitCode = NullableInt(reader["test_exit_code"]);
            try { entry.RetryCount = Convert.ToInt32(reader["retry_count"]); } catch { }
            try { entry.LeaseExpiresUtc = FromIso8601Nullable(reader["lease_expires_utc"]); } catch { }
            entry.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            entry.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            entry.TestStartedUtc = FromIso8601Nullable(reader["test_started_utc"]);
            entry.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            return entry;
        }

        private async Task ExecuteMigrationAsync(MySqlConnection conn, MySqlTransaction tx, SchemaMigration migration, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(migration.AlreadyAppliedCheckSql))
            {
                using (MySqlCommand check = conn.CreateCommand())
                {
                    check.Transaction = tx;
                    check.CommandText = migration.AlreadyAppliedCheckSql;
                    object? found = await check.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (found != null && found != DBNull.Value && Convert.ToInt64(found) != 0) return;
                }
            }

            foreach (string sql in migration.Statements)
            {
                await ExecuteMigrationStatementAsync(conn, tx, sql, token).ConfigureAwait(false);
            }
        }

        private async Task ExecuteMigrationStatementAsync(
            MySqlConnection conn,
            MySqlTransaction tx,
            string sql,
            CancellationToken token)
        {
            using (MySqlCommand cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;

                try
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (MySqlException ex) when (IsIgnorableReplayError(ex))
                {
                    _Logging.Debug(_Header + "ignoring duplicate schema artifact while replaying migration: " + ex.Message);
                }
            }
        }

        private static bool IsIgnorableReplayError(MySqlException ex)
        {
            return ex.Number == 1060   // Duplicate column name
                || ex.Number == 1061   // Duplicate key name
                || ex.Number == 1826;  // Duplicate foreign key constraint name
        }

        #endregion
    }
}


