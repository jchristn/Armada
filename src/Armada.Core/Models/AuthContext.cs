namespace Armada.Core.Models
{
    /// <summary>
    /// Represents the authenticated identity context for a request.
    /// Populated by the authentication service and passed to handlers.
    /// </summary>
    public class AuthContext
    {
        #region Public-Members

        /// <summary>
        /// Whether the request is authenticated.
        /// </summary>
        public bool IsAuthenticated { get; set; } = false;

        /// <summary>
        /// Tenant identifier of the authenticated user.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User identifier of the authenticated user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Whether the authenticated user has admin privileges.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the authenticated user has tenant-scoped admin privileges.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Authentication method used: "Bearer", "Session", "ApiKey", or null.
        /// </summary>
        public string? AuthMethod { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (unauthenticated by default).
        /// </summary>
        public AuthContext()
        {
        }

        /// <summary>
        /// Create an authenticated context.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="isAdmin">Admin flag.</param>
        /// <param name="isTenantAdmin">Tenant admin flag.</param>
        /// <param name="authMethod">Authentication method.</param>
        /// <returns>Authenticated AuthContext.</returns>
        public static AuthContext Authenticated(string tenantId, string userId, bool isAdmin, bool isTenantAdmin, string authMethod)
        {
            return new AuthContext
            {
                IsAuthenticated = true,
                TenantId = tenantId,
                UserId = userId,
                IsAdmin = isAdmin,
                IsTenantAdmin = isTenantAdmin,
                AuthMethod = authMethod
            };
        }

        #endregion
    }
}
