namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// Salted, adaptive password hashing for stored user passwords (PBKDF2-HMAC-SHA256).
    /// <para>
    /// The value that is stretched is the lowercase hex SHA-256 of the password (<see cref="UserMaster.ComputePasswordHash"/>),
    /// the same value API clients may send as <c>PasswordSha256</c>. Stretching that value (rather than the plaintext) lets a
    /// legacy unsalted SHA-256 hash be upgraded without knowing the password, and keeps the public API unchanged.
    /// </para>
    /// <para>
    /// Stored format: <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>. Anything else is a legacy
    /// unsalted SHA-256 hex hash. Verification is constant-time in both formats.
    /// </para>
    /// </summary>
    public static class PasswordHasher
    {
        #region Public-Members

        /// <summary>
        /// Scheme prefix of the stored format.
        /// </summary>
        public const string Scheme = "pbkdf2-sha256";

        /// <summary>
        /// PBKDF2 iteration count for new hashes (OWASP 2023 recommendation for PBKDF2-HMAC-SHA256).
        /// </summary>
        public const int Iterations = 600000;

        /// <summary>
        /// Salt length in bytes.
        /// </summary>
        public const int SaltBytes = 16;

        /// <summary>
        /// Derived key length in bytes.
        /// </summary>
        public const int HashBytes = 32;

        #endregion

        #region Private-Members

        private const int _MinIterations = 1000;
        private const int _MaxIterations = 10000000;
        private const int _MaxCacheEntries = 4096;
        private static readonly ConcurrentDictionary<string, bool> _DefaultPasswordCache = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Hash a plaintext password for storage.
        /// </summary>
        /// <param name="plainText">Plaintext password.</param>
        /// <returns>Stored-format hash.</returns>
        public static string HashPassword(string plainText)
        {
            if (String.IsNullOrEmpty(plainText)) throw new ArgumentNullException(nameof(plainText));
            return HashSha256Hex(UserMaster.ComputePasswordHash(plainText));
        }

        /// <summary>
        /// Hash the hex SHA-256 of a password (as sent by API clients in <c>PasswordSha256</c>, or a legacy stored value)
        /// for storage.
        /// </summary>
        /// <param name="sha256Hex">Hex SHA-256 of the password.</param>
        /// <returns>Stored-format hash.</returns>
        public static string HashSha256Hex(string sha256Hex)
        {
            if (String.IsNullOrEmpty(sha256Hex)) throw new ArgumentNullException(nameof(sha256Hex));
            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = Derive(sha256Hex, salt, Iterations);
            return Scheme + "$" + Iterations.ToString(CultureInfo.InvariantCulture) + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Whether a stored value is in the salted adaptive format.
        /// </summary>
        /// <param name="stored">Stored value.</param>
        /// <returns>True for the PBKDF2 format.</returns>
        public static bool IsAdaptiveHash(string? stored)
        {
            return TryParse(stored, out int _, out byte[]? _, out byte[]? _);
        }

        /// <summary>
        /// Convert any value about to be written to the password column into the stored format: a value already in the
        /// PBKDF2 format is kept, anything else is treated as a hex SHA-256 of the password and stretched. Every database
        /// driver calls this on user create and update, so an unsalted hash never reaches storage.
        /// </summary>
        /// <param name="value">Value to store.</param>
        /// <returns>Stored-format hash.</returns>
        public static string ToStorageFormat(string value)
        {
            if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(value));
            if (IsAdaptiveHash(value)) return value;
            return HashSha256Hex(value);
        }

        /// <summary>
        /// Verify a plaintext password against a stored value (PBKDF2 format or legacy SHA-256 hex), in constant time.
        /// </summary>
        /// <param name="plainText">Candidate plaintext password.</param>
        /// <param name="stored">Stored value.</param>
        /// <returns>True when the password matches.</returns>
        public static bool Verify(string? plainText, string? stored)
        {
            if (String.IsNullOrEmpty(plainText) || String.IsNullOrEmpty(stored)) return false;
            string candidateHex = UserMaster.ComputePasswordHash(plainText);

            if (TryParse(stored, out int iterations, out byte[]? salt, out byte[]? expected))
            {
                byte[] computed = Derive(candidateHex, salt!, iterations, expected!.Length);
                return CryptographicOperations.FixedTimeEquals(computed, expected);
            }

            byte[] candidate = Encoding.ASCII.GetBytes(candidateHex);
            byte[] legacy = Encoding.ASCII.GetBytes(stored.Trim().ToLowerInvariant());
            return CryptographicOperations.FixedTimeEquals(candidate, legacy);
        }

        /// <summary>
        /// Whether a stored value should be replaced after a successful login: it is a legacy unsalted hash, or a PBKDF2
        /// hash with fewer iterations than <see cref="Iterations"/>.
        /// </summary>
        /// <param name="stored">Stored value.</param>
        /// <returns>True when a rehash is due.</returns>
        public static bool NeedsRehash(string? stored)
        {
            if (!TryParse(stored, out int iterations, out byte[]? _, out byte[]? _)) return true;
            return iterations < Iterations;
        }

        /// <summary>
        /// Whether a stored value matches the well-known default password. The result is cached per stored value (a
        /// password change produces a new value with a new salt), because the check runs on every authenticated request
        /// of a seeded admin session and PBKDF2 is deliberately slow.
        /// </summary>
        /// <param name="stored">Stored value.</param>
        /// <returns>True when the stored value is the default password.</returns>
        public static bool MatchesDefaultPassword(string? stored)
        {
            if (String.IsNullOrEmpty(stored)) return false;
            if (_DefaultPasswordCache.TryGetValue(stored, out bool cached)) return cached;
            bool matches = Verify(Constants.DefaultUserPassword, stored);
            if (_DefaultPasswordCache.Count >= _MaxCacheEntries) _DefaultPasswordCache.Clear();
            _DefaultPasswordCache[stored] = matches;
            return matches;
        }

        #endregion

        #region Private-Methods

        private static byte[] Derive(string sha256Hex, byte[] salt, int iterations, int length = HashBytes)
        {
            byte[] input = Encoding.ASCII.GetBytes(sha256Hex.Trim().ToLowerInvariant());
            return Rfc2898DeriveBytes.Pbkdf2(input, salt, iterations, HashAlgorithmName.SHA256, length);
        }

        private static bool TryParse(string? stored, out int iterations, out byte[]? salt, out byte[]? hash)
        {
            iterations = 0;
            salt = null;
            hash = null;
            if (String.IsNullOrEmpty(stored)) return false;
            if (!stored.StartsWith(Scheme + "$", StringComparison.Ordinal)) return false;

            string[] parts = stored.Split('$');
            if (parts.Length != 4) return false;
            if (!Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)) return false;
            if (iterations < _MinIterations || iterations > _MaxIterations) return false;

            try
            {
                salt = Convert.FromBase64String(parts[2]);
                hash = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            return salt.Length >= 8 && hash.Length >= 16 && hash.Length <= 64;
        }

        #endregion
    }
}
