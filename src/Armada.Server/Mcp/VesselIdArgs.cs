namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments containing a vessel identifier.
    /// </summary>
    public class VesselIdArgs
    {
        /// <summary>
        /// Vessel ID (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";
    }
}
