namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// A "service" or "startup" block from publisher.json.
    /// </summary>
    public sealed class PublisherRegistrationView
    {
        /// <summary>
        /// Service name (service blocks only).
        /// </summary>
        public string ServiceName { get; set; } = "";

        /// <summary>
        /// Service display name (service blocks only).
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Arguments the service manager passes (service blocks only).
        /// </summary>
        public string RunArgs { get; set; } = "";

        /// <summary>
        /// Arguments the installer passes to register.
        /// </summary>
        public string InstallArgs { get; set; } = "";

        /// <summary>
        /// Arguments the uninstaller passes to unregister.
        /// </summary>
        public string UninstallArgs { get; set; } = "";
    }
}
