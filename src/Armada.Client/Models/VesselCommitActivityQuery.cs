namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Filters for a vessel's per-day commit counts.
    /// </summary>
    public class VesselCommitActivityQuery
    {
        #region Public-Members

        /// <summary>
        /// Branch, or null for the vessel's default branch.
        /// </summary>
        public string? Branch { get; set; } = null;

        /// <summary>
        /// First day, yyyy-MM-dd, or null for 364 days before To.
        /// </summary>
        public string? From { get; set; } = null;

        /// <summary>
        /// Last day, yyyy-MM-dd, or null for today in the requested offset. At most 1830 days after From.
        /// </summary>
        public string? To { get; set; } = null;

        /// <summary>
        /// UTC offset in minutes to bucket days in (-840 to 840), or null for UTC.
        /// </summary>
        public int? UtcOffsetMinutes { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCommitActivityQuery()
        {
        }

        #endregion
    }
}
