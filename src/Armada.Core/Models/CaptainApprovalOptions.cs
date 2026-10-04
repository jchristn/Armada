namespace Armada.Core.Models
{
    /// <summary>
    /// Runtime-independent captain options read from <see cref="Captain.RuntimeOptionsJson"/> (camelCase JSON, for
    /// example <c>{"autoApprove": false}</c>). They share the JSON object with runtime-specific options such as
    /// <see cref="MuxCaptainOptions"/>.
    /// </summary>
    public class CaptainApprovalOptions
    {
        #region Public-Members

        /// <summary>
        /// Whether the CLI captain runs with its auto-approve or permission-bypass flag. Null (the default) and true
        /// keep the current behavior (Claude Code --dangerously-skip-permissions, Codex --full-auto or the bypass flag
        /// on Windows, Gemini --approval-mode yolo, Cursor --force, OpenCode --auto, Mux --yolo). False runs the
        /// captain without them where the runtime supports it: Claude Code --permission-mode acceptEdits, Codex
        /// --sandbox workspace-write, Gemini --approval-mode auto_edit, Cursor without --force, OpenCode without
        /// --auto, Mux --approval-policy deny (unless the captain sets an explicit Mux approval policy). Shell commands
        /// the agent wants to run are then refused unless the runtime's own configuration allows them.
        /// </summary>
        public bool? AutoApprove { get; set; } = null;

        #endregion
    }
}
