namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Request payload for creating or updating a release.
    /// </summary>
    public class ReleaseUpsertRequest
    {
        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Optional workflow-profile override.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Optional title override.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional version override.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Optional tag-name override.
        /// </summary>
        public string? TagName { get; set; } = null;

        /// <summary>
        /// Optional summary override.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Optional notes override.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Optional status override.
        /// </summary>
        public ReleaseStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Linked voyage identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string> VoyageIds { get; set; } = new List<string>();

        /// <summary>
        /// Linked mission identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string> MissionIds { get; set; } = new List<string>();

        /// <summary>
        /// Linked check-run identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string> CheckRunIds { get; set; } = new List<string>();

        /// <summary>
        /// Optional linked objective identifiers to associate with the release.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string> ObjectiveIds { get; set; } = new List<string>();
    }
}
