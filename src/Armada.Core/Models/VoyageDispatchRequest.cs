namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request payload for dispatching a voyage with mission descriptions and optional playbooks.
    /// </summary>
    public class VoyageDispatchRequest
    {
        /// <summary>
        /// Voyage title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Optional voyage description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Target vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Optional pipeline identifier.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Optional pipeline name.
        /// </summary>
        public string? Pipeline { get; set; } = null;

        /// <summary>
        /// Missions to dispatch.
        /// </summary>
        public List<MissionDescription> Missions { get; set; } = new List<MissionDescription>();

        /// <summary>
        /// Ordered playbooks to apply during dispatch.
        /// </summary>
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();
    }
}
