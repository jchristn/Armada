namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// First-class deployment target metadata for a vessel.
    /// </summary>
    public class DeploymentEnvironment
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
                _Id = value;
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
        /// Vessel this environment belongs to.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Human-facing environment name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value.Trim();
            }
        }

        /// <summary>
        /// Optional environment description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Environment classification.
        /// </summary>
        public EnvironmentKindEnum Kind { get; set; } = EnvironmentKindEnum.Development;

        /// <summary>
        /// Optional configuration source description.
        /// </summary>
        public string? ConfigurationSource { get; set; } = null;

        /// <summary>
        /// Optional base URL for the deployed system.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Optional health endpoint URL or path.
        /// </summary>
        public string? HealthEndpoint { get; set; } = null;

        /// <summary>
        /// Optional access or operator notes.
        /// </summary>
        public string? AccessNotes { get; set; } = null;

        /// <summary>
        /// Optional deployment rules or constraints.
        /// </summary>
        public string? DeploymentRules { get; set; } = null;

        /// <summary>
        /// Reusable HTTP verification definitions for deploy verification and rollout monitoring.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<DeploymentVerificationDefinition> VerificationDefinitions { get; set; } = new List<DeploymentVerificationDefinition>();

        /// <summary>
        /// Rollout monitoring window in minutes after a deployment succeeds.
        /// </summary>
        public int RolloutMonitoringWindowMinutes { get; set; } = 0;

        /// <summary>
        /// Rollout monitoring interval in seconds.
        /// </summary>
        public int RolloutMonitoringIntervalSeconds { get; set; } = 300;

        /// <summary>
        /// Whether regression alerts should be raised during rollout monitoring.
        /// </summary>
        public bool AlertOnRegression { get; set; } = true;

        /// <summary>
        /// Indicates whether this environment requires approval before deployment.
        /// </summary>
        public bool RequiresApproval { get; set; } = false;

        /// <summary>
        /// Indicates whether this is the default environment for its vessel.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Indicates whether the environment is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.EnvironmentIdPrefix, 24);
        private string _Name = "Environment";
    }
}
