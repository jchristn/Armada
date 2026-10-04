namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Computes a SHA-256 hash over every package manifest and lock file in a repository (paths and contents), so
    /// dependency and vulnerability checks can be skipped while the manifests are unchanged. Stateless and thread-safe.
    /// </summary>
    public static class ManifestHasher
    {
        #region Public-Members

        /// <summary>
        /// File names (case-insensitive) that count as manifests or lock files.
        /// </summary>
        public static readonly IReadOnlyList<string> ManifestFileNames = new List<string>
        {
            "Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets", "packages.lock.json",
            "packages.config", "global.json", "nuget.config", "package.json", "package-lock.json",
            "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml"
        };

        /// <summary>
        /// File extensions (case-insensitive) that count as manifests.
        /// </summary>
        public static readonly IReadOnlyList<string> ManifestExtensions = new List<string>
        {
            ".csproj", ".fsproj", ".vbproj", ".sln", ".slnx"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a repository-relative path is a manifest or lock file.
        /// </summary>
        /// <param name="relativePath">Relative path.</param>
        /// <returns>True for a manifest or lock file.</returns>
        public static bool IsManifest(string relativePath)
        {
            if (String.IsNullOrEmpty(relativePath)) return false;
            string name = RepositoryFileInventory.GetFileName(relativePath);
            if (ManifestFileNames.Any(n => String.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return true;
            return ManifestExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Compute the manifest hash for an inventory. Contents are read when the inventory was scanned from disk;
        /// for a bare repository only the paths are hashed.
        /// </summary>
        /// <param name="inventory">Repository file inventory.</param>
        /// <returns>Lowercase hex SHA-256, or null when the repository has no manifests.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inventory is null.</exception>
        public static string? Compute(RepositoryFileInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            List<string> manifests = inventory.Files.Where(IsManifest).OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (manifests.Count == 0) return null;

            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (string manifest in manifests)
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(manifest + "\n"));
                    string? content = inventory.ReadText(manifest, 8 * 1024 * 1024);
                    if (content != null) hash.AppendData(Encoding.UTF8.GetBytes(content));
                    hash.AppendData(new byte[] { 0 });
                }

                return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
        }

        #endregion
    }
}
