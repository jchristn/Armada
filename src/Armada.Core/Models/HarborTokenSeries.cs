namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Tokens used by one runtime and model, in a bucket or over a whole window.
    /// </summary>
    public class HarborTokenSeries
    {
        #region Public-Members

        /// <summary>
        /// Runtime name as the token-usage record stored it, or "unknown".
        /// </summary>
        public string Runtime { get; set; } = String.Empty;

        /// <summary>
        /// Model identifier, or "unknown".
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Input (prompt) tokens.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output (completion) tokens.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Cache-read tokens.
        /// </summary>
        public long CachedTokens { get; set; } = 0;

        /// <summary>
        /// Total tokens.
        /// </summary>
        public long TotalTokens { get; set; } = 0;

        /// <summary>
        /// True for the aggregate of the series past <see cref="Armada.Core.Metrics.HarborMetricsRanges.MaxTokenSeries"/>
        /// (Runtime and Model are "other"); <see cref="SeriesCount"/> says how many it combines.
        /// </summary>
        public bool IsOther { get; set; } = false;

        /// <summary>
        /// Runtime and model series this entry combines: 1, or more for the IsOther aggregate.
        /// </summary>
        public int SeriesCount { get; set; } = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborTokenSeries()
        {
        }

        #endregion
    }
}
