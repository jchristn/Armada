namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The item of a Codex <c>item.started</c> / <c>item.updated</c> / <c>item.completed</c> event.
    /// </summary>
    public class CodexStreamItem
    {
        #region Public-Members

        /// <summary>agent_message.</summary>
        public const string TypeAgentMessage = "agent_message";

        /// <summary>reasoning.</summary>
        public const string TypeReasoning = "reasoning";

        /// <summary>command_execution.</summary>
        public const string TypeCommandExecution = "command_execution";

        /// <summary>mcp_tool_call.</summary>
        public const string TypeMcpToolCall = "mcp_tool_call";

        /// <summary>file_change.</summary>
        public const string TypeFileChange = "file_change";

        /// <summary>
        /// Item identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Item type (agent_message, reasoning, command_execution, file_change, mcp_tool_call, web_search, todo_list,
        /// error).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text of an agent_message or reasoning item, or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Command of a command_execution item, or null.
        /// </summary>
        [JsonPropertyName("command")]
        public string? Command { get; set; } = null;

        /// <summary>
        /// Exit code of a command_execution item, or null.
        /// </summary>
        [JsonPropertyName("exit_code")]
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Item status (in_progress, completed, failed), or null.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; } = null;

        /// <summary>
        /// MCP server of an mcp_tool_call item, or null.
        /// </summary>
        [JsonPropertyName("server")]
        public string? Server { get; set; } = null;

        /// <summary>
        /// Tool of an mcp_tool_call item, or null.
        /// </summary>
        [JsonPropertyName("tool")]
        public string? Tool { get; set; } = null;

        #endregion
    }
}
