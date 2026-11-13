namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One Codex app-server rate-limit snapshot (<c>RateLimitSnapshot</c> in the generated schema).
    /// </summary>
    public class CodexRateLimitSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Metered limit identifier (for example codex), or null.
        /// </summary>
        [JsonPropertyName("limitId")]
        public string? LimitId { get; set; } = null;

        /// <summary>
        /// Display name of the limit, or null.
        /// </summary>
        [JsonPropertyName("limitName")]
        public string? LimitName { get; set; } = null;

        /// <summary>
        /// Plan type reported by the server, or null.
        /// </summary>
        [JsonPropertyName("planType")]
        public string? PlanType { get; set; } = null;

        /// <summary>
        /// Which limit was reached, or null.
        /// </summary>
        [JsonPropertyName("rateLimitReachedType")]
        public string? RateLimitReachedType { get; set; } = null;

        /// <summary>
        /// Primary window, or null.
        /// </summary>
        [JsonPropertyName("primary")]
        public CodexRateLimitWindow? Primary { get; set; } = null;

        /// <summary>
        /// Secondary window, or null.
        /// </summary>
        [JsonPropertyName("secondary")]
        public CodexRateLimitWindow? Secondary { get; set; } = null;

        /// <summary>
        /// Credit state, or null.
        /// </summary>
        [JsonPropertyName("credits")]
        public CodexRateLimitCredits? Credits { get; set; } = null;

        #endregion
    }
}
