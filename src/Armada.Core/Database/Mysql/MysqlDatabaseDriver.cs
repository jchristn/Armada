namespace Armada.Core.Database.Mysql
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using SyslogLogging;
    using Armada.Core.Database.Mysql.Queries;
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
                )
            };
        }

        #endregion
    }
}
