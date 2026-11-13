namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the provider-neutral filter and order SQL for paging a table with tenant_id, user_id, (optionally) scope,
    /// created_utc, and id columns. The caller's visibility (<see cref="ScopedEnumerationFilter"/>) and the query's
    /// CreatedAfter/CreatedBefore apply in the WHERE clause, so the COUNT and the page agree. Ordering is created_utc in
    /// the query's direction with id as the tie-break, so pages never overlap or skip rows with equal timestamps.
    /// </summary>
    internal static class ScopedEnumerationQueryBuilder
    {
        #region Internal-Methods

        /// <summary>
        /// Build the fragments.
        /// </summary>
        /// <param name="query">Paging, order, and created-time filters.</param>
        /// <param name="filter">Caller visibility.</param>
        /// <param name="hasScopeColumn">Whether the table has a scope column (TenantWide or UserSpecific).</param>
        /// <param name="extraConditions">Further conditions (already parameterized by the caller), or null.</param>
        /// <returns>The fragments.</returns>
        internal static ScopedEnumerationSqlQuery Build(EnumerationQuery query, ScopedEnumerationFilter filter, bool hasScopeColumn, List<string>? extraConditions = null)
        {
            if (query == null) query = new EnumerationQuery();
            if (filter == null) filter = new ScopedEnumerationFilter();

            ScopedEnumerationSqlQuery result = new ScopedEnumerationSqlQuery();
            List<string> conditions = new List<string>();

            if (filter.RestrictTenant)
            {
                if (filter.TenantId == null)
                {
                    conditions.Add("tenant_id IS NULL");
                }
                else
                {
                    conditions.Add("tenant_id = @scope_tenant_id");
                    result.Parameters.Add(new QueryParameter("@scope_tenant_id", filter.TenantId));
                }
            }

            if (filter.RestrictToViewer)
            {
                string owner = filter.ViewerUserId == null ? "user_id IS NULL" : "user_id = @scope_viewer_user_id";
                if (filter.ViewerUserId != null) result.Parameters.Add(new QueryParameter("@scope_viewer_user_id", filter.ViewerUserId));
                if (hasScopeColumn)
                {
                    // A row without a recognizable scope reads as TenantWide (see the providers' readers).
                    conditions.Add("(scope IS NULL OR scope <> @scope_user_specific OR " + owner + ")");
                    result.Parameters.Add(new QueryParameter("@scope_user_specific", ScopeEnum.UserSpecific.ToString()));
                }
                else
                {
                    conditions.Add(owner);
                }
            }

            if (query.CreatedAfter.HasValue)
            {
                conditions.Add("created_utc > @created_after");
                result.Parameters.Add(new QueryParameter("@created_after", query.CreatedAfter.Value));
            }

            if (query.CreatedBefore.HasValue)
            {
                conditions.Add("created_utc < @created_before");
                result.Parameters.Add(new QueryParameter("@created_before", query.CreatedBefore.Value));
            }

            if (extraConditions != null) conditions.AddRange(extraConditions);

            result.Where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";
            result.OrderBy = " ORDER BY created_utc " + direction + ", id " + direction;
            return result;
        }

        #endregion
    }
}
