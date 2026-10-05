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
    /// Builds a signed Windows Inno Setup installer (.exe) per Windows runtime identifier. For tray and
    /// service artifacts it wires the artifact's own startup/service registration into the install step.
    /// </summary>
    public class InnoChannel : IChannel
    {
        #region Public-Members

        /// <inheritdoc />
        public ChannelKindEnum Kind
        {
            get { return ChannelKindEnum.Inno; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IEnumerable<ToolRequirement> Requirements()
        {
            return new List<ToolRequirement>
            {
                new ToolRequirement("iscc", "choco install innosetup -y", "win"),
                new ToolRequirement("signtool", "Install the Windows SDK (ships on windows-latest); needed to Authenticode-sign the installer.", "win")
            };
        }

        /// <inheritdoc />
        public void Execute(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Inno Setup runs on Windows only.");

            Directory.CreateDirectory(context.OutputDirectory);

            foreach (PublishedArtifact published in context.Published)
            {
                if (!published.RuntimeIdentifier.StartsWith("win", StringComparison.OrdinalIgnoreCase)) continue;

                string scriptPath = WriteInnoScript(context, published);
                ProcessRunner.Run("iscc", new List<string> { scriptPath }, context.RepoRoot);
            }

            SignProducedInstallers(context);
        }

        #endregion

        #region Private-Methods

        private static string WriteInnoScript(ChannelContext context, PublishedArtifact published)
        {
            string appName = context.Artifact.DisplayName;
            string binaryFile = Path.GetFileName(published.BinaryPath);
            string outputBaseName = context.Artifact.BinaryName + "-" + context.Version + "-" + published.RuntimeIdentifier;

            StringBuilder script = new StringBuilder();
            script.AppendLine("[Setup]");
            script.AppendLine("AppName=" + appName);
            script.AppendLine("AppVersion=" + context.Version);
            script.AppendLine("AppPublisher=" + context.Manifest.Product.Publisher);
            script.AppendLine("DefaultDirName={autopf}\\" + appName);
            script.AppendLine("DefaultGroupName=" + appName);
            script.AppendLine("OutputDir=" + context.OutputDirectory);
            script.AppendLine("OutputBaseFilename=" + outputBaseName);
            script.AppendLine("Compression=lzma2");
            script.AppendLine("SolidCompression=yes");
            if (!string.IsNullOrEmpty(context.Artifact.Icon)) script.AppendLine("SetupIconFile=" + Path.Combine(context.RepoRoot, context.Artifact.Icon));
            script.AppendLine();
            script.AppendLine("[Files]");
            script.AppendLine("Source: \"" + published.OutputDirectory + "\\*\"; DestDir: \"{app}\"; Flags: recursesubdirs ignoreversion");
            script.AppendLine();
            script.AppendLine("[Run]");

            // Reuse the artifact's own registration flags rather than reimplementing service or Run-key code here.
            // --install-service registers and starts the Windows service and returns; it never runs the server in the
            // foreground. Inno does not act on [Run] exit codes (the WiX channel does, with Return="check").
            // --install-startup writes the per-user Run value, so it runs as the user who started Setup rather than
            // the elevated administrator.
            if (context.Artifact.Service != null && !string.IsNullOrEmpty(context.Artifact.Service.InstallArgs))
            {
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Parameters: \"" + context.Artifact.Service.InstallArgs + "\"; StatusMsg: \"Registering the " + appName + " service...\"; Flags: runhidden waituntilterminated");
            }
            else if (context.Artifact.Startup != null && !string.IsNullOrEmpty(context.Artifact.Startup.InstallArgs))
            {
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Parameters: \"" + context.Artifact.Startup.InstallArgs + "\"; StatusMsg: \"Registering " + appName + " to start at sign-in...\"; Flags: runhidden waituntilterminated runasoriginaluser");
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Description: \"Launch " + appName + "\"; Flags: postinstall nowait skipifsilent runasoriginaluser");
            }

            script.AppendLine();
            script.AppendLine("[UninstallRun]");
            if (context.Artifact.Service != null && !string.IsNullOrEmpty(context.Artifact.Service.UninstallArgs))
            {
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Parameters: \"" + context.Artifact.Service.UninstallArgs + "\"; RunOnceId: \"UnregisterService\"; Flags: runhidden waituntilterminated");
            }
            else if (context.Artifact.Startup != null && !string.IsNullOrEmpty(context.Artifact.Startup.UninstallArgs))
            {
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Parameters: \"" + context.Artifact.Startup.UninstallArgs + "\"; RunOnceId: \"UnregisterStartup\"; Flags: runhidden waituntilterminated");
            }

            string scriptPath = Path.Combine(context.OutputDirectory, outputBaseName + ".iss");
            File.WriteAllText(scriptPath, script.ToString());
            return scriptPath;
        }

        private static void SignProducedInstallers(ChannelContext context)
        {
            AuthenticodeSigner.SignAll(context.Manifest.Signing.Windows, context.OutputDirectory, "*.exe", context.RepoRoot, "[inno]");
        }

        #endregion
    }
}
