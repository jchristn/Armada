namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Request payload for creating or updating a deployment.
    /// </summary>
    public class DeploymentUpsertRequest
    {
        /// <summary>
        /// Optional vessel identifier. May be inferred from linked records.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Optional workflow-profile override.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Optional environment identifier. Preferred over EnvironmentName when both are supplied.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Optional environment name when selecting the target by name.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// Optional linked release identifier.
        /// </summary>
        public string? ReleaseId { get; set; } = null;

        /// <summary>
        /// Optional linked mission identifier.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Optional linked voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Optional backlog/objective identifiers that should remain linked to this deployment.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds)]
        public List<string>? ObjectiveIds { get; set; } = null;

        /// <summary>
        /// Optional title override.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional freeform source ref such as a branch, tag, or commit.
        /// </summary>
        public string? SourceRef { get; set; } = null;

        /// <summary>
        /// Optional summary override.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Optional operator notes.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Whether the deployment should execute immediately when approval is not required.
        /// </summary>
        public bool? AutoExecute { get; set; } = null;
    }
}
