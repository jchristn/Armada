namespace Armada.Core.Models
{
    /// <summary>
    /// Result of an authentication attempt.
    /// </summary>
    public class AuthenticateResult
    {
        #region Public-Members

        /// <summary>
        /// Whether authentication was successful.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Encrypted session token (returned on successful email/password authentication).
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// Session token expiration timestamp in UTC.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// True when the authenticated user must change the default password before using the API with this session.
        /// </summary>
        public bool PasswordChangeRequired { get; set; } = false;

        #endregion
    }
}
