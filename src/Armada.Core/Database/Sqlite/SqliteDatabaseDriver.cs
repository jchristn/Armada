namespace Armada.Core.Database.Sqlite
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Sqlite.Implementations;
    using Armada.Core.Database.Sqlite.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// SQLite implementation of the Armada database driver.
    /// </summary>
    public class SqliteDatabaseDriver : DatabaseDriver
    {
        #region Public-Members

        /// <summary>
        /// Connection string for the SQLite database.
        /// </summary>
        internal string ConnectionString
        {
            get { return _ConnectionString; }
        }

        #endregion

        #region Private-Members

        private string _Header = "[SqliteDatabaseDriver] ";
        private DatabaseSettings _Settings;
        private string _ConnectionString;
        private LoggingModule _Logging;
        private SemaphoreSlim _Semaphore = new SemaphoreSlim(1, 1);
        private bool _Disposed = false;

        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the SQLite database driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public SqliteDatabaseDriver(DatabaseSettings settings, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _ConnectionString = settings.GetConnectionString();

            Fleets = new FleetMethods(this, _Settings, _Logging);
            Vessels = new VesselMethods(this, _Settings, _Logging);
            Captains = new CaptainMethods(this, _Settings, _Logging);
            Missions = new MissionMethods(this, _Settings, _Logging);
            Voyages = new VoyageMethods(this, _Settings, _Logging);
            Docks = new DockMethods(this, _Settings, _Logging);
            Signals = new SignalMethods(this, _Settings, _Logging);
            Events = new EventMethods(this, _Settings, _Logging);
            MergeEntries = new MergeEntryMethods(this, _Settings, _Logging);
            Tenants = new TenantMethods(this, _Settings, _Logging);
            Users = new UserMethods(this, _Settings, _Logging);
            Credentials = new CredentialMethods(this, _Settings, _Logging);
            PromptTemplates = new PromptTemplateMethods(this, _Settings, _Logging);
            Playbooks = new PlaybookMethods(this, _Settings, _Logging);
            Personas = new PersonaMethods(this, _Settings, _Logging);
            Pipelines = new PipelineMethods(this, _Settings, _Logging);
        }

        /// <summary>
        /// Instantiate the SQLite database driver with a raw connection string.
        /// </summary>
        /// <param name="connectionString">SQLite connection string.</param>
        /// <param name="logging">Logging module.</param>
        public SqliteDatabaseDriver(string connectionString, LoggingModule logging)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = new DatabaseSettings();

            Fleets = new FleetMethods(this, _Settings, _Logging);
            Vessels = new VesselMethods(this, _Settings, _Logging);
            Captains = new CaptainMethods(this, _Settings, _Logging);
            Missions = new MissionMethods(this, _Settings, _Logging);
            Voyages = new VoyageMethods(this, _Settings, _Logging);
            Docks = new DockMethods(this, _Settings, _Logging);
            Signals = new SignalMethods(this, _Settings, _Logging);
            Events = new EventMethods(this, _Settings, _Logging);
            MergeEntries = new MergeEntryMethods(this, _Settings, _Logging);
            Tenants = new TenantMethods(this, _Settings, _Logging);
            Users = new UserMethods(this, _Settings, _Logging);
            Credentials = new CredentialMethods(this, _Settings, _Logging);
            PromptTemplates = new PromptTemplateMethods(this, _Settings, _Logging);
            Playbooks = new PlaybookMethods(this, _Settings, _Logging);
            Personas = new PersonaMethods(this, _Settings, _Logging);
            Pipelines = new PipelineMethods(this, _Settings, _Logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the database, running any pending schema migrations.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            _Logging.Info(_Header + "initializing database");

            using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                using (SqliteCommand walCmd = conn.CreateCommand())
                {
                    walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                    await walCmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                using (SqliteCommand fkCmd = conn.CreateCommand())
                {
                    fkCmd.CommandText = "PRAGMA foreign_keys=ON;";
                    await fkCmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Create migration tracking table
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS schema_migrations (
                        version INTEGER PRIMARY KEY,
                        description TEXT NOT NULL,
                        applied_utc TEXT NOT NULL
                    );";
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Get current schema version
                int currentVersion = 0;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result != null && result != DBNull.Value) currentVersion = Convert.ToInt32(result);
                }

                // Apply pending migrations
                List<SchemaMigration> migrations = TableQueries.GetMigrations();
                int applied = 0;

                foreach (SchemaMigration migration in migrations)
                {
                    if (migration.Version <= currentVersion) continue;

                    _Logging.Info(_Header + "applying migration v" + migration.Version + ": " + migration.Description);

                    using (SqliteTransaction tx = conn.BeginTransaction())
                    {
                        foreach (string sql in migration.Statements)
                        {
                            using (SqliteCommand cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = sql;
                                try
                                {
                                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                                }
                                catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name"))
                                {
                                    // Column already exists in the CREATE TABLE definition.
                                    // This happens when migrations add columns that were later
                                    // incorporated into the initial schema. Safe to skip.
                                    _Logging.Info(_Header + "migration v" + migration.Version + ": column already exists, skipping");
                                }
                            }
                        }

                        // Record migration
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "INSERT INTO schema_migrations (version, description, applied_utc) VALUES (@v, @d, @t);";
                            cmd.Parameters.AddWithValue("@v", migration.Version);
                            cmd.Parameters.AddWithValue("@d", migration.Description);
                            cmd.Parameters.AddWithValue("@t", ToIso8601(DateTime.UtcNow));
                            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }

                        tx.Commit();
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
        /// Get the current schema version.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Current schema version number, or 0 if no migrations have been applied.</returns>
        public async Task<int> GetSchemaVersionAsync(CancellationToken token = default)
        {
            using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                // Check if schema_migrations table exists
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result == null || result == DBNull.Value) return 0;
                }

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result != null && result != DBNull.Value) return Convert.ToInt32(result);
                    return 0;
                }
            }
        }

        /// <summary>
        /// Dispose of resources.
        /// </summary>
        public override void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Semaphore.Dispose();
            _Logging.Info(_Header + "disposed");
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Convert a DateTime to ISO 8601 format string.
        /// </summary>
        /// <param name="dt">DateTime value.</param>
        /// <returns>ISO 8601 formatted string.</returns>
        internal static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse an ISO 8601 string to DateTime.
        /// </summary>
        /// <param name="value">ISO 8601 string.</param>
        /// <returns>DateTime value.</returns>
        internal static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }

        /// <summary>
        /// Parse an ISO 8601 string to nullable DateTime.
        /// </summary>
        /// <param name="value">Object value to parse.</param>
        /// <returns>Nullable DateTime value.</returns>
        internal static DateTime? FromIso8601Nullable(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            if (string.IsNullOrEmpty(str)) return null;
            return FromIso8601(str);
        }

        /// <summary>
        /// Convert an object value to a nullable string, handling DBNull.
        /// </summary>
        /// <param name="value">Object value.</param>
        /// <returns>String value or null.</returns>
        internal static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        /// <summary>
        /// Read a nullable boolean from a SqliteDataReader column.
        /// </summary>
        /// <param name="reader">Data reader.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Nullable boolean value.</returns>
        internal static bool? NullableBool(SqliteDataReader reader, string column)
        {
            try
            {
                object value = reader[column];
                if (value == null || value == DBNull.Value) return null;
                return Convert.ToInt64(value) == 1;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Convert an object value to a nullable int, handling DBNull.
        /// </summary>
        /// <param name="value">Object value.</param>
        /// <returns>Nullable int value.</returns>
        internal static int? NullableInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value);
        }

        /// <summary>
        /// Convert an object value to a nullable long, handling DBNull.
        /// </summary>
        /// <param name="value">Object value.</param>
        /// <returns>Nullable long value.</returns>
        internal static long? NullableLong(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt64(value);
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a Fleet model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Fleet instance.</returns>
        internal static Fleet FleetFromReader(SqliteDataReader reader)
        {
            Fleet fleet = new Fleet();
            fleet.Id = reader["id"].ToString()!;
            fleet.TenantId = NullableString(reader["tenant_id"]);
            fleet.UserId = NullableString(reader["user_id"]);
            fleet.Name = reader["name"].ToString()!;
            fleet.Description = NullableString(reader["description"]);
            try { fleet.DefaultPipelineId = NullableString(reader["default_pipeline_id"]); } catch { }
            fleet.Active = Convert.ToInt64(reader["active"]) == 1;
            fleet.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            fleet.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return fleet;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a Vessel model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Vessel instance.</returns>
        internal static Vessel VesselFromReader(SqliteDataReader reader)
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
            try { vessel.DefaultPipelineId = NullableString(reader["default_pipeline_id"]); } catch { }
            vessel.DefaultBranch = reader["default_branch"].ToString()!;
            vessel.Active = Convert.ToInt64(reader["active"]) == 1;
            vessel.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            vessel.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return vessel;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a Captain model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Captain instance.</returns>
        internal static Captain CaptainFromReader(SqliteDataReader reader)
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
            try { captain.AllowedPersonas = NullableString(reader["allowed_personas"]); } catch { }
            try { captain.PreferredPersona = NullableString(reader["preferred_persona"]); } catch { }
            return captain;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a Mission model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Mission instance.</returns>
        internal static Mission MissionFromReader(SqliteDataReader reader)
        {
            Mission mission = new Mission();
            mission.Id = reader["id"].ToString()!;
            mission.TenantId = NullableString(reader["tenant_id"]);
            mission.UserId = NullableString(reader["user_id"]);
            mission.VoyageId = NullableString(reader["voyage_id"]);
            mission.VesselId = NullableString(reader["vessel_id"]);
            mission.CaptainId = NullableString(reader["captain_id"]);
            mission.Title = reader["title"].ToString()!;
            mission.Description = NullableString(reader["description"]);
            mission.Status = Enum.Parse<MissionStatusEnum>(reader["status"].ToString()!);
            mission.Priority = Convert.ToInt32(reader["priority"]);
            mission.ParentMissionId = NullableString(reader["parent_mission_id"]);
            mission.BranchName = NullableString(reader["branch_name"]);
            mission.DockId = NullableString(reader["dock_id"]);
            mission.ProcessId = NullableInt(reader["process_id"]);
            mission.PrUrl = NullableString(reader["pr_url"]);
            mission.CommitHash = NullableString(reader["commit_hash"]);
            mission.DiffSnapshot = NullableString(reader["diff_snapshot"]);
            try { mission.AgentOutput = NullableString(reader["agent_output"]); } catch { }
            mission.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            mission.StartedUtc = FromIso8601Nullable(reader["started_utc"]);
            mission.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            try { mission.TotalRuntimeMs = NullableLong(reader["total_runtime_ms"]); } catch { }
            mission.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            try { mission.Persona = NullableString(reader["persona"]); } catch { }
            try { mission.DependsOnMissionId = NullableString(reader["depends_on_mission_id"]); } catch { }
            try { mission.FailureReason = NullableString(reader["failure_reason"]); } catch { }
            return mission;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a Voyage model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Voyage instance.</returns>
        internal static Voyage VoyageFromReader(SqliteDataReader reader)
        {
            Voyage voyage = new Voyage();
            voyage.Id = reader["id"].ToString()!;
            voyage.TenantId = NullableString(reader["tenant_id"]);
            voyage.UserId = NullableString(reader["user_id"]);
            voyage.Title = reader["title"].ToString()!;
            voyage.Description = NullableString(reader["description"]);
            voyage.Status = Enum.Parse<VoyageStatusEnum>(reader["status"].ToString()!);
            voyage.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            voyage.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            voyage.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            voyage.AutoPush = NullableBool(reader, "auto_push");
            voyage.AutoCreatePullRequests = NullableBool(reader, "auto_create_pull_requests");
            voyage.AutoMergePullRequests = NullableBool(reader, "auto_merge_pull_requests");
            string? voyageLandingModeStr = NullableString(reader["landing_mode"]);
            if (!String.IsNullOrEmpty(voyageLandingModeStr) && Enum.TryParse<LandingModeEnum>(voyageLandingModeStr, out LandingModeEnum vlm))
                voyage.LandingMode = vlm;
            return voyage;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a Dock model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Dock instance.</returns>
        internal static Dock DockFromReader(SqliteDataReader reader)
        {
            Dock dock = new Dock();
            dock.Id = reader["id"].ToString()!;
            dock.TenantId = NullableString(reader["tenant_id"]);
            dock.UserId = NullableString(reader["user_id"]);
            dock.VesselId = reader["vessel_id"].ToString()!;
            dock.CaptainId = NullableString(reader["captain_id"]);
            dock.WorktreePath = NullableString(reader["worktree_path"]);
            dock.BranchName = NullableString(reader["branch_name"]);
            dock.Active = Convert.ToInt64(reader["active"]) == 1;
            dock.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            dock.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return dock;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a Signal model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Signal instance.</returns>
        internal static Signal SignalFromReader(SqliteDataReader reader)
        {
            Signal signal = new Signal();
            signal.Id = reader["id"].ToString()!;
            signal.TenantId = NullableString(reader["tenant_id"]);
            signal.UserId = NullableString(reader["user_id"]);
            signal.FromCaptainId = NullableString(reader["from_captain_id"]);
            signal.ToCaptainId = NullableString(reader["to_captain_id"]);
            signal.Type = Enum.Parse<SignalTypeEnum>(reader["type"].ToString()!);
            signal.Payload = NullableString(reader["payload"]);
            signal.Read = Convert.ToInt64(reader["read"]) == 1;
            signal.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            return signal;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to an ArmadaEvent model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>ArmadaEvent instance.</returns>
        internal static ArmadaEvent EventFromReader(SqliteDataReader reader)
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

        /// <summary>
        /// Convert a SqliteDataReader row to a MergeEntry model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>MergeEntry instance.</returns>
        internal static MergeEntry MergeEntryFromReader(SqliteDataReader reader)
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

        internal static TenantMetadata TenantFromReader(SqliteDataReader reader)
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

        internal static UserMaster UserFromReader(SqliteDataReader reader)
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

        internal static Credential CredentialFromReader(SqliteDataReader reader)
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

        #endregion
    }
}


