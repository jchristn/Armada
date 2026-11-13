namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Aggregate token-usage summary that feeds both dashboard charts: <see cref="Buckets"/> drives the
    /// time-series chart (usage over time, per model) and <see cref="ByModel"/> drives the horizontal
    /// aggregate chart (usage per model over the whole window). Grand totals and the estimated-record count
    /// support the summary tiles and the "includes estimates" note.
    /// </summary>
    public class TokenUsageSummaryResult
    {
        #region Public-Members

        /// <summary>
        /// Requested summary range start.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Requested summary range end.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Bucket size in minutes (may be fractional, for example 0.5 for 30-second buckets).
        /// </summary>
        public double BucketMinutes { get; set; }

        /// <summary>
        /// Number of usage records aggregated.
        /// </summary>
        public int RecordCount { get; set; }

        /// <summary>
        /// Number of aggregated records whose counts were estimated rather than measured.
        /// </summary>
        public int EstimatedCount { get; set; }

        /// <summary>Number of explicitly synthetic demonstration records in this window.</summary>
        public int SyntheticCount { get; set; }

        /// <summary>
        /// Total input (prompt) tokens across the whole window.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Total output (completion) tokens across the whole window.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Total cache-read tokens across the whole window.
        /// </summary>
        public long CachedTokens { get; set; } = 0;

        /// <summary>
        /// Total tokens across the whole window.
        /// </summary>
        public long TotalTokens { get; set; } = 0;

        /// <summary>
        /// Most models broken out by name (the query's ModelLimit after normalization).
        /// </summary>
        public int ModelLimit { get; set; } = TokenUsageQuery.DefaultModelLimit;

        /// <summary>
        /// Number of distinct models in the window. When it is above <see cref="ModelLimit"/>, the least-used
        /// models are summed into <see cref="OtherModels"/>.
        /// </summary>
        public int ModelCount { get; set; } = 0;

        /// <summary>
        /// Number of distinct models summed into <see cref="OtherModels"/> (zero when every model is in <see cref="ByModel"/>).
        /// </summary>
        public int OtherModelCount { get; set; } = 0;

        /// <summary>
        /// Whole-window aggregate of the models beyond <see cref="ModelLimit"/>, with Model set to
        /// <see cref="TokenUsageSummaryBuilder.OtherModelName"/>; null when every model is in <see cref="ByModel"/>.
        /// ByModel plus OtherModels always sums to the grand totals.
        /// </summary>
        public TokenUsageModelBreakdown? OtherModels { get; set; } = null;

        /// <summary>
        /// Time buckets in chronological order (gap-filled across the requested window). A window and bucket size
        /// that would produce more than <see cref="CollectionLimits.MaxSummaryBuckets"/> buckets is rejected.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxSummaryBuckets)]
        public List<TokenUsageBucket> Buckets { get; set; } = new List<TokenUsageBucket>();

        /// <summary>
        /// Per-model aggregate over the whole window for the <see cref="ModelLimit"/> most-used models, ordered by
        /// total tokens descending, then model name. The rest are summed into <see cref="OtherModels"/>.
        /// </summary>
        [CollectionBound(TokenUsageQuery.MaxModelLimit, Reason = "Top ModelLimit models; the rest are summed into OtherModels")]
        public List<TokenUsageModelBreakdown> ByModel { get; set; } = new List<TokenUsageModelBreakdown>();

        #endregion
    }
}
