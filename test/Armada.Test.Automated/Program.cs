namespace Armada.Test.Automated
{
    using System.Net;
    using System.Net.Sockets;
    using Armada.Core.Settings;
    using Armada.Server;
    using Armada.Test.Automated.Suites;
    using Armada.Test.Common;
    using SyslogLogging;

    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            bool noCleanup = args.Contains("--no-cleanup");

            // Create temp directory for test database and server files
            string tempDir = Path.Combine(Path.GetTempPath(), "armada_test_automated_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            // Allocate random ports
            int restPort = GetAvailablePort();
            int mcpPort = GetAvailablePort();
            int wsPort = GetAvailablePort();
            string apiKey = "test-key-" + Guid.NewGuid().ToString("N");

            string dbPath = Path.Combine(tempDir, "armada.db");

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = tempDir;
            settings.DatabasePath = dbPath;
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
                runner.AddSuite(new McpToolTests(mcpClient));
                runner.AddSuite(new WebSocketTests(authClient, unauthClient, wsPort, apiKey));
                runner.AddSuite(new WorkflowTests(authClient, unauthClient));

                exitCode = await runner.RunAllAsync().ConfigureAwait(false);
            }
            finally
            {
                authClient.Dispose();
                unauthClient.Dispose();
                mcpClient.Dispose();

                try { server.Stop(); } catch { }
                await Task.Delay(200).ConfigureAwait(false);

                if (!noCleanup)
                {
                    try
                    {
                        if (Directory.Exists(tempDir))
                            Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
                else
                {
                    Console.WriteLine("Test data preserved at: " + tempDir);
                }
            }

            return exitCode;
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
