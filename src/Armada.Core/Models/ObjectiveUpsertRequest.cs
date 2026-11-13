namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Request payload for creating or updating an objective.
    /// </summary>
    public class ObjectiveUpsertRequest
    {
        /// <summary>
        /// Title override.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description override.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Status override.
        /// </summary>
        public ObjectiveStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Kind override.
        /// </summary>
        public ObjectiveKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Optional category override.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// Priority override.
        /// </summary>
        public ObjectivePriorityEnum? Priority { get; set; } = null;

        /// <summary>
        /// Rank override.
        /// </summary>
        public int? Rank { get; set; } = null;

        /// <summary>
        /// Backlog maturity override.
        /// </summary>
        public ObjectiveBacklogStateEnum? BacklogState { get; set; } = null;

        /// <summary>
        /// Effort override.
        /// </summary>
        public ObjectiveEffortEnum? Effort { get; set; } = null;

        /// <summary>
        /// Owner override.
        /// </summary>
        public string? Owner { get; set; } = null;

        /// <summary>
        /// Target version override.
        /// </summary>
        public string? TargetVersion { get; set; } = null;

        /// <summary>
        /// Due date override.
        /// </summary>
        public DateTime? DueUtc { get; set; } = null;

        /// <summary>
        /// Parent objective override.
        /// </summary>
        public string? ParentObjectiveId { get; set; } = null;

        /// <summary>
        /// Blocking objective identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? BlockedByObjectiveIds { get; set; } = null;

        /// <summary>
        /// Refinement summary override.
        /// </summary>
        public string? RefinementSummary { get; set; } = null;

        /// <summary>
        /// Suggested pipeline override.
        /// </summary>
        public string? SuggestedPipelineId { get; set; } = null;

        /// <summary>
        /// Suggested playbook selections.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<SelectedPlaybook>? SuggestedPlaybooks { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? Tags { get; set; } = null;

        /// <summary>
        /// Acceptance criteria.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? AcceptanceCriteria { get; set; } = null;

        /// <summary>
        /// Non-goals.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? NonGoals { get; set; } = null;

        /// <summary>
        /// Rollout constraints.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? RolloutConstraints { get; set; } = null;

        /// <summary>
        /// Evidence links.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? EvidenceLinks { get; set; } = null;

        /// <summary>
        /// Linked fleet identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? FleetIds { get; set; } = null;

        /// <summary>
        /// Linked vessel identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? VesselIds { get; set; } = null;

        /// <summary>
        /// Linked planning-session identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? PlanningSessionIds { get; set; } = null;

        /// <summary>
        /// Linked refinement-session identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? RefinementSessionIds { get; set; } = null;

        /// <summary>
        /// Linked voyage identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? VoyageIds { get; set; } = null;

        /// <summary>
        /// Linked mission identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? MissionIds { get; set; } = null;

        /// <summary>
        /// Linked check-run identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? CheckRunIds { get; set; } = null;

        /// <summary>
        /// Linked release identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? ReleaseIds { get; set; } = null;

        /// <summary>
        /// Linked deployment identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? DeploymentIds { get; set; } = null;

        /// <summary>
        /// Linked incident identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? IncidentIds { get; set; } = null;
    }
}
