namespace Armada.Test.Automated
{
    using System.Net;
    using System.Net.Sockets;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Armada.Server;
    using Armada.Test.Automated.Suites;
    using Armada.Test.Common;
    using SyslogLogging;

    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            CommandLineOptions options;

            try
            {
                options = CommandLineOptions.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                Console.WriteLine();
                CommandLineOptions.PrintUsage("Armada.Test.Automated");
                return 1;
            }

            if (options.Help)
            {
                CommandLineOptions.PrintUsage("Armada.Test.Automated");
                return 0;
            }

            // Create temp directory for test server files
            string tempDir = Path.Combine(Path.GetTempPath(), "armada_test_automated_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            // Build database settings
            string defaultSqlitePath = Path.Combine(tempDir, "armada.db");
            DatabaseSettings dbSettings;

            try
            {
                dbSettings = options.BuildDatabaseSettings(defaultSqlitePath);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                Console.WriteLine();
                CommandLineOptions.PrintUsage("Armada.Test.Automated");
                return 1;
            }

            // Print database info at startup
            PrintDatabaseInfo(dbSettings);

            // Allocate random ports
            int restPort = GetAvailablePort();
            int mcpPort = GetAvailablePort();
            int wsPort = GetAvailablePort();
            string apiKey = "test-key-" + Guid.NewGuid().ToString("N");

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = tempDir;
            settings.DatabasePath = dbSettings.Filename;
            settings.Database = dbSettings;
            settings.LogDirectory = Path.Combine(tempDir, "logs");
            settings.DocksDirectory = Path.Combine(tempDir, "docks");
            settings.ReposDirectory = Path.Combine(tempDir, "repos");
            settings.AdmiralPort = restPort;
            settings.McpPort = mcpPort;
            settings.WebSocketPort = wsPort;
            settings.ApiKey = apiKey;
            settings.HeartbeatIntervalSeconds = 300;
            settings.InitializeDirectories();

            ArmadaServer server = new ArmadaServer(logging, settings, quiet: true);
            await server.StartAsync().ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false);

            string baseUrl = "http://localhost:" + restPort;

            // Create shared HttpClient instances
            HttpClient authClient = new HttpClient();
            authClient.BaseAddress = new Uri(baseUrl);
            authClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

            HttpClient unauthClient = new HttpClient();
            unauthClient.BaseAddress = new Uri(baseUrl);

            HttpClient mcpClient = new HttpClient();
            mcpClient.BaseAddress = new Uri("http://localhost:" + mcpPort);

            int exitCode;

            try
            {
                TestRunner runner = new TestRunner("ARMADA AUTOMATED TEST SUITE");

                runner.AddSuite(new FleetTests(authClient, unauthClient));
                runner.AddSuite(new VesselTests(authClient, unauthClient));
                runner.AddSuite(new CaptainTests(authClient, unauthClient));
                runner.AddSuite(new MissionTests(authClient, unauthClient));
                runner.AddSuite(new VoyageTests(authClient, unauthClient));
                runner.AddSuite(new SignalTests(authClient, unauthClient));
                runner.AddSuite(new EventTests(authClient, unauthClient));
                runner.AddSuite(new DockTests(authClient, unauthClient));
                runner.AddSuite(new MergeQueueTests(authClient, unauthClient));
                runner.AddSuite(new StatusTests(authClient, unauthClient));
                runner.AddSuite(new LogTests(authClient, unauthClient, tempDir));
                runner.AddSuite(new AuthenticationTests(authClient, unauthClient, baseUrl, apiKey));
                runner.AddSuite(new AuthApiTests(authClient, unauthClient, baseUrl, apiKey));
                runner.AddSuite(new CrossTenantApiTests(authClient, unauthClient, baseUrl, apiKey));
                runner.AddSuite(new McpToolTests(mcpClient));
                runner.AddSuite(new WebSocketTests(authClient, unauthClient, wsPort, apiKey));
                runner.AddSuite(new WorkflowTests(authClient, unauthClient));
                runner.AddSuite(new LandingPipelineTests(authClient, unauthClient));

                exitCode = await runner.RunAllAsync().ConfigureAwait(false);
            }
            finally
            {
                authClient.Dispose();
                unauthClient.Dispose();
                mcpClient.Dispose();

                try { server.Stop(); } catch { }
                await Task.Delay(200).ConfigureAwait(false);

                if (!options.NoCleanup)
                {
                    if (options.IsTempSqlite)
                    {
                        // Default behavior: delete entire temp directory including database
                        try
                        {
                            if (Directory.Exists(tempDir))
                                Directory.Delete(tempDir, true);
                        }
                        catch { }
                    }
                    else
                    {
                        // Non-default database: only delete temp subdirectories (logs/docks/repos), not the database
                        string[] tempSubDirs = new string[]
                        {
                            Path.Combine(tempDir, "logs"),
                            Path.Combine(tempDir, "docks"),
                            Path.Combine(tempDir, "repos")
                        };

                        foreach (string subDir in tempSubDirs)
                        {
                            try
                            {
                                if (Directory.Exists(subDir))
                                    Directory.Delete(subDir, true);
                            }
                            catch { }
                        }

                        // Try to clean up the temp directory if it's now empty
                        try
                        {
                            if (Directory.Exists(tempDir) && Directory.GetFileSystemEntries(tempDir).Length == 0)
                                Directory.Delete(tempDir);
                        }
                        catch { }
                    }
                }
                else
                {
                    Console.WriteLine("Test data preserved at: " + tempDir);
                }
            }

            return exitCode;
        }

        private static void PrintDatabaseInfo(DatabaseSettings dbSettings)
        {
            switch (dbSettings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    Console.WriteLine("Database: SQLite (" + dbSettings.Filename + ")");
                    break;

                case DatabaseTypeEnum.Postgresql:
                    int pgPort = dbSettings.Port > 0 ? dbSettings.Port : 5432;
                    Console.WriteLine("Database: PostgreSQL (" + dbSettings.Hostname + ":" + pgPort + "/" + dbSettings.DatabaseName + ")");
                    break;

                case DatabaseTypeEnum.SqlServer:
                    int sqlPort = dbSettings.Port > 0 ? dbSettings.Port : 1433;
                    Console.WriteLine("Database: SQL Server (" + dbSettings.Hostname + ":" + sqlPort + "/" + dbSettings.DatabaseName + ")");
                    break;

                case DatabaseTypeEnum.Mysql:
                    int myPort = dbSettings.Port > 0 ? dbSettings.Port : 3306;
                    Console.WriteLine("Database: MySQL (" + dbSettings.Hostname + ":" + myPort + "/" + dbSettings.DatabaseName + ")");
                    break;
            }
        }

        private static int GetAvailablePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
