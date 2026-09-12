namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Visibility and edit-permission rules for scoped configuration entities (those carrying a
    /// <see cref="ScopeEnum"/>). Centralizes the contract: everyone in a tenant can see tenant-wide objects
    /// plus their own user-specific ones; only the owning user (or a tenant/global admin) may edit a
    /// user-specific object; only tenant/global admins may edit a tenant-wide object; and regular users may
    /// only create user-specific objects.
    /// </summary>
    public static class ScopedVisibility
    {
        /// <summary>
        /// Whether the caller may view an object with the given scope and ownership.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="scope">Object scope.</param>
        /// <param name="tenantId">Object's owning tenant.</param>
        /// <param name="ownerUserId">Object's owning user (for user-specific objects).</param>
        /// <returns>True when visible.</returns>
        public static bool CanView(AuthContext auth, ScopeEnum scope, string? tenantId, string? ownerUserId)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (auth.IsAdmin) return true;
            if (!String.Equals(tenantId, auth.TenantId, StringComparison.Ordinal)) return false;
            if (auth.IsTenantAdmin) return true;
            return scope == ScopeEnum.TenantWide || String.Equals(ownerUserId, auth.UserId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether the caller may edit or delete an object with the given scope and ownership.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="scope">Object scope.</param>
        /// <param name="tenantId">Object's owning tenant.</param>
        /// <param name="ownerUserId">Object's owning user (for user-specific objects).</param>
        /// <returns>True when editable/deletable.</returns>
        public static bool CanEdit(AuthContext auth, ScopeEnum scope, string? tenantId, string? ownerUserId)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (auth.IsAdmin) return true;
            if (!String.Equals(tenantId, auth.TenantId, StringComparison.Ordinal)) return false;
            if (auth.IsTenantAdmin) return true;
            return scope == ScopeEnum.UserSpecific && String.Equals(ownerUserId, auth.UserId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolve the scope a newly created object should have for this caller. Regular users can only create
        /// user-specific objects; tenant/global admins may create either and default to tenant-wide.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="requested">Requested scope, or null to use the role default.</param>
        /// <returns>The effective scope to persist.</returns>
        public static ScopeEnum ResolveCreateScope(AuthContext auth, ScopeEnum? requested)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (auth.IsAdmin || auth.IsTenantAdmin) return requested ?? ScopeEnum.TenantWide;
            return ScopeEnum.UserSpecific;
        }
    }
}
