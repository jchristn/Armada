namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments for the list_vessel_dependencies MCP tool.
    /// </summary>
    public class VesselDependencyListArgs
    {
        /// <summary>
        /// Vessel identifier (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Page number (1-based). Null means 1; values below 1 are normalized to 1.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size, 1 to 1000. Null means 10; values outside the range are normalized to the nearest bound.
        /// </summary>
        public int? PageSize { get; set; } = null;
    }
}
