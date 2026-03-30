namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for creating a standalone mission.
    /// </summary>
    public class MissionCreateArgs
    {
        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Mission description/instructions.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Target vessel ID (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Optional voyage ID to associate with (vyg_ prefix).
        /// </summary>
        public string? VoyageId { get; set; }

        /// <summary>
        /// Persona for this mission (e.g. Worker, Architect, Judge, TestEngineer).
        /// </summary>
        public string? Persona { get; set; }
    }
}
