namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The sanitized Custom usage contract fixture defined by CAPTAIN_USAGE_PLAN.md.
    /// </summary>
    public class CustomUsageContractFixture
    {
        /// <summary>
        /// Contract schema version.
        /// </summary>
        [JsonPropertyName("schemaVersion")]
        public int? SchemaVersion { get; set; } = null;

        /// <summary>
        /// Reported status.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; } = null;

        /// <summary>
        /// Account key placeholder; the host owns the final HMAC.
        /// </summary>
        [JsonPropertyName("accountKey")]
        public string? AccountKey { get; set; } = null;

        /// <summary>
        /// Provider name.
        /// </summary>
        [JsonPropertyName("provider")]
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Observation time.
        /// </summary>
        [JsonPropertyName("capturedUtc")]
        public DateTimeOffset? CapturedUtc { get; set; } = null;

        /// <summary>
        /// Reported source.
        /// </summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; } = null;

        /// <summary>
        /// Bounded usage windows.
        /// </summary>
        [JsonPropertyName("windows")]
        public List<CustomUsageWindowFixture>? Windows { get; set; } = null;
    }
}
