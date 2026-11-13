namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Codec for the windows column of a usage snapshot (usage_snapshots.windows_json) and the bounded
    /// windows summary of a latest-state row. Windows are stored as the typed <see cref="UsageWindow"/>
    /// DTO list in the documented camelCase contract, so the stored text matches the Custom executable
    /// contract. Round-tripping null, empty, an empty object or malformed JSON never throws: unreadable
    /// text becomes an empty list rather than a failed read.
    /// </summary>
    public static class UsageWindowsJson
    {
        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <summary>
        /// Serialize the windows; null or empty becomes null so unobserved captures store no windows text.
        /// </summary>
        /// <param name="windows">Windows.</param>
        /// <returns>JSON, or null.</returns>
        public static string? Serialize(List<UsageWindow>? windows)
        {
            if (windows == null || windows.Count == 0) return null;
            return JsonSerializer.Serialize(windows, _Options);
        }

        /// <summary>
        /// Deserialize the windows; null, empty or unreadable JSON becomes an empty list.
        /// </summary>
        /// <param name="json">JSON, or null.</param>
        /// <returns>The windows.</returns>
        public static List<UsageWindow> Deserialize(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new List<UsageWindow>();
            try
            {
                List<UsageWindow>? windows = JsonSerializer.Deserialize<List<UsageWindow>>(json, _Options);
                return windows ?? new List<UsageWindow>();
            }
            catch (JsonException)
            {
                return new List<UsageWindow>();
            }
        }
    }
}