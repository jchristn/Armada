namespace Armada.Core.Metrics
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The windows a Harbor metrics request can ask for, their wire names (1h, 24h, 7d), and their bucket sizes: the last
    /// hour in 1-minute buckets (60), the last 24 hours in 30-minute buckets (48), and the last 7 days in 3-hour buckets
    /// (56).
    /// </summary>
    public static class HarborMetricsRanges
    {
        #region Public-Members

        /// <summary>
        /// Wire name of the default range.
        /// </summary>
        public const string DefaultWireName = "24h";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a wire name (1h, 24h, or 7d, case-insensitive). Null or empty means the default (24h).
        /// </summary>
        /// <param name="value">The wire name.</param>
        /// <param name="range">The range, when recognized.</param>
        /// <returns>True when recognized.</returns>
        public static bool TryParse(string? value, out HarborMetricsRangeEnum range)
        {
            range = HarborMetricsRangeEnum.OneDay;
            if (String.IsNullOrWhiteSpace(value)) return true;
            switch (value.Trim().ToLowerInvariant())
            {
                case "1h":
                    range = HarborMetricsRangeEnum.OneHour;
                    return true;
                case "24h":
                    range = HarborMetricsRangeEnum.OneDay;
                    return true;
                case "7d":
                    range = HarborMetricsRangeEnum.SevenDays;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Wire name of a range.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <returns>1h, 24h, or 7d.</returns>
        public static string ToWireName(HarborMetricsRangeEnum range)
        {
            switch (range)
            {
                case HarborMetricsRangeEnum.OneHour: return "1h";
                case HarborMetricsRangeEnum.SevenDays: return "7d";
                default: return "24h";
            }
        }

        /// <summary>
        /// Length of a range.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <returns>The window length.</returns>
        public static TimeSpan Duration(HarborMetricsRangeEnum range)
        {
            switch (range)
            {
                case HarborMetricsRangeEnum.OneHour: return TimeSpan.FromHours(1);
                case HarborMetricsRangeEnum.SevenDays: return TimeSpan.FromDays(7);
                default: return TimeSpan.FromHours(24);
            }
        }

        /// <summary>
        /// Bucket width of a range.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <returns>The bucket width.</returns>
        public static TimeSpan BucketSize(HarborMetricsRangeEnum range)
        {
            switch (range)
            {
                case HarborMetricsRangeEnum.OneHour: return TimeSpan.FromMinutes(1);
                case HarborMetricsRangeEnum.SevenDays: return TimeSpan.FromHours(3);
                default: return TimeSpan.FromMinutes(30);
            }
        }

        /// <summary>
        /// The bucket layout of a range ending at the bucket that contains <paramref name="nowUtc"/>.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <param name="nowUtc">The current time (UTC).</param>
        /// <returns>The layout.</returns>
        public static TimeBucketLayout Layout(HarborMetricsRangeEnum range, DateTime nowUtc)
        {
            return TimeBucketLayout.EndingAt(nowUtc, Duration(range), BucketSize(range));
        }

        #endregion
    }
}
