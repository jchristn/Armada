namespace Armada.Core.Models
{
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Runbook enumeration filters.
    /// </summary>
    public class RunbookQuery
    {
        /// <summary>
        /// Tenant filter; the service sets it from the caller's scope (a global admin may set it).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// When set, only tenant-wide runbooks and this user's own user-specific runbooks; the service sets it for a
        /// caller who is not an admin.
        /// </summary>
        public string? VisibleToUserId { get; set; } = null;

        /// <summary>
        /// Optional workflow-profile filter.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Optional environment filter.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Optional suggested default check type filter.
        /// </summary>
        public CheckRunTypeEnum? DefaultCheckType { get; set; } = null;

        /// <summary>
        /// Optional active-state filter.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Optional search filter.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// One-based page number. Values below 1 are normalized to 1.
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Page size. Default 50, minimum 1, maximum 1000; values outside the range are normalized to the nearest bound.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = value < CollectionLimits.MinPageSize
                ? CollectionLimits.MinPageSize
                : (value > CollectionLimits.MaxPageSize ? CollectionLimits.MaxPageSize : value);
        }

        /// <summary>
        /// Zero-based row offset of the page.
        /// </summary>
        public int Offset => (PageNumber - 1) * PageSize;

        private int _PageNumber = 1;
        private int _PageSize = 50;
    }
}
