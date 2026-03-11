namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for restarting a failed or cancelled mission.
    /// </summary>
    public class MissionRestartArgs
    {
        /// <summary>
        /// Mission ID (msn_ prefix).
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Optional new title. If null or empty, the original title is preserved.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Optional new description/instructions. If null or empty, the original description is preserved.
        /// </summary>
        public string? Description { get; set; }
    }
}
