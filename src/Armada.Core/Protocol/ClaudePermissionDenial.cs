namespace Armada.Core.Protocol
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One entry of the <c>permission_denials</c> list on Claude Code's terminal stream-json "result" event: a tool call
    /// the CLI refused because it needed permission that was not granted.
    /// </summary>
    public class ClaudePermissionDenial
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        [JsonPropertyName("tool_name")]
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Tool use identifier (matches the tool_use block id).
        /// </summary>
        [JsonPropertyName("tool_use_id")]
        public string? ToolUseId { get; set; } = null;

        /// <summary>
        /// Tool input as raw JSON text, or null.
        /// </summary>
        [JsonPropertyName("tool_input")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? ToolInput { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ClaudePermissionDenial()
        {
        }

        #endregion
    }
}
