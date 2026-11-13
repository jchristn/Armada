namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// cli_permission_prompt arguments.
    /// </summary>
    public sealed class FakePermissionPromptArguments
    {
        /// <summary>
        /// tool_name.
        /// </summary>
        [JsonPropertyName("tool_name")]
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Tool input, including numeric options such as bash timeout.
        /// </summary>
        [JsonPropertyName("input")]
        public Dictionary<string, JsonElement>? Input { get; set; } = null;

        /// <summary>
        /// tool_use_id.
        /// </summary>
        [JsonPropertyName("tool_use_id")]
        public string? ToolUseId { get; set; } = null;
    }
}
