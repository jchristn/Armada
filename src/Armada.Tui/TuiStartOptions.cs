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
        /// Server used when nothing else names one (Helm passes its configured Admiral URL). Default
        /// <c>http://127.0.0.1:7890</c>.
        /// </summary>
        public string DefaultServerUrl { get; set; } = "http://127.0.0.1:7890";

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
