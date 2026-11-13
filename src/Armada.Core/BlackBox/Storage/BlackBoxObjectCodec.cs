namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Security.Cryptography;

    /// <summary>
    /// Encodes black box objects for storage (gzip when compression is on) and computes their SHA-256 over the stored
    /// bytes. Shared by every target, so stored bytes and hashes are identical whatever the target. Thread-safe.
    /// </summary>
    public static class BlackBoxObjectCodec
    {
        /// <summary>
        /// gzip content encoding.
        /// </summary>
        public const string Gzip = "gzip";

        /// <summary>
        /// identity content encoding.
        /// </summary>
        public const string Identity = "identity";

        /// <summary>
        /// Encode bytes for storage.
        /// </summary>
        /// <param name="raw">Uncompressed bytes.</param>
        /// <param name="compress">Whether to gzip.</param>
        /// <returns>The encoded object.</returns>
        /// <exception cref="ArgumentNullException">Thrown when raw is null.</exception>
        public static BlackBoxEncodedObject Encode(byte[] raw, bool compress)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            byte[] data = compress ? Compress(raw) : raw;
            return new BlackBoxEncodedObject
            {
                Data = data,
                ContentEncoding = compress ? Gzip : Identity,
                Sha256 = Sha256Hex(data),
                UncompressedBytes = raw.LongLength
            };
        }

        /// <summary>
        /// Decode stored bytes.
        /// </summary>
        /// <param name="stored">Stored bytes.</param>
        /// <param name="contentEncoding">gzip or identity.</param>
        /// <returns>Uncompressed bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stored is null.</exception>
        /// <exception cref="BlackBoxObjectCorruptException">Thrown when gzip data cannot be decoded.</exception>
        public static byte[] Decode(byte[] stored, string? contentEncoding)
        {
            if (stored == null) throw new ArgumentNullException(nameof(stored));
            if (!String.Equals(contentEncoding, Gzip, StringComparison.OrdinalIgnoreCase)) return stored;
            try
            {
                using (MemoryStream input = new MemoryStream(stored))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch (InvalidDataException e)
            {
                throw new BlackBoxObjectCorruptException("Stored gzip data could not be decoded.", e);
            }
        }

        /// <summary>
        /// SHA-256 of bytes as lowercase hex.
        /// </summary>
        /// <param name="data">Bytes.</param>
        /// <returns>64 hex characters.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        public static string Sha256Hex(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            using (SHA256 sha = SHA256.Create())
            {
                return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
            }
        }

        private static byte[] Compress(byte[] raw)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return output.ToArray();
            }
        }
    }
}
