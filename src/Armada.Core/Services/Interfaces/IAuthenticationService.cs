namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Service for authenticating API requests.
    /// </summary>
    public interface IAuthenticationService
    {
        /// <summary>
        /// Authenticate a request from headers (Bearer token, session token, or API key).
        /// </summary>
        /// <param name="authorizationHeader">Authorization header value (e.g. "Bearer xxx").</param>
        /// <param name="sessionTokenHeader">X-Token header value.</param>
        /// <param name="apiKeyHeader">X-Api-Key header value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AuthContext (may be unauthenticated if no valid credentials found).</returns>
        Task<AuthContext> AuthenticateAsync(string? authorizationHeader, string? sessionTokenHeader, string? apiKeyHeader, CancellationToken token = default);

        /// <summary>
        /// Authenticate with email and password credentials.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="email">User email.</param>
        /// <param name="password">Plaintext password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AuthContext (may be unauthenticated if credentials are invalid).</returns>
        Task<AuthContext> AuthenticateWithCredentialsAsync(string tenantId, string email, string password, CancellationToken token = default);
    }
}
