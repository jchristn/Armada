namespace Armada.Publisher.Manifest
{
    /// <summary>
    /// One buildable deliverable in the solution (a CLI, a tray app, or a service) together with the
    /// metadata every channel needs to package it.
    /// </summary>
    public class ArtifactDefinition
    {
        #region Public-Members

        /// <summary>
        /// Stable identifier referenced by channels (for example "helm", "harbor", "server").
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Runtime shape of the artifact.
        /// </summary>
        public ArtifactKindEnum Kind { get; set; } = ArtifactKindEnum.Cli;

        /// <summary>
        /// Repo-relative path to the project file that produces the artifact.
        /// </summary>
        public string Project { get; set; } = string.Empty;

        /// <summary>
        /// Base name of the produced binary, without extension (for example "armada").
        /// </summary>
        public string BinaryName { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable name shown in installers and package listings.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// One-line description used by package metadata.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// NuGet package identifier, used by the NuGet channel and to derive winget/choco ids. Optional.
        /// </summary>
        public string? PackageId { get; set; } = null;

        /// <summary>
        /// Repo-relative path to an icon (.ico) used by installers and bundle assembly. Optional.
        /// </summary>
        public string? Icon { get; set; } = null;

        /// <summary>
        /// Autostart registration for tray artifacts. Null for CLIs and services.
        /// </summary>
        public StartupDefinition? Startup { get; set; } = null;

        /// <summary>
        /// System-service registration for daemon artifacts. Null for CLIs and tray apps.
        /// </summary>
        public ServiceDefinition? Service { get; set; } = null;

        #endregion
    }
}
