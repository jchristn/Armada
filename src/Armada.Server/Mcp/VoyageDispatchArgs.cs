namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// MCP tool arguments for dispatching a voyage with missions.
    /// </summary>
    public class VoyageDispatchArgs
    {
        /// <summary>
        /// Voyage title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Voyage description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Target vessel ID.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// List of missions to create.
        /// </summary>
        public List<MissionDescription> Missions { get; set; } = new List<MissionDescription>();

        /// <summary>
        /// Pipeline ID to use for this dispatch (overrides vessel/fleet default).
        /// </summary>
        public string? PipelineId { get; set; }

        /// <summary>
        /// Pipeline name to use (convenience alias for pipelineId -- resolves by name).
        /// </summary>
        public string? Pipeline { get; set; }
    }
}
