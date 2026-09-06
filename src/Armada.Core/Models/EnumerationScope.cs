namespace Armada.Core.Models
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Resolves the effective (tenant, user) scope for an enumeration from the caller's <see cref="AuthContext"/>
    /// and the query's optional owner filters, implementing the three-tier visibility model:
    /// a regular user is locked to their own records; a tenant admin sees their whole tenant and may narrow to a
    /// specific user via the query's UserId; a global admin sees everything and may narrow by TenantId and/or
    /// UserId.
    /// </summary>
    public class EnumerationScope
    {
        #region Public-Members

        /// <summary>
        /// When true, enumerate globally (no tenant or user filter) -- only ever set for a global admin with no
        /// narrowing filters.
        /// </summary>
        public bool All { get; private set; } = false;

        /// <summary>
        /// Effective tenant to scope to, or null when <see cref="All"/> is true.
        /// </summary>
        public string? TenantId { get; private set; } = null;

        /// <summary>
        /// Effective owner user to scope to, or null to include all users in the resolved tenant.
        /// </summary>
        public string? UserId { get; private set; } = null;

        #endregion

        #region Constructors-and-Factories

        private EnumerationScope()
        {
        }

        /// <summary>
        /// Resolve the effective scope for the caller and query filters.
        /// </summary>
        /// <param name="auth">Caller authentication context.</param>
        /// <param name="queryTenantId">Optional tenant filter from the query (honored only for a global admin).</param>
        /// <param name="queryUserId">Optional owner-user filter from the query (honored only for admins).</param>
        /// <returns>The resolved scope.</returns>
        /// <exception cref="ArgumentNullException">Thrown when auth is null.</exception>
        public static EnumerationScope Resolve(AuthContext auth, string? queryTenantId, string? queryUserId)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));

            EnumerationScope scope = new EnumerationScope();

            if (auth.IsAdmin)
            {
                if (!String.IsNullOrEmpty(queryTenantId) && !String.IsNullOrEmpty(queryUserId))
                {
                    scope.TenantId = queryTenantId;
                    scope.UserId = queryUserId;
                }
                else if (!String.IsNullOrEmpty(queryTenantId))
                {
                    scope.TenantId = queryTenantId;
                }
                else
                {
                    scope.All = true;
                }
            }
            else if (auth.IsTenantAdmin)
            {
                scope.TenantId = auth.TenantId;
                if (!String.IsNullOrEmpty(queryUserId)) scope.UserId = queryUserId;
            }
            else
            {
                scope.TenantId = auth.TenantId;
                scope.UserId = auth.UserId;
            }

            return scope;
        }

        /// <summary>
        /// Resolve scope and invoke the matching enumeration overload: global (no filter), tenant-scoped, or
        /// tenant+user-scoped. Encapsulates the three-tier branch so every route/list endpoint stays consistent
        /// and automatically honors the query's owner filters for privileged callers.
        /// </summary>
        /// <typeparam name="T">Enumerated entity type.</typeparam>
        /// <param name="auth">Caller authentication context.</param>
        /// <param name="query">Enumeration query (its TenantId/UserId drive admin narrowing).</param>
        /// <param name="all">Unscoped enumeration (global admin).</param>
        /// <param name="byTenant">Tenant-scoped enumeration.</param>
        /// <param name="byTenantUser">Tenant+user-scoped enumeration.</param>
        /// <returns>The enumeration result.</returns>
        public static Task<EnumerationResult<T>> EnumerateScopedAsync<T>(
            AuthContext auth,
            EnumerationQuery query,
            Func<EnumerationQuery, Task<EnumerationResult<T>>> all,
            Func<string, EnumerationQuery, Task<EnumerationResult<T>>> byTenant,
            Func<string, string, EnumerationQuery, Task<EnumerationResult<T>>> byTenantUser)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            EnumerationScope scope = Resolve(auth, query.TenantId, query.UserId);
            if (scope.All) return all(query);
            if (!String.IsNullOrEmpty(scope.UserId)) return byTenantUser(scope.TenantId!, scope.UserId!, query);
            return byTenant(scope.TenantId!, query);
        }

        #endregion
    }
}
