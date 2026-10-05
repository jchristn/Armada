namespace Armada.Tui
{
    using System;

    /// <summary>
    /// How <c>armada tui</c> starts: server, profile, and overrides. Values left null come from the environment
    /// (<c>ARMADA_URL</c>, <c>ARMADA_TOKEN</c>) and the preferences file.
    /// </summary>
    public class TuiStartOptions
    {
        #region Public-Members

        /// <summary>
        /// Server URL (<c>--server</c>), or null.
        /// </summary>
        public string? ServerUrl { get; set; } = null;

        /// <summary>
        /// Profile name (<c>--profile</c>), or null.
        /// </summary>
        public string? ProfileName { get; set; } = null;

        /// <summary>
        /// Token or API key for a scripted start, or null (falls back to <c>ARMADA_TOKEN</c>).
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// Server used when nothing else names one (Helm passes its configured Admiral URL). Default: the local
        /// Admiral from <c>settings.json</c> in the data directory (<c>http://127.0.0.1:7890</c> when absent).
        /// </summary>
        public string DefaultServerUrl { get; set; } = Services.LocalAdmiralDefaults.Load().Url;

        /// <summary>
        /// Preferences file, or null for <see cref="Services.TuiPaths.PreferencesFile"/>.
        /// </summary>
        public string? PreferencesPath { get; set; } = null;

        /// <summary>
        /// First route after sign-in, or null for the last route (or Ask Armada).
        /// </summary>
        public string? StartRoute { get; set; } = null;

        /// <summary>
        /// Connect the WebSocket and start background polling. Default true (tests may turn it off).
        /// </summary>
        public bool Live { get; set; } = true;

        /// <summary>
        /// Starts a telemetry exporter when the <c>tui.json</c> telemetry settings enable it, or null for none. Helm
        /// supplies one backed by the Admiral's telemetry host; the returned object is disposed when the TUI exits.
        /// </summary>
        public Func<Armada.Core.Settings.TelemetrySettings, IDisposable?>? TelemetryHostFactory { get; set; } = null;

        /// <summary>
        /// Render the shell only when something changed (input, posted work, a resize) or on a slow idle tick, instead
        /// of on every frame (W8.5 idle CPU). Default true. <see cref="ArmadaTuiApp.RunConsoleAsync"/> honors it; hosts
        /// that drive frames themselves (tests) leave the governor off unless they enable <see cref="ArmadaTuiApp.Frames"/>.
        /// </summary>
        public bool AdaptiveFrames { get; set; } = true;

        /// <summary>
        /// Overrides the UTF-8 check used by the Auto glyph mode, or null for <see cref="Theming.TerminalEncoding"/>.
        /// </summary>
        public Func<bool>? Utf8Probe { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TuiStartOptions()
        {
        }

        #endregion
    }
}
