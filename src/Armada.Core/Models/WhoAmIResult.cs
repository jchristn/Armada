namespace Armada.Core.Models
{
    /// <summary>
    /// Result of GET /api/v1/whoami.
    /// </summary>
    public class WhoAmIResult
    {
        #region Public-Members

        /// <summary>
        /// Tenant metadata.
        /// </summary>
        public TenantMetadata? Tenant { get; set; } = null;

        /// <summary>
        /// User information (password redacted).
        /// </summary>
        public UserMaster? User { get; set; } = null;

        /// <summary>
        /// True when the caller is a seeded admin still using the default password. Dashboard sessions are limited to
        /// the password change until it is changed (PUT /api/v1/account/password).
        /// </summary>
        public bool PasswordChangeRequired { get; set; } = false;

        /// <summary>
        /// True when the server still has default credentials in use (a seeded admin@armada account with the default
        /// password, or the seeded "default" bearer token). Reported to admins and tenant admins only, for the dashboard
        /// warning banner.
        /// </summary>
        public bool DefaultCredentialsInUse { get; set; } = false;

        #endregion
    }
}
