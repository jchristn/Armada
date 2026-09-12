namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Event-backed operational incident tied to deployments, releases, environments, and missions.
    /// </summary>
    public class Incident
    {
        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value.Trim();
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Human-facing incident title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Title));
                _Title = value.Trim();
            }
        }

        /// <summary>
        /// Optional short summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Incident lifecycle state.
        /// </summary>
        public IncidentStatusEnum Status { get; set; } = IncidentStatusEnum.Open;

        /// <summary>
        /// Incident severity.
        /// </summary>
        public IncidentSeverityEnum Severity { get; set; } = IncidentSeverityEnum.High;

        /// <summary>
        /// Related environment identifier.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Related environment name.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// Related deployment identifier.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Related release identifier.
        /// </summary>
        public string? ReleaseId { get; set; } = null;

        /// <summary>
        /// Related vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Related mission identifier.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Related voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Rollback deployment identifier when applicable.
        /// </summary>
        public string? RollbackDeploymentId { get; set; } = null;

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
        /// When the incident was first detected.
        /// </summary>
        public DateTime DetectedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the incident impact was mitigated.
        /// </summary>
        public DateTime? MitigatedUtc { get; set; } = null;

        /// <summary>
        /// When the incident was closed.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

        /// <summary>
        /// Classified failure kind that opened this incident (for autonomous-recovery incidents). Null for
        /// incidents opened through the deployment/release lifecycle.
        /// </summary>
        public string? FailureKind { get; set; } = null;

        /// <summary>
        /// Number of bounded rescue missions the autonomous-recovery coordinator has dispatched for this
        /// incident. Bounds the rescue loop before the incident is left open for a human.
        /// </summary>
        public int RecoveryAttempts { get; set; } = 0;

        /// <summary>
        /// Identifiers of the rescue missions dispatched for this incident, in dispatch order.
        /// </summary>
        public List<string> RescueMissionIds { get; set; } = new List<string>();

        /// <summary>
        /// Last update timestamp.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.IncidentIdPrefix, 24);
        private string _Title = "Incident";
    }
}
