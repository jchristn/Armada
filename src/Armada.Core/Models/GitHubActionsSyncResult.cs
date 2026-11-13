namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result payload returned after importing GitHub Actions runs.
    /// </summary>
    public class GitHubActionsSyncResult
    {
        /// <summary>
        /// GitHub provider name used for imported runs.
        /// </summary>
        public string ProviderName { get; set; } = "GitHubActions";

        /// <summary>
        /// Vessel identifier used for the sync.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Deployment identifier linked during sync when supplied.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Number of imported runs that were newly created.
        /// </summary>
        public int CreatedCount { get; set; } = 0;

        /// <summary>
        /// Number of imported runs that updated existing Armada records.
        /// </summary>
        public int UpdatedCount { get; set; } = 0;

        /// <summary>
        /// GitHub page that was read (1-based), as normalized from the request.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Runs requested per GitHub page (the request's RunCount, normalized to 1..100).
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// Workflow runs GitHub reports for the branch, status and commit filters (its <c>total_count</c>), across every
        /// page. Read the next page with a higher PageNumber; nothing beyond this page was imported.
        /// </summary>
        public long SourceTotalCount { get; set; } = 0;

        /// <summary>
        /// GitHub pages at PageSize runs per page for SourceTotalCount runs.
        /// </summary>
        public int SourceTotalPages { get; set; } = 0;

        /// <summary>
        /// Runs GitHub returned on this page.
        /// </summary>
        public int FetchedCount { get; set; } = 0;

        /// <summary>
        /// Runs on this page that were not imported because they did not match WorkflowName or CommitHash.
        /// </summary>
        public int FilteredOutCount { get; set; } = 0;

        /// <summary>
        /// Ids of the imported or refreshed check runs (read them with GET /api/v1/check-runs/{id} or the check-run
        /// enumeration). A mutation result carries ids, not the runs (docs/ENUMERATION.md).
        /// </summary>
        [CollectionBound(100, Reason = "One per imported run on one GitHub page; RunCount is normalized to 1..100")]
        public List<string> CheckRunIds { get; set; } = new List<string>();
    }
}
