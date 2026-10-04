namespace Armada.Publisher
{
    using System;
    using System.IO;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// Entry point for the packaging orchestrator.
    ///
    /// Usage:
    ///   Armada.Publisher doctor [--manifest publisher.json]
    ///   Armada.Publisher --channel &lt;name&gt; --version &lt;x.y.z&gt; [--manifest publisher.json] [--output artifacts]
    ///   Armada.Publisher --all --version &lt;x.y.z&gt; [--manifest publisher.json] [--output artifacts]
    ///   Armada.Publisher list [--manifest publisher.json]
    ///   Armada.Publisher checksums --dir installers/&lt;version&gt;
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Application entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static int Main(string[] args)
        {
            try
            {
                CliOptions options = CliOptions.Parse(args);
                string manifestPath = Path.GetFullPath(options.ManifestPath);
                string repoRoot = Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory();
                PublisherManifest manifest = ManifestLoader.Load(manifestPath);

                switch (options.Command)
                {
                    case CliCommandEnum.Doctor:
                        return new Doctor(manifest).Report() ? 0 : 1;

                    case CliCommandEnum.List:
                        ListChannels(manifest);
                        return 0;

                    case CliCommandEnum.Channel:
                        RequireVersion(options);
                        new ChannelRunner(manifest, repoRoot, options.Version, ResolveOutput(repoRoot, options)).RunChannel(options.ChannelName);
                        return 0;

                    case CliCommandEnum.Checksums:
                        if (string.IsNullOrEmpty(options.Directory)) throw new InvalidOperationException("checksums requires --dir <directory>.");
                        string? sums = Armada.Publisher.Build.ChecksumWriter.WriteManifest(Path.GetFullPath(options.Directory));
                        Console.WriteLine(sums == null ? "No release files found in " + options.Directory : "Wrote " + sums);
                        return 0;

                    case CliCommandEnum.All:
                        RequireVersion(options);
                        new ChannelRunner(manifest, repoRoot, options.Version, ResolveOutput(repoRoot, options)).RunAll();
                        return 0;

                    default:
                        CliOptions.PrintUsage();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 1;
            }
        }

        #endregion

        #region Private-Methods

        private static void RequireVersion(CliOptions options)
        {
            if (string.IsNullOrEmpty(options.Version))
            {
                throw new InvalidOperationException("--version is required for packaging (a release is the manifest replayed against a version string).");
            }
        }

        private static string ResolveOutput(string repoRoot, CliOptions options)
        {
            string output = string.IsNullOrEmpty(options.OutputDirectory) ? Path.Combine(repoRoot, "artifacts") : options.OutputDirectory;
            Directory.CreateDirectory(output);
            return output;
        }

        private static void ListChannels(PublisherManifest manifest)
        {
            Console.WriteLine("Channels declared in the manifest:");
            foreach (ChannelDefinition channel in manifest.Channels)
            {
                Console.WriteLine("  " + channel.Name.PadRight(16) + (channel.Enabled ? "[enabled] " : "[disabled] ") + channel.Kind + " -> " + channel.Artifact);
            }
        }

        #endregion
    }
}
