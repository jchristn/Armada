namespace Armada.Test.Unit.TestHelpers
{
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Sqlite;
    using SyslogLogging;

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
