namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for updating a fleet.
    /// </summary>
    public class FleetUpdateArgs
    {
        /// <summary>
        /// Fleet ID (flt_ prefix).
        /// </summary>
        public string FleetId { get; set; } = "";

        /// <summary>
        /// New fleet name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// New fleet description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Default pipeline ID for dispatches to vessels in this fleet (ppl_ prefix).
        /// </summary>
        public string? DefaultPipelineId { get; set; }
    }
}
