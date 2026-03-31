namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Service for creating and validating encrypted session tokens.
    /// </summary>
    public interface ISessionTokenService
    {
        /// <summary>
        /// Create an encrypted session token for a tenant and user.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <returns>Authentication result with token and expiry.</returns>
        AuthenticateResult CreateToken(string tenantId, string userId);

        /// <summary>
        /// Validate and decrypt a session token.
        /// </summary>
        /// <param name="encryptedToken">Base64-encoded encrypted token.</param>
        /// <returns>AuthContext if valid, null if invalid or expired.</returns>
        AuthContext? ValidateToken(string encryptedToken);
    }
}
