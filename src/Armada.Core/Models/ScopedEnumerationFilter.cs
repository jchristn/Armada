namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Caller visibility applied in the data source before a page is counted and cut, so TotalRecords is exact for the
    /// caller. Built from an <see cref="AuthContext"/> with the factory that matches the entity's visibility rule.
    /// </summary>
    public class ScopedEnumerationFilter
    {
        #region Public-Members

        /// <summary>
        /// When true, only rows whose tenant is <see cref="TenantId"/> (a null TenantId matches rows with no tenant).
        /// When false, every tenant.
        /// </summary>
        public bool RestrictTenant { get; set; } = false;

        /// <summary>
        /// Tenant the rows must belong to when <see cref="RestrictTenant"/> is true.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// When true, only tenant-wide rows plus user-specific rows owned by <see cref="ViewerUserId"/> (rows of a scoped
        /// entity; see <see cref="ScopedVisibility.CanView"/>). When false, every row in the tenant scope.
        /// </summary>
        public bool RestrictToViewer { get; set; } = false;

        /// <summary>
        /// Viewer whose user-specific rows are included when <see cref="RestrictToViewer"/> is true.
        /// </summary>
        public string? ViewerUserId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a filter that sees every row.
        /// </summary>
        public ScopedEnumerationFilter()
        {
        }

        /// <summary>
        /// The filter for a scoped configuration entity, matching <see cref="ScopedVisibility.CanView"/>: an admin sees
        /// every row; a tenant admin sees the tenant's rows; anyone else sees the tenant's tenant-wide rows and their own.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <returns>The filter.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="auth"/> is null.</exception>
        public static ScopedEnumerationFilter ForScopedEntity(AuthContext auth)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            ScopedEnumerationFilter filter = new ScopedEnumerationFilter();
            if (auth.IsAdmin) return filter;
            filter.RestrictTenant = true;
            filter.TenantId = auth.TenantId;
            if (auth.IsTenantAdmin) return filter;
            filter.RestrictToViewer = true;
            filter.ViewerUserId = auth.UserId;
            return filter;
        }

        /// <summary>
        /// A filter limited to one tenant's rows.
        /// </summary>
        /// <param name="tenantId">Tenant, or null for rows with no tenant.</param>
        /// <returns>The filter.</returns>
        public static ScopedEnumerationFilter ForTenant(string? tenantId)
        {
            return new ScopedEnumerationFilter { RestrictTenant = true, TenantId = tenantId };
        }

        #endregion
    }
}
