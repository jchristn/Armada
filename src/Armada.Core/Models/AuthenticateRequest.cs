namespace Armada.Core.Models
{
    /// <summary>
    /// Request body for POST /api/v1/authenticate.
    /// </summary>
    public class AuthenticateRequest
    {
        #region Public-Members

        /// <summary>
        /// Tenant identifier (used after tenant selection).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User email address.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// User password (plaintext, will be hashed for verification).
        /// </summary>
        public string? Password { get; set; } = null;

        #endregion
    }
}
