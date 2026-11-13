using System;
using System.Collections.Generic;

namespace Armada.Core.Models
{
    using Armada.Core.Services;

    /// <summary>
    /// Aggregate request-history summary for cards and charts.
    /// </summary>
    public class RequestHistorySummaryResult
    {
        #region Public-Members

        /// <summary>
        /// Total matching request count.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Successful matching request count.
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Failed matching request count.
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Success percentage in range [0, 100].
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Average duration in milliseconds.
        /// </summary>
        public double AverageDurationMs { get; set; }

        /// <summary>
        /// Requested summary range start.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Requested summary range end.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Bucket size in minutes.
        /// </summary>
        public int BucketMinutes { get; set; }

        /// <summary>
        /// Summary buckets in chronological order.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxSummaryBuckets)]
        public List<RequestHistorySummaryBucket> Buckets { get; set; } = new List<RequestHistorySummaryBucket>();

        #endregion
    }
}
