namespace Armada.Core.Hosting
{
    /// <summary>
    /// The two settings-file values a desktop tool needs from the local Admiral (where its logs are and its API key),
    /// read leniently when the whole file does not pass <see cref="Armada.Core.Settings.ArmadaSettings"/> validation.
    /// </summary>
    public class LocalAdmiralSettingsHints
    {
        #region Public-Members

        /// <summary>
        /// Log directory, or null when not set.
        /// </summary>
        public string? LogDirectory { get; set; } = null;

        /// <summary>
        /// Local API key, or null when not set.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        #endregion
    }
}
