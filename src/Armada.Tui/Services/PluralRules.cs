namespace Armada.Tui.Services
{
    using System;

    /// <summary>
    /// CLDR cardinal plural categories for the dashboard's locales (the TUI's equivalent of <c>Intl.PluralRules</c>).
    /// Thread-safe (stateless).
    /// </summary>
    public static class PluralRules
    {
        #region Public-Methods

        /// <summary>
        /// Plural category for a count: <c>one</c> or <c>other</c> for the supported locales (French treats 0 and 1 as
        /// one; Chinese, Cantonese, and Japanese have only other).
        /// </summary>
        /// <param name="locale">Locale code.</param>
        /// <param name="count">Count.</param>
        /// <returns>Category name.</returns>
        public static string Select(string? locale, long count)
        {
            string lang = (locale ?? "en").Split('-')[0].ToLowerInvariant();
            switch (lang)
            {
                case "ja":
                case "zh":
                case "yue":
                case "ko":
                    return "other";
                case "fr":
                    return count == 0 || count == 1 ? "one" : "other";
                default:
                    return count == 1 ? "one" : "other";
            }
        }

        #endregion
    }
}
