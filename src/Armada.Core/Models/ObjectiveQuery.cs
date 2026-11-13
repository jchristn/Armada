namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Query for objective enumeration.
    /// </summary>
    public class ObjectiveQuery
    {
        /// <summary>
        /// Tenant scope or filter.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User scope or filter.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Optional owner filter.
        /// </summary>
        public string? Owner { get; set; } = null;

        /// <summary>
        /// Optional category filter.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// Optional parent objective filter.
        /// </summary>
        public string? ParentObjectiveId { get; set; } = null;

        /// <summary>
        /// Optional vessel filter.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Optional fleet filter.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Optional planning-session filter.
        /// </summary>
        public string? PlanningSessionId { get; set; } = null;

        /// <summary>
        /// Optional voyage filter.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Optional mission filter.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Optional check-run filter.
        /// </summary>
        public string? CheckRunId { get; set; } = null;

        /// <summary>
        /// Optional release filter.
        /// </summary>
        public string? ReleaseId { get; set; } = null;

        /// <summary>
        /// Optional deployment filter.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Optional incident filter.
        /// </summary>
        public string? IncidentId { get; set; } = null;

        /// <summary>
        /// Optional tag filter.
        /// </summary>
        public string? Tag { get; set; } = null;

        /// <summary>
        /// Optional state filter.
        /// </summary>
        public ObjectiveStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Optional backlog-state filter.
        /// </summary>
        public ObjectiveBacklogStateEnum? BacklogState { get; set; } = null;

        /// <summary>
        /// Optional kind filter.
        /// </summary>
        public ObjectiveKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Optional priority filter.
        /// </summary>
        public ObjectivePriorityEnum? Priority { get; set; } = null;

        /// <summary>
        /// Optional effort filter.
        /// </summary>
        public ObjectiveEffortEnum? Effort { get; set; } = null;

        /// <summary>
        /// Optional target-version filter.
        /// </summary>
        public string? TargetVersion { get; set; } = null;

        /// <summary>
        /// Optional free-text search.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Optional lower-bound UTC timestamp.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Optional upper-bound UTC timestamp.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Page number (1-based). Default 1; a smaller value is normalized to 1.
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Page size. Default 50, minimum 1, maximum 1000 (CollectionLimits.MaxPageSize); a value outside the range is
        /// normalized to the nearest bound.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set
            {
                if (value < CollectionLimits.MinPageSize) _PageSize = CollectionLimits.MinPageSize;
                else if (value > CollectionLimits.MaxPageSize) _PageSize = CollectionLimits.MaxPageSize;
                else _PageSize = value;
            }
        }

        /// <summary>
        /// Zero-based offset.
        /// </summary>
        public int Offset => (PageNumber - 1) * PageSize;

        private int _PageNumber = 1;
        private int _PageSize = 50;
    }
}
