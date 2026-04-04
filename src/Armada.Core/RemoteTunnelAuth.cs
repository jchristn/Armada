namespace Armada.Core
{
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// Shared helpers for proxy and remote-tunnel authentication proofs.
    /// </summary>
    public static class RemoteTunnelAuth
    {
        #region Public-Constants

        /// <summary>
        /// Purpose string for browser login proofs.
        /// </summary>
        public static readonly string BrowserLoginPurpose = "proxy-browser-login";

        /// <summary>
        /// Purpose string for tunnel handshake proofs.
        /// </summary>
        public static readonly string TunnelHandshakePurpose = "tunnel-handshake";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize the configured shared password, applying the default when blank.
        /// </summary>
        public static string NormalizePassword(string? password)
        {
            return String.IsNullOrWhiteSpace(password)
                ? Constants.DefaultRemoteTunnelPassword
                : password.Trim();
        }

        /// <summary>
        /// Compute the SHA256 hash for a shared password.
        /// </summary>
        public static string ComputePasswordHash(string? password)
        {
            return UserMaster.ComputePasswordHash(NormalizePassword(password));
        }

        /// <summary>
        /// Generate a lowercase hex nonce.
        /// </summary>
        public static string CreateNonce(int numBytes = 16)
        {
            if (numBytes < 8) throw new ArgumentOutOfRangeException(nameof(numBytes), "Must be at least 8 bytes.");
            byte[] bytes = new byte[numBytes];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Compute a browser login proof from a password hash and nonce.
        /// </summary>
        public static string ComputeBrowserLoginProofFromHash(string passwordHash, string nonce)
        {
            return ComputeSha256Hex(
                BrowserLoginPurpose + ":" +
                NormalizeSubject("proxy") + ":" +
                NormalizeNonce(nonce) + ":" +
                NormalizeHash(passwordHash));
        }

        /// <summary>
        /// Compute a browser login proof from a plaintext password and nonce.
        /// </summary>
        public static string ComputeBrowserLoginProof(string? password, string nonce)
        {
            return ComputeBrowserLoginProofFromHash(ComputePasswordHash(password), nonce);
        }

        /// <summary>
        /// Compute a tunnel-handshake proof from a password hash.
        /// </summary>
        public static string ComputeTunnelHandshakeProofFromHash(string passwordHash, string instanceId, string timestampUtc, string nonce)
        {
            return ComputeSha256Hex(
                TunnelHandshakePurpose + ":" +
                NormalizeSubject(instanceId) + ":" +
                NormalizeTimestamp(timestampUtc) + ":" +
                NormalizeNonce(nonce) + ":" +
                NormalizeHash(passwordHash));
        }

        /// <summary>
        /// Compute a tunnel-handshake proof from a plaintext password.
        /// </summary>
        public static string ComputeTunnelHandshakeProof(string? password, string instanceId, string timestampUtc, string nonce)
        {
            return ComputeTunnelHandshakeProofFromHash(ComputePasswordHash(password), instanceId, timestampUtc, nonce);
        }

        /// <summary>
        /// Constant-time comparison for lowercase hex strings.
        /// </summary>
        public static bool FixedTimeEqualsHex(string? left, string? right)
        {
            if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            string normalizedLeft = NormalizeHash(left);
            string normalizedRight = NormalizeHash(right);
            if (normalizedLeft.Length != normalizedRight.Length)
            {
                return false;
            }

            byte[] leftBytes = Encoding.UTF8.GetBytes(normalizedLeft);
            byte[] rightBytes = Encoding.UTF8.GetBytes(normalizedRight);
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        /// <summary>
        /// Parse a UTC timestamp string.
        /// </summary>
        public static bool TryParseTimestampUtc(string? value, out DateTime parsedUtc)
        {
            parsedUtc = default;
            if (String.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out parsedUtc))
            {
                return false;
            }

            parsedUtc = DateTime.SpecifyKind(parsedUtc, DateTimeKind.Utc);
            return true;
        }

        #endregion

        #region Private-Methods

        private static string ComputeSha256Hex(string basis)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string NormalizeHash(string? value)
        {
            return (value ?? String.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizeNonce(string? value)
        {
            return (value ?? String.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizeSubject(string? value)
        {
            return (value ?? String.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizeTimestamp(string? value)
        {
            return (value ?? String.Empty).Trim();
        }

        #endregion
    }
}
