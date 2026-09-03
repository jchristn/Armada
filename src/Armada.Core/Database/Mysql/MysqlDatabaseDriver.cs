namespace Armada.Core.Database.Mysql
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Mysql.Implementations;
    using Armada.Core.Database.Mysql.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

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
        private static readonly Regex _AddColumnIfNotExistsRegex = new Regex(@"\bADD\s+COLUMN\s+IF\s+NOT\s+EXISTS\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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

            Fleets = new FleetMethods(_ConnectionString);
            Vessels = new VesselMethods(_ConnectionString);
            Captains = new CaptainMethods(_ConnectionString);
            Missions = new MissionMethods(_ConnectionString);
            Voyages = new VoyageMethods(_ConnectionString);
            PlanningSessions = new PlanningSessionMethods(_ConnectionString);
            PlanningSessionMessages = new PlanningSessionMessageMethods(_ConnectionString);
            Objectives = new ObjectiveMethods(_ConnectionString);
            Jobs = new JobMethods(_ConnectionString);
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
            _Logging.Info(_Header + "initializing database");

            using (MySqlConnection conn = await GetConnectionAsync(token).ConfigureAwait(false))
            {
                // Create migration tracking table
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = TableQueries.SchemaMigrations;
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Get current schema version
                int currentVersion = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
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

                    using (MySqlTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                    {
                        foreach (string sql in migration.Statements)
                        {
                            await ExecuteMigrationStatementAsync(conn, tx, sql, token).ConfigureAwait(false);
                        }

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
                    _Logging.Info(_Header + "schema is up to date at v" + currentVersion);
            }

            _Logging.Info(_Header + "database initialized successfully");

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
            _Logging.Info(_Header + "disposed");
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
                )
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
            captain.SystemInstructions = NullableString(reader["system_instructions"]);
            try { captain.RuntimeOptionsJson = NullableString(reader["runtime_options_json"]); } catch { }
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

        private async Task ExecuteMigrationStatementAsync(
            MySqlConnection conn,
            MySqlTransaction tx,
            string sql,
            CancellationToken token)
        {
            using (MySqlCommand cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = NormalizeMigrationStatement(sql);

                try
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (MySqlException ex) when (IsIgnorableReplayError(ex))
                {
                    _Logging.Info(_Header + "ignoring duplicate schema artifact while replaying migration: " + ex.Message);
                }
            }
        }

        private static string NormalizeMigrationStatement(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return sql;
            return _AddColumnIfNotExistsRegex.Replace(sql, "ADD COLUMN ");
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


