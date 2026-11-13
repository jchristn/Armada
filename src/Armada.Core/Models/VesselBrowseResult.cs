namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result of browsing a directory for vessel import: one page of its browsable subdirectories by name. When no path
    /// was requested, <see cref="Path"/> is null and <see cref="Entries"/> lists the allowed roots.
    /// </summary>
    public class VesselBrowseResult
    {
        #region Public-Members

        /// <summary>
        /// The directory listed, or null when the allowed roots were listed.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Parent directory when it is still inside an allowed root, otherwise null.
        /// </summary>
        public string? Parent { get; set; } = null;

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
        /// Number of browsable subdirectories (or allowed roots) across every page.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// True when the directory has more entries than the browse examines (VesselDiscoveryService.MaxBrowseEntries);
        /// TotalRecords then counts only the examined ones. Narrow the path to see the rest.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// One page of subdirectories ordered by name. Never null.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxPageSize, Reason = "One page; PageNumber, PageSize, TotalPages, TotalRecords describe the rest")]
        public List<VesselBrowseEntry> Entries
        {
            get => _Entries;
            set => _Entries = value ?? new List<VesselBrowseEntry>();
        }

        #endregion

        #region Private-Members

        private List<VesselBrowseEntry> _Entries = new List<VesselBrowseEntry>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselBrowseResult()
        {
        }

        #endregion
    }
}
