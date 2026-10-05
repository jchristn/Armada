namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One artifact from publisher.json (identity plus its service or startup registration block).
    /// </summary>
    public sealed class PublisherArtifactView
    {
        /// <summary>
        /// Artifact id, for example "server".
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// macOS bundle identifier (also the launchd label).
        /// </summary>
        public string BundleIdentifier { get; set; } = "";

        /// <summary>
        /// Service block for daemon artifacts.
        /// </summary>
        public PublisherRegistrationView? Service { get; set; } = null;

        /// <summary>
        /// Startup block for tray artifacts.
        /// </summary>
        public PublisherRegistrationView? Startup { get; set; } = null;
    }
}
