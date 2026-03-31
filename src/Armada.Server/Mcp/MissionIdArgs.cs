namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments containing a mission identifier.
    /// </summary>
    public class MissionIdArgs
    {
        /// <summary>
        /// Mission ID (msn_ prefix).
        /// </summary>
        public string MissionId { get; set; } = "";
    }
}
