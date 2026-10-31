namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Filters and paging for listing usage account memberships. The effective-at filter answers "which
    /// account group did this captain belong to at this time", which is how token records and snapshots are
    /// attributed; a lookup never rewrites history to a captain's current account.
    /// </summary>
    public class UsageMembershipQuery
    {
        /// <summary>
        /// Tenant, or null for every tenant (global admin only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Only memberships of this captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Only memberships on this credential host, or null.
        /// </summary>
        public string? HostId { get; set; } = null;

        /// <summary>
        /// Only memberships for this runtime, or null.
        /// </summary>
        public AgentRuntimeEnum? Runtime { get; set; } = null;

        /// <summary>
        /// Only memberships for this account key, or null.
        /// </summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>
        /// When set, only memberships that cover this UTC instant (effective from inclusive, effective to
        /// exclusive or open) are returned.
        /// </summary>
        public DateTime? EffectiveAtUtc { get; set; } = null;

        /// <summary>
        /// When true, only currently open memberships (no effective-to) are returned.
        /// </summary>
        public bool OpenOnly { get; set; } = false;

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