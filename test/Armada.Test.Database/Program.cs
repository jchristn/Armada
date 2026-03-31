namespace Armada.Test.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// Entry point for the Armada database integration test runner.
    /// </summary>
    public class Program
    {
        #region Public-Methods

        /// <summary>
        /// Main entry point. Returns 0 on success, 1 on test failures, 2 on fatal error.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            CommandLineOptions options = CommandLineOptions.Parse(args);

            if (options.Help)
            {
                CommandLineOptions.PrintUsage();
                return 0;
            }

            List<string> errors = options.Validate();
            if (errors.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Validation errors:");
                foreach (string error in errors)
                {
                    Console.WriteLine("  - " + error);
                }
                Console.ResetColor();
                Console.WriteLine();
                CommandLineOptions.PrintUsage();
                return 2;
            }

            DatabaseSettings settings = BuildSettings(options);

            PrintConnectionInfo(options, settings);

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                    Console.WriteLine();
                    Console.WriteLine("Cancellation requested, stopping tests...");
                };

                try
                {
                    Console.WriteLine("Initializing database driver...");
                    DatabaseDriver driver = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, cts.Token).ConfigureAwait(false);

                    Console.WriteLine("Running tests...");
                    Console.WriteLine();

                    using (driver)
                    {
                        DatabaseTestRunner runner = new DatabaseTestRunner(driver, settings, options.NoCleanup);
                        List<TestResult> results = await runner.RunAllAsync(cts.Token).ConfigureAwait(false);
                        return PrintResults(results);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Tests cancelled by user.");
                    Console.ResetColor();
                    return 2;
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Fatal error: " + ex.Message);
                    Console.ResetColor();
                    Console.WriteLine(ex.ToString());
                    return 2;
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Build DatabaseSettings from the parsed command-line options.
        /// </summary>
        private static DatabaseSettings BuildSettings(CommandLineOptions options)
        {
            DatabaseSettings settings = new DatabaseSettings();
            settings.Type = ParseDatabaseType(options.Type);

            if (!String.IsNullOrEmpty(options.Filename)) settings.Filename = options.Filename;
            if (!String.IsNullOrEmpty(options.Hostname)) settings.Hostname = options.Hostname;
            if (options.Port > 0) settings.Port = options.Port;
            if (!String.IsNullOrEmpty(options.Username)) settings.Username = options.Username;
            if (!String.IsNullOrEmpty(options.Password)) settings.Password = options.Password;
            if (!String.IsNullOrEmpty(options.Database)) settings.DatabaseName = options.Database;
            if (!String.IsNullOrEmpty(options.Schema)) settings.Schema = options.Schema;

            return settings;
        }

        /// <summary>
        /// Parse a database type string into the corresponding enum value.
        /// </summary>
        private static DatabaseTypeEnum ParseDatabaseType(string type)
        {
            switch (type)
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
                    throw new ArgumentException("Unknown database type: " + type);
            }
        }

        /// <summary>
        /// Print connection information to the console.
        /// </summary>
        private static void PrintConnectionInfo(CommandLineOptions options, DatabaseSettings settings)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine("ARMADA DATABASE INTEGRATION TESTS");
            Console.WriteLine("================================================================================");
            Console.WriteLine();
            Console.WriteLine("  Database Type : " + settings.Type.ToString());

            if (settings.Type == DatabaseTypeEnum.Sqlite)
            {
                Console.WriteLine("  Filename      : " + options.Filename);
            }
            else
            {
                Console.WriteLine("  Hostname      : " + options.Hostname);
                Console.WriteLine("  Port          : " + options.GetEffectivePort());
                Console.WriteLine("  Username      : " + options.Username);
                Console.WriteLine("  Password      : " + new String('*', options.Password.Length));
                Console.WriteLine("  Database      : " + options.Database);
                if (!String.IsNullOrEmpty(options.Schema))
                {
                    Console.WriteLine("  Schema        : " + options.Schema);
                }
            }

            Console.WriteLine("  No Cleanup    : " + options.NoCleanup);
            Console.WriteLine();
        }

        /// <summary>
        /// Print test results and return the appropriate exit code.
        /// </summary>
        private static int PrintResults(List<TestResult> results)
        {
            int total = results.Count;
            int passed = 0;
            int failed = 0;
            List<TestResult> failedTests = new List<TestResult>();

            foreach (TestResult result in results)
            {
                if (result.Passed)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    failedTests.Add(result);
                }
            }

            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine("TEST SUMMARY");
            Console.WriteLine("================================================================================");
            Console.WriteLine("Total: " + total + "  Passed: " + passed + "  Failed: " + failed);

            if (failedTests.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failed Tests:");
                foreach (TestResult result in failedTests)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  " + result.ToString());
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.WriteLine("================================================================================");

            if (failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("RESULT: PASS");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("RESULT: FAIL");
            }

            Console.ResetColor();
            Console.WriteLine("================================================================================");
            Console.WriteLine();

            return failed == 0 ? 0 : 1;
        }

        #endregion
    }
}
