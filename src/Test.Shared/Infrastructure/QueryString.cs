namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Parses a URL query string into decoded name/value lists so tests compare parameter values exactly instead of
    /// searching the URL text (where "lines=500" also matches "lines=5000").
    /// </summary>
    public static class QueryString
    {
        #region Public-Methods

        /// <summary>
        /// Parse a query string (with or without the leading '?'). Names are case-sensitive; values are URL-decoded
        /// ('+' is a space). A name without '=' has an empty value.
        /// </summary>
        /// <param name="query">Query string, possibly null or empty.</param>
        /// <returns>Each name with its values in order of appearance.</returns>
        public static Dictionary<string, List<string>> Parse(string? query)
        {
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (String.IsNullOrEmpty(query)) return result;
            string trimmed = query.StartsWith("?", StringComparison.Ordinal) ? query.Substring(1) : query;
            foreach (string pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string name = Decode(eq >= 0 ? pair.Substring(0, eq) : pair);
                string value = eq >= 0 ? Decode(pair.Substring(eq + 1)) : "";
                if (!result.TryGetValue(name, out List<string>? values))
                {
                    values = new List<string>();
                    result[name] = values;
                }

                values.Add(value);
            }

            return result;
        }

        /// <summary>
        /// The first value of a parameter, or null when absent.
        /// </summary>
        /// <param name="query">Query string.</param>
        /// <param name="name">Parameter name (case-sensitive).</param>
        /// <returns>Value or null.</returns>
        public static string? Get(string? query, string name)
        {
            Dictionary<string, List<string>> parsed = Parse(query);
            return parsed.TryGetValue(name, out List<string>? values) && values.Count > 0 ? values[0] : null;
        }

        #endregion

        #region Private-Methods

        private static string Decode(string text)
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }

        #endregion
    }
}
