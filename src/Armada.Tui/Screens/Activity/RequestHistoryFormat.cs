namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;

    /// <summary>
    /// Formatting helpers shared by the API Requests screen and the API Explorer (the dashboard's
    /// <c>formatBytes</c>, <c>parseJsonString</c>, and body prettifying).
    /// </summary>
    public static class RequestHistoryFormat
    {
        #region Public-Methods

        /// <summary>
        /// Human-readable size ("0 B", "512 B", "1.5 KB", "12 MB").
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        /// <returns>Text.</returns>
        public static string Bytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = new string[] { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            string format = value >= 10 || unit == 0 ? "0" : "0.0";
            return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unit];
        }

        /// <summary>
        /// Milliseconds with two decimals ("12.34 ms").
        /// </summary>
        /// <param name="ms">Milliseconds.</param>
        /// <returns>Text.</returns>
        public static string Ms(double ms)
        {
            return ms.ToString("0.00", CultureInfo.InvariantCulture) + " ms";
        }

        /// <summary>
        /// Parse a JSON object of string values (null values become empty); empty on null or invalid input.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>Ordered pairs.</returns>
        public static List<KeyValuePair<string, string>> ParseDictionary(string? json)
        {
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            if (String.IsNullOrWhiteSpace(json)) return result;
            try
            {
                Dictionary<string, string?>? parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(json!);
                if (parsed == null) return result;
                foreach (KeyValuePair<string, string?> kvp in parsed) result.Add(new KeyValuePair<string, string>(kvp.Key, kvp.Value ?? ""));
            }
            catch (JsonException)
            {
            }

            return result;
        }

        /// <summary>
        /// Pairs as indented JSON (the dashboard's detail block text), "{}" when empty.
        /// </summary>
        /// <param name="pairs">Pairs.</param>
        /// <returns>JSON text.</returns>
        public static string PairsJson(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kvp in pairs) dict[kvp.Key] = kvp.Value;
            return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Pretty-print a body when the content type is JSON and it parses; "(empty)" for empty.
        /// </summary>
        /// <param name="body">Body.</param>
        /// <param name="contentType">Content type.</param>
        /// <returns>Display text.</returns>
        public static string Prettify(string? body, string? contentType)
        {
            if (String.IsNullOrEmpty(body)) return "(empty)";
            if ((contentType ?? "").Contains("application/json", StringComparison.OrdinalIgnoreCase)) return PrettyJson(body!);
            return body!;
        }

        /// <summary>
        /// Indented JSON when the text parses, else the text unchanged.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Text.</returns>
        public static string PrettyJson(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return text ?? "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                }
            }
            catch (JsonException)
            {
                return text;
            }
        }

        /// <summary>
        /// True when the text parses as JSON.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>True when JSON.</returns>
        public static bool IsJson(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text!))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        #endregion
    }
}
