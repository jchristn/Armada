namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Fluent builder for <see cref="ITextLocalizer.T(string, IDictionary{string, object})"/> placeholder values.
    /// </summary>
    public static class LocalizationArgs
    {
        #region Public-Methods

        /// <summary>
        /// One placeholder.
        /// </summary>
        /// <param name="name">Placeholder name.</param>
        /// <param name="value">Value.</param>
        /// <returns>Arguments.</returns>
        public static Dictionary<string, object?> Of(string name, object? value)
        {
            Dictionary<string, object?> args = new Dictionary<string, object?>(StringComparer.Ordinal);
            args[name] = value;
            return args;
        }

        /// <summary>
        /// Two placeholders.
        /// </summary>
        /// <param name="name1">First name.</param>
        /// <param name="value1">First value.</param>
        /// <param name="name2">Second name.</param>
        /// <param name="value2">Second value.</param>
        /// <returns>Arguments.</returns>
        public static Dictionary<string, object?> Of(string name1, object? value1, string name2, object? value2)
        {
            Dictionary<string, object?> args = Of(name1, value1);
            args[name2] = value2;
            return args;
        }

        /// <summary>
        /// Three placeholders.
        /// </summary>
        /// <param name="name1">First name.</param>
        /// <param name="value1">First value.</param>
        /// <param name="name2">Second name.</param>
        /// <param name="value2">Second value.</param>
        /// <param name="name3">Third name.</param>
        /// <param name="value3">Third value.</param>
        /// <returns>Arguments.</returns>
        public static Dictionary<string, object?> Of(string name1, object? value1, string name2, object? value2, string name3, object? value3)
        {
            Dictionary<string, object?> args = Of(name1, value1, name2, value2);
            args[name3] = value3;
            return args;
        }

        #endregion
    }
}
