namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for schema migration behavior: SchemaMigration value-object construction and
    /// validation, InitializeAsync applying versioned migrations (schema-version table, captain
    /// model, mission runtime, backlog objectives), idempotent re-initialization, migration record
    /// timestamps, and versioned migration-script content. Positive cases verify successful
    /// construction and migration application; negative cases cover invalid construction arguments
    /// and the fresh-database zero-version boundary.
    /// </summary>
    public sealed class SchemaMigrationSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Schema Migration suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(CaseAsync("vessel_admission_backfill", "Migration 117 preserves serialized/unlimited vessel intent and sets new defaults", TestTags.Positive, async () =>
            {
                using SqliteConnection conn = new SqliteConnection("Data Source=:memory:");
                await conn.OpenAsync();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE vessels (id TEXT PRIMARY KEY, allow_concurrent_missions INTEGER NOT NULL DEFAULT 0); INSERT INTO vessels VALUES ('serial',0),('parallel',1);";
                await cmd.ExecuteNonQueryAsync();
                foreach (SchemaMigration migration in Armada.Core.Database.Sqlite.Queries.TableQueries.GetMigrations())
                {
                    if (migration.Version != 117) continue;
                    foreach (string sql in migration.Statements)
                    {
                        cmd.CommandText = sql;
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                cmd.CommandText = "SELECT COUNT(*) FROM vessels WHERE (id='serial' AND max_parallel_voyages=1 AND max_parallel_missions_per_voyage=1) OR (id='parallel' AND max_parallel_voyages=0 AND max_parallel_missions_per_voyage=0);";
                AssertEqual(2L, (long)(await cmd.ExecuteScalarAsync())!);
                cmd.CommandText = "INSERT INTO vessels(id) VALUES ('new'); SELECT max_parallel_missions_per_voyage FROM vessels WHERE id='new';";
                AssertEqual(4L, (long)(await cmd.ExecuteScalarAsync())!);
            }));

            cases.Add(CaseAsync("active_migration_preserves_existing_and_new_captains", "Migration 112 backfills existing captains and defaults future inserts to active", TestTags.Positive, async () =>
            {
                using (SqliteConnection conn = new SqliteConnection("Data Source=:memory:"))
                {
                    await conn.OpenAsync();
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "CREATE TABLE captains (id TEXT PRIMARY KEY, state TEXT); INSERT INTO captains VALUES ('old', 'Working');";
                        await cmd.ExecuteNonQueryAsync();
                        foreach (SchemaMigration migration in Armada.Core.Database.Sqlite.Queries.TableQueries.GetMigrations())
                        {
                            if (migration.Version != 112) continue;
                            foreach (string sql in migration.Statements)
                            {
                                cmd.CommandText = sql;
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }
                        cmd.CommandText = "INSERT INTO captains (id, state) VALUES ('new', 'Idle'); SELECT COUNT(*) FROM captains WHERE active = 1;";
                        AssertEqual(2L, (long)(await cmd.ExecuteScalarAsync())!);
                        cmd.CommandText = "SELECT state FROM captains WHERE id = 'old';";
                        AssertEqual("Working", (string)(await cmd.ExecuteScalarAsync())!);
                    }
                }
            }));


            cases.Add(Case("schema_migration_valid_construction", "SchemaMigration valid construction", TestTags.Positive, () =>
            {
                SchemaMigration migration = new SchemaMigration(1, "Initial schema", "CREATE TABLE test (id TEXT);");
                AssertEqual(1, migration.Version);
                AssertEqual("Initial schema", migration.Description);
                AssertEqual(1, migration.Statements.Count);
            }));

            cases.Add(Case("schema_migration_multiple_statements", "SchemaMigration multiple statements", TestTags.Positive, () =>
            {
                SchemaMigration migration = new SchemaMigration(2, "Add indexes",
                    "CREATE INDEX idx1 ON test(id);",
                    "CREATE INDEX idx2 ON test(id);");
                AssertEqual(2, migration.Statements.Count);
            }));

            cases.Add(Case("schema_migration_invalid_version_throws", "SchemaMigration invalid version throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentOutOfRangeException>(() =>
                    new SchemaMigration(0, "Bad version", "CREATE TABLE test (id TEXT);"));
            }));

            cases.Add(Case("schema_migration_null_description_throws", "SchemaMigration null description throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() =>
                    new SchemaMigration(1, null!, "CREATE TABLE test (id TEXT);"));
            }));

            cases.Add(Case("schema_migration_no_statements_throws", "SchemaMigration no statements throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentException>(() =>
                    new SchemaMigration(1, "Empty migration"));
            }));

            cases.Add(CaseAsync("initialize_async_creates_schema_version_table", "InitializeAsync creates schema version table", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
                    {
                        await conn.OpenAsync();
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
                            object? result = await cmd.ExecuteScalarAsync();
                            AssertEqual("schema_migrations", result);
                        }
                    }
                }
            }, sqliteOnly: true));

            cases.Add(CaseAsync("initialize_async_records_migration_version", "InitializeAsync records migration version", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    int version = await testDb.Driver.GetSchemaVersionAsync();
                    AssertTrue(version >= 43, "Schema version should include the backlog/objective migration after initialization");
                }
            }));

            cases.Add(CaseAsync("initialize_async_applies_captain_model_and_mission_runtime_migrations", "InitializeAsync applies captain model and mission runtime migrations", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
                    {
                        await conn.OpenAsync();

                        using (SqliteCommand versionCmd = conn.CreateCommand())
                        {
                            versionCmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version IN (26, 27);";
                            long appliedCount = (long)(await versionCmd.ExecuteScalarAsync() ?? 0L);
                            AssertEqual(2L, appliedCount);
                        }

                        AssertTrue(await ColumnExistsAsync(conn, "captains", "model").ConfigureAwait(false), "captains.model should exist");
                        AssertTrue(await ColumnExistsAsync(conn, "missions", "total_runtime_ms").ConfigureAwait(false), "missions.total_runtime_ms should exist");
                    }
                }
            }, sqliteOnly: true));

            cases.Add(CaseAsync("versioned_migration_scripts_include_captain_model_and_mission_runtime_statements", "Versioned migration scripts include captain model and mission runtime statements", TestTags.Positive, async () =>
            {
                string repoRoot = FindRepositoryRoot();
                string shellScript = await File.ReadAllTextAsync(Path.Combine(repoRoot, "migrations", "migrate_v0.4.0_to_v0.5.0.sh")).ConfigureAwait(false);
                string batchScript = await File.ReadAllTextAsync(Path.Combine(repoRoot, "migrations", "migrate_v0.4.0_to_v0.5.0.bat")).ConfigureAwait(false);

                AssertContains("ALTER TABLE captains ADD COLUMN model TEXT NULL;", shellScript, "Shell script sqlite/postgresql/mysql model migration");
                AssertContains("ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;", shellScript, "Shell script mission runtime migration");
                AssertContains("ALTER TABLE captains ADD model NVARCHAR(MAX) NULL;", shellScript, "Shell script sqlserver model migration");
                AssertContains("ALTER TABLE missions ADD total_runtime_ms BIGINT NULL;", shellScript, "Shell script sqlserver mission runtime migration");
                AssertContains("VALUES (26, 'Add model to captains'", shellScript, "Shell script migration version 26");
                AssertContains("VALUES (27, 'Add total_runtime_ms to missions'", shellScript, "Shell script migration version 27");

                AssertContains("echo ALTER TABLE captains ADD COLUMN model TEXT NULL;", batchScript, "Batch script sqlite/postgresql/mysql model migration");
                AssertContains("echo ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;", batchScript, "Batch script mission runtime migration");
                AssertContains("echo ALTER TABLE captains ADD model NVARCHAR^(MAX^) NULL;", batchScript, "Batch script sqlserver model migration");
                AssertContains("echo ALTER TABLE missions ADD total_runtime_ms BIGINT NULL;", batchScript, "Batch script sqlserver mission runtime migration");
                AssertContains("VALUES ^(26, 'Add model to captains'", batchScript, "Batch script migration version 26");
                AssertContains("VALUES ^(27, 'Add total_runtime_ms to missions'", batchScript, "Batch script migration version 27");
            }));

            cases.Add(CaseAsync("versioned_migration_scripts_include_v080_backlog_release_handoff", "Versioned migration scripts include v080 backlog release handoff", TestTags.Positive, async () =>
            {
                string repoRoot = FindRepositoryRoot();
                string shellScript = await File.ReadAllTextAsync(Path.Combine(repoRoot, "migrations", "migrate_v0.7.0_to_v0.8.0.sh")).ConfigureAwait(false);
                string batchScript = await File.ReadAllTextAsync(Path.Combine(repoRoot, "migrations", "migrate_v0.7.0_to_v0.8.0.bat")).ConfigureAwait(false);
                string sqliteSql = await File.ReadAllTextAsync(Path.Combine(repoRoot, "migrations", "migrate_v0.7.0_to_v0.8.0_sqlite.sql")).ConfigureAwait(false);
                string postgresqlSql = await File.ReadAllTextAsync(Path.Combine(repoRoot, "migrations", "migrate_v0.7.0_to_v0.8.0_postgresql.sql")).ConfigureAwait(false);
                string mysqlSql = await File.ReadAllTextAsync(Path.Combine(repoRoot, "migrations", "migrate_v0.7.0_to_v0.8.0_mysql.sql")).ConfigureAwait(false);
                string sqlServerSql = await File.ReadAllTextAsync(Path.Combine(repoRoot, "migrations", "migrate_v0.7.0_to_v0.8.0_sqlserver.sql")).ConfigureAwait(false);

                AssertContains("Back up the database", shellScript, "Shell script should require a backup precheck");
                AssertContains("automatically on first startup after upgrade", shellScript, "Shell script should explain automatic startup migration");
                AssertContains("controlled DBA-managed pre-stage", shellScript, "Shell script should explain the manual-use case");
                AssertContains("Back up the database", batchScript, "Batch script should require a backup precheck");
                AssertContains("automatically on first startup after upgrade", batchScript, "Batch script should explain automatic startup migration");
                AssertContains("controlled DBA-managed pre-stage", batchScript, "Batch script should explain the manual-use case");

                AssertContains("CREATE TABLE IF NOT EXISTS objectives", sqliteSql, "SQLite SQL should create objectives");
                AssertContains("CREATE TABLE IF NOT EXISTS objective_refinement_sessions", sqliteSql, "SQLite SQL should create refinement sessions");
                AssertContains("CREATE TABLE IF NOT EXISTS objective_refinement_messages", sqliteSql, "SQLite SQL should create refinement messages");
                AssertContains("VALUES (43, 'Add normalized objectives backlog tables'", sqliteSql, "SQLite SQL should record schema migration 43");

                AssertContains("CREATE TABLE IF NOT EXISTS objectives", postgresqlSql, "PostgreSQL SQL should create objectives");
                AssertContains("CREATE TABLE IF NOT EXISTS objective_refinement_sessions", postgresqlSql, "PostgreSQL SQL should create refinement sessions");
                AssertContains("CREATE TABLE IF NOT EXISTS objective_refinement_messages", postgresqlSql, "PostgreSQL SQL should create refinement messages");
                AssertContains("VALUES (42, 'Add normalized objectives backlog tables'", postgresqlSql, "PostgreSQL SQL should record schema migration 42");

                AssertContains("CREATE TABLE IF NOT EXISTS objectives", mysqlSql, "MySQL SQL should create objectives");
                AssertContains("CREATE TABLE IF NOT EXISTS objective_refinement_sessions", mysqlSql, "MySQL SQL should create refinement sessions");
                AssertContains("CREATE TABLE IF NOT EXISTS objective_refinement_messages", mysqlSql, "MySQL SQL should create refinement messages");
                AssertContains("VALUES (42, 'Add normalized objectives backlog tables'", mysqlSql, "MySQL SQL should record schema migration 42");

                AssertContains("CREATE TABLE objectives", sqlServerSql, "SQL Server SQL should create objectives");
                AssertContains("CREATE TABLE objective_refinement_sessions", sqlServerSql, "SQL Server SQL should create refinement sessions");
                AssertContains("CREATE TABLE objective_refinement_messages", sqlServerSql, "SQL Server SQL should create refinement messages");
                AssertContains("VALUES (42, 'Add normalized objectives backlog tables'", sqlServerSql, "SQL Server SQL should record schema migration 42");
            }));

            cases.Add(CaseAsync("initialize_async_applies_backlog_objective_migrations", "InitializeAsync applies backlog objective migrations", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
                    {
                        await conn.OpenAsync();

                        using (SqliteCommand versionCmd = conn.CreateCommand())
                        {
                            versionCmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 43;";
                            long appliedCount = (long)(await versionCmd.ExecuteScalarAsync() ?? 0L);
                            AssertEqual(1L, appliedCount);
                        }

                        AssertTrue(await TableExistsAsync(conn, "objectives").ConfigureAwait(false), "objectives table should exist");
                        AssertTrue(await TableExistsAsync(conn, "objective_refinement_sessions").ConfigureAwait(false), "objective_refinement_sessions table should exist");
                        AssertTrue(await TableExistsAsync(conn, "objective_refinement_messages").ConfigureAwait(false), "objective_refinement_messages table should exist");
                        AssertTrue(await ColumnExistsAsync(conn, "objectives", "backlog_state").ConfigureAwait(false), "objectives.backlog_state should exist");
                        AssertTrue(await ColumnExistsAsync(conn, "objectives", "deployment_ids_json").ConfigureAwait(false), "objectives.deployment_ids_json should exist");
                        AssertTrue(await ColumnExistsAsync(conn, "objectives", "incident_ids_json").ConfigureAwait(false), "objectives.incident_ids_json should exist");
                    }
                }
            }, sqliteOnly: true));

            cases.Add(CaseAsync("initialize_async_idempotent_run_twice", "InitializeAsync idempotent run twice", TestTags.Positive, async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_" + Guid.NewGuid().ToString("N") + ".db");
                string connectionString = $"Data Source={tempFile}";

                try
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    DatabaseDriver driver1 = new SqliteDatabaseDriver(connectionString, logging);
                    await driver1.InitializeAsync();
                    int v1 = await driver1.GetSchemaVersionAsync();
                    driver1.Dispose();

                    DatabaseDriver driver2 = new SqliteDatabaseDriver(connectionString, logging);
                    await driver2.InitializeAsync();
                    int v2 = await driver2.GetSchemaVersionAsync();
                    driver2.Dispose();

                    AssertEqual(v1, v2);
                    AssertTrue(v1 >= 1);
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }));

            cases.Add(CaseAsync("initialize_async_migration_records_have_timestamps", "InitializeAsync migration records have timestamps", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
                    {
                        await conn.OpenAsync();
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT version, description, applied_utc FROM schema_migrations ORDER BY version;";
                            using (SqliteDataReader reader = await cmd.ExecuteReaderAsync())
                            {
                                AssertTrue(await reader.ReadAsync(), "Should have at least one migration record");
                                AssertEqual(1, reader.GetInt32(0));
                                AssertFalse(string.IsNullOrEmpty(reader.GetString(1)));
                                AssertFalse(string.IsNullOrEmpty(reader.GetString(2)));
                            }
                        }
                    }
                }
            }, sqliteOnly: true));

            cases.Add(CaseAsync("get_schema_version_async_fresh_database_returns_zero", "GetSchemaVersionAsync fresh database returns zero", TestTags.Negative, async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_" + Guid.NewGuid().ToString("N") + ".db");
                string connectionString = $"Data Source={tempFile}";

                try
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    DatabaseDriver driver = new SqliteDatabaseDriver(connectionString, logging);
                    int version = await driver.GetSchemaVersionAsync();
                    AssertEqual(0, version);
                    driver.Dispose();
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Database.SchemaMigration",
                displayName: "Schema Migration",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<bool> TableExistsAsync(SqliteConnection conn, string tableName)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name;";
                cmd.Parameters.AddWithValue("@name", tableName);
                object? result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return String.Equals(result?.ToString(), tableName, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static async Task<bool> ColumnExistsAsync(SqliteConnection conn, string tableName, string columnName)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(" + tableName + ");";
                using (SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        if (String.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "migrations")) &&
                    Directory.Exists(Path.Combine(current.FullName, "src")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Database.SchemaMigration",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body, bool sqliteOnly = false)
        {
            // Cases that introspect the schema through a raw SqliteConnection (sqlite_master, PRAGMA
            // table_info) cannot run against a server provider's connection string; the provider-agnostic
            // migration coverage lives in the GetSchemaVersionAsync-based cases, which run everywhere.
            bool skip = sqliteOnly && !TestDatabaseConfig.IsSqlite;
            string? skipReason = skip ? "Case uses SQLite-specific schema introspection; not applicable to server providers." : null;

            return new TestCaseDescriptor(
                suiteId: "Database.SchemaMigration",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag },
                skip: skip,
                skipReason: skipReason);
        }

        #endregion
    }
}
