namespace Armada.Test.Unit.TestHelpers
{
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Sqlite;
    using SyslogLogging;

    /// <summary>
    /// Helper for creating disposable SQLite databases for testing.
    /// Uses temp files since in-memory databases close when the last connection drops,
    /// which conflicts with the driver's per-operation connection pattern.
    /// </summary>
    public static class TestDatabaseHelper
    {
        /// <summary>
        /// Create an initialized temp-file SQLite database driver for testing.
        /// The returned wrapper disposes the driver and deletes the temp file.
        /// Access the driver via the Driver property.
        /// </summary>
        public static async Task<TestDatabase> CreateDatabaseAsync()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_" + Guid.NewGuid().ToString("N") + ".db");
            string connectionString = $"Data Source={tempFile}";

            SqliteDatabaseDriver driver = new SqliteDatabaseDriver(connectionString, logging);
            await driver.InitializeAsync().ConfigureAwait(false);
            return new TestDatabase(driver, tempFile, connectionString);
        }
    }
}
