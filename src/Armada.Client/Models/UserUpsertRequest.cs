namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to create or update a user.
    /// </summary>
    public class UserUpsertRequest
    {
        #region Public-Members

        /// <summary>
        /// Tenant id (global admins only), or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Password; blank keeps the stored password on update.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// Pre-hashed password, or null.
        /// </summary>
        public string? PasswordSha256 { get; set; } = null;

        /// <summary>
        /// First name, or null.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name, or null.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Global admin flag (global admins only).
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Tenant admin flag.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Active flag. Default true.
        /// </summary>
        public bool Active { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UserUpsertRequest()
        {
        }

        #endregion
    }
}
