namespace Armada.Core.Services.Health
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Protocol;

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
        /// Extract the JSON object from tool output that may carry leading or trailing log lines: the largest balanced
        /// top-level object found by the shared string-aware scanner that is valid JSON. A brace inside a log line or a
        /// string literal can no longer stretch the extracted range.
        /// </summary>
        /// <param name="output">Raw output.</param>
        /// <returns>The JSON text, or null when the output contains no valid object.</returns>
        public static string? ExtractObject(string? output)
        {
            if (String.IsNullOrWhiteSpace(output)) return null;

            string? best = null;
            foreach (string candidate in EmbeddedJsonExtractor.FindObjects(output))
            {
                if (best != null && candidate.Length <= best.Length) continue;
                try
                {
                    using (JsonDocument.Parse(candidate, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })) { }
                    best = candidate;
                }
                catch (JsonException)
                {
                }
            }

            return best;
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

        #endregion
    }
}
