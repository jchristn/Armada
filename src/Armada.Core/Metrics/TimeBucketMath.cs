namespace Armada.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Arithmetic for time-bucketed metrics: flooring a time to its bucket and order statistics (median, percentiles)
    /// over durations.
    /// </summary>
    public static class TimeBucketMath
    {
        #region Public-Methods

        /// <summary>
        /// Floor a time to the start of its bucket on the absolute (UTC epoch) grid.
        /// </summary>
        /// <param name="utc">The time; a local time is converted to UTC first.</param>
        /// <param name="bucketSize">Bucket width; must be positive.</param>
        /// <returns>The bucket start (UTC).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the bucket size is not positive.</exception>
        public static DateTime Floor(DateTime utc, TimeSpan bucketSize)
        {
            if (bucketSize <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bucketSize));
            DateTime value = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
            long ticks = (value.Ticks / bucketSize.Ticks) * bucketSize.Ticks;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        /// <summary>
        /// A percentile of a set of values, interpolating linearly between the two nearest ranks (the same definition as
        /// a spreadsheet's PERCENTILE.INC). Returns null for an empty set.
        /// </summary>
        /// <param name="values">The values, in any order; not modified.</param>
        /// <param name="percentile">The percentile, 0 to 100.</param>
        /// <returns>The percentile rounded to the nearest whole number, or null when there are no values.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the percentile is outside 0 to 100.</exception>
        public static long? Percentile(IReadOnlyCollection<long> values, double percentile)
        {
            if (percentile < 0 || percentile > 100) throw new ArgumentOutOfRangeException(nameof(percentile));
            if (values == null || values.Count == 0) return null;

            List<long> sorted = new List<long>(values);
            sorted.Sort();
            if (sorted.Count == 1) return sorted[0];

            double rank = (percentile / 100.0) * (sorted.Count - 1);
            int lower = (int)Math.Floor(rank);
            int upper = (int)Math.Ceiling(rank);
            double fraction = rank - lower;
            double value = sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
            return (long)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// The median (50th percentile) of a set of values, or null for an empty set.
        /// </summary>
        /// <param name="values">The values, in any order; not modified.</param>
        /// <returns>The median, or null.</returns>
        public static long? Median(IReadOnlyCollection<long> values)
        {
            return Percentile(values, 50);
        }

        #endregion
    }
}
