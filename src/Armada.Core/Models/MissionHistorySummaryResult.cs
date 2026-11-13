namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Aggregate mission-history summary for charts.
    /// </summary>
    public class MissionHistorySummaryResult
    {
        /// <summary>
        /// Total matching mission count.
        /// </summary>
        public int TotalCount { get; set; } = 0;

        /// <summary>
        /// Matching completed mission count.
        /// </summary>
        public int CompleteCount { get; set; } = 0;

        /// <summary>
        /// Matching failed mission count.
        /// </summary>
        public int FailedCount { get; set; } = 0;

        /// <summary>
        /// Matching non-complete/non-failed mission count.
        /// </summary>
        public int OtherCount { get; set; } = 0;

        /// <summary>
        /// Requested summary range start.
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.UtcNow.AddDays(-7);

        /// <summary>
        /// Requested summary range end.
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Bucket size in minutes.
        /// </summary>
        public int BucketMinutes { get; set; } = 60;

        /// <summary>
        /// Summary buckets in chronological order.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxSummaryBuckets, Reason = "The query is rejected when its window would produce more buckets")]
        public List<MissionHistoryBucket> Buckets { get; set; } = new List<MissionHistoryBucket>();
    }
}
