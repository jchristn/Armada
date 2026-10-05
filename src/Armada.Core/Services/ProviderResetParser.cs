namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Reads a provider rate-limit / usage-limit reset time from a structured <see cref="RuntimeProviderError"/>
    /// (an absolute reset time the provider stated, or its Retry-After seconds). Used to set a captain's quarantine
    /// window to the real reset time when the provider states one, falling back to the configured backoff
    /// otherwise. Output text is never searched for a time, and an out-of-range value is never trusted.
    /// </summary>
    public static class ProviderResetParser
    {
        #region Private-Members

        private static readonly TimeSpan _MaxWindow = TimeSpan.FromHours(24);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to read a reset time from a provider error relative to <paramref name="nowUtc"/>. An explicit
        /// <see cref="RuntimeProviderError.ResetUtc"/> wins over <see cref="RuntimeProviderError.RetryAfterSeconds"/>.
        /// The result must fall in the interval (now, now + 24h]; anything outside is rejected.
        /// </summary>
        /// <param name="error">The structured provider error; null yields no reset time.</param>
        /// <param name="nowUtc">Reference "now" in UTC.</param>
        /// <param name="resetUtc">The reset time, when found.</param>
        /// <returns>True when a valid, in-range reset time was found.</returns>
        public static bool TryGetResetUtc(RuntimeProviderError? error, DateTime nowUtc, out DateTime? resetUtc)
        {
            resetUtc = null;
            if (error == null) return false;

            if (error.ResetUtc.HasValue)
            {
                DateTime candidate = error.ResetUtc.Value.Kind == DateTimeKind.Utc
                    ? error.ResetUtc.Value
                    : DateTime.SpecifyKind(error.ResetUtc.Value, DateTimeKind.Utc);
                return Accept(candidate, nowUtc, out resetUtc);
            }

            if (error.RetryAfterSeconds.HasValue && error.RetryAfterSeconds.Value > 0)
            {
                return Accept(nowUtc.AddSeconds(error.RetryAfterSeconds.Value), nowUtc, out resetUtc);
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
