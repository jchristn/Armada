namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Result of the MCP get_mission_diff tool.
    /// </summary>
    public class McpMissionDiffResult
    {
        #region Public-Members

        /// <summary>
        /// Mission id, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Branch the diff was taken from, or null.
        /// </summary>
        public string? Branch { get; set; } = null;

        /// <summary>
        /// Unified diff text, or null.
        /// </summary>
        public string? Diff { get; set; } = null;

        #endregion
    }
}
