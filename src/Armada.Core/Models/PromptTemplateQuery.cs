namespace Armada.Core.Models
{
    /// <summary>
    /// Paged prompt-template query with the caller's visibility applied in the data source, so TotalRecords counts only
    /// templates the caller may see. Paging, created-range filters and Order come from <see cref="EnumerationQuery"/>;
    /// <see cref="EnumerationQuery.TenantId"/> names the visible tenant (null means every tenant, for a global admin).
    /// </summary>
    public class PromptTemplateQuery : EnumerationQuery
    {
        /// <summary>
        /// When set, only tenant-wide templates and templates owned by this user are returned (a regular user's view).
        /// Null returns every template in the visible tenant (a tenant or global admin's view).
        /// </summary>
        public string? ViewerUserId { get; set; } = null;

        /// <summary>
        /// When true, templates stored without a tenant are visible too (MCP list_prompt_templates treats them as
        /// shared defaults). Default false, matching the REST list.
        /// </summary>
        public bool IncludeUntenanted { get; set; } = false;

        /// <summary>
        /// Optional category filter, compared case-insensitively.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// When true, results are ordered by category, then name, then id; otherwise by creation time in
        /// <see cref="EnumerationQuery.Order"/> with the id as the tie-break.
        /// </summary>
        public bool OrderByCategoryAndName { get; set; } = false;

        /// <summary>
        /// Copy the paging, ordering and created-range filters of an <see cref="EnumerationQuery"/>.
        /// </summary>
        /// <param name="query">Source query, or null for defaults.</param>
        /// <returns>A prompt-template query with no visibility restriction.</returns>
        public static PromptTemplateQuery From(EnumerationQuery? query)
        {
            PromptTemplateQuery result = new PromptTemplateQuery();
            if (query == null) return result;
            result.PageNumber = query.PageNumber;
            result.PageSize = query.PageSize;
            result.Order = query.Order;
            result.CreatedAfter = query.CreatedAfter;
            result.CreatedBefore = query.CreatedBefore;
            return result;
        }
    }
}
