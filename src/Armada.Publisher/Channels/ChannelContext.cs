namespace Armada.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using Armada.Publisher.Build;
    using Armada.Publisher.Manifest;

    /// <summary>
    /// Everything a channel needs to package one artifact: the manifest, the artifact and channel
    /// definitions, the release version, the repository root, and the output directory.
    /// </summary>
    public class ChannelContext
    {
        #region Public-Members

        /// <summary>
        /// The loaded manifest.
        /// </summary>
        public PublisherManifest Manifest { get; set; }

        /// <summary>
        /// The channel being executed.
        /// </summary>
        public ChannelDefinition Channel { get; set; }

        /// <summary>
        /// The artifact the channel targets.
        /// </summary>
        public ArtifactDefinition Artifact { get; set; }

        /// <summary>
        /// Release version string supplied per release.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Absolute repository root.
        /// </summary>
        public string RepoRoot { get; set; }

        /// <summary>
        /// Directory into which finished packages are written.
        /// </summary>
        public string OutputDirectory { get; set; }

        /// <summary>
        /// Self-contained publishes the runner produced for this channel's effective runtimes, one per RID.
        /// </summary>
        public List<PublishedArtifact> Published { get; set; } = new List<PublishedArtifact>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a channel context.
        /// </summary>
        /// <param name="manifest">Loaded manifest.</param>
        /// <param name="channel">Channel to execute.</param>
        /// <param name="artifact">Target artifact.</param>
        /// <param name="version">Release version.</param>
        /// <param name="repoRoot">Repository root.</param>
        /// <param name="outputDirectory">Output directory.</param>
        public ChannelContext(PublisherManifest manifest, ChannelDefinition channel, ArtifactDefinition artifact, string version, string repoRoot, string outputDirectory)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            RepoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
            OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        }

        #endregion
    }
}
