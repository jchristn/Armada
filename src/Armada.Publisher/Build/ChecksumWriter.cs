namespace Armada.Publisher.Build
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Security.Cryptography;

    /// <summary>
    /// Computes and records SHA-256 checksums so package-manager manifests can reference a verified hash
    /// rather than a hand-copied one.
    /// </summary>
    public static class ChecksumWriter
    {
        #region Public-Members

        /// <summary>
        /// Name of the per-release checksum manifest.
        /// </summary>
        public const string ManifestFileName = "SHA256SUMS";

        /// <summary>
        /// File extensions treated as release deliverables when writing checksum manifests.
        /// </summary>
        public static readonly string[] ReleaseExtensions = new string[]
        {
            ".exe", ".msi", ".dmg", ".pkg", ".deb", ".rpm", ".AppImage", ".nupkg", ".zip", ".tar.gz"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return true when the file name ends with one of the release deliverable extensions.
        /// </summary>
        /// <param name="path">File path or name.</param>
        /// <returns>True for installers and packages.</returns>
        public static bool IsReleaseFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string name = Path.GetFileName(path);
            foreach (string extension in ReleaseExtensions)
            {
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Write "SHA256SUMS" in the directory covering every release file directly inside it, one
        /// "&lt;hash&gt;  &lt;filename&gt;" line per file sorted by name (the format "sha256sum -c" and
        /// "shasum -a 256 -c" read). Existing content is replaced so the manifest always matches the directory.
        /// </summary>
        /// <param name="directory">Directory holding the release files.</param>
        /// <returns>Path to the manifest, or null when the directory has no release files.</returns>
        public static string? WriteManifest(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentNullException(nameof(directory));
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Directory not found: " + directory);

            List<string> files = new List<string>();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (IsReleaseFile(file)) files.Add(file);
            }
            if (files.Count == 0) return null;

            files.Sort(StringComparer.Ordinal);
            StringBuilder content = new StringBuilder();
            foreach (string file in files)
            {
                content.Append(ComputeSha256(file)).Append("  ").Append(Path.GetFileName(file)).Append('\n');
            }

            string manifestPath = Path.Combine(directory, ManifestFileName);
            File.WriteAllText(manifestPath, content.ToString());
            return manifestPath;
        }

        /// <summary>
        /// Compute the lowercase hexadecimal SHA-256 of a file.
        /// </summary>
        /// <param name="path">File to hash.</param>
        /// <returns>Lowercase hexadecimal digest.</returns>
        public static string ComputeSha256(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("File to hash not found.", path);

            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Write a ".sha256" sidecar file next to the artifact in the "&lt;hash&gt;  &lt;filename&gt;" format.
        /// </summary>
        /// <param name="path">Artifact whose checksum should be recorded.</param>
        /// <returns>The computed digest.</returns>
        public static string WriteSidecar(string path)
        {
            string digest = ComputeSha256(path);
            string sidecar = path + ".sha256";
            File.WriteAllText(sidecar, digest + "  " + Path.GetFileName(path) + Environment.NewLine);
            return digest;
        }

        #endregion
    }
}
