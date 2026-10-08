namespace Armada.Core.Metrics
{
    using System;

    /// <summary>
    /// How many things (jobs) were active at once during one time bucket.
    /// </summary>
    public class ConcurrencyBucket
    {
        #region Public-Members

        /// <summary>
        /// Inclusive bucket start (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; }

        /// <summary>
        /// The most active at any instant during the bucket.
        /// </summary>
        public int Peak { get; set; } = 0;

        /// <summary>
        /// The time-weighted average number active over the bucket (over the elapsed part of the current bucket), rounded
        /// to two decimals.
        /// </summary>
        public double Average { get; set; } = 0;

        #endregion
    }
}
