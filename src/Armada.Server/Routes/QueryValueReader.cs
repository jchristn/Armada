namespace Armada.Server.Routes
{
    using System;
    using WatsonWebserver.Core;

    /// <summary>
    /// Reads one REST query value. Watson stores the raw query text, so a percent-escape is decoded once here
    /// before a route parses it. A literal plus sign is left unchanged. A malformed escape is returned as stored
    /// so the route can still answer.
    /// </summary>
    internal static class QueryValueReader
    {
        #region Public-Methods

        /// <summary>
        /// Read one query value by key. The key itself is not decoded.
        /// </summary>
        /// <param name="query">Request query parameters.</param>
        /// <param name="key">Query key.</param>
        /// <returns>
        /// The value decoded once, the raw value when the escape is malformed, or null when the key is absent.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        public static string? Read(RequestParameters query, string key)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return Decode(query.GetValueOrDefault(key));
        }

        #endregion

        #region Private-Methods

        private static string? Decode(string? value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            if (value.IndexOf('%') < 0) return value;
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return value;
            }
        }

        #endregion
    }
}
