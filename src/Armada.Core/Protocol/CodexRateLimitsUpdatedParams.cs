namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Parameters of the passive Codex app-server <c>account/rateLimits/updated</c> notification.
    /// </summary>
    public class CodexRateLimitsUpdatedParams
    {
        #region Public-Members

        /// <summary>
        /// The pushed snapshot, or null.
        /// </summary>
        [JsonPropertyName("rateLimits")]
        public CodexRateLimitSnapshot? RateLimits { get; set; } = null;

        #endregion
    }
}
