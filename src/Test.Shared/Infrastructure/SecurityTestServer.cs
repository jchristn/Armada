namespace Test.Shared.Infrastructure
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;

    /// <summary>
    /// A dedicated in-process Admiral for security suites that need their own configuration (a non-loopback hostname,
    /// a changed default password, MCP settings). Ports come from <see cref="TestPorts"/>; the server and its temp
    /// directory are removed by <see cref="Dispose"/>.
    /// </summary>
    public sealed class SecurityTestServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The server.
        /// </summary>
        public ArmadaServer Server { get; private set; } = null!;

        /// <summary>
        /// The stub agent runtime the server's CLI captain runtimes launch, so no test starts a real agent CLI.
        /// </summary>
        public StubAgentProcesses StubAgents { get; private set; } = null!;

        /// <summary>
        /// Settings the server runs with.
        /// </summary>
        public ArmadaSettings Settings { get; private set; } = null!;

        /// <summary>
        /// REST base URL on IPv4 loopback; final once <see cref="StartAsync"/> has bound the listeners.
        /// </summary>
        public string BaseUrl { get; private set; } = "";

        /// <summary>
        /// MCP base URL on IPv4 loopback; final once <see cref="StartAsync"/> has bound the listeners.
        /// </summary>
        public string McpUrl { get; private set; } = "";

        /// <summary>
        /// Local API key (global admin).
        /// </summary>
        public string ApiKey { get; private set; } = "";

        /// <summary>
        /// Temp directory holding the database and logs.
        /// </summary>
        public string TempDir { get; private set; } = "";

        /// <summary>
        /// Log directory.
        /// </summary>
        public string LogDirectory { get; private set; } = "";

        #endregion

        #region Private-Members


        #endregion

        #region Constructors-and-Factories

        private SecurityTestServer()
        {
        }

        /// <summary>
        /// Prepare (but do not start) a server.
        /// </summary>
        /// <param name="hostname">REST/MCP hostname.</param>
        /// <param name="newAdminPassword">When set, the seeded admin@armada password is changed in the database before start.</param>
        /// <returns>The prepared server; call <see cref="StartAsync"/>.</returns>
        public static async Task<SecurityTestServer> PrepareAsync(string hostname, string? newAdminPassword)
        {
            SecurityTestServer server = new SecurityTestServer();
            server.TempDir = TestTemp.NewDirectory("security");
            server.LogDirectory = Path.Combine(server.TempDir, "logs");

            string sqlitePath = Path.Combine(server.TempDir, "armada.db");
            DatabaseSettings dbSettings = new DatabaseSettings();
            dbSettings.Type = DatabaseTypeEnum.Sqlite;
            dbSettings.Filename = sqlitePath;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = server.TempDir;
            settings.DatabasePath = sqlitePath;
            settings.Database = dbSettings;
            settings.LogDirectory = server.LogDirectory;
            settings.DocksDirectory = Path.Combine(server.TempDir, "docks");
            settings.ReposDirectory = Path.Combine(server.TempDir, "repos");
            settings.SettingsFilePath = Path.Combine(server.TempDir, "settings.json");
            settings.ApiKey = "test-key-" + Guid.NewGuid().ToString("N");
            settings.HeartbeatIntervalSeconds = 300;
            settings.Rest.Hostname = hostname;
            settings.InitializeDirectories();
            server.Settings = settings;
            server.ApiKey = settings.ApiKey;
            // Provisional ports so the settings never name a default port; StartAsync binds on fresh ones.
            int[] ports = TestPorts.Reserve(2);
            server.UsePorts(ports[0], ports[1]);

            TestDatabaseHelper.SeedDatabaseFile(sqlitePath);

            if (!String.IsNullOrEmpty(newAdminPassword))
            {
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                using (DatabaseDriver db = DatabaseDriverFactory.Create(dbSettings, quiet))
                {
                    await db.InitializeAsync().ConfigureAwait(false);
                    UserMaster? admin = await db.Users.ReadByIdAsync(Constants.DefaultUserId).ConfigureAwait(false);
                    if (admin != null)
                    {
                        admin.PasswordSha256 = UserMaster.ComputePasswordHash(newAdminPassword);
                        await db.Users.UpdateAsync(admin).ConfigureAwait(false);
                    }
                }
            }

            return server;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the server and wait for the REST and MCP listeners.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StartAsync()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            logging.Settings.FileLogging = FileLoggingMode.SingleLogFile;
            logging.Settings.LogFilename = Path.Combine(LogDirectory, "security-test.log");
            logging.Settings.MinimumSeverity = Severity.Debug;

            // Bind through TestPorts: a reserved port that something else bound before the Admiral did is replaced
            // by fresh ports instead of failing the suite with "Address already in use".
            Server = await TestPorts.StartOnFreePortsAsync(2, async ports =>
            {
                UsePorts(ports[0], ports[1]);
                ArmadaServer candidate = new ArmadaServer(logging, Settings, quiet: true);
                candidate.RuntimeToolDiscoverySource = new RecordingRuntimeToolDiscoverySource();
                candidate.PushTransport = new RecordingPushTransport();
                try
                {
                    await candidate.StartAsync().ConfigureAwait(false);
                    return candidate;
                }
                catch
                {
                    try { candidate.Stop(); } catch { }
                    throw;
                }
            }).ConfigureAwait(false);
            StubAgents = new StubAgentProcesses(logging);
            StubAgents.InstallOn(Server);

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
                while (!deadline.Passed)
                {
                    try
                    {
                        HttpResponseMessage rest = await client.GetAsync(BaseUrl + "/api/v1/status/health").ConfigureAwait(false);
                        HttpResponseMessage mcp = await client.GetAsync(McpUrl + "/").ConfigureAwait(false);
                        if (rest.StatusCode == HttpStatusCode.OK && mcp.StatusCode == HttpStatusCode.OK) return;
                    }
                    catch (Exception)
                    {
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }
            }

            throw new TimeoutException("security test server did not become ready");
        }

        /// <summary>
        /// A client for the REST API with optional headers.
        /// </summary>
        /// <param name="apiKey">Send the local API key.</param>
        /// <returns>Client; the caller disposes it.</returns>
        public HttpClient CreateRestClient(bool apiKey)
        {
            HttpClient client = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
            if (apiKey) client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
            return client;
        }

        /// <summary>
        /// A client for the MCP server with optional API key.
        /// </summary>
        /// <param name="apiKey">Send the local API key.</param>
        /// <returns>Client; the caller disposes it.</returns>
        public HttpClient CreateMcpClient(bool apiKey)
        {
            HttpClient client = new HttpClient { BaseAddress = new Uri(McpUrl), Timeout = TimeSpan.FromSeconds(30) };
            if (apiKey) client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
            return client;
        }

        /// <summary>
        /// Stop the server and delete its temp directory.
        /// </summary>
        public void Dispose()
        {
            try { Server?.Stop(); } catch { }
            try { StubAgents?.StopAll(); } catch { }
            TestTemp.TryDelete(TempDir);
        }

        #endregion

        #region Private-Methods

        private void UsePorts(int restPort, int mcpPort)
        {
            Settings.AdmiralPort = restPort;
            Settings.McpPort = mcpPort;
            BaseUrl = "http://127.0.0.1:" + restPort;
            McpUrl = "http://127.0.0.1:" + mcpPort;
        }

        #endregion
    }
}
