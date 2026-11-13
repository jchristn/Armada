namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>Wait aggregates over a window, computed from recorded mission wait intervals.</summary>
    public class ConcurrencyMetrics
    {
        /// <summary>Window start.</summary>
        public DateTime FromUtc { get; set; }

        /// <summary>Window end.</summary>
        public DateTime ToUtc { get; set; }

        /// <summary>The vessel filter, if any.</summary>
        public string? VesselId { get; set; }

        /// <summary>The captain filter, if any.</summary>
        public string? CaptainId { get; set; }

        /// <summary>Bucket width in seconds.</summary>
        public int BucketSeconds { get; set; }

        /// <summary>Slot wait across the window.</summary>
        public SlotWaitStatistics SlotWait { get; set; } = new SlotWaitStatistics();

        /// <summary>Wait time by gate across the window, largest first.</summary>
        [CollectionBound(16, Reason = "One per MissionWaitGateEnum value")]
        public List<GateWaitTotal> ByGate { get; set; } = new List<GateWaitTotal>();

        /// <summary>Time buckets across the window, oldest first.</summary>
        [CollectionBound(48, Reason = "About 48 buckets on a round width; a 31-day window uses 31 one-day buckets")]
        public List<ConcurrencyMetricsBucket> Buckets { get; set; } = new List<ConcurrencyMetricsBucket>();

        /// <summary>True when the interval read hit its row cap, so totals are a lower bound.</summary>
        public bool Truncated { get; set; }
    }
}
