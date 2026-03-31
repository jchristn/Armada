namespace Armada.Test.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Command-line options for the database integration test runner.
    /// </summary>
    public class CommandLineOptions
    {
        #region Public-Members

        /// <summary>
        /// Database type: sqlite, mysql, postgresql, postgres, sqlserver, mssql.
        /// </summary>
        public string Type { get; set; } = "sqlite";

        /// <summary>
        /// Database filename, for use with SQLite.
        /// </summary>
        public string Filename { get; set; } = "";

        /// <summary>
        /// Database server hostname.
        /// </summary>
        public string Hostname { get; set; } = "";

        /// <summary>
        /// Database server port. Zero enables auto-detection based on database type.
        /// </summary>
        public int Port { get; set; } = 0;

        /// <summary>
        /// Database username.
        /// </summary>
        public string Username { get; set; } = "";

        /// <summary>
        /// Database password.
        /// </summary>
        public string Password { get; set; } = "";

        /// <summary>
        /// Database name.
        /// </summary>
        public string Database { get; set; } = "";

        /// <summary>
        /// Database schema.
        /// </summary>
        public string Schema { get; set; } = "";

        /// <summary>
        /// When true, do not clean up test data after execution.
        /// </summary>
        public bool NoCleanup { get; set; } = false;

        /// <summary>
        /// When true, display usage information and exit.
        /// </summary>
        public bool Help { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public CommandLineOptions()
        {
        }

        /// <summary>
        /// Parse command-line arguments into a CommandLineOptions instance.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Parsed options.</returns>
        public static CommandLineOptions Parse(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            CommandLineOptions options = new CommandLineOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                switch (arg)
                {
                    case "--type":
                    case "-t":
                        if (i + 1 < args.Length) options.Type = args[++i].ToLowerInvariant();
                        break;

                    case "--filename":
                    case "-f":
                        if (i + 1 < args.Length) options.Filename = args[++i];
                        break;

                    case "--hostname":
                    case "-h":
                        if (i + 1 < args.Length) options.Hostname = args[++i];
                        break;

                    case "--port":
                    case "-p":
                        if (i + 1 < args.Length && Int32.TryParse(args[i + 1], out int port))
                        {
                            options.Port = port;
                            i++;
                        }
                        break;

                    case "--username":
                    case "-u":
                        if (i + 1 < args.Length) options.Username = args[++i];
                        break;

                    case "--password":
                    case "-w":
                        if (i + 1 < args.Length) options.Password = args[++i];
                        break;

                    case "--database":
                    case "-d":
                        if (i + 1 < args.Length) options.Database = args[++i];
                        break;

                    case "--schema":
                    case "-s":
                        if (i + 1 < args.Length) options.Schema = args[++i];
                        break;

                    case "--no-cleanup":
                        options.NoCleanup = true;
                        break;

                    case "--help":
                    case "-?":
                        options.Help = true;
                        break;
                }
            }

            return options;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate the options, returning a list of error messages.
        /// An empty list indicates valid options.
        /// </summary>
        /// <returns>List of validation error messages.</returns>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();

            HashSet<string> validTypes = new HashSet<string>
            {
                "sqlite", "mysql", "postgresql", "postgres", "sqlserver", "mssql"
            };

            if (String.IsNullOrWhiteSpace(Type))
            {
                errors.Add("Database type is required (--type/-t).");
                return errors;
            }

            if (!validTypes.Contains(Type))
            {
                errors.Add("Invalid database type '" + Type + "'. Valid types: sqlite, mysql, postgresql, postgres, sqlserver, mssql.");
                return errors;
            }

            if (Type == "sqlite")
            {
                if (String.IsNullOrWhiteSpace(Filename))
                {
                    errors.Add("SQLite requires a filename (--filename/-f).");
                }
            }
            else
            {
                if (String.IsNullOrWhiteSpace(Hostname))
                {
                    errors.Add("Hostname is required for " + Type + " (--hostname/-h).");
                }

                if (String.IsNullOrWhiteSpace(Username))
                {
                    errors.Add("Username is required for " + Type + " (--username/-u).");
                }

                if (String.IsNullOrWhiteSpace(Password))
                {
                    errors.Add("Password is required for " + Type + " (--password/-w).");
                }

                if (String.IsNullOrWhiteSpace(Database))
                {
                    errors.Add("Database name is required for " + Type + " (--database/-d).");
                }
            }

            if (Port < 0 || Port > 65535)
            {
                errors.Add("Port must be between 0 and 65535.");
            }

            return errors;
        }

        /// <summary>
        /// Get the effective port, applying defaults based on database type if port is zero.
        /// </summary>
        /// <returns>Effective port number.</returns>
        public int GetEffectivePort()
        {
            if (Port > 0) return Port;

            switch (Type)
            {
                case "postgresql":
                case "postgres":
                    return 5432;

                case "sqlserver":
                case "mssql":
                    return 1433;

                case "mysql":
                    return 3306;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Print usage information to the console.
        /// </summary>
        public static void PrintUsage()
        {
            Console.WriteLine("Armada Database Integration Tests");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  Armada.Test.Database --type <type> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --type, -t       Database type: sqlite, mysql, postgresql, postgres, sqlserver, mssql");
            Console.WriteLine("  --filename, -f   Database filename (SQLite only)");
            Console.WriteLine("  --hostname, -h   Database server hostname");
            Console.WriteLine("  --port, -p       Database server port (default: auto-detect)");
            Console.WriteLine("  --username, -u   Database username");
            Console.WriteLine("  --password, -w   Database password");
            Console.WriteLine("  --database, -d   Database name");
            Console.WriteLine("  --schema, -s     Database schema");
            Console.WriteLine("  --no-cleanup     Do not clean up test data after execution");
            Console.WriteLine("  --help, -?       Show this help message");
            Console.WriteLine();
            Console.WriteLine("Default Ports:");
            Console.WriteLine("  PostgreSQL: 5432");
            Console.WriteLine("  SQL Server: 1433");
            Console.WriteLine("  MySQL:      3306");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine();
            Console.WriteLine("  SQLite:");
            Console.WriteLine("    Armada.Test.Database -t sqlite -f ./test.db");
            Console.WriteLine();
            Console.WriteLine("  PostgreSQL:");
            Console.WriteLine("    Armada.Test.Database -t postgresql -h localhost -u postgres -w secret -d armada_test");
            Console.WriteLine();
            Console.WriteLine("  SQL Server:");
            Console.WriteLine("    Armada.Test.Database -t sqlserver -h localhost -u sa -w secret -d armada_test");
            Console.WriteLine();
            Console.WriteLine("  MySQL:");
            Console.WriteLine("    Armada.Test.Database -t mysql -h localhost -u root -w secret -d armada_test");
        }

        #endregion
    }
}
