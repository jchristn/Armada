namespace Armada.Test.Unit.Suites.Database
{
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
        }
    }
}
