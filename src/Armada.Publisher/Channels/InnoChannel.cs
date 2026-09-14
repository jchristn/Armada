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

            // Reuse the artifact's own registration logic rather than reimplementing Run-key/Service code here.
            if (context.Artifact.Service != null && !string.IsNullOrEmpty(context.Artifact.Service.InstallArgs))
            {
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Parameters: \"" + context.Artifact.Service.InstallArgs + "\"; Flags: runhidden");
            }
            else if (context.Artifact.Startup != null && !string.IsNullOrEmpty(context.Artifact.Startup.InstallArgs))
            {
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Parameters: \"" + context.Artifact.Startup.InstallArgs + "\"; Flags: runhidden nowait");
            }

            script.AppendLine();
            script.AppendLine("[UninstallRun]");
            if (context.Artifact.Service != null && !string.IsNullOrEmpty(context.Artifact.Service.UninstallArgs))
            {
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Parameters: \"" + context.Artifact.Service.UninstallArgs + "\"; Flags: runhidden");
            }
            else if (context.Artifact.Startup != null && !string.IsNullOrEmpty(context.Artifact.Startup.UninstallArgs))
            {
                script.AppendLine("Filename: \"{app}\\" + binaryFile + "\"; Parameters: \"" + context.Artifact.Startup.UninstallArgs + "\"; Flags: runhidden");
            }

            string scriptPath = Path.Combine(context.OutputDirectory, outputBaseName + ".iss");
            File.WriteAllText(scriptPath, script.ToString());
            return scriptPath;
        }

        private static void SignProducedInstallers(ChannelContext context)
        {
            string certBase64 = Environment.GetEnvironmentVariable(context.Manifest.Signing.Windows.CertBase64Secret) ?? string.Empty;
            string certPassword = Environment.GetEnvironmentVariable(context.Manifest.Signing.Windows.CertPasswordSecret) ?? string.Empty;
            if (string.IsNullOrEmpty(certBase64))
            {
                Console.WriteLine("[inno] " + context.Manifest.Signing.Windows.CertBase64Secret + " not set; produced unsigned installer(s).");
                return;
            }

            string pfxPath = Path.Combine(context.OutputDirectory, "codesign.pfx");
            File.WriteAllBytes(pfxPath, Convert.FromBase64String(certBase64));
            try
            {
                foreach (string installer in Directory.EnumerateFiles(context.OutputDirectory, "*.exe"))
                {
                    List<string> arguments = new List<string>
                    {
                        "sign", "/f", pfxPath,
                        "/fd", "SHA256",
                        "/tr", context.Manifest.Signing.Windows.TimestampUrl,
                        "/td", "SHA256",
                        installer
                    };
                    if (!string.IsNullOrEmpty(certPassword)) arguments.InsertRange(4, new List<string> { "/p", certPassword });
                    ProcessRunner.Run("signtool", arguments, context.RepoRoot);
                    ChecksumWriter.WriteSidecar(installer);
                }
            }
            finally
            {
                if (File.Exists(pfxPath)) File.Delete(pfxPath);
            }
        }

        #endregion
    }
}
