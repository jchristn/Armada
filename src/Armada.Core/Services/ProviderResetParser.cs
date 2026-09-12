namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Pure parser that extracts a provider rate-limit / usage-limit reset time from runtime output. Used to
    /// set a captain's quarantine window to the real reset time when the provider states one, falling back to
    /// a configured backoff otherwise. An unparseable or out-of-range value is never trusted.
    /// </summary>
    public static class ProviderResetParser
    {
        #region Private-Members

        private static readonly TimeSpan _MaxWindow = TimeSpan.FromHours(24);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to parse a reset time from provider output relative to <paramref name="nowUtc"/>. Recognizes a
        /// numeric retry-after (seconds), a "resets/try again in N seconds/minutes/hours" phrase, and an
        /// explicit ISO-8601 timestamp following a reset/retry cue. The result is clamped to the interval
        /// (now, now + 24h]; anything outside is rejected.
        /// </summary>
        /// <param name="output">Runtime output / failure text.</param>
        /// <param name="nowUtc">Reference "now" in UTC.</param>
        /// <param name="resetUtc">The parsed reset time, when found.</param>
        /// <returns>True when a valid, in-range reset time was parsed.</returns>
        public static bool TryParseResetUtc(string? output, DateTime nowUtc, out DateTime? resetUtc)
        {
            resetUtc = null;
            if (String.IsNullOrWhiteSpace(output)) return false;

            string text = output!;

            // 1) An explicit ISO-8601 timestamp near a reset/retry cue.
            Match iso = Regex.Match(text,
                @"(?i)(?:reset|retry|available|try\s+again)[^0-9]{0,20}(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?Z?)");
            if (iso.Success && DateTime.TryParse(iso.Groups[1].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsedIso))
            {
                return Accept(parsedIso.ToUniversalTime(), nowUtc, out resetUtc);
            }

            // 2) An HTTP-style retry-after in seconds.
            Match retryAfter = Regex.Match(text, @"(?i)retry[-\s]?after\s*[:=]?\s*(\d{1,6})");
            if (retryAfter.Success && Int32.TryParse(retryAfter.Groups[1].Value, out int retrySeconds))
            {
                return Accept(nowUtc.AddSeconds(retrySeconds), nowUtc, out resetUtc);
            }

            // 3) A "resets / try again in N <unit>" phrase.
            Match relative = Regex.Match(text,
                @"(?i)(?:reset[s]?|retry|try\s+again|available)\s+in\s+(\d{1,5})\s*(seconds?|secs?|minutes?|mins?|hours?|hrs?|h|m|s)\b");
            if (relative.Success && Int32.TryParse(relative.Groups[1].Value, out int amount))
            {
                string unit = relative.Groups[2].Value.ToLowerInvariant();
                TimeSpan span = unit.StartsWith("h", StringComparison.Ordinal)
                    ? TimeSpan.FromHours(amount)
                    : unit.StartsWith("m", StringComparison.Ordinal)
                        ? TimeSpan.FromMinutes(amount)
                        : TimeSpan.FromSeconds(amount);
                return Accept(nowUtc.Add(span), nowUtc, out resetUtc);
            }

            return false;
        }

        #endregion

        #region Private-Methods

        private static bool Accept(DateTime candidateUtc, DateTime nowUtc, out DateTime? resetUtc)
        {
            resetUtc = null;
            if (candidateUtc <= nowUtc) return false;
            if (candidateUtc > nowUtc + _MaxWindow) return false;
            resetUtc = candidateUtc;
            return true;
        }

        #endregion
    }
}
