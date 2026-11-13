namespace Armada.Core.Models
{
    /// <summary>
    /// Request payload for creating a voyage from a planning session.
    /// </summary>
    public class PlanningSessionDispatchRequest
    {
        /// <summary>
        /// Optional source message identifier.
        /// When omitted, Armada uses the latest non-empty assistant message.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Optional voyage title override.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional mission description override.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Optional missions for a tree dispatch: each with a key, an optional parentKey, a title, a description and
        /// owned paths. Empty means the draft is the single mission in Title and Description.
        /// </summary>
        [Armada.Core.Services.CollectionBound(Armada.Core.Services.CollectionLimits.MaxRequestItems)]
        public System.Collections.Generic.List<PlanningDraftMission> Missions { get; set; } = new System.Collections.Generic.List<PlanningDraftMission>();
    }
}
