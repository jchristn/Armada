namespace Armada.Publisher.Build
{
    using System;
    using System.IO;
    using System.Security.Cryptography;

    /// <summary>
    /// Computes and records SHA-256 checksums so package-manager manifests can reference a verified hash
    /// rather than a hand-copied one.
    /// </summary>
    public static class ChecksumWriter
    {
        #region Public-Methods

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
