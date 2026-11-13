namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Event-backed execution record for a runbook.
    /// </summary>
    public class RunbookExecution
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
        /// Runbook identifier.
        /// </summary>
        public string RunbookId { get; set; } = String.Empty;

        /// <summary>
        /// Backing playbook identifier.
        /// </summary>
        public string PlaybookId { get; set; } = String.Empty;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Human-facing execution title.
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
        /// Execution lifecycle state.
        /// </summary>
        public RunbookExecutionStatusEnum Status { get; set; } = RunbookExecutionStatusEnum.Running;

        /// <summary>
        /// Optional bound workflow profile.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Optional bound environment identifier.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Optional bound environment name.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// Optional suggested check type used from this execution.
        /// </summary>
        public CheckRunTypeEnum? CheckType { get; set; } = null;

        /// <summary>
        /// Optional related deployment.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Optional related incident.
        /// </summary>
        public string? IncidentId { get; set; } = null;

        /// <summary>
        /// Parameter values for the execution.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public Dictionary<string, string> ParameterValues { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Completed step identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> CompletedStepIds { get; set; } = new List<string>();

        /// <summary>
        /// Optional per-step notes.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public Dictionary<string, string> StepNotes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Optional operator notes for the execution.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Start timestamp.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Completion timestamp.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Last update timestamp.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.RunbookExecutionIdPrefix, 24);
        private string _Title = "Runbook Execution";
    }
}
