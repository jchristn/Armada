namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The raw Claude API streaming event carried by a Claude Code <c>stream_event</c> line
    /// (with <c>--include-partial-messages</c>).
    /// </summary>
    public class ClaudeStreamApiEvent
    {
        #region Public-Members

        /// <summary>
        /// API event type (message_start, content_block_start, content_block_delta, content_block_stop, message_delta,
        /// message_stop).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Delta of a content_block_delta event, or null.
        /// </summary>
        [JsonPropertyName("delta")]
        public ClaudeStreamDelta? Delta { get; set; } = null;

        #endregion
    }
}
