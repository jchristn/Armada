namespace Armada.Core.Services
{
    using Armada.Core.Authorization;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for authorizing API requests using the authorization matrix.
    /// </summary>
    public class AuthorizationService : IAuthorizationService
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AuthorizationService()
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool IsAuthorized(AuthContext ctx, string method, string path)
        {
            PermissionLevel required = AuthorizationConfig.GetPermissionLevel(method, path);

            switch (required)
            {
                case PermissionLevel.NoAuthRequired:
                    return true;

                case PermissionLevel.Authenticated:
                    return ctx.IsAuthenticated;

                case PermissionLevel.AdminOnly:
                    return ctx.IsAuthenticated && ctx.IsAdmin;

                case PermissionLevel.TenantAdmin:
                    return ctx.IsAuthenticated && (ctx.IsAdmin || ctx.IsTenantAdmin);

                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public void RequireAuth(AuthContext ctx)
        {
            if (ctx == null || !ctx.IsAuthenticated)
                throw new UnauthorizedAccessException("Authentication required.");
        }

        /// <inheritdoc />
        public void RequireAdmin(AuthContext ctx)
        {
            RequireAuth(ctx);
            if (!ctx.IsAdmin)
                throw new UnauthorizedAccessException("Admin privileges required.");
        }

        /// <inheritdoc />
        public void RequireTenantAdmin(AuthContext ctx)
        {
            RequireAuth(ctx);
            if (!ctx.IsAdmin && !ctx.IsTenantAdmin)
                throw new UnauthorizedAccessException("Tenant admin privileges required.");
        }

        #endregion
    }
}
