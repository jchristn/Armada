namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One Codex RateLimitSnapshot inside the sanitized account/rateLimits/read fixture.
    /// </summary>
    public class CodexRateLimitSnapshotFixture
    {
        /// <summary>
        /// Metered limit id, nullable.
        /// </summary>
        [JsonPropertyName("limitId")]
        public string? LimitId { get; set; } = null;

        /// <summary>
        /// Plan type, nullable.
        /// </summary>
        [JsonPropertyName("planType")]
        public string? PlanType { get; set; } = null;

        /// <summary>
        /// Primary window, nullable.
        /// </summary>
        [JsonPropertyName("primary")]
        public CodexRateLimitWindowFixture? Primary { get; set; } = null;

        /// <summary>
        /// Secondary window, nullable.
        /// </summary>
        [JsonPropertyName("secondary")]
        public CodexRateLimitWindowFixture? Secondary { get; set; } = null;
    }
}
