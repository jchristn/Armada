namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a CLI captain's own tools (shell, web, file tools outside the accepted edits) are permitted. Armada MCP tools are governed separately (Ask proposals and per-tool authorization).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionPolicyEnum
    {
        /// <summary>
        /// Tools that need approval are refused (Claude Code: --permission-mode acceptEdits in print mode; other runtimes: their non-bypass flags). The turn or mission continues without them.
        /// </summary>
        [EnumMember(Value = "Refuse")]
        Refuse,

        /// <summary>
        /// The CLI's permission prompts are routed to Armada as pending CLI permission requests that an approver allows or denies (Claude Code --permission-prompt-tool). Runtimes without an equivalent hook fall back to Refuse.
        /// </summary>
        [EnumMember(Value = "ApproveInArmada")]
        ApproveInArmada,

        /// <summary>
        /// The runtime's own permission-bypass flag is used (Claude Code --dangerously-skip-permissions, Codex full-auto or bypass, Gemini yolo, Cursor --force, OpenCode --auto, Mux --yolo). Only admins can choose it.
        /// </summary>
        [EnumMember(Value = "Bypass")]
        Bypass
    }
}
