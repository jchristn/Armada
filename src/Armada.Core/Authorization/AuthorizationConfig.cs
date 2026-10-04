namespace Armada.Core.Authorization
{
    using System;

    /// <summary>
    /// Path-based lookup over <see cref="RouteAuthorizationRegistry"/>. Kept for callers that only have a concrete
    /// request path; the requirement itself is declared per route in the registry, not by URL prefix.
    /// </summary>
    public static class AuthorizationConfig
    {
        #region Public-Methods

        /// <summary>
        /// Get the required permission level for a concrete request.
        /// Dashboard and root paths (served by the default route, not by a declared API route) need no authentication.
        /// An API path that matches no declared route is <see cref="PermissionLevel.AdminOnly"/> (fail closed).
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path (without query string).</param>
        /// <returns>Required permission level.</returns>
        public static PermissionLevel GetPermissionLevel(string method, string path)
        {
            return GetRequirement(method, path).Level;
        }

        /// <summary>
        /// Get the declared requirement for a concrete request.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path (without query string).</param>
        /// <returns>Requirement (never null).</returns>
        public static AuthorizationRequirement GetRequirement(string method, string path)
        {
            if (String.IsNullOrEmpty(method)) method = "GET";
            if (String.IsNullOrEmpty(path)) path = "/";

            if (RouteAuthorizationRegistry.TryResolvePath(method, path, out AuthorizationRequirement? requirement, out string? _) && requirement != null)
                return requirement;

            string lower = path.ToLowerInvariant();
            if (lower == "/" || lower.StartsWith("/dashboard", StringComparison.Ordinal) || lower.StartsWith("/assets/", StringComparison.Ordinal) || lower.StartsWith("/img/", StringComparison.Ordinal))
                return new AuthorizationRequirement("Dashboard", ResourceOperationEnum.Read, PermissionLevel.NoAuthRequired);

            return new AuthorizationRequirement("Undeclared", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
        }

        #endregion
    }
}
