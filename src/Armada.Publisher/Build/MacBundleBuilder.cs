namespace Armada.Publisher.Build
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security;
    using System.Text;
    using Armada.Publisher.Channels;

    /// <summary>
    /// Assembles a macOS .app bundle (Contents/Info.plist, Contents/MacOS, Contents/Resources/AppIcon.icns)
    /// around a self-contained publish, and writes the hardened-runtime entitlements .NET needs.
    /// </summary>
    public static class MacBundleBuilder
    {
        #region Public-Members

        /// <summary>
        /// File name (without extension) of the icon inside Contents/Resources.
        /// </summary>
        public const string IconName = "AppIcon";

        /// <summary>
        /// Oldest macOS version the bundle declares support for. .NET 10 supports macOS 14 and later.
        /// </summary>
        public const string MinimumSystemVersion = "14.0";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build "&lt;DisplayName&gt;.app" under the target directory from one published runtime.
        /// </summary>
        /// <param name="context">Channel context (artifact metadata, version, repository root).</param>
        /// <param name="published">The self-contained publish for one osx runtime.</param>
        /// <param name="targetDirectory">Directory that will contain the .app.</param>
        /// <param name="workDirectory">Scratch directory for the iconset.</param>
        /// <returns>Absolute path to the bundle.</returns>
        public static string Build(ChannelContext context, PublishedArtifact published, string targetDirectory, string workDirectory)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (published == null) throw new ArgumentNullException(nameof(published));
            if (string.IsNullOrEmpty(targetDirectory)) throw new ArgumentNullException(nameof(targetDirectory));
            if (string.IsNullOrEmpty(workDirectory)) throw new ArgumentNullException(nameof(workDirectory));

            string bundlePath = Path.Combine(targetDirectory, context.Artifact.DisplayName + ".app");
            if (Directory.Exists(bundlePath)) Directory.Delete(bundlePath, true);

            string contents = Path.Combine(bundlePath, "Contents");
            string macOs = Path.Combine(contents, "MacOS");
            string resources = Path.Combine(contents, "Resources");
            Directory.CreateDirectory(macOs);
            Directory.CreateDirectory(resources);

            CopyPayload(published.OutputDirectory, macOs);

            string executableName = Path.GetFileName(published.BinaryPath);
            string executablePath = Path.Combine(macOs, executableName);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executablePath, File.GetUnixFileMode(executablePath)
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            bool hasIcon = false;
            if (!string.IsNullOrEmpty(context.Artifact.MacIcon))
            {
                string sourcePng = Path.Combine(context.RepoRoot, context.Artifact.MacIcon);
                IcnsBuilder.Build(sourcePng, Path.Combine(resources, IconName + ".icns"), workDirectory);
                hasIcon = true;
            }

            File.WriteAllText(Path.Combine(contents, "Info.plist"), BuildInfoPlist(context, executableName, hasIcon));
            File.WriteAllText(Path.Combine(contents, "PkgInfo"), "APPL????");

            Console.WriteLine("[bundle] " + bundlePath);
            return bundlePath;
        }

        /// <summary>
        /// Write the entitlements plist a .NET single-file app needs under the hardened runtime: JIT,
        /// unsigned executable memory, and loading the native libraries it extracts at startup.
        /// </summary>
        /// <param name="path">Destination path.</param>
        /// <returns>The path written.</returns>
        public static string WriteEntitlements(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            StringBuilder plist = new StringBuilder();
            plist.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            plist.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            plist.Append("<plist version=\"1.0\">\n<dict>\n");
            plist.Append("    <key>com.apple.security.cs.allow-jit</key>\n    <true/>\n");
            plist.Append("    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>\n    <true/>\n");
            plist.Append("    <key>com.apple.security.cs.disable-library-validation</key>\n    <true/>\n");
            plist.Append("</dict>\n</plist>\n");

            File.WriteAllText(path, plist.ToString());
            return path;
        }

        /// <summary>
        /// Escape a value for inclusion in a plist string element.
        /// </summary>
        /// <param name="value">Raw value.</param>
        /// <returns>XML-escaped value.</returns>
        public static string Escape(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }

        #endregion

        #region Private-Methods

        private static void CopyPayload(string sourceDirectory, string destinationDirectory)
        {
            foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) continue;
                if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;

                string relative = Path.GetRelativePath(sourceDirectory, file);
                string destination = Path.Combine(destinationDirectory, relative);
                string? parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.Copy(file, destination, true);
            }
        }

        private static string BuildInfoPlist(ChannelContext context, string executableName, bool hasIcon)
        {
            string numericVersion = VersionText.NumericCore(context.Version);
            Dictionary<string, string> strings = new Dictionary<string, string>
            {
                { "CFBundleDevelopmentRegion", "en" },
                { "CFBundleDisplayName", context.Artifact.DisplayName },
                { "CFBundleExecutable", executableName },
                { "CFBundleIdentifier", context.Artifact.ResolveBundleIdentifier() },
                { "CFBundleInfoDictionaryVersion", "6.0" },
                { "CFBundleName", context.Artifact.DisplayName },
                { "CFBundlePackageType", "APPL" },
                { "CFBundleShortVersionString", context.Version },
                { "CFBundleVersion", numericVersion },
                { "LSApplicationCategoryType", "public.app-category.developer-tools" },
                { "LSMinimumSystemVersion", MinimumSystemVersion },
                { "NSHumanReadableCopyright", context.Manifest.Product.Publisher }
            };
            if (hasIcon) strings.Add("CFBundleIconFile", IconName);

            StringBuilder plist = new StringBuilder();
            plist.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            plist.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            plist.Append("<plist version=\"1.0\">\n<dict>\n");

            List<string> keys = new List<string>(strings.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                plist.Append("    <key>").Append(key).Append("</key>\n");
                plist.Append("    <string>").Append(Escape(strings[key])).Append("</string>\n");
            }

            plist.Append("    <key>NSHighResolutionCapable</key>\n    <true/>\n");
            plist.Append("    <key>NSSupportsAutomaticGraphicsSwitching</key>\n    <true/>\n");
            plist.Append("</dict>\n</plist>\n");
            return plist.ToString();
        }

        #endregion
    }
}
