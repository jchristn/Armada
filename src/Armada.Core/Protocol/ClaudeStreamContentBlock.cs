namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A content block of a Claude Code stream-json message: text, thinking, tool_use (a call), or tool_result (its
    /// outcome).
    /// </summary>
    public class ClaudeStreamContentBlock
    {
        #region Public-Members

        /// <summary>
        /// Block type for text.
        /// </summary>
        public const string TypeText = "text";

        /// <summary>
        /// Block type for a tool call.
        /// </summary>
        public const string TypeToolUse = "tool_use";

        /// <summary>
        /// Block type for a tool result.
        /// </summary>
        public const string TypeToolResult = "tool_result";

        /// <summary>
        /// Block type (text, thinking, tool_use, tool_result).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text of a text block, or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Tool call identifier (tool_use).
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Tool name (tool_use), for example mcp__armada__dispatch.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Tool input (tool_use) as raw JSON, or null.
        /// </summary>
        [JsonPropertyName("input")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Input { get; set; } = null;

        /// <summary>
        /// Identifier of the call a tool_result answers.
        /// </summary>
        [JsonPropertyName("tool_use_id")]
        public string? ToolUseId { get; set; } = null;

        /// <summary>
        /// Result content (tool_result): a string or an array of text blocks, read into text.
        /// </summary>
        [JsonPropertyName("content")]
        public ClaudeToolResultContent? Content { get; set; } = null;

        /// <summary>
        /// Whether the tool_result is an error.
        /// </summary>
        [JsonPropertyName("is_error")]
        public bool? IsError { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The tool_result content as text.
        /// </summary>
        /// <returns>The result text, or null when the block has no content.</returns>
        public string? ContentText()
        {
            return Content?.Text;
        }

        #endregion
    }
}
