namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Readiness summary for one vessel and optional workflow action.
    /// </summary>
    public class VesselReadinessResult
    {
        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Whether the vessel has a usable checkout: its working directory on the Admiral host, or a checkout on a
        /// connected Harbor that can serve it.
        /// </summary>
        public bool HasWorkingDirectory { get; set; }

        /// <summary>
        /// Where checks, Workspace, and readiness probes run: the working directory on the Admiral host, or the checkout's
        /// path on <see cref="HarborId"/>. Null when there is none.
        /// </summary>
        public string? CheckoutPath { get; set; } = null;

        /// <summary>
        /// The Harbor that has the checkout, or null for the Admiral host (or when there is none).
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// The Harbor's name (without its identifier, which is <see cref="HarborId"/>), or null.
        /// </summary>
        public string? HarborName { get; set; } = null;

        /// <summary>
        /// Why there is no checkout Armada can use, or null when there is one (or when Harbors were not considered).
        /// </summary>
        public VesselCheckoutErrorCodeEnum? CheckoutErrorCode { get; set; } = null;

        /// <summary>
        /// Whether Armada has a usable bare repository path or can recover it from RepoUrl.
        /// </summary>
        public bool HasRepositoryContext { get; set; }

        /// <summary>
        /// Resolved workflow profile identifier, if any.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Resolved workflow profile name, if any.
        /// </summary>
        public string? WorkflowProfileName { get; set; } = null;

        /// <summary>
        /// Resolved workflow profile scope, if any.
        /// </summary>
        public WorkflowProfileScopeEnum? WorkflowProfileScope { get; set; } = null;

        /// <summary>
        /// Requested check type when evaluating readiness for a specific check.
        /// </summary>
        public CheckRunTypeEnum? RequestedCheckType { get; set; } = null;

        /// <summary>
        /// Requested environment name when evaluating readiness for a specific environment-bound check.
        /// </summary>
        public string? RequestedEnvironmentName { get; set; } = null;

        /// <summary>
        /// Check types currently exposed by the resolved profile.
        /// </summary>
        public List<string> AvailableCheckTypes { get; set; } = new List<string>();

        /// <summary>
        /// Current working branch, if available.
        /// </summary>
        public string? CurrentBranch { get; set; } = null;

        /// <summary>
        /// Whether the working directory currently has uncommitted changes.
        /// </summary>
        public bool? HasUncommittedChanges { get; set; } = null;

        /// <summary>
        /// Whether HEAD is detached.
        /// </summary>
        public bool? IsDetachedHead { get; set; } = null;

        /// <summary>
        /// Commits ahead of the remote-tracking default branch, when known.
        /// </summary>
        public int? CommitsAhead { get; set; } = null;

        /// <summary>
        /// Commits behind the remote-tracking default branch, when known.
        /// </summary>
        public int? CommitsBehind { get; set; } = null;

        /// <summary>
        /// Toolchains inferred from repository contents.
        /// </summary>
        public List<string> DetectedToolchains { get; set; } = new List<string>();

        /// <summary>
        /// Detailed toolchain probes with availability and version metadata.
        /// </summary>
        public List<VesselToolchainProbe> ToolchainProbes { get; set; } = new List<VesselToolchainProbe>();

        /// <summary>
        /// Environment names exposed by the resolved profile.
        /// </summary>
        public List<string> DeploymentEnvironments { get; set; } = new List<string>();

        /// <summary>
        /// Deployment-oriented workflow coverage summary.
        /// </summary>
        public VesselDeploymentMetadata DeploymentMetadata { get; set; } = new VesselDeploymentMetadata();

        /// <summary>
        /// Actionable setup checklist items.
        /// </summary>
        public List<VesselSetupChecklistItem> SetupChecklist { get; set; } = new List<VesselSetupChecklistItem>();

        /// <summary>
        /// Readiness issues discovered during evaluation.
        /// </summary>
        public List<VesselReadinessIssue> Issues { get; set; } = new List<VesselReadinessIssue>();

        /// <summary>
        /// Number of satisfied onboarding checklist items.
        /// </summary>
        public int SetupChecklistSatisfiedCount { get; set; }

        /// <summary>
        /// Total number of onboarding checklist items.
        /// </summary>
        public int SetupChecklistTotalCount { get; set; }

        /// <summary>
        /// Number of blocking issues.
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// Number of warning issues.
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// True when no blocking issues were found.
        /// </summary>
        public bool IsReady { get; set; }
    }
}
