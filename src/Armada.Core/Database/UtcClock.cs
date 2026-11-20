namespace Armada.Core.Database
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Puts a timestamp on the UTC clock before a database provider binds it or writes it as text.
    /// Armada stores every timestamp in UTC, so a value with no kind (as read back from a native
    /// timestamp column, or built without a kind) is already on the UTC clock and is only marked UTC.
    /// A local value is converted to UTC. A UTC value is unchanged. Thread safe: every member is pure.
    /// </summary>
    internal static class UtcClock
    {
        /// <summary>
        /// Return the value on the UTC clock with <see cref="DateTimeKind.Utc"/>.
        /// </summary>
        /// <param name="value">Timestamp of any kind.</param>
        /// <returns>The UTC timestamp.</returns>
        internal static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return value;
        }

        /// <summary>
        /// Return the nullable value on the UTC clock, or null.
        /// </summary>
        /// <param name="value">Timestamp of any kind, or null.</param>
        /// <returns>The UTC timestamp, or null.</returns>
        internal static DateTime? ToUtc(DateTime? value)
        {
            return value.HasValue ? ToUtc(value.Value) : (DateTime?)null;
        }

        /// <summary>
        /// Format the value on the UTC clock with an invariant culture.
        /// </summary>
        /// <param name="value">Timestamp of any kind.</param>
        /// <param name="format">Format string, for example "o" or "yyyy-MM-ddTHH:mm:ss.fffffffZ".</param>
        /// <returns>The formatted UTC timestamp.</returns>
        internal static string Format(DateTime value, string format)
        {
            return ToUtc(value).ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse stored timestamp text onto the UTC clock. Text with an offset or a Z suffix is
        /// converted to UTC; text with neither is read as UTC, matching how it was written.
        /// </summary>
        /// <param name="value">Stored timestamp text.</param>
        /// <returns>The UTC timestamp.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
        /// <exception cref="FormatException">Thrown when <paramref name="value"/> is not a timestamp.</exception>
        internal static DateTime Parse(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }
    }
}
