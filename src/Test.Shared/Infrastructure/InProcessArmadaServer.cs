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
    /// An in-process Admiral bound to 127.0.0.1 on ports from <see cref="PortRangeStart"/>..<see cref="PortRangeEnd"/>,
    /// against a caller-owned data directory and database, that can be stopped and started again on the same data.
    /// Used by suites that need a restart (restore drill, upgrade verification) or a private server whose settings
    /// they change (retention). The settings file is kept inside the data directory, never ~/.armada.
    /// </summary>
    public sealed class InProcessArmadaServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// First port tried for the REST and MCP listeners.
        /// </summary>
        public const int PortRangeStart = 22000;

        /// <summary>
        /// Last port tried for the REST and MCP listeners.
        /// </summary>
        public const int PortRangeEnd = 22099;

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

        private static readonly SemaphoreSlim _PortGate = new SemaphoreSlim(1, 1);
        private static int _NextPort = PortRangeStart;
        private ArmadaServer? _Server;

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

            Settings.AdmiralPort = await ReservePortAsync().ConfigureAwait(false);
            Settings.McpPort = await ReservePortAsync().ConfigureAwait(false);
            Settings.InitializeDirectories();

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            ArmadaServer server = new ArmadaServer(logging, Settings, quiet: true);
            await server.StartAsync().ConfigureAwait(false);
            _Server = server;

            BaseUrl = "http://127.0.0.1:" + Settings.AdmiralPort;
            Client?.Dispose();
            Client = new HttpClient();
            Client.BaseAddress = new Uri(BaseUrl);
            Client.DefaultRequestHeaders.Add("X-Api-Key", Settings.ApiKey);
            Client.Timeout = TimeSpan.FromSeconds(60);

            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
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
        /// Reserve a free loopback port in <see cref="PortRangeStart"/>..<see cref="PortRangeEnd"/> (round robin, so
        /// back-to-back reservations differ even before the caller binds).
        /// </summary>
        /// <returns>A port that was free when checked.</returns>
        public static async Task<int> ReservePortAsync()
        {
            await _PortGate.WaitAsync().ConfigureAwait(false);
            try
            {
                for (int attempt = 0; attempt <= PortRangeEnd - PortRangeStart; attempt++)
                {
                    int port = _NextPort;
                    _NextPort = _NextPort >= PortRangeEnd ? PortRangeStart : _NextPort + 1;
                    if (IsFree(port)) return port;
                }
            }
            finally
            {
                _PortGate.Release();
            }

            throw new InvalidOperationException("No free port in " + PortRangeStart + "-" + PortRangeEnd + ".");
        }

        #endregion

        #region Private-Methods

        private static bool IsFree(int port)
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        #endregion
    }
}
