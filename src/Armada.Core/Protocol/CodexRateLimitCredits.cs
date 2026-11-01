namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Credit state on a Codex app-server rate-limit snapshot.
    /// </summary>
    public class CodexRateLimitCredits
    {
        #region Public-Members

        /// <summary>
        /// Whether the account has credits, or null.
        /// </summary>
        [JsonPropertyName("hasCredits")]
        public bool? HasCredits { get; set; } = null;

        /// <summary>
        /// Whether credits are unlimited, or null.
        /// </summary>
        [JsonPropertyName("unlimited")]
        public bool? Unlimited { get; set; } = null;

        /// <summary>
        /// Credit balance as the server formats it, or null.
        /// </summary>
        [JsonPropertyName("balance")]
        public string? Balance { get; set; } = null;

        #endregion
    }
}
