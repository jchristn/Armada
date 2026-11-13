namespace Armada.Core.Models
{
    using Armada.Core.Services;

    /// <summary>
    /// Result of a workspace search.
    /// </summary>
    public class WorkspaceSearchResult
    {
        /// <summary>
        /// Search query.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Number of matches returned.
        /// </summary>
        public int TotalMatches { get; set; }

        /// <summary>
        /// True if the result set was truncated.
        /// </summary>
        public bool Truncated { get; set; }

        /// <summary>
        /// Search matches.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxPageSize, Reason = "maxResults is clamped to 1-1000 by the route and by WorkspaceFileEngine.SearchAsync (also on a Harbor)")]
        public List<WorkspaceSearchMatch> Matches { get; set; } = new List<WorkspaceSearchMatch>();
    }
}
