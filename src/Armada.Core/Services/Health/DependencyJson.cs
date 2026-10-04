namespace Armada.Core.Services.Health
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// Shared JSON helpers for dependency tool output: case-insensitive options and extraction of the JSON payload from
    /// output that may carry leading log lines. Stateless and thread-safe.
    /// </summary>
    public static class DependencyJson
    {
        #region Public-Members

        /// <summary>
        /// Deserialization options (case-insensitive property names, comments and trailing commas allowed).
        /// </summary>
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Extract the JSON object from tool output: everything from the first '{' to the last '}'.
        /// </summary>
        /// <param name="output">Raw output.</param>
        /// <returns>The JSON text, or null when the output contains no object.</returns>
        public static string? ExtractObject(string? output)
        {
            if (String.IsNullOrWhiteSpace(output)) return null;
            int start = output.IndexOf('{');
            int end = output.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            return output.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Map a severity string from dotnet or npm to <see cref="VulnerabilitySeverityEnum"/>. Unrecognized or missing
        /// values map to Moderate; "info" maps to Low.
        /// </summary>
        /// <param name="severity">Severity text.</param>
        /// <returns>The severity.</returns>
        public static VulnerabilitySeverityEnum ParseSeverity(string? severity)
        {
            if (String.IsNullOrWhiteSpace(severity)) return VulnerabilitySeverityEnum.Moderate;
            switch (severity.Trim().ToLowerInvariant())
            {
                case "critical": return VulnerabilitySeverityEnum.Critical;
                case "high": return VulnerabilitySeverityEnum.High;
                case "moderate":
                case "medium": return VulnerabilitySeverityEnum.Moderate;
                case "low":
                case "info": return VulnerabilitySeverityEnum.Low;
                default: return VulnerabilitySeverityEnum.Moderate;
            }
        }

        /// <summary>
        /// Whether tool text indicates that a package restore or install is required.
        /// </summary>
        /// <param name="text">Tool output or problem text.</param>
        /// <returns>True when a restore is indicated.</returns>
        public static bool IndicatesRestore(string? text)
        {
            if (String.IsNullOrEmpty(text)) return false;
            return text.Contains("restore", StringComparison.OrdinalIgnoreCase)
                || text.Contains("assets file", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ENOLOCK", StringComparison.OrdinalIgnoreCase)
                || text.Contains("npm install", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
