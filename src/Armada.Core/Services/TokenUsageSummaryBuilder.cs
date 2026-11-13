namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Builds token-usage summaries (time buckets with a per-model breakdown, a whole-window per-model
    /// aggregate, and grand totals) from matching token-usage records. Every collection in the result is bounded:
    /// the bucket count by <see cref="CollectionLimits.MaxSummaryBuckets"/> (a larger window is rejected) and the
    /// per-model breakdowns by <see cref="TokenUsageQuery.ModelLimit"/>, with the remaining models summed into an
    /// explicit OtherModels aggregate so no usage is dropped.
    /// </summary>
    public static class TokenUsageSummaryBuilder
    {
        #region Public-Members

        /// <summary>
        /// Model name used for the aggregate of models beyond the model limit.
        /// </summary>
        public const string OtherModelName = "(other)";

        /// <summary>
        /// Largest bucket width in minutes (366 days). A larger width is rejected.
        /// </summary>
        public const double MaxBucketMinutes = 527040;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reject a query whose bucket width is out of range or whose window would produce more than
        /// <see cref="CollectionLimits.MaxSummaryBuckets"/> buckets. Call before reading records so an oversized
        /// request does no work.
        /// </summary>
        /// <param name="query">The query window (from/to/bucket width).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the bucket width is above <see cref="MaxBucketMinutes"/>.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when the window would produce too many buckets.</exception>
        public static void ValidateWindow(TokenUsageQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            double bucketMinutes = query.BucketMinutes <= 0 ? 15 : query.BucketMinutes;
            if (bucketMinutes > MaxBucketMinutes)
                throw new ArgumentOutOfRangeException(nameof(query.BucketMinutes), "BucketMinutes must be at most " + MaxBucketMinutes + " (366 days).");
            if (query.FromUtc.HasValue && query.ToUtc.HasValue)
            {
                DateTime start = FloorToBucket(query.FromUtc.Value.ToUniversalTime(), bucketMinutes);
                DateTime end = query.ToUtc.Value.ToUniversalTime();
                CollectionLimits.RequireBuckets(end - start, BucketSpan(bucketMinutes));
            }
        }

        /// <summary>
        /// Build a token-usage summary for the supplied records and query window.
        /// </summary>
        /// <param name="records">Token-usage records that matched the query.</param>
        /// <param name="query">The query window (from/to/bucket width) and model limit.</param>
        /// <returns>The aggregated summary.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the bucket width is above <see cref="MaxBucketMinutes"/>.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when the window would produce too many buckets.</exception>
        public static TokenUsageSummaryResult Build(List<TokenUsageRecord> records, TokenUsageQuery query)
        {
            List<TokenUsageRecord> safeRecords = records ?? new List<TokenUsageRecord>();
            TokenUsageQuery safeQuery = query ?? new TokenUsageQuery();
            ValidateWindow(safeQuery);

            TokenUsageSummaryResult result = new TokenUsageSummaryResult
            {
                FromUtc = safeQuery.FromUtc,
                ToUtc = safeQuery.ToUtc,
                BucketMinutes = safeQuery.BucketMinutes <= 0 ? 15 : safeQuery.BucketMinutes,
                ModelLimit = safeQuery.ModelLimit,
                RecordCount = safeRecords.Count,
                EstimatedCount = safeRecords.Count(record => record.Estimated),
                SyntheticCount = safeRecords.Count(record => record.Source == "synthetic")
            };

            // Without an explicit window the buckets span the records, so bound that span too.
            if ((!safeQuery.FromUtc.HasValue || !safeQuery.ToUtc.HasValue) && safeRecords.Count > 0)
            {
                DateTime first = FloorToBucket(safeRecords.Min(record => record.CreatedUtc.ToUniversalTime()), result.BucketMinutes);
                DateTime last = safeRecords.Max(record => record.CreatedUtc.ToUniversalTime());
                CollectionLimits.RequireBuckets(last - first, BucketSpan(result.BucketMinutes));
            }

            // Grand totals and whole-window per-model aggregate.
            Dictionary<string, TokenUsageModelBreakdown> byModel = new Dictionary<string, TokenUsageModelBreakdown>(StringComparer.OrdinalIgnoreCase);

            // Time buckets keyed by floored start; each bucket carries its own per-model breakdown.
            Dictionary<DateTime, TokenUsageBucket> buckets = new Dictionary<DateTime, TokenUsageBucket>();
            Dictionary<DateTime, Dictionary<string, TokenUsageModelBreakdown>> bucketModels =
                new Dictionary<DateTime, Dictionary<string, TokenUsageModelBreakdown>>();

            foreach (TokenUsageRecord record in safeRecords)
            {
                string model = string.IsNullOrWhiteSpace(record.Model) ? "unknown" : record.Model;

                result.InputTokens += record.InputTokens;
                result.OutputTokens += record.OutputTokens;
                result.CachedTokens += record.CachedTokens;
                result.TotalTokens += record.TotalTokens;

                Accumulate(byModel, model, record);

                DateTime bucketStart = FloorToBucket(record.CreatedUtc, result.BucketMinutes);
                if (!buckets.TryGetValue(bucketStart, out TokenUsageBucket? bucket))
                {
                    bucket = new TokenUsageBucket
                    {
                        BucketStartUtc = bucketStart,
                        BucketEndUtc = bucketStart.AddMinutes(result.BucketMinutes)
                    };
                    buckets[bucketStart] = bucket;
                    bucketModels[bucketStart] = new Dictionary<string, TokenUsageModelBreakdown>(StringComparer.OrdinalIgnoreCase);
                }

                bucket.InputTokens += record.InputTokens;
                bucket.OutputTokens += record.OutputTokens;
                bucket.CachedTokens += record.CachedTokens;
                bucket.TotalTokens += record.TotalTokens;
                Accumulate(bucketModels[bucketStart], model, record);
            }

            // Gap-fill the requested window so the time axis is continuous (bounded by ValidateWindow).
            if (safeQuery.FromUtc.HasValue && safeQuery.ToUtc.HasValue)
            {
                DateTime cursor = FloorToBucket(safeQuery.FromUtc.Value.ToUniversalTime(), result.BucketMinutes);
                DateTime end = safeQuery.ToUtc.Value.ToUniversalTime();
                while (cursor <= end)
                {
                    if (!buckets.ContainsKey(cursor))
                    {
                        buckets[cursor] = new TokenUsageBucket
                        {
                            BucketStartUtc = cursor,
                            BucketEndUtc = cursor.AddMinutes(result.BucketMinutes)
                        };
                        bucketModels[cursor] = new Dictionary<string, TokenUsageModelBreakdown>(StringComparer.OrdinalIgnoreCase);
                    }

                    cursor = cursor.AddMinutes(result.BucketMinutes);
                }
            }

            // The top models are chosen over the whole window so every bucket breaks out the same series.
            List<TokenUsageModelBreakdown> rankedModels = Rank(byModel.Values);
            List<TokenUsageModelBreakdown> topModels = rankedModels.Take(result.ModelLimit).ToList();
            HashSet<string> topNames = new HashSet<string>(topModels.Select(entry => entry.Model), StringComparer.OrdinalIgnoreCase);
            List<TokenUsageModelBreakdown> otherModels = rankedModels.Skip(result.ModelLimit).ToList();

            result.ModelCount = rankedModels.Count;
            result.ByModel = topModels;
            result.OtherModelCount = otherModels.Count;
            result.OtherModels = Sum(otherModels);

            foreach (KeyValuePair<DateTime, TokenUsageBucket> pair in buckets)
            {
                List<TokenUsageModelBreakdown> ranked = Rank(bucketModels[pair.Key].Values);
                pair.Value.Models = ranked.Where(entry => topNames.Contains(entry.Model)).ToList();
                List<TokenUsageModelBreakdown> rest = ranked.Where(entry => !topNames.Contains(entry.Model)).ToList();
                pair.Value.OtherModelCount = rest.Count;
                pair.Value.OtherModels = Sum(rest);
            }

            result.Buckets = buckets.Values
                .OrderBy(bucket => bucket.BucketStartUtc)
                .ToList();

            return result;
        }

        #endregion

        #region Private-Methods

        private static List<TokenUsageModelBreakdown> Rank(IEnumerable<TokenUsageModelBreakdown> entries)
        {
            return entries
                .OrderByDescending(entry => entry.TotalTokens)
                .ThenBy(entry => entry.Model, StringComparer.Ordinal)
                .ToList();
        }

        private static TokenUsageModelBreakdown? Sum(List<TokenUsageModelBreakdown> entries)
        {
            if (entries.Count == 0) return null;
            TokenUsageModelBreakdown other = new TokenUsageModelBreakdown { Model = OtherModelName };
            foreach (TokenUsageModelBreakdown entry in entries)
            {
                other.InputTokens += entry.InputTokens;
                other.OutputTokens += entry.OutputTokens;
                other.CachedTokens += entry.CachedTokens;
                other.TotalTokens += entry.TotalTokens;
            }

            return other;
        }

        private static void Accumulate(Dictionary<string, TokenUsageModelBreakdown> map, string model, TokenUsageRecord record)
        {
            if (!map.TryGetValue(model, out TokenUsageModelBreakdown? breakdown))
            {
                breakdown = new TokenUsageModelBreakdown { Model = model };
                map[model] = breakdown;
            }

            breakdown.InputTokens += record.InputTokens;
            breakdown.OutputTokens += record.OutputTokens;
            breakdown.CachedTokens += record.CachedTokens;
            breakdown.TotalTokens += record.TotalTokens;
        }

        private static TimeSpan BucketSpan(double bucketMinutes)
        {
            long bucketTicks = (long)((bucketMinutes > 0 ? bucketMinutes : 1) * TimeSpan.TicksPerMinute);
            return TimeSpan.FromTicks(bucketTicks < 1 ? 1 : bucketTicks);
        }

        private static DateTime FloorToBucket(DateTime value, double bucketMinutes)
        {
            // Floor on the absolute epoch grid so bucket boundaries match the dashboard's
            // Math.floor(timeMs / bucketMs) * bucketMs alignment for every bucket width. Supports
            // fractional bucket widths (for example 0.5 minutes = 30-second buckets).
            DateTime utc = value.ToUniversalTime();
            double minutes = bucketMinutes > 0 ? bucketMinutes : 1;
            long bucketTicks = (long)(minutes * TimeSpan.TicksPerMinute);
            if (bucketTicks < 1) bucketTicks = 1;
            long flooredTicks = (utc.Ticks / bucketTicks) * bucketTicks;
            return new DateTime(flooredTicks, DateTimeKind.Utc);
        }

        #endregion
    }
}
