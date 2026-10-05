namespace Armada.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Armada.Publisher.Build;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// Builds a macOS installer package (.pkg) per osx runtime identifier with pkgbuild and productbuild.
    ///
    /// Payload layout:
    /// <list type="bullet">
    /// <item>/usr/local/lib/&lt;binary&gt;/ - the self-contained publish plus an uninstall.sh helper.</item>
    /// <item>/usr/local/bin/&lt;binary&gt; - symlink to the executable.</item>
    /// </list>
    /// Service artifacts register themselves through their own flags, like the Windows and Linux installers: the
    /// postinstall script runs "&lt;binary&gt; --install-service" as root, which writes
    /// /Library/LaunchAgents/&lt;bundle id&gt;.plist (so the server runs in each user's session, where captains have the
    /// user's home directory, git credentials, and agent CLI logins) and loads it for the console user; uninstall.sh
    /// runs "--uninstall-service". The preinstall script stops a previously loaded agent so the binary can be replaced.
    /// The package is signed and notarized when credentials are present.
    /// </summary>
    public class PkgChannel : IChannel
    {
        #region Public-Members

        /// <inheritdoc />
        public ChannelKindEnum Kind
        {
            get { return ChannelKindEnum.Pkg; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IEnumerable<ToolRequirement> Requirements()
        {
            return new List<ToolRequirement>
            {
                new ToolRequirement("pkgbuild", "Install Xcode command-line tools: xcode-select --install", "osx"),
                new ToolRequirement("productbuild", "Install Xcode command-line tools: xcode-select --install", "osx"),
                new ToolRequirement("codesign", "Install Xcode command-line tools: xcode-select --install", "osx"),
                new ToolRequirement("xcrun", "Install Xcode command-line tools: xcode-select --install (provides notarytool and stapler)", "osx")
            };
        }

        /// <inheritdoc />
        public void Execute(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("pkgbuild and productbuild run on macOS only.");

            Directory.CreateDirectory(context.OutputDirectory);
            string workRoot = Path.Combine(context.OutputDirectory, "_work");
            Directory.CreateDirectory(workRoot);

            string identifier = context.Artifact.ResolveBundleIdentifier();
            string binaryName = context.Artifact.BinaryName;
            string installDirectory = "/usr/local/lib/" + binaryName;

            using (MacSigningSession signing = new MacSigningSession(context.Manifest.Signing.Macos, workRoot, "[pkg]"))
            {
                string entitlements = MacBundleBuilder.WriteEntitlements(Path.Combine(workRoot, "entitlements.plist"));

                foreach (PublishedArtifact published in context.Published)
                {
                    if (!published.RuntimeIdentifier.StartsWith("osx", StringComparison.OrdinalIgnoreCase)) continue;

                    string runtimeWork = Path.Combine(workRoot, published.RuntimeIdentifier);
                    if (Directory.Exists(runtimeWork)) Directory.Delete(runtimeWork, true);
                    string root = Path.Combine(runtimeWork, "root");
                    string scripts = Path.Combine(runtimeWork, "scripts");
                    Directory.CreateDirectory(root);
                    Directory.CreateDirectory(scripts);

                    string executableName = Path.GetFileName(published.BinaryPath);
                    string libDirectory = Path.Combine(root, "usr", "local", "lib", binaryName);
                    StagePayload(published.OutputDirectory, libDirectory);
                    string stagedExecutable = Path.Combine(libDirectory, executableName);
                    MakeExecutable(stagedExecutable);
                    signing.Sign(stagedExecutable, entitlements);

                    string binDirectory = Path.Combine(root, "usr", "local", "bin");
                    Directory.CreateDirectory(binDirectory);
                    File.CreateSymbolicLink(Path.Combine(binDirectory, binaryName), installDirectory + "/" + executableName);

                    // The agent plist is not part of the payload: the binary's --install-service writes it (same label and
                    // content the payload used to carry), so there is one definition for the .pkg and a manual install.
                    ServiceDefinition? service = context.Artifact.Service;
                    bool hasAgent = service != null && !string.IsNullOrEmpty(service.InstallArgs);
                    string agentPlistPath = "/Library/LaunchAgents/" + identifier + ".plist";
                    string installedExecutable = installDirectory + "/" + executableName;

                    WriteScript(Path.Combine(libDirectory, "uninstall.sh"), BuildUninstallScript(identifier, binaryName, installDirectory, hasAgent ? agentPlistPath : string.Empty, installedExecutable, hasAgent ? service!.UninstallArgs : string.Empty));
                    WriteScript(Path.Combine(scripts, "preinstall"), BuildPreinstallScript(identifier, hasAgent ? agentPlistPath : string.Empty));
                    WriteScript(Path.Combine(scripts, "postinstall"), BuildPostinstallScript(hasAgent ? installedExecutable : string.Empty, hasAgent ? service!.InstallArgs : string.Empty));

                    string componentPath = Path.Combine(runtimeWork, binaryName + "-component.pkg");
                    ProcessRunner.Run("pkgbuild", new List<string>
                    {
                        "--root", root,
                        "--scripts", scripts,
                        "--identifier", identifier,
                        "--version", VersionText.NumericCore(context.Version),
                        "--install-location", "/",
                        "--ownership", "recommended",
                        componentPath
                    });

                    string pkgPath = Path.Combine(context.OutputDirectory, binaryName + "-" + context.Version + "-" + published.RuntimeIdentifier + ".pkg");
                    if (File.Exists(pkgPath)) File.Delete(pkgPath);

                    List<string> productArguments = new List<string>
                    {
                        "--identifier", identifier,
                        "--version", VersionText.NumericCore(context.Version),
                        "--package", componentPath
                    };
                    signing.AddInstallerSigning(productArguments);
                    productArguments.Add(pkgPath);
                    ProcessRunner.Run("productbuild", productArguments);

                    if (!string.IsNullOrEmpty(signing.InstallerIdentity)) signing.NotarizeAndStaple(pkgPath);
                    Console.WriteLine("[pkg] " + pkgPath);
                }
            }

            Directory.Delete(workRoot, true);
        }

        #endregion

        #region Private-Methods

        private static void StagePayload(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) continue;
                if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;

                string destination = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
                string? parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.Copy(file, destination, true);
            }
        }

        private static void MakeExecutable(string path)
        {
            if (OperatingSystem.IsWindows()) return;
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        private static void WriteScript(string path, string content)
        {
            File.WriteAllText(path, content.Replace("\r\n", "\n"));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }

        private static string BuildPreinstallScript(string label, string agentPlistPath)
        {
            StringBuilder script = new StringBuilder();
            script.Append("#!/bin/sh\n");
            script.Append("# Stop a previously installed agent so the binary can be replaced.\n");
            if (!string.IsNullOrEmpty(agentPlistPath))
            {
                script.Append("CONSOLE_USER=$(stat -f%Su /dev/console 2>/dev/null)\n");
                script.Append("if [ -n \"$CONSOLE_USER\" ] && [ \"$CONSOLE_USER\" != \"root\" ]; then\n");
                script.Append("  CONSOLE_UID=$(id -u \"$CONSOLE_USER\")\n");
                script.Append("  launchctl bootout \"gui/$CONSOLE_UID/").Append(label).Append("\" >/dev/null 2>&1 || true\n");
                script.Append("fi\n");
            }
            script.Append("exit 0\n");
            return script.ToString();
        }

        private static string BuildPostinstallScript(string executable, string installArgs)
        {
            StringBuilder script = new StringBuilder();
            script.Append("#!/bin/sh\n");
            if (!string.IsNullOrEmpty(executable))
            {
                script.Append("# Register the launchd agent through the binary's own flag. As root it writes /Library/LaunchAgents and\n");
                script.Append("# loads the agent for the logged-in user; launchd loads it for every user at login after this.\n");
                script.Append("\"").Append(executable).Append("\" ").Append(installArgs).Append(" || echo \"service registration failed; run: sudo ")
                    .Append(executable).Append(' ').Append(installArgs).Append("\" >&2\n");
            }
            script.Append("exit 0\n");
            return script.ToString();
        }

        private static string BuildUninstallScript(string label, string binaryName, string installDirectory, string agentPlistPath, string executable, string uninstallArgs)
        {
            StringBuilder script = new StringBuilder();
            script.Append("#!/bin/sh\n");
            script.Append("# Removes what the ").Append(binaryName).Append(" package installed. Run with sudo.\n");
            script.Append("# User data (for example ~/.armada) is left in place.\n");
            script.Append("set -e\n");
            script.Append("if [ \"$(id -u)\" -ne 0 ]; then echo \"Run with sudo: sudo $0\"; exit 1; fi\n");
            if (!string.IsNullOrEmpty(agentPlistPath))
            {
                script.Append("\"").Append(executable).Append("\" ").Append(uninstallArgs).Append(" || true\n");
                script.Append("rm -f \"").Append(agentPlistPath).Append("\"\n");
            }
            script.Append("rm -f \"/usr/local/bin/").Append(binaryName).Append("\"\n");
            script.Append("rm -rf \"").Append(installDirectory).Append("\"\n");
            script.Append("pkgutil --forget \"").Append(label).Append("\" >/dev/null 2>&1 || true\n");
            script.Append("echo \"Removed ").Append(binaryName).Append(".\"\n");
            return script.ToString();
        }

        #endregion
    }
}
