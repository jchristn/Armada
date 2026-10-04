namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments containing a fleet action run identifier.
    /// </summary>
    public class FleetActionRunIdArgs
    {
        /// <summary>
        /// Run ID (far_ prefix).
        /// </summary>
        public string RunId { get; set; } = "";
    }
}
