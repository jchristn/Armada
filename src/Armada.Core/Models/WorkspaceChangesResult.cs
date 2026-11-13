namespace Armada.Core.Models
{
    using Armada.Core.Services;

    /// <summary>
    /// Working tree change summary for a vessel workspace: branch state and one page of changed files in git status
    /// order. git status has no paging of its own, so the whole status is read and the page is cut from it;
    /// TotalRecords is exact.
    /// </summary>
    public class WorkspaceChangesResult
    {
        /// <summary>
        /// Current branch name, if available.
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// True if the working tree has any local changes.
        /// </summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// Number of commits ahead of the remote tracking branch.
        /// </summary>
        public int CommitsAhead { get; set; }

        /// <summary>
        /// Number of commits behind the remote tracking branch.
        /// </summary>
        public int CommitsBehind { get; set; }

        /// <summary>
        /// Page number of <see cref="Changes"/> (1-based).
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size used for <see cref="Changes"/> (1 to 1000).
        /// </summary>
        public int PageSize { get; set; } = CollectionLimits.DefaultPageSize;

        /// <summary>
        /// Number of pages.
        /// </summary>
        public int TotalPages { get; set; } = 0;

        /// <summary>
        /// Number of changed files in total, across every page.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// One page of changed files.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxPageSize, Reason = "One page; PageNumber, PageSize, TotalPages, TotalRecords describe the rest")]
        public List<WorkspaceChangeEntry> Changes { get; set; } = new List<WorkspaceChangeEntry>();

        /// <summary>
        /// Optional git inspection error.
        /// </summary>
        public string? Error { get; set; }
    }
}
