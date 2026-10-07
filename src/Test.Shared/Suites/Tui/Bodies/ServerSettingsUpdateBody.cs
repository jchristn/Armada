namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// The fields of PUT /api/v1/settings that the server settings tests check; nullable so an absent field reads as null.
    /// </summary>
    public class ServerSettingsUpdateBody
    {
        #region Public-Members

        /// <summary>
        /// Maximum captains.
        /// </summary>
        public int? MaxCaptains { get; set; } = null;

        /// <summary>
        /// Global landing mode.
        /// </summary>
        public string? LandingMode { get; set; } = null;

        /// <summary>
        /// Retention group.
        /// </summary>
        public ServerSettingsRetentionBody? Retention { get; set; } = null;

        #endregion
    }
}
