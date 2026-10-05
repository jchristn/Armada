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
    /// identifier. The payload goes to /usr/lib/&lt;binary&gt; with a /usr/bin symlink. Service artifacts register
    /// themselves through their own flags, like the Inno and WiX installers: the after-install script runs
    /// "--install-service" (as root this writes /etc/systemd/system/&lt;service&gt;.service, enables it, and starts it)
    /// and the before-remove script runs "--uninstall-service" on removal (not on upgrade). Both scripts are skipped
    /// when systemd is not running (containers, chroots) and never fail the package transaction.
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

                List<string> scriptArguments = new List<string>();
                ServiceDefinition? service = context.Artifact.Service;
                if (service != null && !string.IsNullOrEmpty(service.InstallArgs))
                {
                    string executable = InstalledExecutable(context, published);
                    string afterInstall = Path.Combine(context.OutputDirectory, "after-install-" + published.RuntimeIdentifier + ".sh");
                    string beforeRemove = Path.Combine(context.OutputDirectory, "before-remove-" + published.RuntimeIdentifier + ".sh");
                    File.WriteAllText(afterInstall, BuildAfterInstallScript(executable, service));
                    File.WriteAllText(beforeRemove, BuildBeforeRemoveScript(executable, service));
                    scriptArguments.AddRange(new List<string> { "--after-install", afterInstall, "--before-remove", beforeRemove });
                }

                BuildPackage(context, stagingDirectory, "deb", architecture, scriptArguments);
                BuildPackage(context, stagingDirectory, "rpm", architecture, scriptArguments);
            }
        }

        #endregion

        #region Private-Methods

        private static string StagePayload(ChannelContext context, PublishedArtifact published)
        {
            string stagingRoot = Path.Combine(context.OutputDirectory, "stage-" + published.RuntimeIdentifier);
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
            string installDir = Path.Combine(stagingRoot, "usr", "lib", context.Artifact.BinaryName);
            Directory.CreateDirectory(installDir);

            // Recursive: the server publish carries the React dashboard in a dashboard/ subdirectory.
            foreach (string file in Directory.EnumerateFiles(published.OutputDirectory, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) continue;
                string destination = Path.Combine(installDir, Path.GetRelativePath(published.OutputDirectory, file));
                string? parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.Copy(file, destination, true);
            }

            // Symlink into PATH (fpm keeps symlinks from a dir source).
            string binDir = Path.Combine(stagingRoot, "usr", "bin");
            Directory.CreateDirectory(binDir);
            File.CreateSymbolicLink(Path.Combine(binDir, context.Artifact.BinaryName), InstalledExecutable(context, published));

            return stagingRoot;
        }

        /// <summary>
        /// Absolute path of the executable once the package is installed.
        /// </summary>
        /// <param name="context">Channel context.</param>
        /// <param name="published">Published runtime output.</param>
        /// <returns>Path under /usr/lib.</returns>
        public static string InstalledExecutable(ChannelContext context, PublishedArtifact published)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (published == null) throw new ArgumentNullException(nameof(published));
            return "/usr/lib/" + context.Artifact.BinaryName + "/" + Path.GetFileName(published.BinaryPath);
        }

        /// <summary>
        /// After-install script for a service artifact. Deb passes "configure &lt;old-version&gt;" and rpm passes the
        /// installed-instance count; on an upgrade the running service is restarted so it picks up the new binary.
        /// </summary>
        /// <param name="executable">Installed executable path.</param>
        /// <param name="service">Service definition.</param>
        /// <returns>POSIX sh script.</returns>
        public static string BuildAfterInstallScript(string executable, ServiceDefinition service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            return "#!/bin/sh\n"
                + "# Register the service through the binary's own flag (same contract as the Windows installers).\n"
                + "if [ ! -d /run/systemd/system ]; then\n"
                + "  echo \"" + service.ServiceName + ": systemd is not running; skipped service registration. Run '" + executable + " " + service.InstallArgs + "' later.\" >&2\n"
                + "  exit 0\n"
                + "fi\n"
                + "\"" + executable + "\" " + service.InstallArgs + " || echo \"" + service.ServiceName + ": service registration failed; retry with 'sudo " + executable + " " + service.InstallArgs + "'\" >&2\n"
                + "UPGRADE=0\n"
                + "if [ \"$1\" = \"configure\" ] && [ -n \"$2\" ]; then UPGRADE=1; fi\n"
                + "if [ \"$1\" -ge 2 ] 2>/dev/null; then UPGRADE=1; fi\n"
                + "if [ \"$UPGRADE\" = \"1\" ]; then systemctl try-restart " + service.ServiceName + ".service || true; fi\n"
                + "exit 0\n";
        }

        /// <summary>
        /// Before-remove script for a service artifact: unregisters on removal only (deb "remove"/"purge", rpm "0"),
        /// never on upgrade.
        /// </summary>
        /// <param name="executable">Installed executable path.</param>
        /// <param name="service">Service definition.</param>
        /// <returns>POSIX sh script.</returns>
        public static string BuildBeforeRemoveScript(string executable, ServiceDefinition service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            return "#!/bin/sh\n"
                + "case \"$1\" in\n"
                + "  remove|purge|0)\n"
                + "    if [ -d /run/systemd/system ]; then\n"
                + "      \"" + executable + "\" " + service.UninstallArgs + " || true\n"
                + "    fi\n"
                + "    ;;\n"
                + "esac\n"
                + "exit 0\n";
        }

        private static void BuildPackage(ChannelContext context, string stagingDirectory, string outputType, string architecture, List<string> scriptArguments)
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
            foreach (string dependency in Dependencies(outputType))
            {
                arguments.Add("-d");
                arguments.Add(dependency);
            }

            arguments.AddRange(scriptArguments);
            ProcessRunner.Run("fpm", arguments, context.RepoRoot);
        }

        /// <summary>
        /// Package dependencies for a self-contained .NET program that drives git. The publish carries the .NET
        /// runtime but not the native libraries it loads (ICU for globalization, OpenSSL for TLS), so a clean
        /// machine without them aborts at startup; git is required for vessels, docks, and landing. Debian
        /// package names differ per release, hence the alternatives (newest first).
        /// </summary>
        /// <param name="outputType">fpm output type: deb or rpm.</param>
        /// <returns>Dependency expressions, one per -d argument.</returns>
        public static List<string> Dependencies(string outputType)
        {
            if (string.Equals(outputType, "rpm", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "libicu", "openssl-libs", "ca-certificates", "tzdata", "git" };
            }

            return new List<string>
            {
                "libicu78 | libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66",
                "libssl3t64 | libssl3 | libssl1.1",
                "ca-certificates",
                "tzdata",
                "git"
            };
        }

        #endregion
    }
}
