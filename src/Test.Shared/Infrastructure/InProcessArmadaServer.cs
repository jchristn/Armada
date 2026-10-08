namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;

    /// <summary>
    /// An in-process Admiral bound to 127.0.0.1 on ports from <see cref="TestPorts"/>,
    /// against a caller-owned data directory and database, that can be stopped and started again on the same data.
    /// Used by suites that need a restart (restore drill, upgrade verification) or a private server whose settings
    /// they change (retention). The settings file is kept inside the data directory, never ~/.armada.
    /// </summary>
    public sealed class InProcessArmadaServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Settings the server runs with.
        /// </summary>
        public ArmadaSettings Settings { get; }

        /// <summary>
        /// HTTP client authenticated with the server's API key.
        /// </summary>
        public HttpClient Client { get; private set; } = null!;

        /// <summary>
        /// REST base URL.
        /// </summary>
        public string BaseUrl { get; private set; } = "";

        /// <summary>
        /// Whether the server is running.
        /// </summary>
        public bool Running
        {
            get { return _Server != null; }
        }

        #endregion

        #region Private-Members

        private ArmadaServer? _Server;
        private readonly StubAgentProcesses _StubAgents = new StubAgentProcesses(new LoggingModule { Settings = { EnableConsole = false } });

        #endregion

        #region Constructors-and-Factories

        private InProcessArmadaServer(ArmadaSettings settings)
        {
            Settings = settings;
        }

        /// <summary>
        /// Build settings for a server rooted at a data directory and start it.
        /// </summary>
        /// <param name="dataDirectory">Data directory (created when missing).</param>
        /// <param name="database">Database settings.</param>
        /// <param name="configure">Optional settings customization applied before start.</param>
        /// <returns>The running server.</returns>
        public static async Task<InProcessArmadaServer> StartAsync(string dataDirectory, DatabaseSettings database, Action<ArmadaSettings>? configure = null)
        {
            if (String.IsNullOrEmpty(dataDirectory)) throw new ArgumentNullException(nameof(dataDirectory));
            if (database == null) throw new ArgumentNullException(nameof(database));

            BaseAgentRuntime.GracefulStopTimeoutMs = 500;
            Directory.CreateDirectory(dataDirectory);

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = dataDirectory;
            settings.SettingsFilePath = Path.Combine(dataDirectory, "settings.json");
            settings.Database = database;
            if (database.Type == Armada.Core.Enums.DatabaseTypeEnum.Sqlite) settings.DatabasePath = database.Filename;
            settings.LogDirectory = Path.Combine(dataDirectory, "logs");
            settings.DocksDirectory = Path.Combine(dataDirectory, "docks");
            settings.ReposDirectory = Path.Combine(dataDirectory, "repos");
            settings.ApiKey = "test-key-" + Guid.NewGuid().ToString("N");
            settings.HeartbeatIntervalSeconds = 300;
            // Shared fixtures serve many suites from one loopback address, several of which test bad passwords and
            // invalid tokens on purpose: lift the login rate limits so one suite cannot lock out the next. The limiter
            // itself is covered by E2E.LoginSecurity on a dedicated server.
            settings.LoginRateLimit.MaxFailuresPerAccount = 1000;
            settings.LoginRateLimit.MaxFailuresPerAddress = 100000;
            settings.Rest.Hostname = "127.0.0.1";
            configure?.Invoke(settings);

            InProcessArmadaServer server = new InProcessArmadaServer(settings);
            await server.StartAgainAsync().ConfigureAwait(false);
            return server;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start (or restart after <see cref="Stop"/>) the server on fresh ports with the same settings and data.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StartAgainAsync()
        {
            if (_Server != null) throw new InvalidOperationException("Server is already running.");

            Settings.InitializeDirectories();

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            // Bind through TestPorts: a reserved port that something else bound before the Admiral did is replaced
            // by fresh ports instead of failing the start with "Address already in use".
            ArmadaServer server = await TestPorts.StartOnFreePortsAsync(2, async ports =>
            {
                Settings.AdmiralPort = ports[0];
                Settings.McpPort = ports[1];
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
            _StubAgents.InstallOn(server);
            _Server = server;

            BaseUrl = "http://127.0.0.1:" + Settings.AdmiralPort;
            Client?.Dispose();
            Client = new HttpClient();
            Client.BaseAddress = new Uri(BaseUrl);
            Client.DefaultRequestHeaders.Add("X-Api-Key", Settings.ApiKey);
            Client.Timeout = TimeSpan.FromSeconds(60);

            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
            while (!deadline.Passed)
            {
                try
                {
                    HttpResponseMessage health = await Client.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                    if (health.StatusCode == HttpStatusCode.OK) return;
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            throw new TimeoutException("In-process Admiral did not become ready within 30 seconds on " + BaseUrl);
        }

        /// <summary>
        /// Stop the server, keeping its data directory and database.
        /// </summary>
        public void Stop()
        {
            ArmadaServer? server = _Server;
            _Server = null;
            try { server?.Stop(); } catch { }
            _StubAgents.StopAll();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        /// <summary>
        /// Stop the server and dispose the client. The data directory is left for the caller to delete.
        /// </summary>
        public void Dispose()
        {
            Stop();
            try { Client?.Dispose(); } catch { }
        }

        /// <summary>
        /// Reserve a free loopback port (see <see cref="TestPorts"/>); back-to-back reservations always differ.
        /// </summary>
        /// <returns>A port that was free when checked.</returns>
        public static Task<int> ReservePortAsync()
        {
            return Task.FromResult(TestPorts.Reserve(1)[0]);
        }

        #endregion

    }
}
