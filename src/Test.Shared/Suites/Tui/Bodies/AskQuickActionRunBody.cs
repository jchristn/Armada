namespace Test.Shared.Suites.Tui.Bodies
{
    using Armada.Client.Models;

    /// <summary>
    /// Body the TUI posts to POST /api/v1/ask/threads/{id}/actions.
    /// </summary>
    public class AskQuickActionRunBody
    {
        #region Public-Members

        /// <summary>
        /// Quick action tool name.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Tool arguments as raw JSON.
        /// </summary>
        public ArmadaRawJson? Arguments { get; set; } = null;

        #endregion
    }
}
