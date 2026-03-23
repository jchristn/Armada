namespace Armada.Server
{
    /// <summary>
    /// Request model for creating or updating a user.
    /// </summary>
    public class UserUpsertRequest
    {
        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>
        /// User email address.
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Optional plaintext password. If supplied, the server will hash it.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Optional pre-hashed password for backward compatibility.
        /// </summary>
        public string? PasswordSha256 { get; set; }

        /// <summary>
        /// User's first name.
        /// </summary>
        public string? FirstName { get; set; }

        /// <summary>
        /// User's last name.
        /// </summary>
        public string? LastName { get; set; }

        /// <summary>
        /// Whether the user has global admin privileges.
        /// </summary>
        public bool IsAdmin { get; set; }

        /// <summary>
        /// Whether the user has tenant admin privileges.
        /// </summary>
        public bool IsTenantAdmin { get; set; }

        /// <summary>
        /// Whether the user is active.
        /// </summary>
        public bool Active { get; set; } = true;
    }
}
