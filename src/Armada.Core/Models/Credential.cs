namespace Armada.Core.Models
{
    using System.Security.Cryptography;

    /// <summary>
    /// Represents a bearer token credential for API authentication.
    /// </summary>
    public class Credential
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (prefix: crd_).
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get => _TenantId;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// User identifier.
        /// </summary>
        public string UserId
        {
            get => _UserId;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(UserId));
                _UserId = value;
            }
        }

        /// <summary>
        /// Friendly name for this credential.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Bearer token for API authentication (64-char random alphanumeric).
        /// </summary>
        public string BearerToken
        {
            get => _BearerToken;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(BearerToken));
                _BearerToken = value;
            }
        }

        /// <summary>
        /// Whether the credential is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether the credential is protected from direct deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.CredentialIdPrefix, 24);
        private string _TenantId = Constants.DefaultTenantId;
        private string _UserId = Constants.DefaultUserId;
        private string _BearerToken = GenerateBearerToken();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with auto-generated bearer token.
        /// </summary>
        public Credential()
        {
        }

        /// <summary>
        /// Instantiate with tenant and user.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        public Credential(string tenantId, string userId)
        {
            TenantId = tenantId;
            UserId = userId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a cryptographically random 64-character alphanumeric bearer token.
        /// </summary>
        /// <returns>64-character token string.</returns>
        public static string GenerateBearerToken()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            char[] result = new char[64];
            Span<byte> randomBytes = stackalloc byte[64];
            RandomNumberGenerator.Fill(randomBytes);
            for (int i = 0; i < 64; i++)
                result[i] = chars[randomBytes[i] % chars.Length];
            return new string(result);
        }

        #endregion
    }
}
