namespace Armada.Core.Authorization
{
    using System.Collections.Generic;

    /// <summary>
    /// Permission levels for API authorization.
    /// </summary>
    public enum PermissionLevel
    {
        /// <summary>
        /// No authentication required.
        /// </summary>
        NoAuthRequired,

        /// <summary>
        /// Requires a valid authenticated identity.
        /// </summary>
        Authenticated,

        /// <summary>
        /// Requires an authenticated admin identity.
        /// </summary>
        AdminOnly,

        /// <summary>
        /// Requires a global admin or a tenant-scoped admin identity.
        /// </summary>
        TenantAdmin
    }

    /// <summary>
    /// Static authorization matrix mapping API endpoints to required permission levels.
    /// </summary>
    public static class AuthorizationConfig
    {
        #region Public-Members

        /// <summary>
        /// Get the required permission level for an endpoint.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path (lowercase, without query string).</param>
        /// <returns>Required permission level.</returns>
        public static PermissionLevel GetPermissionLevel(string method, string path)
        {
            method = method.ToUpperInvariant();
            path = path.ToLowerInvariant();

            // NoAuthRequired endpoints
            if (path.EndsWith("/status/health")) return PermissionLevel.NoAuthRequired;
            if (path.EndsWith("/authenticate") && method == "POST") return PermissionLevel.NoAuthRequired;
            if (path.EndsWith("/tenants/lookup") && method == "POST") return PermissionLevel.NoAuthRequired;
            if (path.EndsWith("/onboarding") && method == "POST") return PermissionLevel.NoAuthRequired;
            if (path.StartsWith("/dashboard")) return PermissionLevel.NoAuthRequired;
            if (path == "/") return PermissionLevel.NoAuthRequired;

            // AdminOnly endpoints
            if (path.StartsWith("/api/v1/server")) return PermissionLevel.AdminOnly;
            if (path.StartsWith("/api/v1/settings")) return PermissionLevel.AdminOnly;
            if (path.StartsWith("/api/v1/backup")) return PermissionLevel.AdminOnly;
            if (path.StartsWith("/api/v1/restore")) return PermissionLevel.AdminOnly;
            if (path.EndsWith("/tenants") && method == "GET") return PermissionLevel.AdminOnly;
            if (path.EndsWith("/tenants") && method == "POST") return PermissionLevel.AdminOnly;
            if (_TenantIdPattern.IsMatch(path) && (method == "PUT" || method == "DELETE")) return PermissionLevel.AdminOnly;

            // TenantAdmin endpoints
            if (path.EndsWith("/users") && method == "POST") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/fleets") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/vessels") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/captains") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/voyages") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/missions") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/playbooks") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/docks") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/signals") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/events") && method != "GET") return PermissionLevel.TenantAdmin;
            if (path.StartsWith("/api/v1/merge-queue") && method != "GET") return PermissionLevel.TenantAdmin;

            // Everything else requires authentication
            return PermissionLevel.Authenticated;
        }

        #endregion

        #region Private-Members

        private static readonly System.Text.RegularExpressions.Regex _TenantIdPattern =
            new System.Text.RegularExpressions.Regex(@"/tenants/[^/]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _UserIdPattern =
            new System.Text.RegularExpressions.Regex(@"/users/[^/]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _CredentialIdPattern =
            new System.Text.RegularExpressions.Regex(@"/credentials/[^/]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        #endregion
    }
}
