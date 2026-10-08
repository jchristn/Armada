namespace Armada.Core.Metrics.Charts
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Text for chart values and bucket times, shared by the Harbor app and the TUI so both read the same way as the
    /// dashboard: durations as 850ms, 4.2s, 3m 12s, or 1h 5m; tokens as 950, 1.5K, or 2.3M; bucket times in the
    /// viewer's time zone.
    /// </summary>
    public static class ChartFormat
    {
        #region Public-Methods

        /// <summary>
        /// Write a value in a format.
        /// </summary>
        /// <param name="value">Value, or null for none.</param>
        /// <param name="format">Format.</param>
        /// <returns>Text; "-" for null.</returns>
        public static string Value(double? value, ChartValueFormatEnum format)
        {
            if (value == null || Double.IsNaN(value.Value)) return "-";
            switch (format)
            {
                case ChartValueFormatEnum.DurationMs:
                    return Duration(value.Value);
                case ChartValueFormatEnum.Tokens:
                    return Tokens(value.Value);
                case ChartValueFormatEnum.Decimal:
                    return Math.Round(value.Value, 2).ToString("0.##", CultureInfo.InvariantCulture);
                default:
                    return Math.Round(value.Value).ToString("0", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// A compact duration: 850ms, 4.2s, 3m 12s, 1h 5m.
        /// </summary>
        /// <param name="milliseconds">Milliseconds, or null.</param>
        /// <returns>Text; "-" for null.</returns>
        public static string Duration(double? milliseconds)
        {
            if (milliseconds == null || Double.IsNaN(milliseconds.Value)) return "-";
            double value = Math.Max(0, milliseconds.Value);
            if (value < 1000) return Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + "ms";
            double seconds = value / 1000;
            if (seconds < 60) return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
            long totalSeconds = (long)Math.Round(seconds);
            long minutes = totalSeconds / 60;
            if (minutes < 60) return minutes.ToString(CultureInfo.InvariantCulture) + "m " + (totalSeconds % 60).ToString(CultureInfo.InvariantCulture) + "s";
            return (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h " + (minutes % 60).ToString(CultureInfo.InvariantCulture) + "m";
        }

        /// <summary>
        /// A compact token count: 950, 1.5K, 2.3M, 1.1B.
        /// </summary>
        /// <param name="value">Tokens.</param>
        /// <returns>Text.</returns>
        public static string Tokens(double value)
        {
            double abs = Math.Abs(value);
            if (abs >= 1_000_000_000) return (value / 1_000_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "B";
            if (abs >= 1_000_000) return (value / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (abs >= 1_000) return (value / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A short axis label for a bucket: the local time of day, with the weekday for buckets of three hours or more.
        /// </summary>
        /// <param name="startUtc">Bucket start (UTC).</param>
        /// <param name="bucketMinutes">Bucket width in minutes.</param>
        /// <param name="zone">Viewer's time zone.</param>
        /// <returns>Label.</returns>
        public static string BucketLabel(DateTime startUtc, int bucketMinutes, TimeZoneInfo zone)
        {
            DateTime local = ToZone(startUtc, zone);
            return bucketMinutes >= 180
                ? local.ToString("ddd HH:mm", CultureInfo.InvariantCulture)
                : local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The span a bucket covers in the viewer's time zone, for tooltips: "Thu Oct 8, 14:30 - 15:00".
        /// </summary>
        /// <param name="startUtc">Bucket start (UTC).</param>
        /// <param name="bucketMinutes">Bucket width in minutes.</param>
        /// <param name="zone">Viewer's time zone.</param>
        /// <returns>Text.</returns>
        public static string BucketSpan(DateTime startUtc, int bucketMinutes, TimeZoneInfo zone)
        {
            DateTime start = ToZone(startUtc, zone);
            DateTime end = ToZone(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc).AddMinutes(Math.Max(1, bucketMinutes)), zone);
            string first = start.ToString("ddd MMM d, HH:mm", CultureInfo.InvariantCulture);
            string last = end.Date == start.Date
                ? end.ToString("HH:mm", CultureInfo.InvariantCulture)
                : end.ToString("ddd MMM d, HH:mm", CultureInfo.InvariantCulture);
            return first + " - " + last;
        }

        /// <summary>
        /// A time span between two UTC instants in the viewer's time zone: "Thu Oct 8, 14:30 - 15:12".
        /// </summary>
        /// <param name="startUtc">Start (UTC).</param>
        /// <param name="endUtc">End (UTC).</param>
        /// <param name="zone">Viewer's time zone.</param>
        /// <returns>Text.</returns>
        public static string Span(DateTime startUtc, DateTime endUtc, TimeZoneInfo zone)
        {
            DateTime start = ToZone(startUtc, zone);
            DateTime end = ToZone(endUtc, zone);
            string first = start.ToString("ddd MMM d, HH:mm", CultureInfo.InvariantCulture);
            string last = end.Date == start.Date
                ? end.ToString("HH:mm", CultureInfo.InvariantCulture)
                : end.ToString("ddd MMM d, HH:mm", CultureInfo.InvariantCulture);
            return first + " - " + last;
        }

        /// <summary>
        /// Plain-language name of a metrics range: "last hour", "last 24 hours", or "last 7 days".
        /// </summary>
        /// <param name="range">Wire name (1h, 24h, 7d).</param>
        /// <returns>Text.</returns>
        public static string RangeName(string? range)
        {
            if (!HarborMetricsRanges.TryParse(range, out Armada.Core.Enums.HarborMetricsRangeEnum parsed)) parsed = Armada.Core.Enums.HarborMetricsRangeEnum.OneDay;
            switch (parsed)
            {
                case Armada.Core.Enums.HarborMetricsRangeEnum.OneHour: return "last hour";
                case Armada.Core.Enums.HarborMetricsRangeEnum.SevenDays: return "last 7 days";
                default: return "last 24 hours";
            }
        }

        #endregion

        #region Private-Methods

        private static DateTime ToZone(DateTime utc, TimeZoneInfo zone)
        {
            DateTime asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(asUtc, zone ?? TimeZoneInfo.Utc);
        }

        #endregion
    }
}
