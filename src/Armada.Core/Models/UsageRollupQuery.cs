namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Filters and paging for listing usage hourly rollups, newest hour first with a stable id tie-break.
    /// Hour bounds are half-open [FromHourUtc, ToHourUtc) and are matched against the rolled-up hour
    /// bucket, not the raw observation times inside it.
    /// </summary>
    public class UsageRollupQuery
    {
        /// <summary>
        /// Tenant, or null for every tenant (global admin only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Only rollups for this credential host, or null.
        /// </summary>
        public string? HostId { get; set; } = null;

        /// <summary>
        /// Only rollups for this runtime, or null.
        /// </summary>
        public AgentRuntimeEnum? Runtime { get; set; } = null;

        /// <summary>
        /// Only rollups for this provider, or null.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Only rollups for this account key, or null.
        /// </summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>
        /// Only rollups for this window name, or null. Empty string selects capture-level rollups with no
        /// window.
        /// </summary>
        public string? WindowName { get; set; } = null;

        /// <summary>
        /// Only rollups for this model, or null. Empty string selects account-wide rollups.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Only rollups for hours at or after this UTC time (inclusive), or null.
        /// </summary>
        public DateTime? FromHourUtc { get; set; } = null;

        /// <summary>
        /// Only rollups for hours before this UTC time (exclusive), or null.
        /// </summary>
        public DateTime? ToHourUtc { get; set; } = null;

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