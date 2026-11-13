namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Request to import an externally-executed check run into Armada.
    /// </summary>
    public class CheckRunImportRequest
    {
        /// <summary>
        /// Vessel against which the external run applies.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Optional workflow profile association.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Optional mission association.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Optional voyage association.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Optional deployment association.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        /// <summary>
        /// Imported check type.
        /// </summary>
        public CheckRunTypeEnum Type { get; set; } = CheckRunTypeEnum.Build;

        /// <summary>
        /// Imported run status.
        /// </summary>
        public CheckRunStatusEnum Status { get; set; } = CheckRunStatusEnum.Passed;

        /// <summary>
        /// Optional source provider name such as GitHubActions or Jenkins.
        /// </summary>
        public string? ProviderName { get; set; } = null;

        /// <summary>
        /// Optional provider-specific run identifier.
        /// </summary>
        public string? ExternalId { get; set; } = null;

        /// <summary>
        /// Optional deep link to the provider run.
        /// </summary>
        public string? ExternalUrl { get; set; } = null;

        /// <summary>
        /// Optional environment target.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// Optional label override.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Optional associated branch name.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Optional associated commit hash.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// Optional imported command string or job name.
        /// </summary>
        public string? Command { get; set; } = null;

        /// <summary>
        /// Optional imported summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Optional imported output.
        /// </summary>
        public string? Output { get; set; } = null;

        /// <summary>
        /// Optional exit code.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Optional structured test summary supplied by the caller.
        /// </summary>
        public CheckRunTestSummary? TestSummary { get; set; } = null;

        /// <summary>
        /// Optional structured coverage summary supplied by the caller.
        /// </summary>
        public CheckRunCoverageSummary? CoverageSummary { get; set; } = null;

        /// <summary>
        /// Optional imported artifact metadata.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<CheckRunArtifact> Artifacts { get; set; } = new List<CheckRunArtifact>();

        /// <summary>
        /// Optional explicit duration.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Optional provider-reported start time.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Optional provider-reported completion time.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;
    }
}
