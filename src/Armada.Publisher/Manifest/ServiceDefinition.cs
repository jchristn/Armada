namespace Armada.Publisher.Manifest
{
    /// <summary>
    /// Describes how a daemon artifact registers as a system service (Windows Service, systemd unit,
    /// or macOS LaunchDaemon). The installer invokes the binary's own service-registration arguments.
    /// </summary>
    public class ServiceDefinition
    {
        #region Public-Members

        /// <summary>
        /// Registration scope: "System" for a machine-wide daemon, "User" for a per-user daemon.
        /// </summary>
        public string Scope { get; set; } = "System";

        /// <summary>
        /// Short service identifier used by the service manager (for example "armada").
        /// </summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable service display name.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Arguments passed to the binary when the service manager starts it (for example "--run-service").
        /// </summary>
        public string RunArgs { get; set; } = string.Empty;

        /// <summary>
        /// Arguments the installer passes to register the service (for example "--install-service").
        /// </summary>
        public string InstallArgs { get; set; } = string.Empty;

        /// <summary>
        /// Arguments the uninstaller passes to deregister the service (for example "--uninstall-service").
        /// </summary>
        public string UninstallArgs { get; set; } = string.Empty;

        #endregion
    }
}
