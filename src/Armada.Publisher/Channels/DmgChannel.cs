namespace Armada.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Publisher.Build;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// Builds a macOS .app bundle per osx runtime identifier and wraps it in a compressed, read-only
    /// .dmg (with an Applications shortcut) using the built-in hdiutil. The bundle and image are signed
    /// with a Developer ID and notarized when credentials are present; otherwise the bundle is ad-hoc
    /// signed so Apple Silicon will run it, and the image is left unsigned.
    /// </summary>
    public class DmgChannel : IChannel
    {
        #region Public-Members

        /// <inheritdoc />
        public ChannelKindEnum Kind
        {
            get { return ChannelKindEnum.Dmg; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IEnumerable<ToolRequirement> Requirements()
        {
            return new List<ToolRequirement>
            {
                new ToolRequirement("hdiutil", "Ships with macOS.", "osx"),
                new ToolRequirement("sips", "Ships with macOS.", "osx"),
                new ToolRequirement("iconutil", "Install Xcode command-line tools: xcode-select --install", "osx"),
                new ToolRequirement("codesign", "Install Xcode command-line tools: xcode-select --install", "osx"),
                new ToolRequirement("xcrun", "Install Xcode command-line tools: xcode-select --install (provides notarytool and stapler)", "osx")
            };
        }

        /// <inheritdoc />
        public void Execute(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("The .app and .dmg recipe runs on macOS only.");

            Directory.CreateDirectory(context.OutputDirectory);
            string workRoot = Path.Combine(context.OutputDirectory, "_work");
            Directory.CreateDirectory(workRoot);

            using (MacSigningSession signing = new MacSigningSession(context.Manifest.Signing.Macos, workRoot, "[dmg]"))
            {
                string entitlements = MacBundleBuilder.WriteEntitlements(Path.Combine(workRoot, "entitlements.plist"));

                foreach (PublishedArtifact published in context.Published)
                {
                    if (!published.RuntimeIdentifier.StartsWith("osx", StringComparison.OrdinalIgnoreCase)) continue;

                    string runtimeWork = Path.Combine(workRoot, published.RuntimeIdentifier);
                    string stage = Path.Combine(runtimeWork, "stage");
                    if (Directory.Exists(runtimeWork)) Directory.Delete(runtimeWork, true);
                    Directory.CreateDirectory(stage);

                    string bundle = MacBundleBuilder.Build(context, published, stage, runtimeWork);
                    SignBundle(signing, bundle, entitlements);
                    ProcessRunner.Run("codesign", new List<string> { "--verify", "--deep", "--strict", "--verbose=2", bundle });

                    // Drag-to-install layout: the app next to a shortcut to /Applications.
                    ProcessRunner.Run("ln", new List<string> { "-s", "/Applications", Path.Combine(stage, "Applications") });

                    string dmgPath = Path.Combine(context.OutputDirectory, context.Artifact.BinaryName + "-" + context.Version + "-" + published.RuntimeIdentifier + ".dmg");
                    if (File.Exists(dmgPath)) File.Delete(dmgPath);

                    ProcessRunner.Run("hdiutil", new List<string>
                    {
                        "create",
                        "-volname", context.Artifact.DisplayName,
                        "-srcfolder", stage,
                        "-fs", "HFS+",
                        "-format", "UDZO",
                        "-ov",
                        dmgPath
                    });

                    signing.SignDiskImage(dmgPath);
                    signing.NotarizeAndStaple(dmgPath);
                    Console.WriteLine("[dmg] " + dmgPath);
                }
            }

            Directory.Delete(workRoot, true);
        }

        #endregion

        #region Private-Methods

        private static void SignBundle(MacSigningSession signing, string bundle, string entitlements)
        {
            // Sign nested Mach-O files first (anything besides the main executable that a non-single-file
            // publish leaves in Contents/MacOS), then seal the bundle itself.
            string macOs = Path.Combine(bundle, "Contents", "MacOS");
            foreach (string file in Directory.EnumerateFiles(macOs, "*.dylib", SearchOption.AllDirectories))
            {
                signing.Sign(file, null);
            }

            signing.Sign(bundle, entitlements);
        }

        #endregion
    }
}
