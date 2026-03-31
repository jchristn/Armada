namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Service for authorizing API requests.
    /// </summary>
    public interface IAuthorizationService
    {
        /// <summary>
        /// Check if a request is authorized for an endpoint.
        /// </summary>
        /// <param name="ctx">Authentication context.</param>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path.</param>
        /// <returns>True if authorized.</returns>
        bool IsAuthorized(AuthContext ctx, string method, string path);

        /// <summary>
        /// Require authentication. Throws if not authenticated.
        /// </summary>
        /// <param name="ctx">Authentication context.</param>
        /// <exception cref="UnauthorizedAccessException">If not authenticated.</exception>
        void RequireAuth(AuthContext ctx);

        /// <summary>
        /// Require admin privileges. Throws if not admin.
        /// </summary>
        /// <param name="ctx">Authentication context.</param>
        /// <exception cref="UnauthorizedAccessException">If not admin.</exception>
        void RequireAdmin(AuthContext ctx);

        /// <summary>
        /// Require tenant-scoped admin privileges. Global admins also satisfy this requirement.
        /// </summary>
        /// <param name="ctx">Authentication context.</param>
        /// <exception cref="UnauthorizedAccessException">If not tenant admin.</exception>
        void RequireTenantAdmin(AuthContext ctx);
    }
}
