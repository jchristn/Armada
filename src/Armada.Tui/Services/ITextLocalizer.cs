namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Translates user-visible text through the shared dashboard catalog and formats numbers and dates for the active
    /// locale. Widgets take this interface so they can be rendered in tests with any locale.
    /// </summary>
    public interface ITextLocalizer
    {
        /// <summary>
        /// Active locale code, for example <c>ja</c>.
        /// </summary>
        string Locale { get; }

        /// <summary>
        /// Translate English source text (the catalog key).
        /// </summary>
        /// <param name="text">English text.</param>
        /// <returns>Translated text, or the source text when no translation exists.</returns>
        string T(string text);

        /// <summary>
        /// Translate English source text and fill <c>{{name}}</c> placeholders and ICU plural blocks
        /// (<c>{count, plural, one {# item} other {# items}}</c>).
        /// </summary>
        /// <param name="text">English text.</param>
        /// <param name="args">Placeholder values.</param>
        /// <returns>Translated, formatted text.</returns>
        string T(string text, IDictionary<string, object?> args);

        /// <summary>
        /// Format a number for the active locale.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Formatted number.</returns>
        string FormatNumber(long value);

        /// <summary>
        /// Format a timestamp (converted to local time) for the active locale.
        /// </summary>
        /// <param name="utc">UTC time.</param>
        /// <returns>Formatted date and time.</returns>
        string FormatDateTime(DateTime utc);

        /// <summary>
        /// Format the time between a past timestamp and now ("5m ago"), translated.
        /// </summary>
        /// <param name="utc">UTC time.</param>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <returns>Relative time.</returns>
        string FormatRelative(DateTime utc, DateTime nowUtc);
    }
}
