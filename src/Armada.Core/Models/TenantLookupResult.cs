namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result of POST /api/v1/tenants/lookup: one page of the active tenants in which the email exists, ordered by
    /// tenant name then id, with exact totals so a caller can page through the rest.
    /// </summary>
    public class TenantLookupResult
    {
        #region Public-Members

        /// <summary>
        /// One page of tenants matching the email (empty if none, or past the last page).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxPageSize, Reason = "One page; PageSize is at most 1000")]
        public List<TenantListEntry> Tenants { get; set; } = new List<TenantListEntry>();

        /// <summary>
        /// One-based page number returned.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size used (after normalization).
        /// </summary>
        public int PageSize { get; set; } = CollectionLimits.DefaultPageSize;

        /// <summary>
        /// Number of pages.
        /// </summary>
        public int TotalPages { get; set; } = 0;

        /// <summary>
        /// Number of matching tenants across every page.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        #endregion
    }
}
