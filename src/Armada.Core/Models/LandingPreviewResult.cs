namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Predictive landing summary for a vessel or mission.
    /// </summary>
    public class LandingPreviewResult
    {
        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Mission identifier when the preview is mission-scoped.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Source branch under consideration.
        /// </summary>
        public string? SourceBranch { get; set; } = null;

        /// <summary>
        /// Target branch that landing would use.
        /// </summary>
        public string TargetBranch { get; set; } = "main";

        /// <summary>
        /// Human-readable branch category.
        /// </summary>
        public string BranchCategory { get; set; } = "Default";

        /// <summary>
        /// Whether the target branch matches a configured protected-branch rule.
        /// </summary>
        public bool TargetBranchProtected { get; set; } = false;

        /// <summary>
        /// Protected-branch rule that matched the target branch, if any.
        /// </summary>
        public string? ProtectedBranchMatch { get; set; } = null;

        /// <summary>
        /// Effective landing mode.
        /// </summary>
        public LandingModeEnum? LandingMode { get; set; } = null;

        /// <summary>
        /// Effective branch cleanup policy.
        /// </summary>
        public BranchCleanupPolicyEnum? BranchCleanupPolicy { get; set; } = null;

        /// <summary>
        /// Whether the vessel requires passing checks before landing.
        /// </summary>
        public bool RequirePassingChecksToLand { get; set; } = false;

        /// <summary>
        /// Whether the vessel requires pull-request-based landing for protected branches.
        /// </summary>
        public bool RequirePullRequestForProtectedBranches { get; set; } = false;

        /// <summary>
        /// Whether release branches require merge-queue landing.
        /// </summary>
        public bool RequireMergeQueueForReleaseBranches { get; set; } = false;

        /// <summary>
        /// Human-readable expected landing action after policies are applied.
        /// </summary>
        public string? ExpectedLandingAction { get; set; } = null;

        /// <summary>
        /// Whether any passing check evidence was found for the current scope.
        /// </summary>
        public bool HasPassingChecks { get; set; } = false;

        /// <summary>
        /// Latest related check run identifier, if any.
        /// </summary>
        public string? LatestCheckRunId { get; set; } = null;

        /// <summary>
        /// Latest related check status, if any.
        /// </summary>
        public CheckRunStatusEnum? LatestCheckStatus { get; set; } = null;

        /// <summary>
        /// Latest related check summary, if any.
        /// </summary>
        public string? LatestCheckSummary { get; set; } = null;

        /// <summary>
        /// Whether the change currently appears ready to land.
        /// </summary>
        public bool IsReadyToLand { get; set; } = false;

        /// <summary>
        /// Mission previews only: the landing mode that actually applies to the mission (voyage, then vessel, then the
        /// Admiral default), or null when none is set anywhere.
        /// </summary>
        public LandingModeEnum? EffectiveLandingMode { get; set; } = null;

        /// <summary>
        /// Mission previews only: true when the effective landing mode is None, so Armada will not land the branch and the
        /// work must be merged by hand (for example from the vessel's Manage Branches). Land cannot succeed.
        /// </summary>
        public bool ManualLandingOnly { get; set; } = false;

        /// <summary>
        /// Mission previews only: the mission's status when the preview was computed.
        /// </summary>
        public MissionStatusEnum? MissionStatus { get; set; } = null;

        /// <summary>
        /// Landing preview issues and warnings.
        /// </summary>
        public List<LandingPreviewIssue> Issues { get; set; } = new List<LandingPreviewIssue>();
    }
}
