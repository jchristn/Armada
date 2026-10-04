namespace Armada.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Publisher.Build;
    using Armada.Publisher.Channels;
    using Armada.Publisher.Manifest;

    /// <summary>
    /// Drives a single channel end to end: self-contained publish for its effective runtimes, then the
    /// channel's packaging (and submission, where the channel submits).
    /// </summary>
    public class ChannelRunner
    {
        #region Private-Members

        private readonly PublisherManifest _Manifest;
        private readonly string _RepoRoot;
        private readonly string _Version;
        private readonly string _OutputRoot;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the runner.
        /// </summary>
        /// <param name="manifest">Loaded manifest.</param>
        /// <param name="repoRoot">Absolute repository root.</param>
        /// <param name="version">Release version string.</param>
        /// <param name="outputRoot">Directory under which publishes and packages are written.</param>
        public ChannelRunner(PublisherManifest manifest, string repoRoot, string version, string outputRoot)
        {
            _Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _RepoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
            _Version = version ?? throw new ArgumentNullException(nameof(version));
            _OutputRoot = outputRoot ?? throw new ArgumentNullException(nameof(outputRoot));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one channel by name.
        /// </summary>
        /// <param name="channelName">Channel name from the manifest.</param>
        public void RunChannel(string channelName)
        {
            ChannelDefinition? channel = _Manifest.FindChannel(channelName);
            if (channel == null) throw new InvalidOperationException("No channel named '" + channelName + "' in the manifest.");
            if (!channel.Enabled)
            {
                Console.WriteLine("[skip] channel '" + channel.Name + "' is disabled in the manifest.");
                return;
            }
            RunChannel(channel);
        }

        /// <summary>
        /// Run every enabled channel in the manifest.
        /// </summary>
        public void RunAll()
        {
            foreach (ChannelDefinition channel in _Manifest.Channels)
            {
                if (!channel.Enabled)
                {
                    Console.WriteLine("[skip] channel '" + channel.Name + "' is disabled.");
                    continue;
                }
                RunChannel(channel);
            }
        }

        #endregion

        #region Private-Methods

        private void RunChannel(ChannelDefinition channel)
        {
            ArtifactDefinition artifact = _Manifest.ResolveArtifact(channel);
            Console.WriteLine("=== channel '" + channel.Name + "' (" + channel.Kind + ") artifact '" + artifact.Id + "' ===");

            string outputDirectory = Path.Combine(_OutputRoot, "packages", channel.Name);
            Directory.CreateDirectory(outputDirectory);

            List<PublishedArtifact> published = new List<PublishedArtifact>();
            if (RequiresSelfContainedPublish(channel.Kind))
            {
                SelfContainedPublisher publisher = new SelfContainedPublisher(_Manifest, _RepoRoot, _Version, Path.Combine(_OutputRoot, "publish"));
                foreach (string runtimeIdentifier in _Manifest.EffectiveRuntimes(channel))
                {
                    published.Add(publisher.Publish(artifact, runtimeIdentifier));
                }
            }

            ChannelContext context = new ChannelContext(_Manifest, channel, artifact, _Version, _RepoRoot, outputDirectory)
            {
                Published = published
            };

            IChannel implementation = ChannelFactory.Create(channel.Kind);
            implementation.Execute(context);
            WriteChecksums(outputDirectory);
            Console.WriteLine("[done] channel '" + channel.Name + "' -> " + outputDirectory);
        }

        private static void WriteChecksums(string outputDirectory)
        {
            // A sidecar per package for package-manager manifests, plus SHA256SUMS for the directory.
            foreach (string file in Directory.EnumerateFiles(outputDirectory))
            {
                if (ChecksumWriter.IsReleaseFile(file)) ChecksumWriter.WriteSidecar(file);
            }

            string? manifest = ChecksumWriter.WriteManifest(outputDirectory);
            if (manifest != null) Console.WriteLine("[checksums] " + manifest);
        }

        private static bool RequiresSelfContainedPublish(ChannelKindEnum kind)
        {
            // NuGet packs the framework-dependent tool itself; every other channel consumes self-contained binaries.
            return kind != ChannelKindEnum.NuGet;
        }

        #endregion
    }
}
