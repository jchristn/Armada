namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The sanitized Codex app-server account/rateLimits/read response fixture, typed for Usage.InterfaceEvidence.
    /// </summary>
    public class CodexRateLimitsReadFixture
    {
        /// <summary>
        /// Account identity; the sanitized fixture must keep it null because raw account ids are never stored.
        /// </summary>
        [JsonPropertyName("accountId")]
        public string? AccountId { get; set; } = null;

        /// <summary>
        /// Backend permission for ordinary included usage, nullable.
        /// </summary>
        [JsonPropertyName("ordinaryUsageAllowed")]
        public bool? OrdinaryUsageAllowed { get; set; } = null;

        /// <summary>
        /// Backward-compatible single snapshot (required by the schema).
        /// </summary>
        [JsonPropertyName("rateLimits")]
        public CodexRateLimitSnapshotFixture? RateLimits { get; set; } = null;

        /// <summary>
        /// Snapshots keyed by metered limit id.
        /// </summary>
        [JsonPropertyName("rateLimitsByLimitId")]
        public Dictionary<string, CodexRateLimitSnapshotFixture>? RateLimitsByLimitId { get; set; } = null;
    }
}
