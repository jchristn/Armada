namespace Armada.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Client;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Server;

    /// <summary>
    /// Manages the connection to the Admiral server, including auto-start of an embedded server.
    /// Provides polling-based data refresh and exposes current state.
    /// </summary>
    public class ArmadaConnectionService : IDisposable
    {
        #region Private-Members

        private ArmadaApiClient? _ApiClient;
        private ArmadaServer? _EmbeddedServer;
        private ArmadaSettings? _Settings;
        private HttpClient _HttpClient = new HttpClient();
        private CancellationTokenSource? _PollCts;
        private bool _IsConnected;
        private bool _Disposed;
        private int _PollIntervalMs = 5000;

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether the service is connected to the Admiral.
        /// </summary>
        public bool IsConnected
        {
            get => _IsConnected;
            private set
            {
                _IsConnected = value;
                ConnectionChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// Latest status snapshot.
        /// </summary>
        public ArmadaStatus? CurrentStatus { get; private set; }

        /// <summary>
        /// Latest captains list.
        /// </summary>
        public List<Captain> Captains { get; private set; } = new List<Captain>();

        /// <summary>
        /// Latest missions list.
        /// </summary>
        public List<Mission> Missions { get; private set; } = new List<Mission>();

        /// <summary>
        /// Latest vessels list.
        /// </summary>
        public List<Vessel> Vessels { get; private set; } = new List<Vessel>();

        /// <summary>
        /// Latest voyages list.
        /// </summary>
        public List<Voyage> Voyages { get; private set; } = new List<Voyage>();

        /// <summary>
        /// Latest fleets list.
        /// </summary>
        public List<Fleet> Fleets { get; private set; } = new List<Fleet>();

        /// <summary>
        /// Latest signals list.
        /// </summary>
        public List<Signal> Signals { get; private set; } = new List<Signal>();

        /// <summary>
        /// Fired when connection state changes.
        /// </summary>
        public event EventHandler<bool>? ConnectionChanged;

        /// <summary>
        /// Fired after each successful data refresh.
        /// </summary>
        public event EventHandler? DataRefreshed;

        /// <summary>
        /// Status message for display during startup.
        /// </summary>
        public string StatusMessage { get; private set; } = "Initializing...";

        /// <summary>
        /// Fired when status message changes.
        /// </summary>
        public event EventHandler<string>? StatusMessageChanged;

        /// <summary>
        /// Polling interval in milliseconds.
        /// </summary>
        public int PollIntervalMs
        {
            get => _PollIntervalMs;
            set
            {
                if (value < 1000) value = 1000;
                _PollIntervalMs = value;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the loaded settings.
        /// </summary>
        public ArmadaSettings GetSettings()
        {
            if (_Settings == null)
            {
                _Settings = ArmadaSettings.LoadAsync().GetAwaiter().GetResult();
            }
            return _Settings;
        }

        /// <summary>
        /// Get the base URL for the Admiral API.
        /// </summary>
        public string GetBaseUrl()
        {
            ArmadaSettings settings = GetSettings();
            return "http://localhost:" + settings.AdmiralPort;
        }

        /// <summary>
        /// Get the API client.
        /// </summary>
        public ArmadaApiClient GetApiClient()
        {
            if (_ApiClient == null)
            {
                _ApiClient = new ArmadaApiClient(_HttpClient, GetBaseUrl());
            }
            return _ApiClient;
        }

        /// <summary>
        /// Initialize the connection: load settings, check/start server, begin polling.
        /// </summary>
        public async Task InitializeAsync(Action<string>? onStatusUpdate = null)
        {
            void UpdateStatus(string msg)
            {
                StatusMessage = msg;
                StatusMessageChanged?.Invoke(this, msg);
                onStatusUpdate?.Invoke(msg);
            }

            UpdateStatus("Loading settings...");
            _Settings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);

            if (!File.Exists(ArmadaSettings.DefaultSettingsPath))
            {
                _Settings.InitializeDirectories();
                await _Settings.SaveAsync().ConfigureAwait(false);
            }

            UpdateStatus("Checking Admiral server...");
            bool healthy = await GetApiClient().HealthCheckAsync().ConfigureAwait(false);

            if (!healthy)
            {
                UpdateStatus("Starting Admiral server...");
                await StartEmbeddedServerAsync().ConfigureAwait(false);
            }

            // Ensure default fleet
            UpdateStatus("Verifying fleet...");
            await EnsureDefaultFleetAsync().ConfigureAwait(false);

            IsConnected = true;
            UpdateStatus("Connected");

            // Start polling
            StartPolling();
        }

        /// <summary>
        /// Perform a single data refresh.
        /// </summary>
        public async Task RefreshAsync()
        {
            try
            {
                ArmadaApiClient client = GetApiClient();

                Task<ArmadaStatus?> statusTask = SafeCall(client.GetStatusAsync());
                Task<EnumerationResult<Captain>?> captainsTask = SafeCall(client.ListCaptainsAsync());
                Task<EnumerationResult<Mission>?> missionsTask = SafeCall(client.ListMissionsAsync());
                Task<EnumerationResult<Vessel>?> vesselsTask = SafeCall(client.ListVesselsAsync());
                Task<EnumerationResult<Voyage>?> voyagesTask = SafeCall(client.ListVoyagesAsync());
                Task<EnumerationResult<Fleet>?> fleetsTask = SafeCall(client.ListFleetsAsync());

                await Task.WhenAll(statusTask, captainsTask, missionsTask, vesselsTask, voyagesTask, fleetsTask).ConfigureAwait(false);

                CurrentStatus = statusTask.Result ?? CurrentStatus;
                Captains = captainsTask.Result?.Objects ?? Captains;
                Missions = missionsTask.Result?.Objects ?? Missions;
                Vessels = vesselsTask.Result?.Objects ?? Vessels;
                Voyages = voyagesTask.Result?.Objects ?? Voyages;
                Fleets = fleetsTask.Result?.Objects ?? Fleets;

                DebugLog($"Refresh: status={statusTask.Result != null} fleets={Fleets.Count} vessels={Vessels.Count} captains={Captains.Count} missions={Missions.Count} voyages={Voyages.Count}");

                if (!IsConnected) IsConnected = true;

                DataRefreshed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArmadaConnection] RefreshAsync FAILED: {ex.GetType().Name}: {ex.Message}");
                if (IsConnected) IsConnected = false;
            }
        }

        /// <summary>
        /// Start the polling loop.
        /// </summary>
        public void StartPolling()
        {
            StopPolling();
            _PollCts = new CancellationTokenSource();
            CancellationToken token = _PollCts.Token;
            _ = PollLoopAsync(token);
        }

        /// <summary>
        /// Stop the polling loop.
        /// </summary>
        public void StopPolling()
        {
            _PollCts?.Cancel();
            _PollCts?.Dispose();
            _PollCts = null;
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            StopPolling();
            _ApiClient?.Dispose();
            _HttpClient?.Dispose();
            _EmbeddedServer?.Stop();
        }

        #endregion

        #region Private-Methods

        private async Task<T?> SafeCall<T>(Task<T?> task) where T : class
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugLog($"SafeCall<{typeof(T).Name}> FAILED: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Write a debug log entry. Public so VMs can use it too.
        /// </summary>
        public static void DebugLogStatic(string message)
        {
            DebugLog(message);
        }

        private static void DebugLog(string message)
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".armada", "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "desktop-debug.log");
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await RefreshAsync().ConfigureAwait(false);

                try
                {
                    await Task.Delay(_PollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task StartEmbeddedServerAsync()
        {
            if (_Settings == null) _Settings = new ArmadaSettings();
            _Settings.InitializeDirectories();

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
            if (!Directory.Exists(_Settings.LogDirectory))
                Directory.CreateDirectory(_Settings.LogDirectory);
            logging.Settings.LogFilename = Path.Combine(_Settings.LogDirectory, "admiral.log");

            _EmbeddedServer = new ArmadaServer(logging, _Settings, quiet: true);
            await _EmbeddedServer.StartAsync().ConfigureAwait(false);

            // Wait for the HTTP listener to be ready
            string healthUrl = GetBaseUrl() + "/api/v1/status/health";
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    HttpResponseMessage resp = await _HttpClient.GetAsync(healthUrl).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode) return;
                }
                catch
                {
                    // Not ready yet
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        private async Task EnsureDefaultFleetAsync()
        {
            try
            {
                EnumerationResult<Fleet>? fleets = await GetApiClient().ListFleetsAsync().ConfigureAwait(false);
                if (fleets != null && fleets.Objects.Count > 0) return;

                Fleet fleet = new Fleet(Constants.DefaultFleetName) { Description = "Default fleet (auto-created)" };
                await GetApiClient().CreateFleetAsync(fleet).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort
            }
        }

        #endregion
    }
}
