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

    /// <summary>
    /// Wraps a database driver with cleanup of the temp file on dispose.
    /// </summary>
    public class TestDatabase : IDisposable
    {
        /// <summary>
        /// The database driver.
        /// </summary>
        public SqliteDatabaseDriver Driver { get; }

        /// <summary>
        /// The connection string used for this test database.
        /// </summary>
        public string ConnectionString { get; }

        private string _TempFile;

        internal TestDatabase(SqliteDatabaseDriver driver, string tempFile, string connectionString)
        {
            Driver = driver;
            _TempFile = tempFile;
            ConnectionString = connectionString;
        }

        public void Dispose()
        {
            Driver.Dispose();
            try
            {
                if (File.Exists(_TempFile))
                    File.Delete(_TempFile);
            }
            catch { }
        }
    }
}
