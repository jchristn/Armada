namespace Armada.Helm.Commands
{
    /// <summary>
    /// MCP client whose configuration <c>armada mcp install</c> and <c>armada mcp remove</c> manage. Decisions use this
    /// kind, never the display name.
    /// </summary>
    internal enum McpClientKindEnum
    {
        /// <summary>
        /// Claude Code.
        /// </summary>
        ClaudeCode,

        /// <summary>
        /// Codex CLI.
        /// </summary>
        Codex,

        /// <summary>
        /// Gemini CLI.
        /// </summary>
        GeminiCli,

        /// <summary>
        /// Cursor.
        /// </summary>
        Cursor,

        /// <summary>
        /// Mux.
        /// </summary>
        Mux,

        /// <summary>
        /// OpenCode.
        /// </summary>
        OpenCode
    }
}
