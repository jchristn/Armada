namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Who may see and decide CLI permission requests, shared by every surface (REST, WebSocket, MCP, inbox):
    /// <list type="bullet">
    /// <item>See: a global admin; the tenant's tenant admins; the owner of the thread or mission.</item>
    /// <item>Decide (allow once or deny): a global admin; the tenant's tenant admins; the owner only when
    /// Permissions.AllowOwnerApproval is on.</item>
    /// <item>Remember (store an allow rule with the decision): a global admin; the tenant's tenant admins.</item>
    /// </list>
    /// A caller using a mission- or thread-scoped session token (a captain) can never decide or remember.
    /// </summary>
    public static class CliPermissionAccess
    {
        #region Public-Methods

        /// <summary>
        /// Whether the caller is a captain session (a mission- or thread-scoped token).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <returns>True for a scoped captain session.</returns>
        public static bool IsCaptainSession(AuthContext caller)
        {
            if (caller == null) return false;
            return !String.IsNullOrEmpty(caller.AskThreadId) || !String.IsNullOrEmpty(caller.MissionId);
        }

        /// <summary>
        /// Whether the caller may see a request.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Request.</param>
        /// <returns>True when visible.</returns>
        public static bool CanView(AuthContext caller, CliPermissionRequest request)
        {
            if (caller == null || request == null || !caller.IsAuthenticated) return false;
            if (IsCaptainSession(caller)) return false;
            if (caller.IsAdmin) return true;
            if (!String.Equals(caller.TenantId, request.TenantId, StringComparison.Ordinal)) return false;
            if (caller.IsTenantAdmin) return true;
            return !String.IsNullOrEmpty(caller.UserId) && String.Equals(caller.UserId, request.UserId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether the caller may allow once or deny a request.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Request.</param>
        /// <param name="settings">Settings (Permissions.AllowOwnerApproval).</param>
        /// <returns>True when the caller may decide.</returns>
        public static bool CanDecide(AuthContext caller, CliPermissionRequest request, CliPermissionSettings settings)
        {
            if (!CanView(caller, request)) return false;
            if (IsAdminFor(caller, request.TenantId)) return true;
            return settings != null && settings.AllowOwnerApproval;
        }

        /// <summary>
        /// Whether the caller may store an allow rule with a decision.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Request.</param>
        /// <returns>True for admins of the request's tenant.</returns>
        public static bool CanRemember(AuthContext caller, CliPermissionRequest request)
        {
            if (!CanView(caller, request)) return false;
            return IsAdminFor(caller, request.TenantId);
        }

        /// <summary>
        /// Whether the caller is a global admin or a tenant admin of the tenant (and not a captain session).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="tenantId">Tenant.</param>
        /// <returns>True for an admin of the tenant.</returns>
        public static bool IsAdminFor(AuthContext caller, string? tenantId)
        {
            if (caller == null || !caller.IsAuthenticated || IsCaptainSession(caller)) return false;
            if (caller.IsAdmin) return true;
            return caller.IsTenantAdmin && !String.IsNullOrEmpty(tenantId) && String.Equals(caller.TenantId, tenantId, StringComparison.Ordinal);
        }

        #endregion
    }
}
