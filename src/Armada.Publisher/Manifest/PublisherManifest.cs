namespace Armada.Publisher.Manifest
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Deserialized publisher.json: the single source of truth for a release, minus the version string,
    /// which is supplied per release on the command line.
    /// </summary>
    public class PublisherManifest
    {
        #region Public-Members

        /// <summary>
        /// Product-level identity.
        /// </summary>
        public ProductInfo Product { get; set; } = new ProductInfo();

        /// <summary>
        /// Target framework moniker used for the self-contained publish (for example "net10.0").
        /// </summary>
        public string Framework { get; set; } = "net10.0";

        /// <summary>
        /// The full runtime-identifier matrix a release may target.
        /// </summary>
        public List<string> RuntimeIdentifiers { get; set; } = new List<string>();

        /// <summary>
        /// Buildable artifacts (CLIs, tray apps, services).
        /// </summary>
        public List<ArtifactDefinition> Artifacts { get; set; } = new List<ArtifactDefinition>();

        /// <summary>
        /// Packaging channels bound to artifacts.
        /// </summary>
        public List<ChannelDefinition> Channels { get; set; } = new List<ChannelDefinition>();

        /// <summary>
        /// Per-operating-system signing configuration.
        /// </summary>
        public SigningSettings Signing { get; set; } = new SigningSettings();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the artifact a channel targets.
        /// </summary>
        /// <param name="channel">Channel definition.</param>
        /// <returns>The matching artifact.</returns>
        public ArtifactDefinition ResolveArtifact(ChannelDefinition channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));

            ArtifactDefinition? artifact = Artifacts.FirstOrDefault(a => string.Equals(a.Id, channel.Artifact, StringComparison.OrdinalIgnoreCase));
            if (artifact == null) throw new InvalidOperationException("Channel '" + channel.Name + "' references unknown artifact '" + channel.Artifact + "'.");
            return artifact;
        }

        /// <summary>
        /// Find a channel by name.
        /// </summary>
        /// <param name="name">Channel name.</param>
        /// <returns>The matching channel, or null when not found.</returns>
        public ChannelDefinition? FindChannel(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Channels.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Return the runtime identifiers a channel targets, defaulting to the manifest-wide matrix.
        /// </summary>
        /// <param name="channel">Channel definition.</param>
        /// <returns>Effective runtime identifiers.</returns>
        public List<string> EffectiveRuntimes(ChannelDefinition channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (channel.Runtimes != null && channel.Runtimes.Count > 0) return channel.Runtimes;
            return RuntimeIdentifiers;
        }

        /// <summary>
        /// Validate cross-references and required fields, throwing on the first problem found.
        /// </summary>
        public void Validate()
        {
            if (Artifacts.Count == 0) throw new InvalidOperationException("Manifest declares no artifacts.");
            if (Channels.Count == 0) throw new InvalidOperationException("Manifest declares no channels.");

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ArtifactDefinition artifact in Artifacts)
            {
                if (string.IsNullOrEmpty(artifact.Id)) throw new InvalidOperationException("An artifact is missing its id.");
                if (!ids.Add(artifact.Id)) throw new InvalidOperationException("Duplicate artifact id '" + artifact.Id + "'.");
                if (string.IsNullOrEmpty(artifact.Project)) throw new InvalidOperationException("Artifact '" + artifact.Id + "' is missing its project path.");
            }

            HashSet<string> channelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ChannelDefinition channel in Channels)
            {
                if (string.IsNullOrEmpty(channel.Name)) throw new InvalidOperationException("A channel is missing its name.");
                if (!channelNames.Add(channel.Name)) throw new InvalidOperationException("Duplicate channel name '" + channel.Name + "'.");
                ResolveArtifact(channel);
            }
        }

        #endregion
    }
}
