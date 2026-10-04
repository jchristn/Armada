namespace Armada.Server.Ask
{
    using System;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A content block of a Claude Code stream-json message: text, tool_use (a call), or tool_result (its outcome).
    /// </summary>
    public class ClaudeStreamContentBlock
    {
        #region Public-Members

        /// <summary>
        /// Block type (text, tool_use, tool_result, thinking).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

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
        /// Tool input (tool_use).
        /// </summary>
        [JsonPropertyName("input")]
        public JsonNode? Input { get; set; } = null;

        /// <summary>
        /// Identifier of the call a tool_result answers.
        /// </summary>
        [JsonPropertyName("tool_use_id")]
        public string? ToolUseId { get; set; } = null;

        /// <summary>
        /// Result content (tool_result): a string or an array of text blocks.
        /// </summary>
        [JsonPropertyName("content")]
        public JsonNode? Content { get; set; } = null;

        /// <summary>
        /// Whether the tool_result is an error.
        /// </summary>
        [JsonPropertyName("is_error")]
        public bool? IsError { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Flatten the tool_result content to text.
        /// </summary>
        /// <returns>The result text, or null.</returns>
        public string? ContentText()
        {
            if (Content == null) return null;
            if (Content is JsonValue value) return value.ToString();
            if (Content is JsonArray array)
            {
                StringBuilder builder = new StringBuilder();
                foreach (JsonNode? item in array)
                {
                    if (item is JsonObject obj && obj["text"] is JsonValue text)
                    {
                        if (builder.Length > 0) builder.Append('\n');
                        builder.Append(text.ToString());
                    }
                }

                return builder.ToString();
            }

            return Content.ToJsonString();
        }

        #endregion
    }
}
