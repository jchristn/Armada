namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One page of a vessel's commit history, newest first (GET /api/v1/vessels/{id}/history/commits).
    /// </summary>
    public class VesselCommitPage
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; set; } = String.Empty;

        /// <summary>
        /// Branch the history was read from.
        /// </summary>
        public string Branch { get; set; } = String.Empty;

        /// <summary>
        /// Commits, newest commit date first. Never null.
        /// </summary>
        public List<VesselCommit> Commits { get; set; } = new List<VesselCommit>();

        /// <summary>
        /// Opaque cursor for the next (older) page, or null when this is the last page. Pass it back as cursor with no other filters.
        /// </summary>
        public string? NextCursor { get; set; } = null;

        /// <summary>
        /// Error reading the repository, or null. Returned with HTTP 200 like the branches route.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCommitPage()
        {
        }

        #endregion
    }
}
