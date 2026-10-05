namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A Token Usage time range: key, English label, hours covered, and bucket width (hour: 2 per minute, day: 4 per
    /// hour, week: 12 per day, month: 4 per day; the dashboard's TIME_RANGES).
    /// </summary>
    public class TokenUsageRange
    {
        #region Public-Members

        /// <summary>
        /// Key ("hour", "day", "week", "month").
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Hours covered.
        /// </summary>
        public int Hours { get; }

        /// <summary>
        /// Bucket width in minutes.
        /// </summary>
        public double StepMinutes { get; }

        /// <summary>
        /// Every range in display order.
        /// </summary>
        public static IReadOnlyList<TokenUsageRange> All { get; } = new List<TokenUsageRange>
        {
            new TokenUsageRange("hour", "Last Hour", 1, 0.5),
            new TokenUsageRange("day", "Last Day", 24, 15),
            new TokenUsageRange("week", "Last Week", 168, 120),
            new TokenUsageRange("month", "Last Month", 720, 360),
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="label">English label.</param>
        /// <param name="hours">Hours.</param>
        /// <param name="stepMinutes">Bucket minutes.</param>
        public TokenUsageRange(string key, string label, int hours, double stepMinutes)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Label = label ?? key;
            Hours = hours;
            StepMinutes = stepMinutes;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Range for a key (default: day).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Range.</returns>
        public static TokenUsageRange For(string? key)
        {
            foreach (TokenUsageRange r in All)
            {
                if (String.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) return r;
            }

            return All[1];
        }

        #endregion
    }
}
