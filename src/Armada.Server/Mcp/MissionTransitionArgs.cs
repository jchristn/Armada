namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for transitioning a mission to a new status.
    /// </summary>
    public class MissionTransitionArgs
    {
        /// <summary>
        /// Mission ID (msn_ prefix).
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Target status: Pending, Assigned, InProgress, Testing, Review, Complete, Failed, Cancelled.
        /// </summary>
        public string Status { get; set; } = "";
    }
}
