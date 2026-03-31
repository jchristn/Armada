namespace Armada.Server.WebSocket
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Data payload for the create_voyage WebSocket command.
    /// </summary>
    public class WebSocketVoyageData
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
        /// Target vessel identifier.
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// List of missions to create with the voyage.
        /// </summary>
        public List<MissionDescription>? Missions { get; set; }
    }
}
