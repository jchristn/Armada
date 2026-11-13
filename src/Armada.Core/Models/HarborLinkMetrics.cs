namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Metrics;
    using Armada.Core.Services;

    /// <summary>
    /// Health of a Harbor's link over a metrics window: a timeline of connected, reconnecting, and down stretches, and the heartbeat round-trip times the Harbor reported.
    /// </summary>
    public class HarborLinkMetrics
    {
        #region Public-Members

        /// <summary>
        /// Contiguous stretches covering the window up to now, oldest first.
        /// </summary>
        [CollectionBound(HarborMetricsRanges.MaxLinkSegments, Reason = "Above the limit the timeline is coalesced to bucket resolution (SegmentsCoalesced)")]
        public List<HarborLinkSegment> Segments { get; set; } = new List<HarborLinkSegment>();

        /// <summary>
        /// True when the window had more than <see cref="HarborMetricsRanges.MaxLinkSegments"/> link-state stretches, so
        /// <see cref="Segments"/> shows each bucket's dominant state instead (adjacent equal buckets merged).
        /// ConnectedPercent is still computed from every stretch.
        /// </summary>
        public bool SegmentsCoalesced { get; set; } = false;

        /// <summary>
        /// Link-state stretches in the window before any coalescing.
        /// </summary>
        public int RawSegmentCount { get; set; } = 0;

        /// <summary>
        /// Round-trip times per bucket, oldest first.
        /// </summary>
        [CollectionBound(HarborMetricsRanges.MaxBucketCount, Reason = "One per window bucket; the fixed ranges produce at most 60")]
        public List<HarborRoundTripBucket> RoundTrip { get; set; } = new List<HarborRoundTripBucket>();

        /// <summary>
        /// Share of the window (up to now) with a known link state during which the link was connected, 0 to 100, or null
        /// when no state is known.
        /// </summary>
        public double? ConnectedPercent { get; set; } = null;

        /// <summary>
        /// Link transitions recorded in the window that closed an open link (Reconnecting events).
        /// </summary>
        public int Disconnects { get; set; } = 0;

        /// <summary>
        /// The Harbor's own reconnect count from its latest heartbeat, or null when it does not report one.
        /// </summary>
        public int? ReconnectCount { get; set; } = null;

        /// <summary>
        /// When the Harbor last reconnected (UTC, its clock), from its latest heartbeat, or null.
        /// </summary>
        public DateTime? LastReconnectUtc { get; set; } = null;

        /// <summary>
        /// Median heartbeat round-trip time over the window, in milliseconds (the median of the per-minute averages), or null.
        /// </summary>
        public long? RoundTripMedianMs { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLinkMetrics()
        {
        }

        #endregion
    }
}
