namespace Armada.Test.Common
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// Parses command-line arguments for database connection settings.
    /// Supports the same syntax across all test projects.
    /// </summary>
    public class CommandLineOptions
    {
        #region Public-Members

        /// <summary>
        /// Database type (sqlite, mysql, postgresql, postgres, sqlserver, mssql).
        /// Null means no --type was specified (use default behavior).
        /// </summary>
        public DatabaseTypeEnum? Type { get; set; } = null;

        /// <summary>
        /// SQLite file path.
        /// </summary>
        public string? Filename { get; set; } = null;

        /// <summary>
        /// Database server hostname.
        /// </summary>
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// Database server port.
        /// </summary>
        public int? Port { get; set; } = null;

        /// <summary>
        /// Database username.
        /// </summary>
        public string? Username { get; set; } = null;

        /// <summary>
        /// Database password.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// Database name.
        /// </summary>
        public string? DatabaseName { get; set; } = null;

        /// <summary>
        /// Database schema.
        /// </summary>
        public string? Schema { get; set; } = null;

        /// <summary>
        /// Skip cleanup of test data after execution.
        /// </summary>
        public bool NoCleanup { get; set; } = false;

        /// <summary>
        /// Show help/usage information.
        /// </summary>
        public bool Help { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse command-line arguments into a CommandLineOptions instance.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Parsed options.</returns>
        public static CommandLineOptions Parse(string[] args)
        {
            CommandLineOptions options = new CommandLineOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                switch (arg)
                {
                    case "--type":
                    case "-t":
                        if (i + 1 >= args.Length) throw new ArgumentException("--type requires a value");
                        options.Type = ParseDatabaseType(args[++i]);
                        break;

                    case "--filename":
                    case "-f":
                        if (i + 1 >= args.Length) throw new ArgumentException("--filename requires a value");
                        options.Filename = args[++i];
                        break;

                    case "--hostname":
                    case "-h":
                        if (i + 1 >= args.Length) throw new ArgumentException("--hostname requires a value");
                        options.Hostname = args[++i];
                        break;

                    case "--port":
                    case "-p":
                        if (i + 1 >= args.Length) throw new ArgumentException("--port requires a value");
                        if (!Int32.TryParse(args[++i], out int port))
                            throw new ArgumentException("--port must be a number");
                        options.Port = port;
                        break;

                    case "--username":
                    case "-u":
                        if (i + 1 >= args.Length) throw new ArgumentException("--username requires a value");
                        options.Username = args[++i];
                        break;

                    case "--password":
                    case "-w":
                        if (i + 1 >= args.Length) throw new ArgumentException("--password requires a value");
                        options.Password = args[++i];
                        break;

                    case "--database":
                    case "-d":
                        if (i + 1 >= args.Length) throw new ArgumentException("--database requires a value");
                        options.DatabaseName = args[++i];
                        break;

                    case "--schema":
                    case "-s":
                        if (i + 1 >= args.Length) throw new ArgumentException("--schema requires a value");
                        options.Schema = args[++i];
                        break;

                    case "--no-cleanup":
                        options.NoCleanup = true;
                        break;

                    case "--help":
                    case "-?":
                        options.Help = true;
                        break;

                    default:
                        throw new ArgumentException("Unknown argument: " + arg);
                }
            }

            return options;
        }

        /// <summary>
        /// Build a DatabaseSettings instance from the parsed options.
        /// For SQLite, uses the provided filename or the supplied default path.
        /// For server-based databases, validates that required fields are present.
        /// </summary>
        /// <param name="defaultSqlitePath">Default SQLite file path when no --filename is specified.</param>
        /// <returns>Configured DatabaseSettings.</returns>
        public DatabaseSettings BuildDatabaseSettings(string defaultSqlitePath)
        {
            DatabaseSettings dbSettings = new DatabaseSettings();

            if (Type == null || Type == DatabaseTypeEnum.Sqlite)
            {
                dbSettings.Type = DatabaseTypeEnum.Sqlite;
                dbSettings.Filename = Filename ?? defaultSqlitePath;
            }
            else
            {
                dbSettings.Type = Type.Value;

                if (String.IsNullOrEmpty(Hostname))
                    throw new ArgumentException("--hostname is required for " + Type.Value + " databases");
                if (String.IsNullOrEmpty(Username))
                    throw new ArgumentException("--username is required for " + Type.Value + " databases");
                if (String.IsNullOrEmpty(Password))
                    throw new ArgumentException("--password is required for " + Type.Value + " databases");
                if (String.IsNullOrEmpty(DatabaseName))
                    throw new ArgumentException("--database is required for " + Type.Value + " databases");

                dbSettings.Hostname = Hostname;
                dbSettings.Username = Username;
                dbSettings.Password = Password;
                dbSettings.DatabaseName = DatabaseName;

                if (Port.HasValue)
                    dbSettings.Port = Port.Value;

                if (!String.IsNullOrEmpty(Schema))
                    dbSettings.Schema = Schema;
            }

            return dbSettings;
        }

        /// <summary>
        /// Whether the database is a temp SQLite file (no --type specified, or --type sqlite without --filename).
        /// Used to determine cleanup behavior.
        /// </summary>
        public bool IsTempSqlite
        {
            get
            {
                return (Type == null || Type == DatabaseTypeEnum.Sqlite) && Filename == null;
            }
        }

        /// <summary>
        /// Print usage information to the console.
        /// </summary>
        /// <param name="programName">Name of the program for usage header.</param>
        public static void PrintUsage(string programName)
        {
            Console.WriteLine("Usage: " + programName + " [options]");
            Console.WriteLine();
            Console.WriteLine("Database Options:");
            Console.WriteLine("  --type, -t <type>        Database type: sqlite, mysql, postgresql, postgres, sqlserver, mssql");
            Console.WriteLine("  --filename, -f <path>    SQLite database file path");
            Console.WriteLine("  --hostname, -h <host>    Database server hostname");
            Console.WriteLine("  --port, -p <port>        Database server port (default: auto-detect by type)");
            Console.WriteLine("  --username, -u <user>    Database username");
            Console.WriteLine("  --password, -w <pass>    Database password");
            Console.WriteLine("  --database, -d <name>    Database name");
            Console.WriteLine("  --schema, -s <schema>    Database schema (default: public)");
            Console.WriteLine();
            Console.WriteLine("Other Options:");
            Console.WriteLine("  --no-cleanup             Preserve test data after execution");
            Console.WriteLine("  --help, -?               Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  # SQLite with temp file (default)");
            Console.WriteLine("  " + programName);
            Console.WriteLine();
            Console.WriteLine("  # SQLite with explicit file");
            Console.WriteLine("  " + programName + " -t sqlite -f /path/to/armada.db");
            Console.WriteLine();
            Console.WriteLine("  # PostgreSQL");
            Console.WriteLine("  " + programName + " -t postgresql -h localhost -u postgres -w secret -d armada_test");
            Console.WriteLine();
            Console.WriteLine("  # SQL Server");
            Console.WriteLine("  " + programName + " -t sqlserver -h localhost -p 1433 -u sa -w secret -d armada_test");
            Console.WriteLine();
            Console.WriteLine("  # MySQL");
            Console.WriteLine("  " + programName + " -t mysql -h localhost -u root -w secret -d armada_test");
        }

        #endregion

        #region Private-Methods

        private static DatabaseTypeEnum ParseDatabaseType(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "sqlite":
                    return DatabaseTypeEnum.Sqlite;
                case "postgresql":
                case "postgres":
                    return DatabaseTypeEnum.Postgresql;
                case "sqlserver":
                case "mssql":
                    return DatabaseTypeEnum.SqlServer;
                case "mysql":
                    return DatabaseTypeEnum.Mysql;
                default:
                    throw new ArgumentException("Unknown database type: " + value + ". Supported: sqlite, mysql, postgresql, postgres, sqlserver, mssql");
            }
        }

        #endregion
    }
}
