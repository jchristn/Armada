namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments for the vessel_health MCP tool.
    /// </summary>
    public class VesselHealthArgs
    {
        /// <summary>
        /// Vessel identifier (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Whether to include the dependency rows (default false; a dependencyCount hint is always returned).
        /// </summary>
        public bool IncludeDependencies { get; set; } = false;
    }
}
