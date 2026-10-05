namespace Armada.Server.Mcp
{
    using System.Text.Json.Serialization;
    using Armada.Core.Protocol;

    /// <summary>
    /// Arguments Claude Code sends to the permission prompt tool (--permission-prompt-tool): the tool that needs permission and its input.
    /// </summary>
    public class CliPermissionPromptArgs
    {
        /// <summary>
        /// Tool that needs permission (for example Bash).
        /// </summary>
        [JsonPropertyName("tool_name")]
        public string ToolName { get; set; } = "";

        /// <summary>
        /// Tool input as raw JSON object text.
        /// </summary>
        [JsonPropertyName("input")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? InputJson { get; set; } = null;

        /// <summary>
        /// Tool use identifier, when the CLI sends one.
        /// </summary>
        [JsonPropertyName("tool_use_id")]
        public string? ToolUseId { get; set; } = null;
    }
}
