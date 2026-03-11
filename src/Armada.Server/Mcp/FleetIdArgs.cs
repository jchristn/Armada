namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments containing a fleet identifier.
    /// </summary>
    public class FleetIdArgs
    {
        /// <summary>
        /// Fleet ID (flt_ prefix).
        /// </summary>
        public string FleetId { get; set; } = "";
    }
}
