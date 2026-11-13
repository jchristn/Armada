namespace Armada.Core.Models
{
    using System;

    /// <summary>Filters for reading mission wait intervals across missions.</summary>
    public class MissionWaitIntervalQuery
    {
        /// <summary>Only intervals owned by this tenant; null reads every tenant.</summary>
        public string? TenantId { get; set; }

        /// <summary>Only intervals on this vessel.</summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// Only intervals attributed to this captain: the waits of missions it went on to run (stamped at assignment)
        /// and the open waits of missions pinned to it with RequestedCaptainId.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>Only intervals still open (the mission still waits on the gate).</summary>
        public bool OpenOnly { get; set; }

        /// <summary>Only intervals still open at or ending after this time.</summary>
        public DateTime? FromUtc { get; set; }

        /// <summary>Only intervals started before this time.</summary>
        public DateTime? ToUtc { get; set; }

        /// <summary>Most rows returned, newest first; clamped to 1 to 50000.</summary>
        public int MaxRows
        {
            get => _MaxRows;
            set => _MaxRows = Math.Clamp(value, 1, 50000);
        }

        private int _MaxRows = 10000;
    }
}
