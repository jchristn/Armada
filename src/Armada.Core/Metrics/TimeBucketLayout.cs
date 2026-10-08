namespace Armada.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A chart window cut into equal time buckets aligned to the UTC epoch grid (a 30-minute bucket starts on the hour or
    /// the half hour). The window ends at the end of the bucket that contains "now", so the current bucket is the last
    /// one and fills in as time passes. Shared by the Admiral's metrics series and by clients that chart them.
    /// </summary>
    public class TimeBucketLayout
    {
        #region Public-Members

        /// <summary>
        /// Inclusive start of the first bucket (UTC).
        /// </summary>
        public DateTime FromUtc { get; }

        /// <summary>
        /// Exclusive end of the last bucket (UTC).
        /// </summary>
        public DateTime ToUtc { get; }

        /// <summary>
        /// Width of one bucket.
        /// </summary>
        public TimeSpan BucketSize { get; }

        /// <summary>
        /// Number of buckets.
        /// </summary>
        public int BucketCount { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a layout of <paramref name="bucketCount"/> buckets starting at <paramref name="fromUtc"/>.
        /// </summary>
        /// <param name="fromUtc">Start of the first bucket (UTC).</param>
        /// <param name="bucketSize">Bucket width; must be positive.</param>
        /// <param name="bucketCount">Number of buckets; must be positive.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the bucket size or count is not positive.</exception>
        public TimeBucketLayout(DateTime fromUtc, TimeSpan bucketSize, int bucketCount)
        {
            if (bucketSize <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bucketSize));
            if (bucketCount < 1) throw new ArgumentOutOfRangeException(nameof(bucketCount));
            FromUtc = DateTime.SpecifyKind(fromUtc.ToUniversalTime(), DateTimeKind.Utc);
            BucketSize = bucketSize;
            BucketCount = bucketCount;
            ToUtc = FromUtc.AddTicks(bucketSize.Ticks * bucketCount);
        }

        /// <summary>
        /// A window of <paramref name="duration"/> ending at the end of the bucket that contains <paramref name="nowUtc"/>.
        /// The duration is rounded up to whole buckets.
        /// </summary>
        /// <param name="nowUtc">The current time (UTC).</param>
        /// <param name="duration">Window length; must be positive.</param>
        /// <param name="bucketSize">Bucket width; must be positive.</param>
        /// <returns>The layout.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the duration or bucket size is not positive.</exception>
        public static TimeBucketLayout EndingAt(DateTime nowUtc, TimeSpan duration, TimeSpan bucketSize)
        {
            if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            if (bucketSize <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bucketSize));
            int count = (int)Math.Ceiling(duration.Ticks / (double)bucketSize.Ticks);
            if (count < 1) count = 1;
            DateTime end = TimeBucketMath.Floor(nowUtc, bucketSize).AddTicks(bucketSize.Ticks);
            return new TimeBucketLayout(end.AddTicks(-bucketSize.Ticks * count), bucketSize, count);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start of a bucket (UTC).
        /// </summary>
        /// <param name="index">Zero-based bucket index.</param>
        /// <returns>The bucket start.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is outside the layout.</exception>
        public DateTime StartOf(int index)
        {
            if (index < 0 || index >= BucketCount) throw new ArgumentOutOfRangeException(nameof(index));
            return FromUtc.AddTicks(BucketSize.Ticks * index);
        }

        /// <summary>
        /// Exclusive end of a bucket (UTC).
        /// </summary>
        /// <param name="index">Zero-based bucket index.</param>
        /// <returns>The bucket end.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is outside the layout.</exception>
        public DateTime EndOf(int index)
        {
            return StartOf(index).AddTicks(BucketSize.Ticks);
        }

        /// <summary>
        /// Index of the bucket that contains a time, or -1 when the time is outside the window.
        /// </summary>
        /// <param name="utc">The time (UTC).</param>
        /// <returns>The bucket index, or -1.</returns>
        public int IndexOf(DateTime utc)
        {
            DateTime value = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
            if (value < FromUtc || value >= ToUtc) return -1;
            return (int)((value.Ticks - FromUtc.Ticks) / BucketSize.Ticks);
        }

        /// <summary>
        /// Every bucket start, oldest first.
        /// </summary>
        /// <returns>The bucket starts.</returns>
        public List<DateTime> Starts()
        {
            List<DateTime> starts = new List<DateTime>(BucketCount);
            for (int i = 0; i < BucketCount; i++) starts.Add(StartOf(i));
            return starts;
        }

        #endregion
    }
}
