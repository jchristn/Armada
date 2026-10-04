namespace Armada.Tui.Ask
{
    /// <summary>
    /// State of one tool call chip in an Ask transcript (the dashboard's <c>ToolEvent.status</c>).
    /// </summary>
    public enum AskToolChipStatusEnum
    {
        /// <summary>
        /// The tool is still running.
        /// </summary>
        Running = 0,

        /// <summary>
        /// The tool finished successfully.
        /// </summary>
        Success = 1,

        /// <summary>
        /// The tool failed.
        /// </summary>
        Failed = 2
    }
}
