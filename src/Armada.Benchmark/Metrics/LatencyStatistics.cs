namespace Armada.Benchmark.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;

    /// <summary>Latency distribution in milliseconds, using linear interpolation between ranks.</summary>
    public sealed class LatencyStatistics
    {
        /// <summary>Samples.</summary>
        [JsonPropertyName("count")]
        public int Count { get; set; }

        /// <summary>Median.</summary>
        [JsonPropertyName("p50Ms")]
        public double P50Ms { get; set; }

        /// <summary>95th percentile.</summary>
        [JsonPropertyName("p95Ms")]
        public double P95Ms { get; set; }

        /// <summary>99th percentile.</summary>
        [JsonPropertyName("p99Ms")]
        public double P99Ms { get; set; }

        /// <summary>Mean.</summary>
        [JsonPropertyName("meanMs")]
        public double MeanMs { get; set; }

        /// <summary>Maximum.</summary>
        [JsonPropertyName("maxMs")]
        public double MaxMs { get; set; }

        /// <summary>Summarize samples.</summary>
        /// <param name="samples">Latency samples in milliseconds.</param>
        /// <returns>The summary; all zero when there are no samples.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="samples"/> is null.</exception>
        public static LatencyStatistics From(IReadOnlyList<double> samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            List<double> ordered = samples.OrderBy(v => v).ToList();
            if (ordered.Count == 0) return new LatencyStatistics();
            return new LatencyStatistics
            {
                Count = ordered.Count, P50Ms = Percentile(ordered, 50), P95Ms = Percentile(ordered, 95), P99Ms = Percentile(ordered, 99),
                MeanMs = Math.Round(ordered.Average(), 1), MaxMs = Math.Round(ordered[ordered.Count - 1], 1)
            };
        }

        private static double Percentile(List<double> ordered, double pct)
        {
            double rank = (ordered.Count - 1) * pct / 100.0;
            int lo = (int)Math.Floor(rank);
            int hi = Math.Min(lo + 1, ordered.Count - 1);
            return Math.Round(ordered[lo] + (ordered[hi] - ordered[lo]) * (rank - lo), 1);
        }
    }
}
