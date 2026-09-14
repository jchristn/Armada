namespace Armada.Publisher.Manifest
{
    using System.Collections.Generic;

    /// <summary>
    /// One packaging step: a channel kind bound to an artifact, optionally scoped to a runtime subset,
    /// with the channel-specific configuration and the name of the secret it consumes.
    /// </summary>
    public class ChannelDefinition
    {
        #region Public-Members

        /// <summary>
        /// Unique channel name selectable with "--channel" (for example "inno-harbor").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The packaging implementation this channel uses.
        /// </summary>
        public ChannelKindEnum Kind { get; set; } = ChannelKindEnum.NuGet;

        /// <summary>
        /// Identifier of the artifact this channel packages.
        /// </summary>
        public string Artifact { get; set; } = string.Empty;

        /// <summary>
        /// Whether the channel participates in a release. Disabled channels are skipped.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Runtime identifiers this channel targets. Empty means the manifest-wide matrix.
        /// </summary>
        public List<string> Runtimes { get; set; } = new List<string>();

        /// <summary>
        /// Name of the GitHub Actions secret / local credential this channel reads. Optional.
        /// </summary>
        public string? SecretName { get; set; } = null;

        /// <summary>
        /// Homebrew tap repository (owner/repo) for Homebrew channels. Optional.
        /// </summary>
        public string? Tap { get; set; } = null;

        /// <summary>
        /// Homebrew delivery type: "formula" for CLIs, "cask" for GUI apps. Optional.
        /// </summary>
        public string? FormulaType { get; set; } = null;

        /// <summary>
        /// Scoop bucket repository (owner/repo). Optional.
        /// </summary>
        public string? Bucket { get; set; } = null;

        /// <summary>
        /// winget package identifier (Publisher.Package). Optional.
        /// </summary>
        public string? PackageId { get; set; } = null;

        #endregion
    }
}
