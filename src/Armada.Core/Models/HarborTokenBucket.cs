namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Tokens used on a Harbor in one time bucket, with the runtime and model breakdown.
    /// </summary>
    public class HarborTokenBucket
    {
        #region Public-Members

        /// <summary>
        /// Inclusive bucket start (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; }

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
        /// Per runtime and model, most tokens first.
        /// </summary>
        public List<HarborTokenSeries> Series { get; set; } = new List<HarborTokenSeries>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborTokenBucket()
        {
        }

        #endregion
    }
}
