namespace Armada.Core.Models
{
    using Armada.Core.Services;

    /// <summary>
    /// Request payload for creating a planning session.
    /// </summary>
    public class PlanningSessionCreateRequest
    {
        /// <summary>
        /// Optional session title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Captain identifier to reserve.
        /// </summary>
        public string CaptainId { get; set; } = String.Empty;

        /// <summary>
        /// Vessel identifier to plan against.
        /// </summary>
        public string VesselId { get; set; } = String.Empty;

        /// <summary>
        /// Optional fleet identifier used for UX filtering.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Optional pipeline identifier inherited by dispatch.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Optional ordered playbook selections.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();

        /// <summary>
        /// Optional objective identifier to link when the session is created from scoped intake work.
        /// </summary>
        public string? ObjectiveId { get; set; } = null;

        /// <summary>
        /// The Harbor (hbr_...) the session runs on: its dock is created there and every turn runs there. When that
        /// Harbor is not connected or cannot serve the vessel, the session is not created. Null (the default) lets
        /// routing choose as for a mission: the vessel's preferred Harbor, then any connected Harbor that can serve the
        /// vessel, then the Admiral host.
        /// </summary>
        public string? HarborId { get; set; } = null;
    }
}
