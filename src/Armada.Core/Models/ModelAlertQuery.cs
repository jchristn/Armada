namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Filters and paging for listing model alerts, newest raised first.
    /// </summary>
    public class ModelAlertQuery
    {
        /// <summary>
        /// Tenant, or null for every tenant (global admin).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Status, or null for both.
        /// </summary>
        public ModelAlertStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Model profile identifier, or null.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Persona name (case-insensitive), or null.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Whether ignored alerts are listed. Default true. The access service turns false into
        /// <see cref="NotIgnoredAtUtc"/> = now.
        /// </summary>
        public bool IncludeIgnored { get; set; } = true;

        /// <summary>
        /// When set, alerts ignored past this time are left out (set by the server, not by callers).
        /// </summary>
        public DateTime? NotIgnoredAtUtc { get; set; } = null;

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
