namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments containing a fleet action identifier.
    /// </summary>
    public class FleetActionIdArgs
    {
        /// <summary>
        /// Fleet action ID (fac_ prefix).
        /// </summary>
        public string ActionId { get; set; } = "";
    }
}
