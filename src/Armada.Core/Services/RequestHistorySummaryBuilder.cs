namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Builds request-history summaries from matching request-history entries. The bucket count is bounded by
    /// <see cref="CollectionLimits.MaxSummaryBuckets"/>: a window and bucket width that would produce more is rejected.
    /// </summary>
    public static class RequestHistorySummaryBuilder
    {
        /// <summary>
        /// Largest bucket width in minutes (366 days). A larger width is rejected.
        /// </summary>
        public const int MaxBucketMinutes = 527040;

        /// <summary>
        /// Reject a query whose bucket width is out of range or whose window would produce more than
        /// <see cref="CollectionLimits.MaxSummaryBuckets"/> buckets. Call before reading entries so an oversized
        /// request does no work.
        /// </summary>
        /// <param name="query">The query window (from/to/bucket width).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the bucket width is above <see cref="MaxBucketMinutes"/>.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when the window would produce too many buckets.</exception>
        public static void ValidateWindow(RequestHistoryQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int bucketMinutes = query.BucketMinutes <= 0 ? 15 : query.BucketMinutes;
            if (bucketMinutes > MaxBucketMinutes)
                throw new ArgumentOutOfRangeException(nameof(query.BucketMinutes), "BucketMinutes must be at most " + MaxBucketMinutes + " (366 days).");
            if (query.FromUtc.HasValue && query.ToUtc.HasValue)
            {
                DateTime start = FloorToBucket(query.FromUtc.Value.ToUniversalTime(), bucketMinutes);
                CollectionLimits.RequireBuckets(query.ToUtc.Value.ToUniversalTime() - start, TimeSpan.FromMinutes(bucketMinutes));
            }
        }

        /// <summary>
        /// Build a request-history summary for the supplied entries and query window.
        /// </summary>
        /// <param name="entries">Entries that matched the query.</param>
        /// <param name="query">The query window (from/to/bucket width).</param>
        /// <returns>The aggregated summary.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the bucket width is above <see cref="MaxBucketMinutes"/>.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when the window would produce too many buckets.</exception>
        public static RequestHistorySummaryResult Build(List<RequestHistoryEntry> entries, RequestHistoryQuery query)
        {
            List<RequestHistoryEntry> safeEntries = entries ?? new List<RequestHistoryEntry>();
            RequestHistoryQuery safeQuery = query ?? new RequestHistoryQuery();
            ValidateWindow(safeQuery);

            RequestHistorySummaryResult result = new RequestHistorySummaryResult
            {
                TotalCount = safeEntries.Count,
                SuccessCount = safeEntries.Count(entry => entry.IsSuccess),
                FailureCount = safeEntries.Count(entry => !entry.IsSuccess),
                AverageDurationMs = safeEntries.Count == 0 ? 0 : Math.Round(safeEntries.Average(entry => entry.DurationMs), 2),
                FromUtc = safeQuery.FromUtc,
                ToUtc = safeQuery.ToUtc,
                BucketMinutes = safeQuery.BucketMinutes <= 0 ? 15 : safeQuery.BucketMinutes
            };

            result.SuccessRate = result.TotalCount == 0
                ? 0
                : Math.Round((double)result.SuccessCount / result.TotalCount * 100.0, 2);

            // Without an explicit window the buckets span the entries, so bound that span too.
            if ((!safeQuery.FromUtc.HasValue || !safeQuery.ToUtc.HasValue) && safeEntries.Count > 0)
            {
                DateTime first = FloorToBucket(safeEntries.Min(entry => entry.CreatedUtc.ToUniversalTime()), result.BucketMinutes);
                DateTime last = safeEntries.Max(entry => entry.CreatedUtc.ToUniversalTime());
                CollectionLimits.RequireBuckets(last - first, TimeSpan.FromMinutes(Math.Max(1, result.BucketMinutes)));
            }

            Dictionary<DateTime, RequestHistorySummaryBucket> buckets = new Dictionary<DateTime, RequestHistorySummaryBucket>();
            foreach (RequestHistoryEntry entry in safeEntries)
            {
                DateTime bucketStart = FloorToBucket(entry.CreatedUtc, result.BucketMinutes);
                if (!buckets.TryGetValue(bucketStart, out RequestHistorySummaryBucket? bucket))
                {
                    bucket = new RequestHistorySummaryBucket
                    {
                        BucketStartUtc = bucketStart,
                        BucketEndUtc = bucketStart.AddMinutes(result.BucketMinutes)
                    };
                    buckets[bucketStart] = bucket;
                }

                bucket.TotalCount++;
                if (entry.IsSuccess) bucket.SuccessCount++;
                else bucket.FailureCount++;
                bucket.AverageDurationMs = bucket.TotalCount == 1
                    ? entry.DurationMs
                    : Math.Round(((bucket.AverageDurationMs * (bucket.TotalCount - 1)) + entry.DurationMs) / bucket.TotalCount, 2);
            }

            if (safeQuery.FromUtc.HasValue && safeQuery.ToUtc.HasValue)
            {
                DateTime cursor = FloorToBucket(safeQuery.FromUtc.Value.ToUniversalTime(), result.BucketMinutes);
                DateTime end = safeQuery.ToUtc.Value.ToUniversalTime();
                while (cursor <= end)
                {
                    if (!buckets.ContainsKey(cursor))
                    {
                        buckets[cursor] = new RequestHistorySummaryBucket
                        {
                            BucketStartUtc = cursor,
                            BucketEndUtc = cursor.AddMinutes(result.BucketMinutes)
                        };
                    }

                    cursor = cursor.AddMinutes(result.BucketMinutes);
                }
            }

            result.Buckets = buckets.Values
                .OrderBy(bucket => bucket.BucketStartUtc)
                .ToList();
            return result;
        }

        private static DateTime FloorToBucket(DateTime value, int bucketMinutes)
        {
            // Floor on the absolute epoch grid so bucket boundaries match the dashboard's
            // Math.floor(timeMs / bucketMs) * bucketMs alignment for every bucket width. Flooring
            // only the minute-of-hour breaks widths >= 60 minutes (e.g. last week/last month), where
            // adjacent hourly buckets would collide or be dropped when the client re-buckets them.
            DateTime utc = value.ToUniversalTime();
            long bucketTicks = (long)Math.Max(1, bucketMinutes) * TimeSpan.TicksPerMinute;
            long flooredTicks = (utc.Ticks / bucketTicks) * bucketTicks;
            return new DateTime(flooredTicks, DateTimeKind.Utc);
        }
    }
}
