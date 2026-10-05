namespace Armada.Tui.Services
{
    /// <summary>
    /// The few fields of the local Admiral's <c>settings.json</c> the login screen uses for localhost defaults.
    /// </summary>
    public class LocalAdmiralSettingsFile
    {
        #region Public-Members

        /// <summary>
        /// REST port, or null when absent.
        /// </summary>
        public int? AdmiralPort { get; set; } = null;

        /// <summary>
        /// Admin API key, or null when absent.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        #endregion
    }
}
