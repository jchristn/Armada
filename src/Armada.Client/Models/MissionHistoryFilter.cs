namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Filters for the mission history chart.
    /// </summary>
    public class MissionHistoryFilter
    {
        #region Public-Members

        /// <summary>
        /// Range start, or null.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Range end, or null.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Bucket size in minutes, or null.
        /// </summary>
        public int? BucketMinutes { get; set; } = null;

        /// <summary>
        /// Fleet filter, or null.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Vessel filter, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionHistoryFilter()
        {
        }

        #endregion
    }
}
