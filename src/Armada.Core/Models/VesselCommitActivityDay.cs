namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Commit count for one calendar day of a vessel's history (one heatmap cell).
    /// </summary>
    public class VesselCommitActivityDay
    {
        #region Public-Members

        /// <summary>
        /// Calendar day, yyyy-MM-dd, in the requested UTC offset.
        /// </summary>
        public string Date { get; set; } = String.Empty;

        /// <summary>
        /// Commits on the branch whose commit date falls on this day.
        /// </summary>
        public int Count { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCommitActivityDay()
        {
        }

        #endregion
    }
}
