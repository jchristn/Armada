namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for updating a vessel's project context and style guide.
    /// </summary>
    public class VesselContextArgs
    {
        /// <summary>
        /// Vessel ID (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Project context describing architecture, key files, and dependencies.
        /// </summary>
        public string? ProjectContext { get; set; }

        /// <summary>
        /// Style guide describing naming conventions, patterns, and library preferences.
        /// </summary>
        public string? StyleGuide { get; set; }

        /// <summary>
        /// Agent-accumulated context about this repository.
        /// </summary>
        public string? ModelContext { get; set; }
    }
}
