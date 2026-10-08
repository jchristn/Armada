namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Heartbeat round-trip times a Harbor reported in one time bucket.
    /// </summary>
    public class HarborRoundTripBucket
    {
        #region Public-Members

        /// <summary>
        /// Inclusive bucket start (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; }

        /// <summary>
        /// Heartbeats received in the bucket.
        /// </summary>
        public int HeartbeatCount { get; set; } = 0;

        /// <summary>
        /// Heartbeats that reported a round-trip time.
        /// </summary>
        public int SampleCount { get; set; } = 0;

        /// <summary>
        /// Average round-trip time, in milliseconds (one decimal), or null when none was reported.
        /// </summary>
        public double? AverageMs { get; set; } = null;

        /// <summary>
        /// Largest round-trip time, in milliseconds, or null.
        /// </summary>
        public long? MaxMs { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborRoundTripBucket()
        {
        }

        #endregion
    }
}
