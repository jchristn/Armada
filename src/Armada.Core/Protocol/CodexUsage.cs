namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Token usage on a Codex <c>turn.completed</c> event.
    /// </summary>
    public class CodexUsage
    {
        #region Public-Members

        /// <summary>
        /// Input tokens, or null.
        /// </summary>
        [JsonPropertyName("input_tokens")]
        public long? InputTokens { get; set; } = null;

        /// <summary>
        /// Cached input tokens, or null.
        /// </summary>
        [JsonPropertyName("cached_input_tokens")]
        public long? CachedInputTokens { get; set; } = null;

        /// <summary>
        /// Output tokens, or null.
        /// </summary>
        [JsonPropertyName("output_tokens")]
        public long? OutputTokens { get; set; } = null;

        #endregion
    }
}
