namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for creating and validating self-contained encrypted session tokens.
    /// AES-256-CBC encrypted, containing TenantId + UserId + ExpiresUtc.
    /// No server-side storage needed.
    /// </summary>
    public class SessionTokenService : ISessionTokenService
    {
        #region Private-Members

        private readonly byte[] _Key;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with encryption key.
        /// </summary>
        /// <param name="encryptionKey">Base64-encoded AES-256 key, or null to auto-generate.</param>
        public SessionTokenService(string? encryptionKey = null)
        {
            if (!string.IsNullOrEmpty(encryptionKey))
            {
                _Key = Convert.FromBase64String(encryptionKey);
            }
            else
            {
                _Key = new byte[32];
                RandomNumberGenerator.Fill(_Key);
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public AuthenticateResult CreateToken(string tenantId, string userId)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));

            DateTime expiresUtc = DateTime.UtcNow.AddHours(Constants.SessionTokenLifetimeHours);

            SessionPayload payload = new SessionPayload
            {
                TenantId = tenantId,
                UserId = userId,
                ExpiresUtc = expiresUtc
            };

            string json = JsonSerializer.Serialize(payload);
            string encrypted = Encrypt(json);

            return new AuthenticateResult
            {
                Success = true,
                Token = encrypted,
                ExpiresUtc = expiresUtc
            };
        }

        /// <inheritdoc />
        public AuthContext? ValidateToken(string encryptedToken)
        {
            if (string.IsNullOrEmpty(encryptedToken)) return null;

            try
            {
                string json = Decrypt(encryptedToken);
                SessionPayload? payload = JsonSerializer.Deserialize<SessionPayload>(json);
                if (payload == null) return null;
                if (string.IsNullOrEmpty(payload.TenantId) || string.IsNullOrEmpty(payload.UserId)) return null;
                if (payload.ExpiresUtc <= DateTime.UtcNow) return null;

                return new AuthContext
                {
                    IsAuthenticated = true,
                    TenantId = payload.TenantId,
                    UserId = payload.UserId,
                    IsAdmin = false,
                    IsTenantAdmin = false,
                    AuthMethod = "Session"
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get the current encryption key as a Base64 string (for persisting to settings).
        /// </summary>
        /// <returns>Base64-encoded key.</returns>
        public string GetKeyBase64()
        {
            return Convert.ToBase64String(_Key);
        }

        #endregion

        #region Private-Methods

        private string Encrypt(string plainText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _Key;
                aes.GenerateIV();

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(aes.IV, 0, aes.IV.Length);
                    using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                        cs.Write(plainBytes, 0, plainBytes.Length);
                        cs.FlushFinalBlock();
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        private string Decrypt(string cipherText)
        {
            byte[] allBytes = Convert.FromBase64String(cipherText);

            using (Aes aes = Aes.Create())
            {
                aes.Key = _Key;
                byte[] iv = new byte[16];
                Array.Copy(allBytes, 0, iv, 0, 16);
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                using (MemoryStream ms = new MemoryStream(allBytes, 16, allBytes.Length - 16))
                using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (StreamReader sr = new StreamReader(cs, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        #endregion

        #region Private-Classes

        private class SessionPayload
        {
            public string? TenantId { get; set; }
            public string? UserId { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        #endregion
    }
}
