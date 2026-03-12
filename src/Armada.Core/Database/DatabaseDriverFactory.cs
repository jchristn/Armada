namespace Armada.Core.Database
{
    using System;
    using SyslogLogging;
    using Armada.Core.Database.Mysql;
    using Armada.Core.Database.Sqlite;
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
                    throw new NotSupportedException("PostgreSQL driver is not yet implemented.");

                case DatabaseTypeEnum.SqlServer:
                    throw new NotSupportedException("SQL Server driver is not yet implemented.");

                default:
                    throw new ArgumentException("Unknown database type: " + settings.Type.ToString());
            }
        }

        #endregion
    }
}
