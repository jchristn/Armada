namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Create or update request for a deployment environment.
    /// </summary>
    public class DeploymentEnvironmentUpsertRequest
    {
        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Environment name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Optional environment description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Optional environment kind override.
        /// </summary>
        public EnvironmentKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Optional configuration source description.
        /// </summary>
        public string? ConfigurationSource { get; set; } = null;

        /// <summary>
        /// Optional base URL for the environment.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Optional health endpoint URL or path.
        /// </summary>
        public string? HealthEndpoint { get; set; } = null;

        /// <summary>
        /// Optional operator notes.
        /// </summary>
        public string? AccessNotes { get; set; } = null;

        /// <summary>
        /// Optional deployment rules or constraints.
        /// </summary>
        public string? DeploymentRules { get; set; } = null;

        /// <summary>
        /// Optional reusable verification definitions.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<DeploymentVerificationDefinition>? VerificationDefinitions { get; set; } = null;

        /// <summary>
        /// Optional rollout monitoring window in minutes.
        /// </summary>
        public int? RolloutMonitoringWindowMinutes { get; set; } = null;

        /// <summary>
        /// Optional rollout monitoring interval in seconds.
        /// </summary>
        public int? RolloutMonitoringIntervalSeconds { get; set; } = null;

        /// <summary>
        /// Optional alert-on-regression setting.
        /// </summary>
        public bool? AlertOnRegression { get; set; } = null;

        /// <summary>
        /// Optional approval requirement.
        /// </summary>
        public bool? RequiresApproval { get; set; } = null;

        /// <summary>
        /// Optional default-environment flag.
        /// </summary>
        public bool? IsDefault { get; set; } = null;

        /// <summary>
        /// Optional active flag.
        /// </summary>
        public bool? Active { get; set; } = null;
    }
}
