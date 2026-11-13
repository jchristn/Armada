namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// One time bucket in a token-usage summary: bucket-level totals plus the per-model breakdown that lets
    /// the time-series chart stack bars or draw a line per model.
    /// </summary>
    public class TokenUsageBucket
    {
        #region Public-Members

        /// <summary>
        /// Inclusive bucket start (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; }

        /// <summary>
        /// Exclusive bucket end (UTC).
        /// </summary>
        public DateTime BucketEndUtc { get; set; }

        /// <summary>
        /// Input (prompt) tokens across all models in the bucket.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output (completion) tokens across all models in the bucket.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Cache-read tokens across all models in the bucket.
        /// </summary>
        public long CachedTokens { get; set; } = 0;

        /// <summary>
        /// Total tokens across all models in the bucket.
        /// </summary>
        public long TotalTokens { get; set; } = 0;

        /// <summary>
        /// Per-model token breakdown within this bucket for the summary's top models (the models in
        /// <see cref="TokenUsageSummaryResult.ByModel"/>), ordered by total tokens descending, then model name. The
        /// bucket's other models are summed into <see cref="OtherModels"/>.
        /// </summary>
        [CollectionBound(TokenUsageQuery.MaxModelLimit, Reason = "Only the summary's top ModelLimit models; the rest are summed into OtherModels")]
        public List<TokenUsageModelBreakdown> Models { get; set; } = new List<TokenUsageModelBreakdown>();

        /// <summary>
        /// Number of distinct models summed into <see cref="OtherModels"/> in this bucket.
        /// </summary>
        public int OtherModelCount { get; set; } = 0;

        /// <summary>
        /// This bucket's usage by models outside the summary's top models, with Model set to
        /// <see cref="TokenUsageSummaryBuilder.OtherModelName"/>; null when there is none.
        /// </summary>
        public TokenUsageModelBreakdown? OtherModels { get; set; } = null;

        #endregion
    }
}
