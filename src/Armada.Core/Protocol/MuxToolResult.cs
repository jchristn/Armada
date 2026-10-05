namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The result of a Mux <c>tool_call_completed</c> event.
    /// </summary>
    public class MuxToolResult
    {
        #region Public-Members

        /// <summary>
        /// Tool call identifier.
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string? ToolCallId { get; set; } = null;

        /// <summary>
        /// Whether the tool succeeded, or null when not reported.
        /// </summary>
        [JsonPropertyName("success")]
        public bool? Success { get; set; } = null;

        /// <summary>
        /// Result content as raw JSON (an object, or a string), or null.
        /// </summary>
        [JsonPropertyName("content")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Content { get; set; } = null;

        #endregion
    }
}
