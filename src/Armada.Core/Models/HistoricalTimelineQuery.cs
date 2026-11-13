namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Query for Armada historical timeline aggregation: the standard <see cref="EnumerationQuery"/> paging (PageNumber,
    /// PageSize 1 to 1000, default 50 here) and Order (CreatedDescending, the default, is newest first by occurrence
    /// time; CreatedAscending is oldest first), its TenantId, UserId, VesselId, MissionId, and VoyageId filters, plus the
    /// timeline-specific filters below.
    /// </summary>
    public class HistoricalTimelineQuery : EnumerationQuery
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the timeline's default page size of 50.
        /// </summary>
        public HistoricalTimelineQuery()
        {
            PageSize = 50;
        }

        #endregion

        /// <summary>
        /// Optional objective filter.
        /// </summary>
        public string? ObjectiveId { get; set; } = null;

        /// <summary>
        /// Optional environment filter.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Optional deployment filter.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Optional incident filter.
        /// </summary>
        public string? IncidentId { get; set; } = null;

        /// <summary>
        /// Optional filter that narrows results to incidents with postmortem data
        /// and the current Armada-side lifecycle entries directly linked to them.
        /// </summary>
        public bool PostmortemOnly { get; set; } = false;

        /// <summary>
        /// When true, request-history entries for read-only HTTP methods (GET, HEAD, OPTIONS) are left out,
        /// so the timeline is not dominated by dashboard and client polling. Defaults to false.
        /// </summary>
        public bool ExcludeReadRequests { get; set; } = false;

        /// <summary>
        /// Optional principal or actor text filter.
        /// </summary>
        public string? Actor { get; set; } = null;

        /// <summary>
        /// Optional free-text search across titles and descriptions.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Optional source-type filters.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> SourceTypes { get; set; } = new List<string>();

        /// <summary>
        /// Optional lower-bound time filter.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Optional upper-bound time filter.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;
    }
}
