namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Metrics;
    using Armada.Core.Services;

    /// <summary>
    /// How fast one runtime started producing output and how long its jobs ran on a Harbor, over the jobs that ended in a metrics window.
    /// </summary>
    public class HarborLaunchSpeed
    {
        #region Public-Members

        /// <summary>
        /// Runtime name (for example ClaudeCode).
        /// </summary>
        public string Runtime { get; set; } = String.Empty;

        /// <summary>
        /// Jobs of this runtime that ended in the window.
        /// </summary>
        public int JobCount { get; set; } = 0;

        /// <summary>
        /// True for the aggregate of the runtimes past <see cref="Armada.Core.Metrics.HarborMetricsRanges.MaxLaunchSpeedRuntimes"/>
        /// (Runtime is "Other"); its medians and percentiles are over every job of those runtimes.
        /// </summary>
        public bool IsOther { get; set; } = false;

        /// <summary>
        /// Runtimes this entry combines: 1, or more for the IsOther aggregate.
        /// </summary>
        public int RuntimeCount { get; set; } = 1;

        /// <summary>
        /// Jobs that reported a time to first output.
        /// </summary>
        public int FirstOutputCount { get; set; } = 0;

        /// <summary>
        /// Median time from launch to first output, in milliseconds, or null when no job reported one.
        /// </summary>
        public long? FirstOutputMedianMs { get; set; } = null;

        /// <summary>
        /// 95th percentile time from launch to first output, in milliseconds, or null.
        /// </summary>
        public long? FirstOutputP95Ms { get; set; } = null;

        /// <summary>
        /// Jobs that reported a total runtime.
        /// </summary>
        public int DurationCount { get; set; } = 0;

        /// <summary>
        /// Median total runtime, in milliseconds, or null.
        /// </summary>
        public long? DurationMedianMs { get; set; } = null;

        /// <summary>
        /// 95th percentile total runtime, in milliseconds, or null.
        /// </summary>
        public long? DurationP95Ms { get; set; } = null;

        /// <summary>
        /// Median time to first output per bucket (by when the job ended), in milliseconds, null where no job of this
        /// runtime reported one; one entry per bucket, for a sparkline.
        /// </summary>
        [CollectionBound(HarborMetricsRanges.MaxBucketCount, Reason = "One per window bucket; the fixed ranges produce at most 60")]
        public List<long?> FirstOutputMedianMsByBucket { get; set; } = new List<long?>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLaunchSpeed()
        {
        }

        #endregion
    }
}
