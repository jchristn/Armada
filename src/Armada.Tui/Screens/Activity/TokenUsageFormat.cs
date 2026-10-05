namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Token Usage number and time formatting (the dashboard's <c>formatTokens</c> and <c>formatBucketLabel</c>).
    /// </summary>
    public static class TokenUsageFormat
    {
        #region Public-Methods

        /// <summary>
        /// Compact token count: 1.5K, 2M, 3.1B, or the whole number below 1000.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Text.</returns>
        public static string Tokens(double value)
        {
            double abs = Math.Abs(value);
            if (abs >= 1e9) return TrimZero(value / 1e9) + "B";
            if (abs >= 1e6) return TrimZero(value / 1e6) + "M";
            if (abs >= 1e3) return TrimZero(value / 1e3) + "K";
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// X-axis label for a bucket start: time of day for short steps, date and time for ranges over two days.
        /// </summary>
        /// <param name="utc">Bucket start (UTC).</param>
        /// <param name="stepMinutes">Bucket width in minutes.</param>
        /// <param name="hours">Range in hours.</param>
        /// <param name="culture">Culture.</param>
        /// <returns>Label.</returns>
        public static string BucketLabel(DateTime utc, double stepMinutes, int hours, CultureInfo culture)
        {
            DateTime local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
            if (stepMinutes <= 15) return local.ToString("HH:mm", culture);
            if (hours > 48) return local.ToString("MMM d HH:mm", culture);
            return local.ToString("HH:mm", culture);
        }

        #endregion

        #region Private-Methods

        private static string TrimZero(double value)
        {
            string fixedText = value.ToString("0.0", CultureInfo.InvariantCulture);
            return fixedText.EndsWith(".0", StringComparison.Ordinal) ? fixedText.Substring(0, fixedText.Length - 2) : fixedText;
        }

        #endregion
    }
}
