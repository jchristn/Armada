namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Microsoft.Data.SqlClient;
    using MySqlConnector;
    using Npgsql;

    /// <summary>
    /// Creates and drops uniquely-named databases on a configured server (PostgreSQL, MySQL, or SQL Server)
    /// so each test case gets a fully isolated database that is torn down afterward. Only used for the
    /// non-SQLite providers; SQLite isolation is handled by <see cref="TestDatabaseHelper"/> with temp files.
    /// </summary>
    public static class TestDatabaseProvisioner
    {
        #region Public-Members

        /// <summary>
        /// Command timeout, in seconds, for the harness's own CREATE/DROP/inspection statements. Default 120; override
        /// with ARMADA_TEST_DB_COMMAND_TIMEOUT. Clamped to 30-600.
        /// </summary>
        public static int CommandTimeoutSeconds
        {
            get
            {
                string? value = Environment.GetEnvironmentVariable("ARMADA_TEST_DB_COMMAND_TIMEOUT");
                int seconds = Int32.TryParse(value, out int parsed) ? parsed : 120;
                return Math.Min(600, Math.Max(30, seconds));
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an empty database named <paramref name="databaseName"/> on the configured server, connecting
        /// through the provider's maintenance database.
        /// </summary>
        /// <param name="databaseName">Database name to create (a generated safe identifier).</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="NotSupportedException">Thrown when the configured provider is not a server provider.</exception>
        public static async Task CreateDatabaseAsync(string databaseName, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(databaseName)) throw new ArgumentNullException(nameof(databaseName));

            string maintenance = MaintenanceConnectionString();
            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    await ExecutePostgresAsync(maintenance, "CREATE DATABASE \"" + databaseName + "\";", token).ConfigureAwait(false);
                    break;
                case DatabaseTypeEnum.Mysql:
                    await ExecuteMysqlAsync(maintenance, "CREATE DATABASE `" + databaseName + "`;", token).ConfigureAwait(false);
                    break;
                case DatabaseTypeEnum.SqlServer:
                    await ExecuteSqlServerAsync(maintenance, "CREATE DATABASE [" + databaseName + "];", token).ConfigureAwait(false);
                    break;
                default:
                    throw new NotSupportedException("CreateDatabaseAsync is only supported for server providers, not " + TestDatabaseConfig.Type + ".");
            }
        }

        /// <summary>
        /// Drop the database named <paramref name="databaseName"/>, forcibly closing any lingering pooled
        /// connections first so the drop is not blocked. Best effort: no-op for SQLite.
        /// </summary>
        /// <param name="databaseName">Database name to drop.</param>
        /// <param name="token">Cancellation token.</param>
        public static async Task DropDatabaseAsync(string databaseName, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(databaseName)) throw new ArgumentNullException(nameof(databaseName));

            string maintenance = MaintenanceConnectionString();
            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    NpgsqlConnection.ClearAllPools();
                    await ExecutePostgresAsync(maintenance, "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '" + databaseName + "' AND pid <> pg_backend_pid();", token).ConfigureAwait(false);
                    await ExecutePostgresAsync(maintenance, "DROP DATABASE IF EXISTS \"" + databaseName + "\";", token).ConfigureAwait(false);
                    break;
                case DatabaseTypeEnum.Mysql:
                    MySqlConnection.ClearAllPools();
                    await ExecuteMysqlAsync(maintenance, "DROP DATABASE IF EXISTS `" + databaseName + "`;", token).ConfigureAwait(false);
                    break;
                case DatabaseTypeEnum.SqlServer:
                    SqlConnection.ClearAllPools();
                    await ExecuteSqlServerAsync(maintenance, "IF DB_ID('" + databaseName + "') IS NOT NULL BEGIN ALTER DATABASE [" + databaseName + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + databaseName + "]; END", token).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Enumerate the base-table names in the database reached by <paramref name="connectionString"/>,
        /// excluding the <c>schema_migrations</c> bookkeeping table. Used to build the per-case reset for the
        /// shared server database.
        /// </summary>
        /// <param name="connectionString">Connection string pointing at the target (non-maintenance) database.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The base-table names, excluding <c>schema_migrations</c>.</returns>
        public static async Task<List<string>> GetTableNamesAsync(string connectionString, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    return await QueryPostgresStringsAsync(connectionString, "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename <> 'schema_migrations';", token).ConfigureAwait(false);
                case DatabaseTypeEnum.Mysql:
                    return await QueryMysqlStringsAsync(connectionString, "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' AND table_name <> 'schema_migrations';", token).ConfigureAwait(false);
                case DatabaseTypeEnum.SqlServer:
                    return await QuerySqlServerStringsAsync(connectionString, "SELECT t.name FROM sys.tables t WHERE t.name <> 'schema_migrations';", token).ConfigureAwait(false);
                default:
                    throw new NotSupportedException("GetTableNamesAsync is only supported for server providers, not " + TestDatabaseConfig.Type + ".");
            }
        }

        /// <summary>
        /// Empty every table in <paramref name="tables"/> in the database reached by
        /// <paramref name="connectionString"/>, temporarily suspending foreign-key enforcement so the order
        /// does not matter. <c>schema_migrations</c> is intentionally not in the list, so the schema stays
        /// migrated; the caller re-seeds default data afterward. This is the per-case reset that lets a single
        /// migrated server database be reused across every test case.
        /// </summary>
        /// <param name="connectionString">Connection string pointing at the target (non-maintenance) database.</param>
        /// <param name="tables">Base-table names to empty (from <see cref="GetTableNamesAsync"/>).</param>
        /// <param name="token">Cancellation token.</param>
        public static async Task ResetDatabaseAsync(string connectionString, IReadOnlyList<string> tables, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (tables.Count == 0) return;

            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Postgresql:
                {
                    StringBuilder list = new StringBuilder();
                    for (int i = 0; i < tables.Count; i++)
                    {
                        if (i > 0) list.Append(", ");
                        list.Append('"').Append(tables[i]).Append('"');
                    }
                    await ExecutePostgresAsync(connectionString, "TRUNCATE " + list + " RESTART IDENTITY CASCADE;", token).ConfigureAwait(false);
                    break;
                }
                case DatabaseTypeEnum.Mysql:
                {
                    // DELETE (DML) rather than TRUNCATE: MySQL TRUNCATE is a DDL operation that recreates each
                    // table's file, which is an order of magnitude slower per statement -- prohibitive when
                    // reset runs before every case. With foreign-key checks suspended the delete order is free.
                    StringBuilder sql = new StringBuilder();
                    sql.Append("SET FOREIGN_KEY_CHECKS = 0; ");
                    foreach (string table in tables) sql.Append("DELETE FROM `").Append(table).Append("`; ");
                    sql.Append("SET FOREIGN_KEY_CHECKS = 1;");
                    await ExecuteMysqlAsync(connectionString, sql.ToString(), token).ConfigureAwait(false);
                    break;
                }
                case DatabaseTypeEnum.SqlServer:
                {
                    // TRUNCATE is blocked by inbound foreign keys even when the referencing table is empty, so
                    // disable all constraints, DELETE every listed table, then re-enable and re-check.
                    StringBuilder sql = new StringBuilder();
                    sql.Append("EXEC sp_MSforeachtable @command1 = 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'; ");
                    foreach (string table in tables) sql.Append("DELETE FROM [").Append(table).Append("]; ");
                    sql.Append("EXEC sp_MSforeachtable @command1 = 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';");
                    await ExecuteSqlServerAsync(connectionString, sql.ToString(), token).ConfigureAwait(false);
                    break;
                }
                default:
                    throw new NotSupportedException("ResetDatabaseAsync is only supported for server providers, not " + TestDatabaseConfig.Type + ".");
            }
        }

        #endregion

        #region Private-Methods

        private static string MaintenanceConnectionString()
        {
            string connectionString = TestDatabaseConfig.BuildSettings(TestDatabaseConfig.MaintenanceDatabaseName()).GetConnectionString();
            if (TestDatabaseConfig.Type == DatabaseTypeEnum.SqlServer)
            {
                // CREATE DATABASE copies the model database and DROP DATABASE ... WITH ROLLBACK IMMEDIATE waits for
                // sessions to close; both can exceed the client's 15 s connect / 30 s command defaults on a slow or
                // emulated server even though nothing is wrong. These are test-harness statements, not product queries.
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
                builder.ConnectTimeout = Math.Max(builder.ConnectTimeout, 60);
                connectionString = builder.ConnectionString;
            }
            return connectionString;
        }

        private static async Task ExecutePostgresAsync(string connectionString, string sql, CancellationToken token)
        {
            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandTimeout = CommandTimeoutSeconds;
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static async Task ExecuteMysqlAsync(string connectionString, string sql, CancellationToken token)
        {
            using (MySqlConnection connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandTimeout = CommandTimeoutSeconds;
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static async Task ExecuteSqlServerAsync(string connectionString, string sql, CancellationToken token)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandTimeout = CommandTimeoutSeconds;
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static async Task<List<string>> QueryPostgresStringsAsync(string connectionString, string sql, CancellationToken token)
        {
            List<string> results = new List<string>();
            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandTimeout = CommandTimeoutSeconds;
                    using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(reader.GetString(0));
                    }
                }
            }
            return results;
        }

        private static async Task<List<string>> QueryMysqlStringsAsync(string connectionString, string sql, CancellationToken token)
        {
            List<string> results = new List<string>();
            using (MySqlConnection connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandTimeout = CommandTimeoutSeconds;
                    using (MySqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(reader.GetString(0));
                    }
                }
            }
            return results;
        }

        private static async Task<List<string>> QuerySqlServerStringsAsync(string connectionString, string sql, CancellationToken token)
        {
            List<string> results = new List<string>();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandTimeout = CommandTimeoutSeconds;
                    using (SqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(reader.GetString(0));
                    }
                }
            }
            return results;
        }

        #endregion
    }
}
