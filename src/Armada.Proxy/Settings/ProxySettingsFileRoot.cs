namespace Armada.Proxy.Settings
{
    /// <summary>
    /// Root of a proxy settings file: either the settings directly at the root, or nested under "ArmadaProxy"
    /// (which takes precedence when present).
    /// </summary>
    public class ProxySettingsFileRoot : ProxySettingsFile
    {
        #region Public-Members

        /// <summary>
        /// Optional nested proxy section.
        /// </summary>
        public ProxySettingsFile? ArmadaProxy { get; set; } = null;

        #endregion
    }
}
