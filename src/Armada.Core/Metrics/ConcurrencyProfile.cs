namespace Armada.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Turns a set of activity intervals into per-bucket concurrency: the peak and the time-weighted average number of
    /// intervals active at once. An interval that ends exactly when another starts does not count as overlapping it.
    /// </summary>
    public static class ConcurrencyProfile
    {
        #region Public-Methods

        /// <summary>
        /// Build the concurrency of every bucket in a layout.
        /// </summary>
        /// <param name="layout">The buckets.</param>
        /// <param name="intervals">Activity intervals; an open interval (no end) is active until <paramref name="nowUtc"/>.</param>
        /// <param name="nowUtc">The current time (UTC): open intervals end here, and later time is not averaged.</param>
        /// <returns>One entry per bucket, oldest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the layout is null.</exception>
        public static List<ConcurrencyBucket> Build(TimeBucketLayout layout, IEnumerable<TimeInterval>? intervals, DateTime nowUtc)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));

            // Edges: +1 at a start, -1 at an end; at the same instant ends sort first so back-to-back jobs do not overlap.
            List<KeyValuePair<long, int>> edges = new List<KeyValuePair<long, int>>();
            if (intervals != null)
            {
                foreach (TimeInterval interval in intervals)
                {
                    if (interval == null) continue;
                    long start = interval.StartUtc.Ticks;
                    long end = (interval.EndUtc ?? nowUtc).Ticks;
                    if (end <= start) continue;
                    edges.Add(new KeyValuePair<long, int>(start, 1));
                    edges.Add(new KeyValuePair<long, int>(end, -1));
                }
            }

            edges.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));

            List<ConcurrencyBucket> buckets = new List<ConcurrencyBucket>(layout.BucketCount);
            int level = 0;
            int next = 0;
            long now = nowUtc.Ticks;

            for (int i = 0; i < layout.BucketCount; i++)
            {
                long bucketStart = layout.StartOf(i).Ticks;
                long bucketEnd = layout.EndOf(i).Ticks;

                // Everything at or before the bucket start sets the level the bucket opens with.
                while (next < edges.Count && edges[next].Key <= bucketStart)
                {
                    level += edges[next].Value;
                    next++;
                }

                long averageEnd = Math.Min(bucketEnd, Math.Max(bucketStart, now));
                int peak = level;
                double area = 0;
                long cursor = bucketStart;

                while (next < edges.Count && edges[next].Key < bucketEnd)
                {
                    long at = edges[next].Key;
                    if (at > cursor && cursor < averageEnd)
                        area += level * (double)(Math.Min(at, averageEnd) - cursor);
                    cursor = Math.Max(cursor, at);
                    level += edges[next].Value;
                    if (level > peak) peak = level;
                    next++;
                }

                if (cursor < averageEnd) area += level * (double)(averageEnd - cursor);
                double span = averageEnd - bucketStart;
                buckets.Add(new ConcurrencyBucket
                {
                    BucketStartUtc = new DateTime(bucketStart, DateTimeKind.Utc),
                    Peak = bucketStart >= now ? 0 : peak,
                    Average = span > 0 ? Math.Round(area / span, 2) : 0
                });
            }

            return buckets;
        }

        #endregion
    }
}
