namespace Armada.Core.Hosting
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Settings;

    /// <summary>
    /// Where a desktop tool (Harbor) finds the Admiral's data directory, settings file, and logs, and whether those
    /// files belong to the Admiral it is linked to. They do only when the link points at this machine (a loopback
    /// URL) and the data directory exists here; in split mode with a remote Admiral the local directory is not that
    /// Admiral's, so callers should send the operator to the dashboard instead.
    /// </summary>
    public class LocalAdmiralInfo
    {
        #region Public-Members

        /// <summary>
        /// Base name of the Admiral's log file in <see cref="LogDirectory"/> (daily files append a date).
        /// </summary>
        public const string AdmiralLogBaseName = "admiral.log";

        /// <summary>
        /// The Armada data directory (ARMADA_DATA_DIR, else ~/.armada).
        /// </summary>
        public string DataDirectory { get; private set; } = String.Empty;

        /// <summary>
        /// The Admiral's settings file (settings.json in the data directory).
        /// </summary>
        public string SettingsFile { get; private set; } = String.Empty;

        /// <summary>
        /// The Admiral's log directory: LogDirectory from the settings file when it can be read, else logs/ in the
        /// data directory.
        /// </summary>
        public string LogDirectory { get; private set; } = String.Empty;

        /// <summary>
        /// The Admiral's database backups (backups/ in the data directory).
        /// </summary>
        public string BackupsDirectory
        {
            get { return Path.Combine(DataDirectory, "backups"); }
        }

        /// <summary>
        /// The terminal UI's preferences file (tui.json in the data directory).
        /// </summary>
        public string TuiPreferencesFile
        {
            get { return Path.Combine(DataDirectory, "tui.json"); }
        }

        /// <summary>
        /// The local API key from the settings file, which authorizes REST calls to this machine's Admiral; null when
        /// none is configured or the file cannot be read. Never log it.
        /// </summary>
        public string? LocalApiKey { get; private set; } = null;

        /// <summary>
        /// The log directory's layout.
        /// </summary>
        public ArmadaLogPaths Logs
        {
            get { return new ArmadaLogPaths(LogDirectory); }
        }

        /// <summary>
        /// True when the linked Admiral runs on this machine and its data directory exists here.
        /// </summary>
        public bool IsLocal { get; private set; } = false;

        /// <summary>
        /// Why <see cref="IsLocal"/> is false, for a tooltip or disabled-item hint. Empty when local.
        /// </summary>
        public string Reason { get; private set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Resolve the paths for the Admiral reached at <paramref name="admiralUrl"/>.
        /// </summary>
        /// <param name="admiralUrl">Any URL of the Admiral (the Harbor link URL works).</param>
        /// <param name="dataDirectory">Data directory override; null uses <see cref="Constants.DefaultDataDirectory"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolved information.</returns>
        public static async Task<LocalAdmiralInfo> ResolveAsync(string? admiralUrl, string? dataDirectory = null, CancellationToken token = default)
        {
            LocalAdmiralInfo info = new LocalAdmiralInfo();
            info.DataDirectory = String.IsNullOrWhiteSpace(dataDirectory) ? Constants.DefaultDataDirectory : dataDirectory!;
            info.SettingsFile = Path.Combine(info.DataDirectory, "settings.json");
            info.LogDirectory = Path.Combine(info.DataDirectory, "logs");

            if (File.Exists(info.SettingsFile))
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    ArmadaSettings settings = await ArmadaSettings.LoadAsync(info.SettingsFile).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(settings.LogDirectory)) info.LogDirectory = settings.LogDirectory;
                    if (!String.IsNullOrWhiteSpace(settings.ApiKey)) info.LocalApiKey = settings.ApiKey!.Trim();
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is ArgumentException)
                {
                    // A value the Admiral would reject (the settings editor reports it): still take the log directory and
                    // API key, which the rest of the file does not affect.
                    ApplyHints(info, info.SettingsFile);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // An unreadable settings file keeps the default log directory.
                }
            }

            if (!IsLoopbackUrl(admiralUrl))
            {
                info.Reason = "The Admiral is on another machine; use the dashboard for its settings and logs.";
            }
            else if (!Directory.Exists(info.DataDirectory))
            {
                info.Reason = "No Armada data directory at " + info.DataDirectory + ".";
            }
            else
            {
                info.IsLocal = true;
            }

            return info;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the URL's host is this machine: localhost or a loopback address.
        /// </summary>
        /// <param name="url">Absolute URL.</param>
        /// <returns>True for a loopback host; false for any other host or an invalid URL.</returns>
        public static bool IsLoopbackUrl(string? url)
        {
            if (String.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
            if (uri.IsLoopback) return true;
            string host = uri.Host.Trim('[', ']');
            return IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address);
        }

        /// <summary>
        /// The Admiral's REST base URL for a Harbor link URL: ws becomes http and wss becomes https, keeping the host
        /// and port and dropping the path (ws://127.0.0.1:7890/v1.0/harbor/connect gives http://127.0.0.1:7890).
        /// </summary>
        /// <param name="linkUrl">Harbor link URL.</param>
        /// <returns>Base URL without a trailing slash, or null when the URL is not ws, wss, http, or https.</returns>
        public static string? RestBaseUrl(string? linkUrl)
        {
            if (String.IsNullOrWhiteSpace(linkUrl)) return null;
            if (!Uri.TryCreate(linkUrl.Trim(), UriKind.Absolute, out Uri? uri)) return null;

            string scheme;
            if (uri.Scheme == "ws" || uri.Scheme == Uri.UriSchemeHttp) scheme = Uri.UriSchemeHttp;
            else if (uri.Scheme == "wss" || uri.Scheme == Uri.UriSchemeHttps) scheme = Uri.UriSchemeHttps;
            else return null;

            UriBuilder builder = new UriBuilder(scheme, uri.Host, uri.Port);
            return builder.Uri.GetLeftPart(UriPartial.Authority);
        }

        /// <summary>
        /// The most recently written log file named <paramref name="baseName"/> or <paramref name="baseName"/>.suffix
        /// (daily and rotated files) in a directory.
        /// </summary>
        /// <param name="logDirectory">Directory to search.</param>
        /// <param name="baseName">Log base name, for example admiral.log.</param>
        /// <returns>The newest file's full path, or null when there is none.</returns>
        public static string? FindLatestLog(string logDirectory, string baseName)
        {
            if (String.IsNullOrWhiteSpace(logDirectory)) throw new ArgumentNullException(nameof(logDirectory));
            if (String.IsNullOrWhiteSpace(baseName)) throw new ArgumentNullException(nameof(baseName));
            if (!Directory.Exists(logDirectory)) return null;

            string? newest = null;
            DateTime newestWrite = DateTime.MinValue;
            foreach (string file in Directory.EnumerateFiles(logDirectory))
            {
                string name = Path.GetFileName(file);
                bool matches = String.Equals(name, baseName, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;

                DateTime written = File.GetLastWriteTimeUtc(file);
                if (newest == null || written > newestWrite)
                {
                    newest = file;
                    newestWrite = written;
                }
            }

            return newest;
        }

        /// <summary>
        /// The Admiral's current log file, or null when none has been written.
        /// </summary>
        /// <returns>Full path, or null.</returns>
        public string? FindLatestAdmiralLog()
        {
            return FindLatestLog(LogDirectory, AdmiralLogBaseName);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Read only the log directory and API key from a settings file that does not pass full validation.
        /// </summary>
        private static void ApplyHints(LocalAdmiralInfo info, string settingsFile)
        {
            try
            {
                System.Text.Json.JsonSerializerOptions options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                LocalAdmiralSettingsHints? hints = System.Text.Json.JsonSerializer.Deserialize<LocalAdmiralSettingsHints>(File.ReadAllText(settingsFile), options);
                if (hints == null) return;
                if (!String.IsNullOrWhiteSpace(hints.LogDirectory)) info.LogDirectory = hints.LogDirectory!;
                if (!String.IsNullOrWhiteSpace(hints.ApiKey)) info.LocalApiKey = hints.ApiKey!.Trim();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // Not even well-formed JSON: keep the defaults.
            }
        }

        #endregion
    }
}
