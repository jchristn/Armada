namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Token usage reported on a Claude Code stream-json <c>result</c> event.
    /// </summary>
    public class ClaudeStreamUsage
    {
        #region Public-Members

        /// <summary>
        /// Input tokens, or null.
        /// </summary>
        [JsonPropertyName("input_tokens")]
        public long? InputTokens { get; set; } = null;

        /// <summary>
        /// Output tokens, or null.
        /// </summary>
        [JsonPropertyName("output_tokens")]
        public long? OutputTokens { get; set; } = null;

        /// <summary>
        /// Input tokens read from the prompt cache, or null.
        /// </summary>
        [JsonPropertyName("cache_read_input_tokens")]
        public long? CacheReadInputTokens { get; set; } = null;

        /// <summary>
        /// Input tokens written to the prompt cache, or null.
        /// </summary>
        [JsonPropertyName("cache_creation_input_tokens")]
        public long? CacheCreationInputTokens { get; set; } = null;

        #endregion
    }
}
