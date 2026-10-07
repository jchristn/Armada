namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Per-day commit counts for a vessel branch over a date range (GET /api/v1/vessels/{id}/history/activity), for the history heatmap.
    /// </summary>
    public class VesselCommitActivity
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; set; } = String.Empty;

        /// <summary>
        /// Branch the counts were read from (the vessel's default branch unless one was requested).
        /// </summary>
        public string Branch { get; set; } = String.Empty;

        /// <summary>
        /// First day of the range, yyyy-MM-dd, inclusive.
        /// </summary>
        public string From { get; set; } = String.Empty;

        /// <summary>
        /// Last day of the range, yyyy-MM-dd, inclusive.
        /// </summary>
        public string To { get; set; } = String.Empty;

        /// <summary>
        /// UTC offset in minutes the days were bucketed in (for example -420 for UTC-7).
        /// </summary>
        public int UtcOffsetMinutes { get; set; } = 0;

        /// <summary>
        /// One entry per day from From to To inclusive, in date order, including days with zero commits. Never null.
        /// </summary>
        public List<VesselCommitActivityDay> Days { get; set; } = new List<VesselCommitActivityDay>();

        /// <summary>
        /// Total commits in the range.
        /// </summary>
        public int TotalCommits { get; set; } = 0;

        /// <summary>
        /// Largest single-day count in the range (for scaling heatmap intensity).
        /// </summary>
        public int MaxDayCount { get; set; } = 0;

        /// <summary>
        /// Commit date of the oldest commit on the branch (whole history, not just the range), or null when the branch has no commits.
        /// </summary>
        public DateTime? FirstCommitUtc { get; set; } = null;

        /// <summary>
        /// Commit date of the newest commit on the branch, or null when the branch has no commits.
        /// </summary>
        public DateTime? LastCommitUtc { get; set; } = null;

        /// <summary>
        /// Error reading the repository (for example no local clone yet), or null. Returned with HTTP 200 like the branches route.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCommitActivity()
        {
        }

        #endregion
    }
}
