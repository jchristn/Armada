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
    }
}
