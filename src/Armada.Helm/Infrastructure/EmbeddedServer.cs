namespace Armada.Helm.Infrastructure
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Settings;
    using Armada.Server;

    /// <summary>
    /// Manages an in-process Admiral server for single-process mode.
    /// When the CLI cannot reach a running Admiral, it falls back to
    /// starting an embedded server in the same process.
    /// </summary>
    public static class EmbeddedServer
    {
        #region Private-Members

        private static ArmadaServer? _Server;
        private static readonly object _Lock = new object();
        private static bool _Started = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check if the embedded server is currently running.
        /// </summary>
        public static bool IsRunning => _Started;

        /// <summary>
        /// Start the embedded Admiral server if not already running.
        /// This initializes the database, services, and REST API in-process.
        /// </summary>
        /// <returns>A task that completes when the server is ready to accept requests.</returns>
        public static async Task StartAsync()
        {
            lock (_Lock)
            {
                if (_Started) return;
                _Started = true;
            }

            // Load settings
            ArmadaSettings settings = new ArmadaSettings();
            string settingsPath = Path.Combine(Constants.DefaultDataDirectory, "settings.json");
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                ArmadaSettings? loaded = JsonSerializer.Deserialize<ArmadaSettings>(json);
                if (loaded != null) settings = loaded;
            }

            settings.InitializeDirectories();

            // Quiet logging — no console output for embedded mode
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
            if (!Directory.Exists(settings.LogDirectory))
                Directory.CreateDirectory(settings.LogDirectory);
            logging.Settings.LogFilename = Path.Combine(settings.LogDirectory, "admiral.log");

            _Server = new ArmadaServer(logging, settings, quiet: true);
            await _Server.StartAsync().ConfigureAwait(false);

            // Wait for the HTTP listener to be ready
            using HttpClient probe = new HttpClient();
            string healthUrl = "http://localhost:" + settings.AdmiralPort + "/api/v1/status/health";
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    HttpResponseMessage resp = await probe.GetAsync(healthUrl).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode) break;
                }
                catch
                {
                    // Not ready yet
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stop the embedded server if running.
        /// </summary>
        public static void Stop()
        {
            lock (_Lock)
            {
                if (!_Started) return;
                _Started = false;
            }

            _Server?.Stop();
            _Server = null;
        }

        #endregion
    }
}
