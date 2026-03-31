namespace Armada.Core.Settings
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Database connection and pooling settings.
    /// </summary>
    public class DatabaseSettings
    {
        #region Public-Members

        /// <summary>
        /// Database type.
        /// </summary>
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Sqlite;

        /// <summary>
        /// Database filename, for use with Sqlite.
        /// </summary>
        public string Filename
        {
            get => _Filename;
            set { if (!String.IsNullOrEmpty(value)) _Filename = value; }
        }

        /// <summary>
        /// Database server hostname.
        /// </summary>
        public string Hostname
        {
            get => _Hostname;
            set { if (!String.IsNullOrEmpty(value)) _Hostname = value; }
        }

        /// <summary>
        /// Database server port.
        /// Port 0 enables auto-detection based on database type:
        /// PostgreSQL = 5432, SQL Server = 1433, MySQL = 3306.
        /// </summary>
        public int Port
        {
            get => _Port;
            set
            {
                if (value < 0 || value > 65535) throw new ArgumentOutOfRangeException(nameof(Port));
                _Port = value;
            }
        }

        /// <summary>
        /// Database username.
        /// </summary>
        public string Username
        {
            get => _Username;
            set { if (!String.IsNullOrEmpty(value)) _Username = value; }
        }

        /// <summary>
        /// Database password.
        /// </summary>
        public string Password
        {
            get => _Password;
            set { if (!String.IsNullOrEmpty(value)) _Password = value; }
        }

        /// <summary>
        /// Database name.
        /// </summary>
        public string DatabaseName
        {
            get => _DatabaseName;
            set { if (!String.IsNullOrEmpty(value)) _DatabaseName = value; }
        }

        /// <summary>
        /// Database schema.
        /// </summary>
        public string Schema
        {
            get => _Schema;
            set { if (!String.IsNullOrEmpty(value)) _Schema = value; }
        }

        /// <summary>
        /// Require encryption for the database connection.
        /// </summary>
        public bool RequireEncryption { get; set; } = false;

        /// <summary>
        /// Enable or disable query logging.
        /// </summary>
        public bool LogQueries { get; set; } = false;

        /// <summary>
        /// Minimum connection pool size. Clamped to 0-100.
        /// </summary>
        public int MinPoolSize
        {
            get => _MinPoolSize;
            set
            {
                if (value < 0) value = 0;
                if (value > 100) value = 100;
                _MinPoolSize = value;
            }
        }

        /// <summary>
        /// Maximum connection pool size. Clamped to 1-200 and must be >= MinPoolSize.
        /// </summary>
        public int MaxPoolSize
        {
            get => _MaxPoolSize;
            set
            {
                if (value < 1) value = 1;
                if (value > 200) value = 200;
                if (value < _MinPoolSize) value = _MinPoolSize;
                _MaxPoolSize = value;
            }
        }

        /// <summary>
        /// Connection lifetime in seconds before recycling. Minimum 30.
        /// </summary>
        public int ConnectionLifetimeSeconds
        {
            get => _ConnectionLifetimeSeconds;
            set
            {
                if (value < 30) value = 30;
                _ConnectionLifetimeSeconds = value;
            }
        }

        /// <summary>
        /// Connection idle timeout in seconds before removal from pool. Minimum 10.
        /// </summary>
        public int ConnectionIdleTimeoutSeconds
        {
            get => _ConnectionIdleTimeoutSeconds;
            set
            {
                if (value < 10) value = 10;
                _ConnectionIdleTimeoutSeconds = value;
            }
        }

        #endregion

        #region Private-Members

        private string _Filename = "armada.db";
        private string _Hostname = "localhost";
        private int _Port = 0;
        private string _Username = "";
        private string _Password = "";
        private string _DatabaseName = "armada";
        private string _Schema = "public";
        private int _MinPoolSize = 1;
        private int _MaxPoolSize = 25;
        private int _ConnectionLifetimeSeconds = 300;
        private int _ConnectionIdleTimeoutSeconds = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public DatabaseSettings()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the connection string based on the configured database type.
        /// Includes connection pooling parameters where applicable.
        /// </summary>
        /// <returns>Connection string.</returns>
        public string GetConnectionString()
        {
            switch (Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return "Data Source=" + _Filename;

                case DatabaseTypeEnum.Postgresql:
                    int pgPort = _Port > 0 ? _Port : 5432;
                    return
                        "Host=" + _Hostname + ";" +
                        "Port=" + pgPort + ";" +
                        "Database=" + _DatabaseName + ";" +
                        "Username=" + _Username + ";" +
                        "Password=" + _Password + ";" +
                        "Search Path=" + _Schema + ";" +
                        "SSL Mode=" + (RequireEncryption ? "Require" : "Prefer") + ";" +
                        "Minimum Pool Size=" + _MinPoolSize + ";" +
                        "Maximum Pool Size=" + _MaxPoolSize + ";" +
                        "Connection Idle Lifetime=" + _ConnectionIdleTimeoutSeconds + ";" +
                        "Connection Lifetime=" + _ConnectionLifetimeSeconds;

                case DatabaseTypeEnum.SqlServer:
                    int sqlPort = _Port > 0 ? _Port : 1433;
                    return
                        "Server=" + _Hostname + "," + sqlPort + ";" +
                        "Database=" + _DatabaseName + ";" +
                        "User Id=" + _Username + ";" +
                        "Password=" + _Password + ";" +
                        "Encrypt=" + RequireEncryption.ToString() + ";" +
                        "Min Pool Size=" + _MinPoolSize + ";" +
                        "Max Pool Size=" + _MaxPoolSize + ";" +
                        "Connection Lifetime=" + _ConnectionLifetimeSeconds;

                case DatabaseTypeEnum.Mysql:
                    int myPort = _Port > 0 ? _Port : 3306;
                    return
                        "Server=" + _Hostname + ";" +
                        "Port=" + myPort + ";" +
                        "Database=" + _DatabaseName + ";" +
                        "Uid=" + _Username + ";" +
                        "Pwd=" + _Password + ";" +
                        "SslMode=" + (RequireEncryption ? "Required" : "Preferred") + ";" +
                        "Minimum Pool Size=" + _MinPoolSize + ";" +
                        "Maximum Pool Size=" + _MaxPoolSize + ";" +
                        "Connection Lifetime=" + _ConnectionLifetimeSeconds;

                default:
                    throw new ArgumentException("Unknown database type: " + Type.ToString());
            }
        }

        #endregion
    }
}
