namespace Armada.Core.Metrics.Charts
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Turns a Harbor's metrics (GET /api/v1/harbors/{id}/metrics) into the chart models the Harbor app and the TUI draw:
    /// jobs over time (missions and interactive, finished and failed, stacked), slot usage (peak and average against the
    /// capacity, on an axis that tops out at the capacity), heartbeat round trip (average and largest), the link health
    /// strip, the launch speed trend per runtime, and tokens by type (uncached input, cached input, output) with totals per
    /// runtime and model. Series names and colors follow the dashboard where it has the same chart. The Admiral computes
    /// every number; this only shapes them.
    /// </summary>
    public static class HarborChartMapper
    {
        #region Public-Methods

        /// <summary>
        /// Bucket starts (UTC) shared by every series: FromUtc plus a bucket width per bucket.
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>Bucket starts.</returns>
        public static List<DateTime> BucketStarts(HarborMetrics metrics)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            List<DateTime> starts = new List<DateTime>();
            DateTime from = DateTime.SpecifyKind(metrics.FromUtc, DateTimeKind.Utc);
            int minutes = Math.Max(1, metrics.BucketMinutes);
            for (int i = 0; i < metrics.BucketCount; i++) starts.Add(from.AddMinutes((double)i * minutes));
            return starts;
        }

        /// <summary>
        /// True when the response holds anything worth charting (the dashboard's empty-state rule).
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>True when there is data.</returns>
        public static bool HasAnyData(HarborMetrics metrics)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            HarborJobMetrics jobs = metrics.Jobs;
            if (jobs.MissionsFinished + jobs.MissionsFailed + jobs.InteractiveFinished + jobs.InteractiveFailed + jobs.Running > 0) return true;
            if (metrics.Slots.Peak > 0) return true;
            if (metrics.LaunchSpeed.Count > 0) return true;
            if (metrics.Tokens.RecordCount > 0) return true;
            if (metrics.Link.RoundTrip.Any(b => b.HeartbeatCount > 0)) return true;
            return metrics.Link.Segments.Any(s => s.State != HarborLinkSegmentStateEnum.Unknown);
        }

        /// <summary>
        /// Jobs over time: missions finished and failed, then interactive finished and failed, stacked per bucket.
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>Chart.</returns>
        public static BucketChartModel Jobs(HarborMetrics metrics)
        {
            BucketChartModel model = Base(metrics, "Jobs over time", BucketChartKindEnum.StackedBar, ChartValueFormatEnum.Count);
            List<HarborJobBucket> buckets = Aligned(metrics.Jobs.Buckets, model.BucketCount);
            model.Series.Add(new ChartSeriesData("missionsFinished", "Missions finished", ChartColorEnum.Success, buckets.Select(b => (double?)b.MissionsFinished)));
            model.Series.Add(new ChartSeriesData("missionsFailed", "Missions failed", ChartColorEnum.Danger, buckets.Select(b => (double?)b.MissionsFailed)));
            model.Series.Add(new ChartSeriesData("interactiveFinished", "Interactive finished", ChartColorEnum.Accent, buckets.Select(b => (double?)b.InteractiveFinished)));
            model.Series.Add(new ChartSeriesData("interactiveFailed", "Interactive failed", ChartColorEnum.Warning, buckets.Select(b => (double?)b.InteractiveFailed)));
            return model;
        }

        /// <summary>
        /// Slot usage: the peak and the average of concurrent jobs per bucket, against a dashed line at the capacity. The
        /// value axis tops out at the capacity (MaxConcurrentJobs) itself, with whole-number ticks.
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>Chart.</returns>
        public static BucketChartModel Slots(HarborMetrics metrics)
        {
            BucketChartModel model = Base(metrics, "Slot usage", BucketChartKindEnum.Line, ChartValueFormatEnum.Decimal);
            List<ConcurrencyBucket> buckets = Aligned(metrics.Slots.Buckets, model.BucketCount);
            model.Series.Add(new ChartSeriesData("peak", "Peak", ChartColorEnum.Accent, buckets.Select(b => (double?)b.Peak)));
            model.Series.Add(new ChartSeriesData("average", "Average", ChartColorEnum.Success, buckets.Select(b => (double?)b.Average)));
            int max = metrics.Slots.MaxConcurrentJobs;
            model.Reference = new ChartReferenceLine(max, "Max slots (" + max.ToString(CultureInfo.InvariantCulture) + ")", ChartColorEnum.Danger);
            if (max > 0) model.AxisCeiling = max;
            return model;
        }

        /// <summary>
        /// Heartbeat round trip: the average and the largest per bucket, in milliseconds; gaps where no heartbeat reported
        /// one.
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>Chart.</returns>
        public static BucketChartModel RoundTrip(HarborMetrics metrics)
        {
            BucketChartModel model = Base(metrics, "Heartbeat round trip", BucketChartKindEnum.Line, ChartValueFormatEnum.DurationMs);
            List<HarborRoundTripBucket> buckets = Aligned(metrics.Link.RoundTrip, model.BucketCount);
            model.Series.Add(new ChartSeriesData("average", "Average", ChartColorEnum.Accent, buckets.Select(b => b.AverageMs)));
            model.Series.Add(new ChartSeriesData("max", "Largest", ChartColorEnum.Warning, buckets.Select(b => b.MaxMs.HasValue ? (double?)b.MaxMs.Value : null), true));
            return model;
        }

        /// <summary>
        /// Tokens by type, stacked per bucket: uncached input, cached input, then output. Recorded input already includes
        /// cache reads, so the parts come from <see cref="TokenTypeSplit"/> and each bar adds up to input plus output (the
        /// recorded total) without counting cached tokens twice.
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>Chart.</returns>
        public static BucketChartModel Tokens(HarborMetrics metrics)
        {
            BucketChartModel model = Base(metrics, "Tokens by type", BucketChartKindEnum.StackedBar, ChartValueFormatEnum.Tokens);
            List<TokenTypeSplit> splits = Aligned(metrics.Tokens.Buckets, model.BucketCount)
                .Select(b => TokenTypeSplit.From(b.InputTokens, b.OutputTokens, b.CachedTokens))
                .ToList();
            model.Series.Add(new ChartSeriesData("uncachedInput", "Uncached input", ChartColorEnum.Series1, splits.Select(s => (double?)s.UncachedInput)));
            model.Series.Add(new ChartSeriesData("cachedInput", "Cached input", ChartColorEnum.Series2, splits.Select(s => (double?)s.CachedInput)));
            model.Series.Add(new ChartSeriesData("output", "Output", ChartColorEnum.Series3, splits.Select(s => (double?)s.Output)));
            return model;
        }

        /// <summary>
        /// Token totals per runtime and model over the window, most tokens first (the table beside the token chart).
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>Rows.</returns>
        public static List<HarborTokenSeries> TokenRows(HarborMetrics metrics)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            return metrics.Tokens.Series
                .Where(s => s != null)
                .OrderByDescending(s => s.TotalTokens)
                .ThenBy(s => s.Runtime, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Model, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The link health strip over the window.
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>Strip.</returns>
        public static StatusStripModel Link(HarborMetrics metrics)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            StatusStripModel model = StatusStripModel.FromSegments(metrics.Link.Segments,
                DateTime.SpecifyKind(metrics.FromUtc, DateTimeKind.Utc), DateTime.SpecifyKind(metrics.ToUtc, DateTimeKind.Utc));
            model.RangeName = ChartFormat.RangeName(metrics.Range);
            model.ConnectedPercent = metrics.Link.ConnectedPercent;
            model.Disconnects = metrics.Link.Disconnects;
            return model;
        }

        /// <summary>
        /// The trend of a runtime's median time to first output per bucket.
        /// </summary>
        /// <param name="speed">Launch speed of one runtime.</param>
        /// <returns>Sparkline.</returns>
        public static SparklineModel LaunchTrend(HarborLaunchSpeed speed)
        {
            if (speed == null) throw new ArgumentNullException(nameof(speed));
            SparklineModel model = new SparklineModel();
            model.Name = "First output trend for " + speed.Runtime;
            model.Format = ChartValueFormatEnum.DurationMs;
            model.Color = ChartColorEnum.Accent;
            model.Values = speed.FirstOutputMedianMsByBucket.Select(v => v.HasValue ? (double?)v.Value : null).ToList();
            return model;
        }

        /// <summary>
        /// The peak concurrent jobs per bucket as a sparkline (the TUI's slot usage).
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <returns>Sparkline.</returns>
        public static SparklineModel SlotTrend(HarborMetrics metrics)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            SparklineModel model = new SparklineModel();
            model.Name = "Peak slots in use";
            model.Format = ChartValueFormatEnum.Count;
            model.Color = ChartColorEnum.Accent;
            model.Values = Aligned(metrics.Slots.Buckets, metrics.BucketCount).Select(b => (double?)b.Peak).ToList();
            return model;
        }

        #endregion

        #region Private-Methods

        private static BucketChartModel Base(HarborMetrics metrics, string title, BucketChartKindEnum kind, ChartValueFormatEnum format)
        {
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            BucketChartModel model = new BucketChartModel();
            model.Title = title;
            model.RangeName = ChartFormat.RangeName(metrics.Range);
            model.Kind = kind;
            model.Format = format;
            model.BucketMinutes = Math.Max(1, metrics.BucketMinutes);
            model.BucketStartsUtc = BucketStarts(metrics);
            return model;
        }

        private static List<T> Aligned<T>(List<T>? buckets, int count) where T : class, new()
        {
            // The Admiral sends one entry per bucket; pad or trim defensively so series always line up by index.
            List<T> result = new List<T>();
            for (int i = 0; i < count; i++) result.Add(buckets != null && i < buckets.Count && buckets[i] != null ? buckets[i] : new T());
            return result;
        }

        #endregion
    }
}
