namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One Codex RateLimitWindow inside the sanitized account/rateLimits/read fixture.
    /// </summary>
    public class CodexRateLimitWindowFixture
    {
        /// <summary>
        /// Used percent; required by the installed schema, nullable here so a missing value is detectable.
        /// </summary>
        [JsonPropertyName("usedPercent")]
        public int? UsedPercent { get; set; } = null;

        /// <summary>
        /// Window duration in minutes, nullable.
        /// </summary>
        [JsonPropertyName("windowDurationMins")]
        public long? WindowDurationMins { get; set; } = null;

        /// <summary>
        /// Absolute reset time in Unix epoch seconds, nullable.
        /// </summary>
        [JsonPropertyName("resetsAt")]
        public long? ResetsAt { get; set; } = null;
    }
}
