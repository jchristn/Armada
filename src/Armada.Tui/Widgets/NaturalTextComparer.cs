namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Compares cell text for local sorting: numbers (including thousands separators and percentages) compare
    /// numerically, ISO dates compare chronologically, everything else case-insensitively. Thread-safe.
    /// </summary>
    public class NaturalTextComparer : IComparer<string>
    {
        #region Public-Methods

        /// <inheritdoc />
        public int Compare(string? x, string? y)
        {
            string a = (x ?? "").Trim();
            string b = (y ?? "").Trim();
            if (TryNumber(a, out double na) && TryNumber(b, out double nb)) return na.CompareTo(nb);
            if (DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime da)
                && DateTime.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime db)) return da.CompareTo(db);
            return String.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Private-Methods

        private static bool TryNumber(string text, out double value)
        {
            string cleaned = text.TrimEnd('%').Replace(",", "");
            return Double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        #endregion
    }
}
