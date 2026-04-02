namespace Armada.Test.Unit.Suites.Database
{
    using System.Reflection;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;

    public class SchemaMigrationTests : TestSuite
    {
        public override string Name => "Schema Migration";

        protected override async Task RunTestsAsync()
        {
            await RunTest("SchemaMigration valid construction", () =>
            {
                SchemaMigration migration = new SchemaMigration(1, "Initial schema", "CREATE TABLE test (id TEXT);");
                AssertEqual(1, migration.Version);
                AssertEqual("Initial schema", migration.Description);
                AssertEqual(1, migration.Statements.Count);
            });

            await RunTest("SchemaMigration multiple statements", () =>
            {
                SchemaMigration migration = new SchemaMigration(2, "Add indexes",
                    "CREATE INDEX idx1 ON test(id);",
                    "CREATE INDEX idx2 ON test(id);");
                AssertEqual(2, migration.Statements.Count);
            });

            await RunTest("SchemaMigration invalid version throws", () =>
            {
                AssertThrows<ArgumentOutOfRangeException>(() =>
                    new SchemaMigration(0, "Bad version", "CREATE TABLE test (id TEXT);"));
            });

            await RunTest("SchemaMigration null description throws", () =>
            {
                AssertThrows<ArgumentNullException>(() =>
                    new SchemaMigration(1, null!, "CREATE TABLE test (id TEXT);"));
            });

            await RunTest("SchemaMigration no statements throws", () =>
            {
                AssertThrows<ArgumentException>(() =>
                    new SchemaMigration(1, "Empty migration"));
            });

            await RunTest("Captain model migration exists across backends", () =>
            {
                AssertCaptainModelMigration(
                    Armada.Core.Database.Sqlite.Queries.TableQueries.GetMigrations(),
                    "ALTER TABLE captains ADD COLUMN model TEXT");
                AssertCaptainModelMigration(
                    Armada.Core.Database.Postgresql.Queries.TableQueries.GetMigrations(),
                    "ALTER TABLE captains ADD COLUMN");
                AssertCaptainModelMigration(
                    Armada.Core.Database.SqlServer.Queries.TableQueries.GetMigrations(),
                    "ALTER TABLE captains ADD model NVARCHAR(MAX) NULL");

                MethodInfo getMysqlMigrations = typeof(Armada.Core.Database.Mysql.MysqlDatabaseDriver)
                    .GetMethod("GetMigrations", BindingFlags.NonPublic | BindingFlags.Static)!;
                List<SchemaMigration> mysqlMigrations = (List<SchemaMigration>)getMysqlMigrations.Invoke(null, null)!;
                AssertCaptainModelMigration(mysqlMigrations, "ALTER TABLE captains ADD COLUMN model TEXT NULL");
            });

            await RunTest("InitializeAsync creates schema version table", async () =>
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
            });

            await RunTest("InitializeAsync records migration version", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    int version = await testDb.Driver.GetSchemaVersionAsync();
                    AssertTrue(version >= 1, "Schema version should be at least 1 after initialization");
                }
            });

            await RunTest("InitializeAsync applies captain model migration", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
                    {
                        await conn.OpenAsync();

                        bool hasModelColumn = false;
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "PRAGMA table_info(captains);";
                            using (SqliteDataReader reader = await cmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    if (String.Equals(reader["name"].ToString(), "model", StringComparison.OrdinalIgnoreCase))
                                    {
                                        hasModelColumn = true;
                                        break;
                                    }
                                }
                            }
                        }

                        AssertTrue(hasModelColumn, "captains.model column should exist after initialization");

                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT description FROM schema_migrations WHERE version = 26;";
                            object? result = await cmd.ExecuteScalarAsync();
                            AssertEqual("Add model column to captains table", result);
                        }
                    }
                }
            });

            await RunTest("InitializeAsync idempotent run twice", async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_" + Guid.NewGuid().ToString("N") + ".db");
                string connectionString = $"Data Source={tempFile}";

                try
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    SqliteDatabaseDriver driver1 = new SqliteDatabaseDriver(connectionString, logging);
                    await driver1.InitializeAsync();
                    int v1 = await driver1.GetSchemaVersionAsync();
                    driver1.Dispose();

                    SqliteDatabaseDriver driver2 = new SqliteDatabaseDriver(connectionString, logging);
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
            });

            await RunTest("InitializeAsync migration records have timestamps", async () =>
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
            });

            await RunTest("GetSchemaVersionAsync fresh database returns zero", async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_" + Guid.NewGuid().ToString("N") + ".db");
                string connectionString = $"Data Source={tempFile}";

                try
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    SqliteDatabaseDriver driver = new SqliteDatabaseDriver(connectionString, logging);
                    int version = await driver.GetSchemaVersionAsync();
                    AssertEqual(0, version);
                    driver.Dispose();
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            });

            await RunTest("Captain model migration scripts include alter table statement", async () =>
            {
                string repoRoot = FindRepoRoot();
                string shellScriptPath = Path.Combine(repoRoot, "migrations", "migrate_add_captain_model.sh");
                string batchScriptPath = Path.Combine(repoRoot, "migrations", "migrate_add_captain_model.bat");

                AssertTrue(File.Exists(shellScriptPath), "Shell migration script should exist");
                AssertTrue(File.Exists(batchScriptPath), "Batch migration script should exist");

                string shellScript = await File.ReadAllTextAsync(shellScriptPath);
                string batchScript = await File.ReadAllTextAsync(batchScriptPath);

                AssertContains("ALTER TABLE captains ADD COLUMN model TEXT", shellScript);
                AssertContains("ALTER TABLE captains ADD COLUMN model TEXT", batchScript);
            });
        }

        private void AssertCaptainModelMigration(List<SchemaMigration> migrations, string expectedSql)
        {
            SchemaMigration? migration = migrations.SingleOrDefault(x => x.Version == 26);
            AssertNotNull(migration);
            AssertEqual("Add model column to captains table", migration!.Description);
            AssertEqual(1, migration.Statements.Count);
            AssertContains(expectedSql, migration.Statements[0]);
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "migrations"))
                    && Directory.Exists(Path.Combine(current.FullName, "src"))
                    && Directory.Exists(Path.Combine(current.FullName, "test")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");
        }
    }
}
