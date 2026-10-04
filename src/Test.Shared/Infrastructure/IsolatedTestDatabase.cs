namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;

    /// <summary>
    /// A brand-new, empty, uninitialized database on the configured test provider (a temp SQLite file, or a
    /// uniquely named database on the PostgreSQL / MySQL / SQL Server test server). Unlike
    /// <see cref="TestDatabaseHelper"/>, which hands out pre-migrated databases, this is for tests that must
    /// control migration themselves (idempotence, upgrade, backup-before-migrate). Disposing drops the database
    /// (or deletes the file and its -wal/-shm siblings).
    /// </summary>
    public sealed class IsolatedTestDatabase : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Database settings that point at the isolated database.
        /// </summary>
        public DatabaseSettings Settings { get; }

        /// <summary>
        /// Provider type.
        /// </summary>
        public DatabaseTypeEnum Type
        {
            get { return Settings.Type; }
        }

        /// <summary>
        /// Connection string for the isolated database.
        /// </summary>
        public string ConnectionString
        {
            get { return Settings.GetConnectionString(); }
        }

        /// <summary>
        /// SQLite file path, or null for server providers.
        /// </summary>
        public string? SqlitePath { get; }

        /// <summary>
        /// Server-provider database name, or null for SQLite.
        /// </summary>
        public string? DatabaseName { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private IsolatedTestDatabase(DatabaseSettings settings, string? sqlitePath, string? databaseName)
        {
            Settings = settings;
            SqlitePath = sqlitePath;
            DatabaseName = databaseName;
        }

        /// <summary>
        /// Create an empty database on the configured provider.
        /// </summary>
        /// <param name="label">Short label used in the file or database name (letters, digits, underscore).</param>
        /// <param name="sqliteDirectory">Directory for the SQLite file; a fresh temp directory when null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The isolated database.</returns>
        public static async Task<IsolatedTestDatabase> CreateAsync(string label, string? sqliteDirectory = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(label)) throw new ArgumentNullException(nameof(label));

            if (TestDatabaseConfig.IsSqlite)
            {
                string directory = sqliteDirectory ?? TestTemp.NewDirectory(label);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, label + ".db");
                DatabaseSettings sqlite = new DatabaseSettings();
                sqlite.Type = DatabaseTypeEnum.Sqlite;
                sqlite.Filename = path;
                return new IsolatedTestDatabase(sqlite, path, null);
            }

            string name = (TestDatabaseConfig.BaseDatabaseName + "_" + label + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)).ToLowerInvariant();
            if (name.Length > 60) name = name.Substring(0, 60);
            await TestDatabaseProvisioner.DropDatabaseAsync(name, token).ConfigureAwait(false);
            await TestDatabaseProvisioner.CreateDatabaseAsync(name, token).ConfigureAwait(false);
            return new IsolatedTestDatabase(TestDatabaseConfig.BuildSettings(name), null, name);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a query and return the first column of every row as a string.
        /// </summary>
        /// <param name="sql">SQL text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Values, in row order.</returns>
        public async Task<List<string>> QueryStringsAsync(string sql, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));
            List<string> results = new List<string>();
            switch (Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    using (NpgsqlConnection pg = new NpgsqlConnection(ConnectionString))
                    {
                        await pg.OpenAsync(token).ConfigureAwait(false);
                        using (NpgsqlCommand cmd = pg.CreateCommand())
                        {
                            cmd.CommandText = sql;
                            using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                            {
                                while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Convert.ToString(reader.GetValue(0)) ?? "");
                            }
                        }
                    }
                    break;
                case DatabaseTypeEnum.Mysql:
                    using (MySqlConnection my = new MySqlConnection(ConnectionString))
                    {
                        await my.OpenAsync(token).ConfigureAwait(false);
                        using (MySqlCommand cmd = my.CreateCommand())
                        {
                            cmd.CommandText = sql;
                            using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                            {
                                while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Convert.ToString(reader.GetValue(0)) ?? "");
                            }
                        }
                    }
                    break;
                case DatabaseTypeEnum.SqlServer:
                    using (SqlConnection ss = new SqlConnection(ConnectionString))
                    {
                        await ss.OpenAsync(token).ConfigureAwait(false);
                        using (SqlCommand cmd = ss.CreateCommand())
                        {
                            cmd.CommandText = sql;
                            using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                            {
                                while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Convert.ToString(reader.GetValue(0)) ?? "");
                            }
                        }
                    }
                    break;
                default:
                    using (SqliteConnection lite = new SqliteConnection("Data Source=" + SqlitePath + ";Pooling=False"))
                    {
                        await lite.OpenAsync(token).ConfigureAwait(false);
                        using (SqliteCommand cmd = lite.CreateCommand())
                        {
                            cmd.CommandText = sql;
                            using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                            {
                                while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Convert.ToString(reader.GetValue(0)) ?? "");
                            }
                        }
                    }
                    break;
            }

            return results;
        }

        /// <summary>
        /// Capture the schema as a sorted list of "table.column:type" and "index:table.name" strings, excluding
        /// provider system objects, so two snapshots can be compared for equality.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sorted schema description.</returns>
        public async Task<List<string>> SnapshotSchemaAsync(CancellationToken token = default)
        {
            List<string> rows = new List<string>();
            switch (Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    rows.AddRange(await QueryStringsAsync("SELECT table_name || '.' || column_name || ':' || data_type FROM information_schema.columns WHERE table_schema = 'public';", token).ConfigureAwait(false));
                    rows.AddRange(await QueryStringsAsync("SELECT 'index:' || tablename || '.' || indexname FROM pg_indexes WHERE schemaname = 'public';", token).ConfigureAwait(false));
                    break;
                case DatabaseTypeEnum.Mysql:
                    rows.AddRange(await QueryStringsAsync("SELECT CONCAT(table_name, '.', column_name, ':', column_type) FROM information_schema.columns WHERE table_schema = DATABASE();", token).ConfigureAwait(false));
                    rows.AddRange(await QueryStringsAsync("SELECT DISTINCT CONCAT('index:', table_name, '.', index_name) FROM information_schema.statistics WHERE table_schema = DATABASE();", token).ConfigureAwait(false));
                    break;
                case DatabaseTypeEnum.SqlServer:
                    rows.AddRange(await QueryStringsAsync("SELECT t.name + '.' + c.name + ':' + ty.name FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id JOIN sys.types ty ON c.user_type_id = ty.user_type_id;", token).ConfigureAwait(false));
                    rows.AddRange(await QueryStringsAsync("SELECT 'index:' + t.name + '.' + i.name FROM sys.indexes i JOIN sys.tables t ON i.object_id = t.object_id WHERE i.name IS NOT NULL;", token).ConfigureAwait(false));
                    break;
                default:
                    rows.AddRange(await QueryStringsAsync("SELECT m.name || '.' || p.name || ':' || p.type FROM sqlite_master m JOIN pragma_table_info(m.name) p WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%';", token).ConfigureAwait(false));
                    rows.AddRange(await QueryStringsAsync("SELECT 'index:' || tbl_name || '.' || name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%';", token).ConfigureAwait(false));
                    rows.AddRange(await QueryStringsAsync("SELECT 'fk:' || m.name || '.' || f.\"from\" || '->' || f.\"table\" FROM sqlite_master m JOIN pragma_foreign_key_list(m.name) f WHERE m.type = 'table';", token).ConfigureAwait(false));
                    break;
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        /// <summary>
        /// Count rows in a table.
        /// </summary>
        /// <param name="table">Table name (trusted test input).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row count.</returns>
        public async Task<long> CountAsync(string table, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(table)) throw new ArgumentNullException(nameof(table));
            List<string> values = await QueryStringsAsync("SELECT COUNT(*) FROM " + table + ";", token).ConfigureAwait(false);
            return values.Count == 0 ? 0 : Int64.Parse(values[0]);
        }

        /// <summary>
        /// Drop the database (server providers) or delete the file and its -wal/-shm siblings (SQLite).
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            try
            {
                if (SqlitePath != null)
                {
                    SqliteConnection.ClearAllPools();
                    TestTemp.TryDelete(SqlitePath);
                    TestTemp.TryDelete(SqlitePath + "-wal");
                    TestTemp.TryDelete(SqlitePath + "-shm");
                }
                else if (DatabaseName != null)
                {
                    TestDatabaseProvisioner.DropDatabaseAsync(DatabaseName).GetAwaiter().GetResult();
                }
            }
            catch
            {
                // Best effort: a failed drop must not fail the test.
            }
        }

        #endregion
    }
}
