namespace Armada.Publisher.Manifest
{
    /// <summary>
    /// Describes how a tray or GUI artifact registers itself to autostart. The installer invokes the
    /// artifact's own startup-registration arguments rather than reimplementing per-OS logic.
    /// </summary>
    public class StartupDefinition
    {
        #region Public-Members

        /// <summary>
        /// Registration scope: "User" for a per-user autostart entry, "System" for machine-wide.
        /// </summary>
        public string Scope { get; set; } = "User";

        /// <summary>
        /// Arguments the installer passes to the binary to register autostart
        /// (for example "--install-startup").
        /// </summary>
        public string InstallArgs { get; set; } = string.Empty;

        /// <summary>
        /// Arguments the uninstaller passes to the binary to remove autostart
        /// (for example "--uninstall-startup").
        /// </summary>
        public string UninstallArgs { get; set; } = string.Empty;

        #endregion
    }
}
