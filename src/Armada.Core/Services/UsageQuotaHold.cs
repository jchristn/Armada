namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// The structured reason reference of a quota-owned captain quarantine. A usage observation may only
    /// extend or clear a hold whose <see cref="Armada.Core.Models.Captain.QuarantineReason"/> carries this
    /// prefix; manual, health, crash-loop, configuration and provider-error holds use other reasons and are
    /// never touched. The reason also records the capture time of the observation that set it, which is the
    /// compare-and-set key that stops a late or older sample from overwriting a newer one.
    /// </summary>
    public static class UsageQuotaHold
    {
        #region Public-Members

        /// <summary>
        /// Prefix of every quota-owned quarantine reason.
        /// </summary>
        public const string ReasonPrefix = "usage quota: ";

        #endregion

        #region Private-Members

        private const string _ObservedMarker = "; observed ";
        private const string _TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Format the reason for a quota-owned hold.
        /// </summary>
        /// <param name="windows">Exhausted window names.</param>
        /// <param name="observedUtc">Capture time of the observation.</param>
        /// <returns>The reason text.</returns>
        public static string FormatReason(IEnumerable<string>? windows, DateTime observedUtc)
        {
            List<string> names = new List<string>();
            if (windows != null)
            {
                foreach (string name in windows)
                {
                    if (!String.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
                }
            }

            string list = names.Count == 0 ? "limit" : String.Join(", ", names);
            return ReasonPrefix + list + " exhausted" + _ObservedMarker + Utc(observedUtc).ToString(_TimestampFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Whether a quarantine reason is quota-owned.
        /// </summary>
        /// <param name="reason">Quarantine reason, or null.</param>
        /// <returns>True when a usage observation owns the hold.</returns>
        public static bool IsQuotaOwned(string? reason)
        {
            return !String.IsNullOrEmpty(reason) && reason.StartsWith(ReasonPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Read the capture time recorded in a quota-owned reason.
        /// </summary>
        /// <param name="reason">Quarantine reason.</param>
        /// <param name="observedUtc">The recorded capture time when found.</param>
        /// <returns>True when the reason is quota-owned and records a valid time.</returns>
        public static bool TryReadObservedUtc(string? reason, out DateTime observedUtc)
        {
            observedUtc = DateTime.MinValue;
            if (!IsQuotaOwned(reason)) return false;
            int index = reason!.LastIndexOf(_ObservedMarker, StringComparison.Ordinal);
            if (index < 0) return false;
            string text = reason.Substring(index + _ObservedMarker.Length).Trim();
            if (!DateTime.TryParseExact(text, _TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)) return false;
            observedUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        #endregion

        #region Private-Methods

        private static DateTime Utc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value.ToUniversalTime();
        }

        #endregion
    }
}
