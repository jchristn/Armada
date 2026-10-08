namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Hosting;

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
        /// User identifier sent as x-user-guid at connect. Informational only: the Admiral ignores it and takes
        /// the Harbor's owning user from the AccessKey credential (a Harbor without one has no owner). To make
        /// this "your" Harbor for user-scoped launch policies (requireHarborForLaunch), set AccessKey to one of
        /// your Armada credentials.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Owning tenant identifier this Harbor registers under (sent as x-tenant-guid at connect). Empty
        /// registers the Harbor with no tenant.
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// Capabilities advertised at handshake (runtimes and tools available on this host).
        /// </summary>
        public List<string> Capabilities { get; set; } = new List<string> { "git" };

        /// <summary>
        /// Window color scheme: System (default, follows the operating system), Light, or Dark.
        /// </summary>
        public HarborAppearanceEnum Appearance { get; set; } = HarborAppearanceEnum.System;

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
        /// Persist the settings to the default path. Best-effort; failures are swallowed (used at startup, where
        /// there is nobody to tell). Editors use <see cref="TrySave"/>.
        /// </summary>
        public void Save()
        {
            TrySave(out string? _);
        }

        /// <summary>
        /// Persist the settings to the default path atomically, keeping the previous file as a backup.
        /// </summary>
        /// <param name="error">Why the save failed, or null.</param>
        /// <returns>True when saved.</returns>
        public bool TrySave(out string? error)
        {
            try
            {
                SettingsFileStore.Save(DefaultPath(), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), 3);
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Check the settings for values Harbor cannot run with.
        /// </summary>
        /// <returns>One message per problem; empty when valid.</returns>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Name)) errors.Add("Name is required.");
            if (!Uri.TryCreate(ServerLinkUrl, UriKind.Absolute, out Uri? link) || (link.Scheme != "ws" && link.Scheme != "wss"))
                errors.Add("Link URL must be an absolute ws:// or wss:// URL, for example ws://127.0.0.1:7890/v1.0/harbor/connect.");
            if (!Uri.TryCreate(DashboardUrl, UriKind.Absolute, out Uri? dashboard) || (dashboard.Scheme != Uri.UriSchemeHttp && dashboard.Scheme != Uri.UriSchemeHttps))
                errors.Add("Dashboard URL must be an absolute http:// or https:// URL.");
            if (HeartbeatIntervalMs < 0) errors.Add("Heartbeat interval cannot be negative (0 turns heartbeats off).");
            if (HeartbeatIntervalMs > 0 && HeartbeatIntervalMs < 1000) errors.Add("Heartbeat interval must be at least 1000 ms, or 0 to turn heartbeats off.");
            if (MaxConcurrentJobs < 1 || MaxConcurrentJobs > 64) errors.Add("Maximum concurrent jobs must be between 1 and 64.");
            if (!string.IsNullOrWhiteSpace(Secret) && string.IsNullOrWhiteSpace(AccessKey)) errors.Add("A secret needs an access key.");
            return errors;
        }

        /// <summary>
        /// A copy of these settings, for an editor to change without touching the live ones.
        /// </summary>
        /// <returns>The copy.</returns>
        public HarborAppSettings Clone()
        {
            HarborAppSettings copy = new HarborAppSettings();
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>
        /// Overwrite every value with another instance's.
        /// </summary>
        /// <param name="other">Source.</param>
        public void CopyFrom(HarborAppSettings other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            ServerLinkUrl = other.ServerLinkUrl;
            DashboardUrl = other.DashboardUrl;
            HarborId = other.HarborId;
            Name = other.Name;
            UserId = other.UserId;
            TenantId = other.TenantId;
            Capabilities = new List<string>(other.Capabilities ?? new List<string>());
            Appearance = other.Appearance;
            HeartbeatIntervalMs = other.HeartbeatIntervalMs;
            MaxConcurrentJobs = other.MaxConcurrentJobs;
            AccessKey = other.AccessKey;
            Secret = other.Secret;
        }

        /// <summary>
        /// The Harbor settings file: settings.json in <see cref="SettingsDirectory"/>.
        /// </summary>
        /// <returns>Full path.</returns>
        public static string DefaultPath()
        {
            return Path.Combine(SettingsDirectory(), "settings.json");
        }

        /// <summary>
        /// Harbor's own log folder (logs/ in <see cref="SettingsDirectory"/>).
        /// </summary>
        /// <returns>Full path.</returns>
        public static string LogDirectory()
        {
            return Path.Combine(SettingsDirectory(), "logs");
        }

        /// <summary>
        /// The Harbor settings folder (~/.armada-harbor).
        /// </summary>
        /// <returns>Full path.</returns>
        public static string SettingsDirectory()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".armada-harbor");
        }

        #endregion
    }
}
