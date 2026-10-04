namespace Armada.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security;
    using System.Security.Cryptography;
    using System.Text;
    using Armada.Publisher.Build;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// Builds a Windows Installer package (.msi) per Windows runtime identifier with the WiX v5+ CLI
    /// (dotnet tool install --global wix). Mirrors the Inno recipe: the whole self-contained publish is
    /// installed under Program Files, and service or startup registration is delegated to the artifact's
    /// own install/uninstall arguments through deferred custom actions. The MSI is Authenticode-signed
    /// when the certificate secret is present.
    /// </summary>
    public class WixChannel : IChannel
    {
        #region Public-Members

        /// <inheritdoc />
        public ChannelKindEnum Kind
        {
            get { return ChannelKindEnum.Wix; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IEnumerable<ToolRequirement> Requirements()
        {
            return new List<ToolRequirement>
            {
                new ToolRequirement("wix", "dotnet tool install --global wix", "win"),
                new ToolRequirement("signtool", "Install the Windows SDK (ships on windows-latest); needed to Authenticode-sign the installer.", "win")
            };
        }

        /// <inheritdoc />
        public void Execute(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WiX runs on Windows only.");

            Directory.CreateDirectory(context.OutputDirectory);

            foreach (PublishedArtifact published in context.Published)
            {
                if (!published.RuntimeIdentifier.StartsWith("win", StringComparison.OrdinalIgnoreCase)) continue;

                string outputBaseName = context.Artifact.BinaryName + "-" + context.Version + "-" + published.RuntimeIdentifier;
                string sourcePath = Path.Combine(context.OutputDirectory, outputBaseName + ".wxs");
                File.WriteAllText(sourcePath, BuildWxs(context, published));

                string architecture = published.RuntimeIdentifier.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64";
                ProcessRunner.Run("wix", new List<string>
                {
                    "build", sourcePath,
                    "-arch", architecture,
                    "-o", Path.Combine(context.OutputDirectory, outputBaseName + ".msi")
                }, context.RepoRoot);
            }

            AuthenticodeSigner.SignAll(context.Manifest.Signing.Windows, context.OutputDirectory, "*.msi", context.RepoRoot, "[wix]");
        }

        /// <summary>
        /// Derive a stable GUID from a seed so the MSI UpgradeCode never changes between releases
        /// (a changed UpgradeCode would install side by side instead of upgrading).
        /// </summary>
        /// <param name="seed">Seed text, for example "Armada/server/win-x64".</param>
        /// <returns>Deterministic GUID string.</returns>
        public static string StableGuid(string seed)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            byte[] bytes = new byte[16];
            Array.Copy(hash, bytes, 16);
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes).ToString("D").ToUpperInvariant();
        }

        #endregion

        #region Private-Methods

        private static string Esc(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }

        private static string BuildWxs(ChannelContext context, PublishedArtifact published)
        {
            ArtifactDefinition artifact = context.Artifact;
            string binaryFile = Path.GetFileName(published.BinaryPath);
            string upgradeCode = StableGuid(context.Manifest.Product.Name + "/" + artifact.Id + "/" + published.RuntimeIdentifier);

            string installArgs = string.Empty;
            string uninstallArgs = string.Empty;
            bool waitForInstall = true;
            if (artifact.Service != null && !string.IsNullOrEmpty(artifact.Service.InstallArgs))
            {
                installArgs = artifact.Service.InstallArgs;
                uninstallArgs = artifact.Service.UninstallArgs;
            }
            else if (artifact.Startup != null && !string.IsNullOrEmpty(artifact.Startup.InstallArgs))
            {
                installArgs = artifact.Startup.InstallArgs;
                uninstallArgs = artifact.Startup.UninstallArgs;
                waitForInstall = false;
            }

            StringBuilder wxs = new StringBuilder();
            wxs.AppendLine("<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">");
            wxs.AppendLine("  <Package Name=\"" + Esc(artifact.DisplayName) + "\" Manufacturer=\"" + Esc(context.Manifest.Product.Publisher) + "\""
                + " Version=\"" + VersionText.NumericCore(context.Version) + "\" UpgradeCode=\"" + upgradeCode + "\" Scope=\"perMachine\">");
            wxs.AppendLine("    <MajorUpgrade DowngradeErrorMessage=\"A newer version of " + Esc(artifact.DisplayName) + " is already installed.\" />");
            wxs.AppendLine("    <MediaTemplate EmbedCab=\"yes\" />");
            if (!string.IsNullOrEmpty(artifact.Icon))
            {
                wxs.AppendLine("    <Icon Id=\"ProductIcon\" SourceFile=\"" + Esc(Path.Combine(context.RepoRoot, artifact.Icon)) + "\" />");
                wxs.AppendLine("    <Property Id=\"ARPPRODUCTICON\" Value=\"ProductIcon\" />");
            }
            if (!string.IsNullOrEmpty(context.Manifest.Product.Homepage))
            {
                wxs.AppendLine("    <Property Id=\"ARPURLINFOABOUT\" Value=\"" + Esc(context.Manifest.Product.Homepage) + "\" />");
            }
            wxs.AppendLine("    <StandardDirectory Id=\"ProgramFiles6432Folder\">");
            wxs.AppendLine("      <Directory Id=\"INSTALLFOLDER\" Name=\"" + Esc(artifact.DisplayName) + "\" />");
            wxs.AppendLine("    </StandardDirectory>");
            wxs.AppendLine("    <ComponentGroup Id=\"ProductFiles\" Directory=\"INSTALLFOLDER\">");
            wxs.AppendLine("      <Component>");
            wxs.AppendLine("        <File Id=\"MainExecutable\" Source=\"" + Esc(published.BinaryPath) + "\" KeyPath=\"yes\" />");
            wxs.AppendLine("      </Component>");
            wxs.AppendLine("      <Files Include=\"" + Esc(published.OutputDirectory) + "\\**\">");
            wxs.AppendLine("        <Exclude Files=\"" + Esc(published.BinaryPath) + "\" />");
            wxs.AppendLine("        <Exclude Files=\"" + Esc(published.OutputDirectory) + "\\**\\*.sha256\" />");
            wxs.AppendLine("      </Files>");
            wxs.AppendLine("    </ComponentGroup>");
            wxs.AppendLine("    <Feature Id=\"Main\" Title=\"" + Esc(artifact.DisplayName) + "\">");
            wxs.AppendLine("      <ComponentGroupRef Id=\"ProductFiles\" />");
            wxs.AppendLine("    </Feature>");

            // Same contract as the Inno recipe: the artifact registers its own service or startup entry.
            if (!string.IsNullOrEmpty(installArgs))
            {
                string returnMode = waitForInstall ? "check" : "asyncNoWait";
                wxs.AppendLine("    <CustomAction Id=\"RegisterArtifact\" FileRef=\"MainExecutable\" ExeCommand=\"" + Esc(installArgs) + "\""
                    + " Execute=\"deferred\" Impersonate=\"" + (waitForInstall ? "no" : "yes") + "\" Return=\"" + returnMode + "\" />");
                if (!string.IsNullOrEmpty(uninstallArgs))
                {
                    wxs.AppendLine("    <CustomAction Id=\"UnregisterArtifact\" FileRef=\"MainExecutable\" ExeCommand=\"" + Esc(uninstallArgs) + "\""
                        + " Execute=\"deferred\" Impersonate=\"" + (waitForInstall ? "no" : "yes") + "\" Return=\"ignore\" />");
                }
                wxs.AppendLine("    <InstallExecuteSequence>");
                wxs.AppendLine("      <Custom Action=\"RegisterArtifact\" After=\"InstallFiles\" Condition=\"NOT REMOVE\" />");
                if (!string.IsNullOrEmpty(uninstallArgs))
                {
                    wxs.AppendLine("      <Custom Action=\"UnregisterArtifact\" Before=\"RemoveFiles\" Condition=\"REMOVE=&quot;ALL&quot;\" />");
                }
                wxs.AppendLine("    </InstallExecuteSequence>");
            }

            wxs.AppendLine("  </Package>");
            wxs.AppendLine("</Wix>");
            return wxs.ToString();
        }

        #endregion
    }
}
