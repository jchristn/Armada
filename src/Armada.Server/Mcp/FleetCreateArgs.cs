namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for creating a fleet.
    /// </summary>
    public class FleetCreateArgs
    {
        /// <summary>
        /// Fleet name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Fleet description.
        /// </summary>
        public string? Description { get; set; }
    }
}
