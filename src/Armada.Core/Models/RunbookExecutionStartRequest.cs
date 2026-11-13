namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Start a runbook execution.
    /// </summary>
    public class RunbookExecutionStartRequest
    {
        /// <summary>
        /// Optional override title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional override workflow profile.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Optional override environment identifier.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Optional override environment name.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// Optional override check type.
        /// </summary>
        public CheckRunTypeEnum? CheckType { get; set; } = null;

        /// <summary>
        /// Parameter values for substitution and execution context.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public Dictionary<string, string>? ParameterValues { get; set; } = null;

        /// <summary>
        /// Optional related deployment.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Optional related incident.
        /// </summary>
        public string? IncidentId { get; set; } = null;

        /// <summary>
        /// Optional execution notes.
        /// </summary>
        public string? Notes { get; set; } = null;
    }
}
