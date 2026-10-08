namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Hosting;
    using Avalonia.Threading;

    /// <summary>
    /// What the Harbor windows share: the live settings, the link (through the main window), where the linked
    /// Admiral's files are on this machine, and a REST client for that Admiral. Raises <see cref="Changed"/> on the UI
    /// thread whenever any of them changes, so menus and views can refresh.
    /// </summary>
    public class HarborSession
    {
        #region Public-Members

        /// <summary>
        /// Raised on the UI thread when the link state, settings, or Admiral paths change.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// The live Harbor settings. Edit a <see cref="HarborAppSettings.Clone"/> and apply it with
        /// <see cref="ApplySettings"/>.
        /// </summary>
        public HarborAppSettings Settings { get; }

        /// <summary>
        /// The main window, which runs the link.
        /// </summary>
        public MainWindow Window { get; }

        /// <summary>
        /// Where the linked Admiral's files are on this machine; null until first resolved.
        /// </summary>
        public LocalAdmiralInfo? Admiral { get; private set; } = null;

        /// <summary>
        /// The Admiral's REST base URL derived from the link URL, or null when the link URL is invalid.
        /// </summary>
        public string? RestBaseUrl
        {
            get { return LocalAdmiralInfo.RestBaseUrl(Settings.ServerLinkUrl); }
        }

        /// <summary>
        /// True when the Admiral's files on this machine are its own (see <see cref="LocalAdmiralInfo.IsLocal"/>).
        /// </summary>
        public bool IsAdmiralLocal
        {
            get { return Admiral != null && Admiral.IsLocal; }
        }

        #endregion

        #region Private-Members

        private int _ResolveGeneration = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Live settings.</param>
        /// <param name="window">Main window.</param>
        public HarborSession(HarborAppSettings settings, MainWindow window)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Window = window ?? throw new ArgumentNullException(nameof(window));
            Window.StateChanged += (sender, args) => RaiseChanged();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find the linked Admiral's files again (after the link URL changed, or to pick up a settings file that
        /// appeared). Raises <see cref="Changed"/> when done.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task ResolveAdmiralAsync()
        {
            int generation = Interlocked.Increment(ref _ResolveGeneration);
            string url = Settings.ServerLinkUrl;
            LocalAdmiralInfo info = await Task.Run(() => LocalAdmiralInfo.ResolveAsync(url)).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                // A newer resolve (the URL changed again) wins.
                if (generation != _ResolveGeneration) return;
                Admiral = info;
                RaiseChanged();
            });
        }

        /// <summary>
        /// Validate and save edited settings, make them live, and reconnect when anything the link uses changed.
        /// </summary>
        /// <param name="edited">Edited copy of the settings.</param>
        /// <param name="errors">Validation or save errors.</param>
        /// <returns>True when applied.</returns>
        public bool ApplySettings(HarborAppSettings edited, out List<string> errors)
        {
            if (edited == null) throw new ArgumentNullException(nameof(edited));
            errors = edited.Validate();
            if (errors.Count > 0) return false;

            bool linkChanged = LinkSettingsDiffer(Settings, edited);
            HarborAppSettings previous = Settings.Clone();
            Settings.CopyFrom(edited);
            if (!Settings.TrySave(out string? error))
            {
                Settings.CopyFrom(previous);
                errors.Add("Could not save " + HarborAppSettings.DefaultPath() + ": " + error);
                return false;
            }

            if (Avalonia.Application.Current is App app) app.ApplyAppearance(Settings.Appearance);
            Window.RefreshSettingsDisplay();
            Window.ReportActivity("Settings saved");
            if (linkChanged && Window.IsLinkRunning) Window.Reconnect();
            if (!String.Equals(previous.ServerLinkUrl, Settings.ServerLinkUrl, StringComparison.Ordinal)) _ = ResolveAdmiralAsync();
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// A REST client for the linked Admiral, authorized with the local API key when the Admiral is on this machine
        /// and has one, else with this Harbor's access key. Dispose it after use.
        /// </summary>
        /// <param name="timeoutMs">Request timeout.</param>
        /// <returns>The client, or null when the link URL has no REST equivalent.</returns>
        public ArmadaClient? CreateClient(int timeoutMs = 10000)
        {
            string? baseUrl = RestBaseUrl;
            if (baseUrl == null) return null;

            ArmadaClientOptions options = new ArmadaClientOptions(baseUrl);
            options.TimeoutMs = timeoutMs;
            options.UserAgent = "Armada.Harbor/" + HarborDiagnostics.Version();
            if (IsAdmiralLocal && !String.IsNullOrWhiteSpace(Admiral!.LocalApiKey)) options.ApiKey = Admiral.LocalApiKey;
            else if (!String.IsNullOrWhiteSpace(Settings.AccessKey)) options.BearerToken = Settings.AccessKey.Trim();
            return new ArmadaClient(options);
        }

        /// <summary>
        /// Raise <see cref="Changed"/> (on the UI thread).
        /// </summary>
        public void RaiseChanged()
        {
            if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke(this, EventArgs.Empty);
            else Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        }

        #endregion

        #region Private-Methods

        private static bool LinkSettingsDiffer(HarborAppSettings a, HarborAppSettings b)
        {
            return !String.Equals(a.ServerLinkUrl, b.ServerLinkUrl, StringComparison.Ordinal)
                || !String.Equals(a.Name, b.Name, StringComparison.Ordinal)
                || !String.Equals(a.TenantId, b.TenantId, StringComparison.Ordinal)
                || !String.Equals(a.UserId, b.UserId, StringComparison.Ordinal)
                || !String.Equals(a.AccessKey, b.AccessKey, StringComparison.Ordinal)
                || !String.Equals(a.Secret, b.Secret, StringComparison.Ordinal)
                || a.HeartbeatIntervalMs != b.HeartbeatIntervalMs
                || a.MaxConcurrentJobs != b.MaxConcurrentJobs
                || !String.Equals(String.Join(",", a.Capabilities), String.Join(",", b.Capabilities), StringComparison.Ordinal);
        }

        #endregion
    }
}
