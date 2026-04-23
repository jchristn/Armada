namespace Armada.Core.Database.Mysql
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
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

        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

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
            Docks = new DockMethods(_ConnectionString);
            Signals = new SignalMethods(_ConnectionString);
            Events = new EventMethods(_ConnectionString);
            MergeEntries = new MergeEntryMethods(_ConnectionString);
            Tenants = new TenantMethods(_ConnectionString);
            Users = new UserMethods(_ConnectionString);
            Credentials = new CredentialMethods(_ConnectionString);
            PromptTemplates = new PromptTemplateMethods(_ConnectionString);
            Playbooks = new PlaybookMethods(_ConnectionString);
            Personas = new PersonaMethods(_ConnectionString);
            Pipelines = new PipelineMethods(_ConnectionString);
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
                            using (MySqlCommand cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = sql;
                                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                            }
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
        public async Task<int> GetSchemaVersionAsync(CancellationToken token = default)
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
                TableQueries.MergeEntries
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
                )
            };
        }

        internal static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        internal static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }

        internal static DateTime? FromIso8601Nullable(object value)
        {
            if (value == null || value == DBNull.Value) return null;
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
            tenant.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            tenant.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
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
            user.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            user.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
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
            cred.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            cred.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
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
            fleet.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            fleet.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
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
            try { vessel.AllowConcurrentMissions = Convert.ToInt64(reader["allow_concurrent_missions"]) == 1; }
            catch { vessel.AllowConcurrentMissions = false; }
            vessel.DefaultBranch = reader["default_branch"].ToString()!;
            vessel.Active = Convert.ToInt64(reader["active"]) == 1;
            vessel.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            vessel.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
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
            signal.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
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
            evt.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
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
            entry.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            entry.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            entry.TestStartedUtc = FromIso8601Nullable(reader["test_started_utc"]);
            entry.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            return entry;
        }

        #endregion
    }
}


