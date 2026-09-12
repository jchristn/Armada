namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Incident create or update payload.
    /// </summary>
    public class IncidentUpsertRequest
    {
        /// <summary>
        /// Optional title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Optional lifecycle status.
        /// </summary>
        public IncidentStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Optional severity.
        /// </summary>
        public IncidentSeverityEnum? Severity { get; set; } = null;

        /// <summary>
        /// Optional environment identifier.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Optional environment name.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// Optional deployment identifier.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Optional release identifier.
        /// </summary>
        public string? ReleaseId { get; set; } = null;

        /// <summary>
        /// Optional vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Optional mission identifier.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Optional voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Optional rollback deployment identifier.
        /// </summary>
        public string? RollbackDeploymentId { get; set; } = null;

        /// <summary>
        /// Optional backlog/objective identifiers that should remain linked to this incident.
        /// </summary>
        public List<string>? ObjectiveIds { get; set; } = null;

        /// <summary>
        /// Optional impact summary.
        /// </summary>
        public string? Impact { get; set; } = null;

        /// <summary>
        /// Optional root-cause notes.
        /// </summary>
        public string? RootCause { get; set; } = null;

        /// <summary>
        /// Optional recovery notes.
        /// </summary>
        public string? RecoveryNotes { get; set; } = null;

        /// <summary>
        /// Optional postmortem notes.
        /// </summary>
        public string? Postmortem { get; set; } = null;

        /// <summary>
        /// Optional detection timestamp override.
        /// </summary>
        public DateTime? DetectedUtc { get; set; } = null;

        /// <summary>
        /// Optional mitigation timestamp override.
        /// </summary>
        public DateTime? MitigatedUtc { get; set; } = null;

        /// <summary>
        /// Optional closure timestamp override.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

        /// <summary>
        /// Optional classified failure kind (for autonomous-recovery incidents).
        /// </summary>
        public string? FailureKind { get; set; } = null;

        /// <summary>
        /// Optional count of rescue missions dispatched for this incident.
        /// </summary>
        public int? RecoveryAttempts { get; set; } = null;

        /// <summary>
        /// Optional set of rescue-mission identifiers linked to this incident.
        /// </summary>
        public List<string>? RescueMissionIds { get; set; } = null;
    }
}
