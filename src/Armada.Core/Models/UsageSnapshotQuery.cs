namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Filters and paging for listing usage snapshots, newest observation first with a stable id
    /// tie-break. Times are UTC and half-open [FromUtc, ToUtc). Tenant is always supplied by the server
    /// from the caller's context, never trusted from a request body.
    /// </summary>
    public class UsageSnapshotQuery
    {
        /// <summary>
        /// Tenant, or null for every tenant (global admin only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Only snapshots whose triggering captain or member links match this captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Only snapshots captured on this Harbor, or null.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Only snapshots for this runtime, or null.
        /// </summary>
        public AgentRuntimeEnum? Runtime { get; set; } = null;

        /// <summary>
        /// Only snapshots for this provider, or null.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Only snapshots for this account key, or null.
        /// </summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>
        /// Only snapshots observed at or after this UTC time (inclusive), or null.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Only snapshots observed before this UTC time (exclusive), or null.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Only snapshots with this status, or null.
        /// </summary>
        public UsageSnapshotStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Page number, 1 or more.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size, 1 to 1000.
        /// </summary>
        public int PageSize { get; set; } = 25;
    }
}