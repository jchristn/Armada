namespace Armada.Core.Models
{
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Represents a user in the multi-tenant system.
    /// </summary>
    public class UserMaster
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (prefix: usr_).
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
        /// User email address.
        /// </summary>
        public string Email
        {
            get => _Email;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Email));
                _Email = value;
            }
        }

        /// <summary>
        /// SHA256 hash of the user's password (hex, lowercase).
        /// </summary>
        public string PasswordSha256
        {
            get => _PasswordSha256;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(PasswordSha256));
                _PasswordSha256 = value;
            }
        }

        /// <summary>
        /// User's first name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// User's last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Whether the user has admin privileges.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user has tenant-scoped admin privileges.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user is protected from direct deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Whether the user is active.
        /// </summary>
        public bool Active { get; set; } = true;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.UserIdPrefix, 24);
        private string _TenantId = Constants.DefaultTenantId;
        private string _Email = Constants.DefaultUserEmail;
        private string _PasswordSha256 = ComputePasswordHash("password");

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UserMaster()
        {
        }

        /// <summary>
        /// Instantiate with tenant, email, and password.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="email">Email address.</param>
        /// <param name="password">Plaintext password (will be hashed).</param>
        public UserMaster(string tenantId, string email, string password)
        {
            TenantId = tenantId;
            Email = email;
            PasswordSha256 = ComputePasswordHash(password);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Compute SHA256 hash of a plaintext password.
        /// </summary>
        /// <param name="plainText">Plaintext password.</param>
        /// <returns>Hex-encoded SHA256 hash (lowercase).</returns>
        public static string ComputePasswordHash(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) throw new ArgumentNullException(nameof(plainText));
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Verify a plaintext password against this user's stored hash.
        /// </summary>
        /// <param name="plainText">Plaintext password to verify.</param>
        /// <returns>True if the password matches.</returns>
        public bool VerifyPassword(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return false;
            return ComputePasswordHash(plainText) == PasswordSha256;
        }

        /// <summary>
        /// Return a copy of the user with the password redacted.
        /// </summary>
        /// <param name="user">User to redact.</param>
        /// <returns>Redacted copy.</returns>
        public static UserMaster Redact(UserMaster user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            return new UserMaster
            {
                Id = user.Id,
                TenantId = user.TenantId,
                Email = user.Email,
                PasswordSha256 = "********",
                FirstName = user.FirstName,
                LastName = user.LastName,
                IsAdmin = user.IsAdmin,
                IsTenantAdmin = user.IsTenantAdmin,
                IsProtected = user.IsProtected,
                Active = user.Active,
                CreatedUtc = user.CreatedUtc,
                LastUpdateUtc = user.LastUpdateUtc
            };
        }

        #endregion
    }
}
