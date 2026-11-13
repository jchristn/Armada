namespace Armada.Core.Protocol
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Result of the Codex app-server <c>account/rateLimits/read</c> request. Unknown fields are ignored within this version.
    /// </summary>
    public class CodexRateLimitsReadResponse
    {
        #region Public-Members

        /// <summary>
        /// Provider account identifier, or null. Only ever HMAC-ed on the credential host; never persisted raw.
        /// </summary>
        [JsonPropertyName("accountId")]
        public string? AccountId { get; set; } = null;

        /// <summary>
        /// Whether ordinary usage is allowed, or null.
        /// </summary>
        [JsonPropertyName("ordinaryUsageAllowed")]
        public bool? OrdinaryUsageAllowed { get; set; } = null;

        /// <summary>
        /// Backward-compatible single snapshot, or null.
        /// </summary>
        [JsonPropertyName("rateLimits")]
        public CodexRateLimitSnapshot? RateLimits { get; set; } = null;

        /// <summary>
        /// Snapshots keyed by metered limit id, or null.
        /// </summary>
        [JsonPropertyName("rateLimitsByLimitId")]
        public Dictionary<string, CodexRateLimitSnapshot?>? RateLimitsByLimitId { get; set; } = null;

        #endregion
    }
}
