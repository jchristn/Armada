namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Supported agent runtime types.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AgentRuntimeEnum
    {
        /// <summary>
        /// Anthropic Claude Code CLI.
        /// </summary>
        [EnumMember(Value = "ClaudeCode")]
        ClaudeCode,

        /// <summary>
        /// OpenAI Codex CLI.
        /// </summary>
        [EnumMember(Value = "Codex")]
        Codex,

        /// <summary>
        /// Google Gemini CLI.
        /// </summary>
        [EnumMember(Value = "Gemini")]
        Gemini,

        /// <summary>
        /// Cursor agent CLI.
        /// </summary>
        [EnumMember(Value = "Cursor")]
        Cursor,

        /// <summary>
        /// Mux CLI.
        /// </summary>
        [EnumMember(Value = "Mux")]
        Mux,

        /// <summary>
        /// OpenCode CLI (OpenAI-compatible providers, JSONL event output).
        /// </summary>
        [EnumMember(Value = "OpenCode")]
        OpenCode,

        /// <summary>
        /// API-endpoint captain: an in-process tool-calling loop driven by a configured inference
        /// ModelEndpoint, using the Armada built-in coding tools instead of a CLI harness.
        /// </summary>
        [EnumMember(Value = "ApiEndpoint")]
        ApiEndpoint,

        /// <summary>
        /// Custom agent runtime.
        /// </summary>
        [EnumMember(Value = "Custom")]
        Custom
    }
}
