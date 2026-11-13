namespace Armada.Core.Services
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Converts the reset and retry values that usage sources report into UTC instants. Absolute values
    /// (Unix epoch seconds, RFC 3339 timestamps, HTTP dates) are taken as they are; relative values
    /// (seconds, Go-style durations such as <c>6m0s</c>) are always added to the time of the observation that
    /// carried them, never to a later polling time. Every result is bounded: a value that would land before
    /// the observation minus <see cref="MaximumPast"/> or after it plus <see cref="MaximumFuture"/> is
    /// rejected (null), so weekly windows are accepted without widening the 24-hour provider-error policy of
    /// <see cref="ProviderResetParser"/>.
    /// </summary>
    public static class UsageResetConverter
    {
        #region Public-Members

        /// <summary>
        /// Furthest a converted reset may lie after its observation: a weekly window plus one day of slack.
        /// </summary>
        public static readonly TimeSpan MaximumFuture = TimeSpan.FromDays(8);

        /// <summary>
        /// Furthest a converted reset may lie before its observation (a window that just rolled over).
        /// </summary>
        public static readonly TimeSpan MaximumPast = TimeSpan.FromHours(24);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Convert absolute Unix epoch seconds.
        /// </summary>
        /// <param name="epochSeconds">Seconds since the Unix epoch, or null.</param>
        /// <param name="observedUtc">Time of the observation that carried the value.</param>
        /// <returns>The UTC instant, or null when absent or out of bounds.</returns>
        public static DateTime? FromEpochSeconds(long? epochSeconds, DateTime observedUtc)
        {
            if (!epochSeconds.HasValue || epochSeconds.Value <= 0) return null;
            DateTime value;
            try
            {
                value = DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }

            return Bound(value, observedUtc);
        }

        /// <summary>
        /// Convert a relative number of seconds, counted from the observation (event) time.
        /// </summary>
        /// <param name="seconds">Seconds until the reset, or null.</param>
        /// <param name="eventUtc">Time of the event that carried the value; never the polling time.</param>
        /// <returns>The UTC instant, or null when absent, negative, non-finite or out of bounds.</returns>
        public static DateTime? FromRelativeSeconds(double? seconds, DateTime eventUtc)
        {
            if (!seconds.HasValue || Double.IsNaN(seconds.Value) || Double.IsInfinity(seconds.Value) || seconds.Value < 0) return null;
            if (seconds.Value > MaximumFuture.TotalSeconds) return null;
            return Bound(Utc(eventUtc).AddSeconds(seconds.Value), eventUtc);
        }

        /// <summary>
        /// Convert an RFC 3339 / ISO 8601 timestamp. A timestamp without an offset is rejected rather than
        /// guessed at, because a local time would shift the reset by the host's offset.
        /// </summary>
        /// <param name="text">Timestamp text.</param>
        /// <param name="observedUtc">Time of the observation that carried the value.</param>
        /// <returns>The UTC instant, or null when absent, malformed, offset-less or out of bounds.</returns>
        public static DateTime? FromRfc3339(string? text, DateTime observedUtc)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string trimmed = text.Trim();
            bool hasOffset = trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
                || (trimmed.Length > 6 && (trimmed[trimmed.Length - 6] == '+' || trimmed[trimmed.Length - 6] == '-') && trimmed[trimmed.Length - 3] == ':');
            if (!hasOffset) return null;
            if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)) return null;
            return Bound(parsed.UtcDateTime, observedUtc);
        }

        /// <summary>
        /// Convert a Go-style duration (<c>1h2m3.5s</c>, <c>6m0s</c>, <c>20ms</c>, <c>1s</c>) used by
        /// OpenAI-style <c>x-ratelimit-reset-*</c> headers, counted from the observation time.
        /// </summary>
        /// <param name="text">Duration text.</param>
        /// <param name="observedUtc">Time of the observation that carried the value.</param>
        /// <returns>The UTC instant, or null when absent, malformed or out of bounds.</returns>
        public static DateTime? FromDuration(string? text, DateTime observedUtc)
        {
            TimeSpan? span = ParseDuration(text);
            if (!span.HasValue) return null;
            return FromRelativeSeconds(span.Value.TotalSeconds, observedUtc);
        }

        /// <summary>
        /// Convert a <c>Retry-After</c> value: delay seconds or an HTTP date.
        /// </summary>
        /// <param name="text">Header value.</param>
        /// <param name="observedUtc">Time of the response that carried it.</param>
        /// <returns>The UTC instant, or null when absent, malformed or out of bounds.</returns>
        public static DateTime? FromRetryAfter(string? text, DateTime observedUtc)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string trimmed = text.Trim();
            if (Double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                return FromRelativeSeconds(seconds, observedUtc);
            if (DateTimeOffset.TryParseExact(trimmed, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
                return Bound(date.UtcDateTime, observedUtc);
            return null;
        }

        /// <summary>
        /// Parse a Go-style duration.
        /// </summary>
        /// <param name="text">Duration text.</param>
        /// <returns>The duration, or null when malformed or negative.</returns>
        public static TimeSpan? ParseDuration(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string s = text.Trim();
            double totalSeconds = 0;
            int i = 0;
            bool any = false;
            while (i < s.Length)
            {
                int start = i;
                while (i < s.Length && (Char.IsDigit(s[i]) || s[i] == '.')) i++;
                if (i == start) return null;
                if (!Double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return null;
                int unitStart = i;
                while (i < s.Length && Char.IsLetter(s[i])) i++;
                string unit = s.Substring(unitStart, i - unitStart);
                switch (unit)
                {
                    case "h": totalSeconds += number * 3600; break;
                    case "m": totalSeconds += number * 60; break;
                    case "s": totalSeconds += number; break;
                    case "ms": totalSeconds += number / 1000.0; break;
                    default: return null;
                }

                any = true;
            }

            if (!any || Double.IsInfinity(totalSeconds) || totalSeconds > MaximumFuture.TotalSeconds) return null;
            return TimeSpan.FromSeconds(totalSeconds);
        }

        #endregion

        #region Private-Methods

        private static DateTime? Bound(DateTime value, DateTime observedUtc)
        {
            DateTime utc = Utc(value);
            DateTime observed = Utc(observedUtc);
            if (utc < observed - MaximumPast || utc > observed + MaximumFuture) return null;
            return utc;
        }

        private static DateTime Utc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value.ToUniversalTime();
        }

        #endregion
    }
}
