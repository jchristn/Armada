namespace Armada.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Publisher.Build;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// Builds .deb and .rpm packages from one staging directory with fpm, for each Linux runtime
    /// identifier. Service artifacts also carry a systemd unit inside the package.
    /// </summary>
    public class DebRpmChannel : IChannel
    {
        #region Public-Members

        /// <inheritdoc />
        public ChannelKindEnum Kind
        {
            get { return ChannelKindEnum.DebRpm; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IEnumerable<ToolRequirement> Requirements()
        {
            return new List<ToolRequirement>
            {
                new ToolRequirement("fpm", "sudo gem install --no-document fpm", "linux")
            };
        }

        /// <inheritdoc />
        public void Execute(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("fpm packaging runs on Linux only.");

            Directory.CreateDirectory(context.OutputDirectory);

            foreach (PublishedArtifact published in context.Published)
            {
                if (!published.RuntimeIdentifier.StartsWith("linux", StringComparison.OrdinalIgnoreCase)) continue;

                string stagingDirectory = StagePayload(context, published);
                string architecture = published.RuntimeIdentifier.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "amd64";

                BuildPackage(context, stagingDirectory, "deb", architecture);
                BuildPackage(context, stagingDirectory, "rpm", architecture);
            }
        }

        #endregion

        #region Private-Methods

        private static string StagePayload(ChannelContext context, PublishedArtifact published)
        {
            string stagingRoot = Path.Combine(context.OutputDirectory, "stage-" + published.RuntimeIdentifier);
            string installDir = Path.Combine(stagingRoot, "usr", "lib", context.Artifact.BinaryName);
            Directory.CreateDirectory(installDir);

            foreach (string file in Directory.EnumerateFiles(published.OutputDirectory))
            {
                if (file.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(installDir, Path.GetFileName(file)), true);
            }

            // Symlink into PATH for CLIs.
            string binDir = Path.Combine(stagingRoot, "usr", "bin");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(
                Path.Combine(binDir, context.Artifact.BinaryName + ".link"),
                "/usr/lib/" + context.Artifact.BinaryName + "/" + Path.GetFileName(published.BinaryPath));

            if (context.Artifact.Service != null)
            {
                WriteSystemdUnit(context, stagingRoot);
            }

            return stagingRoot;
        }

        private static void WriteSystemdUnit(ChannelContext context, string stagingRoot)
        {
            string unitDirectory = Path.Combine(stagingRoot, "lib", "systemd", "system");
            Directory.CreateDirectory(unitDirectory);

            string binaryPath = "/usr/lib/" + context.Artifact.BinaryName + "/" + context.Artifact.BinaryName;
            string unit =
                "[Unit]\n"
                + "Description=" + context.Artifact.DisplayName + "\n"
                + "After=network.target\n\n"
                + "[Service]\n"
                + "ExecStart=" + binaryPath + " " + context.Artifact.Service!.RunArgs + "\n"
                + "Restart=on-failure\n\n"
                + "[Install]\n"
                + "WantedBy=multi-user.target\n";

            File.WriteAllText(Path.Combine(unitDirectory, context.Artifact.Service!.ServiceName + ".service"), unit);
        }

        private static void BuildPackage(ChannelContext context, string stagingDirectory, string outputType, string architecture)
        {
            string packageName = context.Artifact.BinaryName;
            List<string> arguments = new List<string>
            {
                "-s", "dir",
                "-t", outputType,
                "-n", packageName,
                "-v", context.Version,
                "-a", architecture,
                "--description", context.Artifact.Description,
                "--url", context.Manifest.Product.Homepage,
                "--vendor", context.Manifest.Product.Publisher,
                "-C", stagingDirectory,
                "-p", context.OutputDirectory,
                "--force"
            };
            ProcessRunner.Run("fpm", arguments, context.RepoRoot);
        }

        #endregion
    }
}
