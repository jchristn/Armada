namespace Armada.Core.Database.Postgresql
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using SyslogLogging;
    using Armada.Core.Database.Postgresql.Implementations;
    using Armada.Core.Database.Postgresql.Queries;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// PostgreSQL implementation of the Armada database driver.
    /// </summary>
    public class PostgresqlDatabaseDriver : DatabaseDriver
    {
        #region Public-Members

        /// <summary>
        /// Connection string for the PostgreSQL database.
        /// </summary>
        internal string ConnectionString
        {
            get { return _ConnectionString; }
        }

        #endregion

        #region Private-Members

        private string _Header = "[PostgresqlDatabaseDriver] ";
        private DatabaseSettings _Settings;
        private string _ConnectionString;
        private LoggingModule _Logging;
        private NpgsqlDataSource _DataSource;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the PostgreSQL database driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public PostgresqlDatabaseDriver(DatabaseSettings settings, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _ConnectionString = settings.GetConnectionString();
            _DataSource = NpgsqlDataSource.Create(_ConnectionString);

            InitializeImplementations();
        }

        /// <summary>
        /// Instantiate the PostgreSQL database driver with a raw connection string.
        /// </summary>
        /// <param name="connectionString">PostgreSQL connection string.</param>
        /// <param name="logging">Logging module.</param>
        public PostgresqlDatabaseDriver(string connectionString, LoggingModule logging)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = new DatabaseSettings();
            _DataSource = NpgsqlDataSource.Create(_ConnectionString);

            InitializeImplementations();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the database, running any pending schema migrations.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            _Logging.Info(_Header + "initializing database");

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                // Create migration tracking table
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS schema_migrations (
                        version INTEGER PRIMARY KEY,
                        description TEXT NOT NULL,
                        applied_utc TIMESTAMP NOT NULL
                    );";
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Get current schema version
                int currentVersion = 0;
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
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

                    using (NpgsqlTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                    {
                        foreach (string sql in migration.Statements)
                        {
                            using (NpgsqlCommand cmd = new NpgsqlCommand())
                            {
                                cmd.Connection = conn;
                                cmd.Transaction = tx;
                                cmd.CommandText = sql;
                                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                            }
                        }

                        // Record migration
                        using (NpgsqlCommand cmd = new NpgsqlCommand())
                        {
                            cmd.Connection = conn;
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
        /// Dispose of the database driver.
        /// </summary>
        public override void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _DataSource?.Dispose();
        }

        /// <summary>
        /// Create a new NpgsqlConnection using the connection string.
        /// </summary>
        /// <returns>An unopened NpgsqlConnection.</returns>
        internal NpgsqlConnection CreateConnection()
        {
            return new NpgsqlConnection(_ConnectionString);
        }

        #endregion

        #region Private-Methods

        private void InitializeImplementations()
        {
            Fleets = new FleetMethods(this, _Settings, _Logging);
            Vessels = new VesselMethods(this, _Settings, _Logging);
            Captains = new CaptainMethods(this, _Settings, _Logging);
            Missions = new MissionMethods(this, _Settings, _Logging);
            Voyages = new VoyageMethods(this, _Settings, _Logging);
            Docks = new DockMethods(this, _Settings, _Logging);
            Signals = new SignalMethods(_DataSource);
            Events = new EventMethods(_DataSource);
            MergeEntries = new MergeEntryMethods(_DataSource);
            Tenants = new TenantMethods(this, _Settings, _Logging);
            Users = new UserMethods(this, _Settings, _Logging);
            Credentials = new CredentialMethods(this, _Settings, _Logging);
            PromptTemplates = new PromptTemplateMethods(this, _Settings, _Logging);
            Playbooks = new PlaybookMethods(this, _Settings, _Logging);
            Personas = new PersonaMethods(this, _Settings, _Logging);
            Pipelines = new PipelineMethods(this, _Settings, _Logging);
        }

        #endregion
    }
}

