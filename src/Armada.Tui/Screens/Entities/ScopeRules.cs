namespace Armada.Tui.Screens.Entities
{
    using System;
    using Armada.Core.Enums;
    using Armada.Tui.Services;

    /// <summary>
    /// The dashboard's <c>lib/scoping.ts</c> (a mirror of the server's <c>ScopedVisibility</c>) for Category B
    /// configuration entities: who may view, edit, or delete a scoped object, which scope a new object gets, and
    /// whether the user may choose it. The server enforces the same rules; the TUI uses these to hide or disable what
    /// the server would refuse. Thread-safe (stateless).
    /// </summary>
    public static class ScopeRules
    {
        #region Public-Methods

        /// <summary>
        /// Whether the signed-in user may see an object (backend <c>CanView</c>).
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="scope">Object scope.</param>
        /// <param name="tenantId">Object tenant.</param>
        /// <param name="userId">Object owner.</param>
        /// <returns>True when visible.</returns>
        public static bool CanView(SessionService session, ScopeEnum scope, string? tenantId, string? userId)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.IsGlobalAdmin) return true;
            if (!SameTenant(session, tenantId)) return false;
            if (session.IsTenantAdmin) return true;
            return scope == ScopeEnum.TenantWide || (userId != null && String.Equals(userId, UserId(session), StringComparison.Ordinal));
        }

        /// <summary>
        /// Whether the signed-in user may edit or delete an object (backend <c>CanEdit</c>).
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="scope">Object scope.</param>
        /// <param name="tenantId">Object tenant.</param>
        /// <param name="userId">Object owner.</param>
        /// <returns>True when editable.</returns>
        public static bool CanEdit(SessionService session, ScopeEnum scope, string? tenantId, string? userId)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.IsGlobalAdmin) return true;
            if (!SameTenant(session, tenantId)) return false;
            if (session.IsTenantAdmin) return true;
            return scope == ScopeEnum.UserSpecific && userId != null && String.Equals(userId, UserId(session), StringComparison.Ordinal);
        }

        /// <summary>
        /// The scope a new object gets: regular users always create personal objects; admins default to tenant-wide.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="requested">Requested scope, or null.</param>
        /// <returns>Scope.</returns>
        public static ScopeEnum ResolveCreateScope(SessionService session, ScopeEnum? requested)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.IsGlobalAdmin || session.IsTenantAdmin) return requested ?? ScopeEnum.TenantWide;
            return ScopeEnum.UserSpecific;
        }

        /// <summary>
        /// Whether the user may choose an object's scope (admins only).
        /// </summary>
        /// <param name="session">Session.</param>
        /// <returns>True for admins.</returns>
        public static bool CanChooseScope(SessionService session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return session.IsGlobalAdmin || session.IsTenantAdmin;
        }

        /// <summary>
        /// English label of a scope ("Personal" or "Tenant-wide").
        /// </summary>
        /// <param name="scope">Scope.</param>
        /// <returns>Label.</returns>
        public static string Label(ScopeEnum scope)
        {
            return scope == ScopeEnum.UserSpecific ? "Personal" : "Tenant-wide";
        }

        /// <summary>
        /// The signed-in user's id, or null.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <returns>User id.</returns>
        public static string? UserId(SessionService session)
        {
            return session?.Identity?.User?.Id;
        }

        /// <summary>
        /// The signed-in user's tenant id, or null.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <returns>Tenant id.</returns>
        public static string? TenantId(SessionService session)
        {
            return session?.Identity?.Tenant?.Id ?? session?.Identity?.User?.TenantId;
        }

        #endregion

        #region Private-Methods

        private static bool SameTenant(SessionService session, string? tenantId)
        {
            return String.Equals(tenantId ?? "", TenantId(session) ?? "", StringComparison.Ordinal);
        }

        #endregion
    }
}
