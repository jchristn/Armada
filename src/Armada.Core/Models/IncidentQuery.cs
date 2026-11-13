namespace Armada.Core.Models
{
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Incident enumeration filters.
    /// </summary>
    public class IncidentQuery
    {
        /// <summary>
        /// Optional tenant filter.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Optional user filter.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Optional vessel filter.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Optional environment filter.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Optional deployment filter.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Optional release filter.
        /// </summary>
        public string? ReleaseId { get; set; } = null;

        /// <summary>
        /// Optional mission filter.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Optional voyage filter.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Optional status filter.
        /// </summary>
        public IncidentStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Optional severity filter.
        /// </summary>
        public IncidentSeverityEnum? Severity { get; set; } = null;

        /// <summary>
        /// When true, only incidents with a FailureKind (opened by autonomous mission recovery); when false, only
        /// incidents without one; null for both.
        /// </summary>
        public bool? HasFailureKind { get; set; } = null;

        /// <summary>
        /// Optional free-text filter.
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
