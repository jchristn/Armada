namespace Armada.Core.Models
{
    using Armada.Core.Services;

    /// <summary>
    /// Result of listing one workspace directory: one page of its direct children (directories first, then files, each
    /// by name). The page is cut on the host that has the checkout (the Admiral or a Harbor).
    /// </summary>
    public class WorkspaceTreeResult
    {
        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Absolute workspace root path.
        /// </summary>
        public string RootPath { get; set; } = string.Empty;

        /// <summary>
        /// Current directory relative path using forward slashes.
        /// Empty means the workspace root.
        /// </summary>
        public string CurrentPath { get; set; } = string.Empty;

        /// <summary>
        /// Parent directory relative path, or null at root.
        /// </summary>
        public string? ParentPath { get; set; }

        /// <summary>
        /// Page number of <see cref="Entries"/> (1-based).
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size used for <see cref="Entries"/> (1 to 1000).
        /// </summary>
        public int PageSize { get; set; } = CollectionLimits.DefaultPageSize;

        /// <summary>
        /// Number of pages.
        /// </summary>
        public int TotalPages { get; set; } = 0;

        /// <summary>
        /// Number of direct children in total, across every page.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// One page of the direct child entries of the current directory.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxPageSize, Reason = "One page; PageNumber, PageSize, TotalPages, TotalRecords describe the rest")]
        public List<WorkspaceTreeEntry> Entries { get; set; } = new List<WorkspaceTreeEntry>();
    }
}
