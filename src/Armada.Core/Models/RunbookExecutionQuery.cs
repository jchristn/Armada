namespace Armada.Core.Models
{
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Runbook execution enumeration filters.
    /// </summary>
    public class RunbookExecutionQuery
    {
        /// <summary>
        /// Tenant filter; the service sets it from the caller's scope.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User filter; the service sets it from the caller's scope.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Optional runbook filter.
        /// </summary>
        public string? RunbookId { get; set; } = null;

        /// <summary>
        /// Optional deployment filter.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Optional incident filter.
        /// </summary>
        public string? IncidentId { get; set; } = null;

        /// <summary>
        /// Optional status filter.
        /// </summary>
        public RunbookExecutionStatusEnum? Status { get; set; } = null;

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
