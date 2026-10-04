namespace Armada.Core.Models
{
    /// <summary>
    /// Request body for PUT /api/v1/account/password (self-service password change).
    /// </summary>
    public class PasswordChangeRequest
    {
        #region Public-Members

        /// <summary>
        /// The caller's current password.
        /// </summary>
        public string? CurrentPassword { get; set; } = null;

        /// <summary>
        /// The new password (at least <see cref="MinimumLength"/> characters, and not the default password).
        /// </summary>
        public string? NewPassword { get; set; } = null;

        /// <summary>
        /// Minimum accepted length of a new password.
        /// </summary>
        public const int MinimumLength = 8;

        #endregion
    }
}
