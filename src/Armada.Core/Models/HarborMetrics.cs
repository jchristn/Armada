namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Charts for one Harbor over a window (GET /api/v1/harbors/{id}/metrics): jobs over time, slot usage, launch speed per runtime, link health, and token usage. Every series shares the same buckets (FromUtc, BucketMinutes, BucketCount), so a client can line them up by index. The Admiral computes every number, so every surface shows the same data.
    /// </summary>
    public class HarborMetrics
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier.
        /// </summary>
        public string HarborId { get; set; } = String.Empty;

        /// <summary>
        /// Harbor name.
        /// </summary>
        public string HarborName { get; set; } = String.Empty;

        /// <summary>
        /// Window: 1h, 24h, or 7d.
        /// </summary>
        public string Range { get; set; } = "24h";

        /// <summary>
        /// Inclusive start of the first bucket (UTC).
        /// </summary>
        public DateTime FromUtc { get; set; }

        /// <summary>
        /// Exclusive end of the last bucket (UTC). The last bucket contains the time the metrics were generated.
        /// </summary>
        public DateTime ToUtc { get; set; }

        /// <summary>
        /// Bucket width in minutes: 1 for 1h, 30 for 24h, 180 for 7d.
        /// </summary>
        public int BucketMinutes { get; set; }

        /// <summary>
        /// Number of buckets in every series.
        /// </summary>
        public int BucketCount { get; set; }

        /// <summary>
        /// When the metrics were computed (UTC).
        /// </summary>
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The Harbor's current connection status.
        /// </summary>
        public HarborConnectionStatusEnum ConnectionStatus { get; set; } = HarborConnectionStatusEnum.Unknown;

        /// <summary>
        /// Finished and failed jobs per bucket, missions and other launches apart.
        /// </summary>
        public HarborJobMetrics Jobs { get; set; } = new HarborJobMetrics();

        /// <summary>
        /// Concurrent jobs per bucket against the Harbor's capacity.
        /// </summary>
        public HarborSlotMetrics Slots { get; set; } = new HarborSlotMetrics();

        /// <summary>
        /// Time to first output and total runtime per runtime, for jobs that ended in the window, most jobs first.
        /// </summary>
        public List<HarborLaunchSpeed> LaunchSpeed { get; set; } = new List<HarborLaunchSpeed>();

        /// <summary>
        /// Link state timeline and heartbeat round-trip times.
        /// </summary>
        public HarborLinkMetrics Link { get; set; } = new HarborLinkMetrics();

        /// <summary>
        /// Tokens used by captains run on this Harbor, per bucket, by runtime and model.
        /// </summary>
        public HarborTokenMetrics Tokens { get; set; } = new HarborTokenMetrics();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborMetrics()
        {
        }

        #endregion
    }
}
