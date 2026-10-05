namespace Armada.Tui.Services
{
    using System;
    using System.IO;
    using System.Net;
    using System.Text.Json;
    using Armada.Core;

    /// <summary>
    /// Login defaults for an Admiral on this machine: the URL from the local <c>settings.json</c> port, the seeded
    /// admin account, and the configured API key. Read-only; never writes the settings file. Immutable after load.
    /// </summary>
    public class LocalAdmiralDefaults
    {
        #region Public-Members

        /// <summary>
        /// REST port of the local Admiral (the settings value, or 7890).
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Admin API key from the local settings, or null.
        /// </summary>
        public string? ApiKey { get; }

        /// <summary>
        /// Base URL of the local Admiral.
        /// </summary>
        public string Url
        {
            get { return "http://127.0.0.1:" + Port; }
        }

        /// <summary>
        /// Seeded admin email.
        /// </summary>
        public string Email
        {
            get { return Constants.DefaultUserEmail; }
        }

        /// <summary>
        /// Seeded admin password.
        /// </summary>
        public string Password
        {
            get { return Constants.DefaultUserPassword; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="port">REST port (clamped to 1-65535; out of range uses 7890).</param>
        /// <param name="apiKey">API key, or null.</param>
        public LocalAdmiralDefaults(int port, string? apiKey)
        {
            Port = port >= 1 && port <= 65535 ? port : Constants.DefaultAdmiralPort;
            ApiKey = String.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        }

        /// <summary>
        /// Load from a settings file (default: <c>settings.json</c> in the Armada data directory). Never throws: a
        /// missing or unreadable file yields the built-in defaults.
        /// </summary>
        /// <param name="settingsPath">Settings file, or null for the default.</param>
        /// <returns>Defaults.</returns>
        public static LocalAdmiralDefaults Load(string? settingsPath = null)
        {
            string path = settingsPath ?? Path.Combine(Constants.DefaultDataDirectory, "settings.json");
            try
            {
                if (File.Exists(path))
                {
                    JsonSerializerOptions options = new JsonSerializerOptions();
                    options.PropertyNameCaseInsensitive = true;
                    LocalAdmiralSettingsFile? file = JsonSerializer.Deserialize<LocalAdmiralSettingsFile>(File.ReadAllText(path), options);
                    if (file != null) return new LocalAdmiralDefaults(file.AdmiralPort ?? Constants.DefaultAdmiralPort, file.ApiKey);
                }
            }
            catch (Exception)
            {
                // Unreadable or malformed settings: fall back to the built-in defaults.
            }

            return new LocalAdmiralDefaults(Constants.DefaultAdmiralPort, null);
        }

        /// <summary>
        /// True when the URL's host is this machine (localhost, 127.0.0.0/8, or ::1).
        /// </summary>
        /// <param name="url">URL.</param>
        /// <returns>True for loopback.</returns>
        public static bool IsLoopback(string? url)
        {
            if (String.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)) return false;
            if (String.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? address) && IPAddress.IsLoopback(address);
        }

        /// <summary>
        /// True when the URL is a loopback URL on this Admiral's port.
        /// </summary>
        /// <param name="url">URL.</param>
        /// <returns>True when it targets the local Admiral.</returns>
        public bool Targets(string? url)
        {
            if (!IsLoopback(url)) return false;
            return Uri.TryCreate(url!.Trim(), UriKind.Absolute, out Uri? uri) && uri.Port == Port;
        }

        #endregion
    }
}
