namespace Armada.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database.Mysql;
    using Armada.Core.Database.Postgresql;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Database.SqlServer;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// Factory for creating database driver instances based on configuration.
    /// </summary>
    public static class DatabaseDriverFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create a database driver matching the configured database type.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A DatabaseDriver instance for the configured type.</returns>
        public static DatabaseDriver Create(DatabaseSettings settings, LoggingModule logging)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            switch (settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteDatabaseDriver(settings.GetConnectionString(), logging);

                case DatabaseTypeEnum.Mysql:
                    return new MysqlDatabaseDriver(settings, logging);

                case DatabaseTypeEnum.Postgresql:
                    return new PostgresqlDatabaseDriver(settings, logging);

                case DatabaseTypeEnum.SqlServer:
                    return new SqlServerDatabaseDriver(settings, logging);

                default:
                    throw new ArgumentException("Unknown database type: " + settings.Type.ToString());
            }
        }

        /// <summary>
        /// Create a database driver and initialize its schema.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An initialized DatabaseDriver instance.</returns>
        public static async Task<DatabaseDriver> CreateAndInitializeAsync(DatabaseSettings settings, CancellationToken token = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            DatabaseDriver driver = Create(settings, logging);
            await driver.InitializeAsync(token).ConfigureAwait(false);
            return driver;
        }

        #endregion
    }
}
