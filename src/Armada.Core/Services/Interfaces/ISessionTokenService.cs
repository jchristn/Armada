namespace Armada.Core.Services.Interfaces
{
    using System;
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
        /// Create a short-lived session token bound to an Ask Armada thread. The token authenticates as the user on the
        /// MCP server only, and every tool call made with it is gated by the thread's approval policy.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier (the thread owner).</param>
        /// <param name="askThreadId">Thread identifier (ath_ prefix).</param>
        /// <param name="lifetime">Token lifetime; clamped to 1 minute .. 24 hours.</param>
        /// <returns>Authentication result with token and expiry.</returns>
        AuthenticateResult CreateThreadScopedToken(string tenantId, string userId, string askThreadId, TimeSpan lifetime);

        /// <summary>
        /// Validate and decrypt a session token.
        /// </summary>
        /// <param name="encryptedToken">Base64-encoded encrypted token.</param>
        /// <returns>AuthContext if valid, null if invalid or expired.</returns>
        AuthContext? ValidateToken(string encryptedToken);
    }
}
