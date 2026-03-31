namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for retrieving a mission's session log.
    /// </summary>
    public class MissionLogArgs
    {
        /// <summary>
        /// Mission ID (msn_ prefix).
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Number of lines to return (default 100).
        /// </summary>
        public int? Lines { get; set; }

        /// <summary>
        /// Line offset to start from (default 0).
        /// </summary>
        public int? Offset { get; set; }
    }
}
