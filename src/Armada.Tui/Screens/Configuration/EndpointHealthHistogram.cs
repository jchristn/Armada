namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// The dashboard's <c>HealthHistogram</c> bucketing for model endpoint probes: one bucket per probe when the
    /// history spans under an hour, one-minute buckets up to six hours, five-minute buckets beyond, keeping the newest
    /// <c>maxBars</c>. Rendered as text markers (<c>+</c> ok, <c>x</c> fail, <c>~</c> mixed). Thread-safe (stateless).
    /// </summary>
    public static class EndpointHealthHistogram
    {
        #region Public-Methods

        /// <summary>
        /// Bucket a probe history.
        /// </summary>
        /// <param name="history">Probes.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="maxBars">Most buckets to keep (at least 6).</param>
        /// <returns>Buckets, oldest first.</returns>
        public static List<HealthBucket> Buckets(IEnumerable<ModelEndpointHealthRecord>? history, DateTime nowUtc, int maxBars)
        {
            List<ModelEndpointHealthRecord> sorted = (history ?? Enumerable.Empty<ModelEndpointHealthRecord>()).OrderBy(r => r.TimestampUtc).ToList();
            List<HealthBucket> buckets = new List<HealthBucket>();
            if (sorted.Count == 0) return buckets;
            double spanHours = (nowUtc - sorted[0].TimestampUtc).TotalHours;
            if (spanHours < 1)
            {
                foreach (ModelEndpointHealthRecord r in sorted)
                {
                    buckets.Add(new HealthBucket { Success = r.Success ? 1 : 0, Fail = r.Success ? 0 : 1, TimeUtc = r.TimestampUtc });
                }
            }
            else
            {
                long bucketTicks = spanHours <= 6 ? TimeSpan.TicksPerMinute : TimeSpan.TicksPerMinute * 5;
                Dictionary<long, HealthBucket> map = new Dictionary<long, HealthBucket>();
                foreach (ModelEndpointHealthRecord r in sorted)
                {
                    long key = r.TimestampUtc.Ticks / bucketTicks;
                    if (!map.TryGetValue(key, out HealthBucket? b))
                    {
                        b = new HealthBucket { TimeUtc = new DateTime(key * bucketTicks, DateTimeKind.Utc) };
                        map[key] = b;
                    }

                    if (r.Success) b.Success++;
                    else b.Fail++;
                }

                buckets = map.Values.OrderBy(b => b.TimeUtc).ToList();
            }

            int cap = Math.Max(6, maxBars);
            if (buckets.Count > cap) buckets = buckets.Skip(buckets.Count - cap).ToList();
            return buckets;
        }

        /// <summary>
        /// Text strip of bucket markers, or empty when there is no history.
        /// </summary>
        /// <param name="history">Probes.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="maxBars">Most buckets.</param>
        /// <returns>Markers.</returns>
        public static string Strip(IEnumerable<ModelEndpointHealthRecord>? history, DateTime nowUtc, int maxBars)
        {
            return new string(Buckets(history, nowUtc, maxBars).Select(b => b.Marker).ToArray());
        }

        /// <summary>
        /// The dashboard's history span text ("&lt;1m", "42m", "3h 5m", "2d 4h", or "-").
        /// </summary>
        /// <param name="firstUtc">Earliest retained probe.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Text.</returns>
        public static string Span(DateTime? firstUtc, DateTime nowUtc)
        {
            if (!firstUtc.HasValue) return "-";
            double ms = (nowUtc - firstUtc.Value).TotalMilliseconds;
            if (ms < 0) return "-";
            int minutes = (int)Math.Floor(ms / 60000);
            if (minutes < 1) return "<1m";
            if (minutes < 60) return minutes + "m";
            int hours = minutes / 60;
            if (hours < 24) return hours + "h " + (minutes % 60) + "m";
            return (hours / 24) + "d " + (hours % 24) + "h";
        }

        #endregion
    }
}
