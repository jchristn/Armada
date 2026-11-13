namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Request payload for dispatching a voyage with mission descriptions and optional playbooks.
    /// </summary>
    public class VoyageDispatchRequest
    {
        /// <summary>Per-run tool permission and mission review approval choices.</summary>
        public Armada.Core.Models.RunApprovalOptions? ApprovalOptions { get; set; } = null;

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
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<MissionDescription> Missions { get; set; } = new List<MissionDescription>();

        /// <summary>
        /// Ordered playbooks to apply during dispatch.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();

        /// <summary>
        /// True for evaluation work: the voyage and its missions are flagged IsEvaluation. Default false.
        /// </summary>
        public bool IsEvaluation { get; set; } = false;
    }
}
