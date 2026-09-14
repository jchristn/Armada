namespace Armada.Publisher.Manifest
{
    /// <summary>
    /// Per-operating-system signing configuration. Each nested block references secret names only.
    /// </summary>
    public class SigningSettings
    {
        #region Public-Members

        /// <summary>
        /// Windows Authenticode signing configuration.
        /// </summary>
        public WindowsSigningSettings Windows { get; set; } = new WindowsSigningSettings();

        /// <summary>
        /// macOS codesign and notarization configuration.
        /// </summary>
        public MacosSigningSettings Macos { get; set; } = new MacosSigningSettings();

        /// <summary>
        /// Linux repository-signing configuration.
        /// </summary>
        public LinuxSigningSettings Linux { get; set; } = new LinuxSigningSettings();

        #endregion
    }
}
