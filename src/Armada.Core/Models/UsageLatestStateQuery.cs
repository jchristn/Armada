namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Filters and paging for listing per-account usage latest-state rows. Expired rows (the account's
    /// membership closed) are excluded unless explicitly included, so a stale account never looks live.
    /// </summary>
    public class UsageLatestStateQuery
    {
        /// <summary>
        /// Tenant, or null for every tenant (global admin only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Only state rows for this credential host, or null.
        /// </summary>
        public string? HostId { get; set; } = null;

        /// <summary>
        /// Only state rows for this runtime, or null.
        /// </summary>
        public AgentRuntimeEnum? Runtime { get; set; } = null;

        /// <summary>
        /// Only state rows for this provider, or null.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Only state rows for this account key, or null.
        /// </summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>
        /// When true, rows expired by a closed membership are included. Default false.
        /// </summary>
        public bool IncludeExpired { get; set; } = false;

        /// <summary>
        /// Page number, 1 or more.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size, 1 to 1000.
        /// </summary>
        public int PageSize { get; set; } = 100;
    }
}