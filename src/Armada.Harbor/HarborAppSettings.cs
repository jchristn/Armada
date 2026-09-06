namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using Armada.Core;

    /// <summary>
    /// Configuration for the Harbor host runner: where to reach the Admiral, the dashboard URL to open, the
    /// Harbor's identity and advertised capabilities, and the credential material presented on the link.
    /// Loaded from a JSON file under the user profile; missing values fall back to sensible defaults.
    /// </summary>
    public class HarborAppSettings
    {
        #region Public-Members

        /// <summary>
        /// WebSocket URL of the Admiral's Harbor link (ws:// or wss://). The link is served on the main
        /// Admiral port (default 7890), not the MCP port.
        /// </summary>
        public string ServerLinkUrl { get; set; } = "ws://127.0.0.1:7890/v1.0/harbor/connect";

        /// <summary>
        /// Dashboard URL opened by the "Open Dashboard" action.
        /// </summary>
        public string DashboardUrl { get; set; } = "http://127.0.0.1:7890/dashboard";

        /// <summary>
        /// Harbor identifier (hbr_ prefix). Generated on first run when empty.
        /// </summary>
        public string HarborId { get; set; } = string.Empty;

        /// <summary>
        /// Human-facing Harbor name. Defaults to the machine name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Capabilities advertised at handshake (runtimes and tools available on this host).
        /// </summary>
        public List<string> Capabilities { get; set; } = new List<string> { "git" };

        /// <summary>
        /// Heartbeat interval in milliseconds; 0 disables heartbeats.
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 15000;

        /// <summary>
        /// Maximum concurrent jobs this Harbor will accept.
        /// </summary>
        public int MaxConcurrentJobs { get; set; } = 4;

        /// <summary>
        /// Credential access key presented on the link, or empty for an unauthenticated local link.
        /// </summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>
        /// Credential secret presented on the link. Never logged.
        /// </summary>
        public string Secret { get; set; } = string.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load settings from the default path, applying defaults for any missing values and generating an
        /// identity on first run.
        /// </summary>
        /// <returns>The loaded settings.</returns>
        public static HarborAppSettings Load()
        {
            HarborAppSettings settings = new HarborAppSettings();
            string path = DefaultPath();

            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    HarborAppSettings? loaded = JsonSerializer.Deserialize<HarborAppSettings>(json);
                    if (loaded != null) settings = loaded;
                }
            }
            catch
            {
                settings = new HarborAppSettings();
            }

            if (string.IsNullOrWhiteSpace(settings.HarborId))
                settings.HarborId = Constants.IdGenerator.GenerateKSortable(Constants.HarborIdPrefix, 24);
            if (string.IsNullOrWhiteSpace(settings.Name))
                settings.Name = Environment.MachineName;

            return settings;
        }

        /// <summary>
        /// Persist the settings to the default path. Best-effort; failures are swallowed.
        /// </summary>
        public void Save()
        {
            try
            {
                string path = DefaultPath();
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }

        #endregion

        #region Private-Methods

        private static string DefaultPath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".armada-harbor", "settings.json");
        }

        #endregion
    }
}
