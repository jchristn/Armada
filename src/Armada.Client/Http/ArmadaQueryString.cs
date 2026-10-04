namespace Armada.Client.Http
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using Armada.Client.Models;

    /// <summary>
    /// Query-string and path helpers that reproduce the dashboard's URL building (<c>buildQuery</c>, the per-entity
    /// <c>build*Query</c> helpers, <c>encodeURIComponent</c>, the workspace path encoding, and base64url browse paths).
    /// Thread-safe (stateless).
    /// </summary>
    public static class ArmadaQueryString
    {
        #region Public-Methods

        /// <summary>
        /// Percent-encode a path segment or query value like JavaScript's <c>encodeURIComponent</c>.
        /// </summary>
        /// <param name="value">Value; null becomes empty.</param>
        /// <returns>Encoded value.</returns>
        public static string Escape(string? value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            return Uri.EscapeDataString(value);
        }

        /// <summary>
        /// Encode a workspace-relative path for a query string, keeping <c>/</c> separators readable (the dashboard's
        /// <c>encodeWorkspaceQueryPath</c>).
        /// </summary>
        /// <param name="path">Relative path; null becomes empty.</param>
        /// <returns>Encoded path.</returns>
        public static string EscapePath(string? path)
        {
            return Escape(path).Replace("%2F", "/");
        }

        /// <summary>
        /// Encode a host path as base64url UTF-8 (the dashboard's <c>encodeBrowsePath</c>).
        /// </summary>
        /// <param name="path">Path; null becomes empty.</param>
        /// <returns>Encoded path with no padding.</returns>
        public static string Base64Url(string? path)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(path ?? "");
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// Build <c>?pageNumber=..&amp;pageSize=..&amp;filters</c> from a page query (the dashboard's <c>buildQuery</c>).
        /// </summary>
        /// <param name="query">Query, or null.</param>
        /// <returns>Query string including the leading <c>?</c>, or empty.</returns>
        public static string FromPage(ArmadaPageQuery? query)
        {
            if (query == null) return "";
            List<string> parts = new List<string>();
            if (query.PageNumber.HasValue) parts.Add("pageNumber=" + query.PageNumber.Value.ToString(CultureInfo.InvariantCulture));
            if (query.PageSize.HasValue) parts.Add("pageSize=" + query.PageSize.Value.ToString(CultureInfo.InvariantCulture));
            foreach (KeyValuePair<string, string> kvp in query.Filters)
            {
                if (String.IsNullOrEmpty(kvp.Value)) continue;
                parts.Add(Escape(kvp.Key) + "=" + Escape(kvp.Value));
            }

            return parts.Count > 0 ? "?" + String.Join("&", parts) : "";
        }

        /// <summary>
        /// Build a query string from the public properties of a typed query object, using camelCase names and the
        /// dashboard's truthiness rules: nulls, empty strings, zero numbers, and non-nullable false booleans are
        /// skipped; nullable booleans and nullable numbers are sent when set; dates are ISO 8601; lists are
        /// comma-joined; <c>SourceTypes</c> is sent as <c>sourceType</c>; <c>Route</c> keeps its slashes.
        /// </summary>
        /// <param name="query">Query object, or null.</param>
        /// <returns>Query string including the leading <c>?</c>, or empty.</returns>
        public static string FromObject(object? query)
        {
            if (query == null) return "";
            List<string> parts = new List<string>();
            PropertyInfo[] properties = query.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo property in properties)
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                if (property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;

                object? value = property.GetValue(query);
                if (value == null) continue;

                string name = QueryName(property.Name);
                bool nullable = Nullable.GetUnderlyingType(property.PropertyType) != null;
                string? text = FormatValue(value, nullable);
                if (String.IsNullOrEmpty(text)) continue;

                if (property.Name == "Route") parts.Add(name + "=" + EscapePath(text));
                else parts.Add(Escape(name) + "=" + Escape(text));
            }

            return parts.Count > 0 ? "?" + String.Join("&", parts) : "";
        }

        /// <summary>
        /// Build a query string from name/value pairs, skipping null or empty values.
        /// </summary>
        /// <param name="pairs">Pairs; null returns empty.</param>
        /// <returns>Query string including the leading <c>?</c>, or empty.</returns>
        public static string FromPairs(IEnumerable<KeyValuePair<string, string?>>? pairs)
        {
            if (pairs == null) return "";
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string?> kvp in pairs)
            {
                if (String.IsNullOrEmpty(kvp.Value)) continue;
                parts.Add(Escape(kvp.Key) + "=" + Escape(kvp.Value));
            }

            return parts.Count > 0 ? "?" + String.Join("&", parts) : "";
        }

        #endregion

        #region Private-Methods

        private static string QueryName(string propertyName)
        {
            if (propertyName == "SourceTypes") return "sourceType";
            if (String.IsNullOrEmpty(propertyName)) return propertyName;
            return Char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        }

        private static string? FormatValue(object value, bool nullable)
        {
            switch (value)
            {
                case string s:
                    return s;
                case bool b:
                    if (!b && !nullable) return null;
                    return b ? "true" : "false";
                case DateTime dt:
                    return dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                case DateTimeOffset dto:
                    return dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                case Enum e:
                    return e.ToString();
                case int i:
                    if (i == 0 && !nullable) return null;
                    return i.ToString(CultureInfo.InvariantCulture);
                case long l:
                    if (l == 0 && !nullable) return null;
                    return l.ToString(CultureInfo.InvariantCulture);
                case double d:
                    if (d == 0 && !nullable) return null;
                    return d.ToString(CultureInfo.InvariantCulture);
                case IEnumerable enumerable:
                    List<string> items = new List<string>();
                    foreach (object? item in enumerable)
                    {
                        if (item == null) continue;
                        string? formatted = FormatValue(item, true);
                        if (!String.IsNullOrEmpty(formatted)) items.Add(formatted);
                    }

                    return items.Count > 0 ? String.Join(",", items) : null;
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        #endregion
    }
}
