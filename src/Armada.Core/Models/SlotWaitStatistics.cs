namespace Armada.Core.Models
{
    /// <summary>
    /// Slot wait over a window: per mission, the time spent on capacity gates (voyage, mission, captain, endpoint,
    /// runtime and global slots, branch locks and placement) within the window.
    /// </summary>
    public class SlotWaitStatistics
    {
        /// <summary>Window length in hours.</summary>
        public int WindowHours { get; set; }

        /// <summary>Missions that waited on a capacity gate in the window.</summary>
        public int Missions { get; set; }

        /// <summary>Median slot wait in seconds, or null with no samples.</summary>
        public double? P50Seconds { get; set; }

        /// <summary>95th percentile slot wait in seconds, or null with no samples.</summary>
        public double? P95Seconds { get; set; }

        /// <summary>Longest slot wait in seconds, or null with no samples.</summary>
        public double? MaxSeconds { get; set; }
    }
}
