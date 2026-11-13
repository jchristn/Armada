namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A pi <c>tool_call</c> event handed to Armada's pi permission gate extension by <see cref="PiPermissionGateHarness"/>.
    /// </summary>
    public sealed class PiGateEvent
    {
        /// <summary>
        /// pi tool name (bash, powershell, read, mcp__armada__status, ...).
        /// </summary>
        [JsonPropertyName("toolName")]
        public string ToolName { get; set; } = "bash";

        /// <summary>
        /// pi tool call id.
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string ToolCallId { get; set; } = "call_1";

        /// <summary>
        /// Tool input, preserving JSON value types.
        /// </summary>
        [JsonPropertyName("input")]
        public Dictionary<string, object> Input { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// A bash call running <paramref name="command"/>.
        /// </summary>
        /// <param name="command">Command line.</param>
        /// <param name="toolCallId">Tool call id.</param>
        /// <returns>The event.</returns>
        public static PiGateEvent Bash(string command, string toolCallId = "call_1")
        {
            return new PiGateEvent { ToolName = "bash", ToolCallId = toolCallId, Input = new Dictionary<string, object> { ["command"] = command } };
        }
    }
}
