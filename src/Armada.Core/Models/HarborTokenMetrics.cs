namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Metrics;
    using Armada.Core.Services;

    /// <summary>
    /// Tokens used by captains run on a Harbor over a metrics window: the token-usage records the Admiral attributed to the Harbor, aggregated per bucket and per runtime and model. Armada records token counts, not prices, so there is no cost series.
    /// </summary>
    public class HarborTokenMetrics
    {
        #region Public-Members

        /// <summary>
        /// One entry per bucket, oldest first.
        /// </summary>
        [CollectionBound(HarborMetricsRanges.MaxBucketCount, Reason = "One per window bucket; the fixed ranges produce at most 60")]
        public List<HarborTokenBucket> Buckets { get; set; } = new List<HarborTokenBucket>();

        /// <summary>
        /// Totals per runtime and model over the window, most tokens first.
        /// </summary>
        [CollectionBound(HarborMetricsRanges.MaxTokenSeries, Reason = "Top series by tokens; the rest aggregated into one IsOther entry")]
        public List<HarborTokenSeries> Series { get; set; } = new List<HarborTokenSeries>();

        /// <summary>
        /// Input (prompt) tokens over the window.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output (completion) tokens over the window.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Cache-read tokens over the window.
        /// </summary>
        public long CachedTokens { get; set; } = 0;

        /// <summary>
        /// Total tokens (input plus output) over the window.
        /// </summary>
        public long TotalTokens { get; set; } = 0;

        /// <summary>
        /// Token-usage records aggregated.
        /// </summary>
        public int RecordCount { get; set; } = 0;

        /// <summary>
        /// Records whose counts were estimated rather than reported by the runtime.
        /// </summary>
        public int EstimatedCount { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborTokenMetrics()
        {
        }

        #endregion
    }
}
