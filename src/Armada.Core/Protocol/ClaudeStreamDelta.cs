namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>delta</c> of a Claude API <c>content_block_delta</c> stream event.
    /// </summary>
    public class ClaudeStreamDelta
    {
        #region Public-Members

        /// <summary>
        /// Delta type (text_delta, thinking_delta, input_json_delta, signature_delta).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text of a text_delta, or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Reasoning text of a thinking_delta, or null.
        /// </summary>
        [JsonPropertyName("thinking")]
        public string? Thinking { get; set; } = null;

        #endregion
    }
}
