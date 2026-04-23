namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using Armada.Core.Models;

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

        /// <summary>
        /// Ordered playbooks to apply during mission dispatch.
        /// </summary>
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();
    }
}
