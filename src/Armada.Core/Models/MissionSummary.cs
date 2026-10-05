namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Lightweight mission projection used by list and status surfaces.
    /// </summary>
    public class MissionSummary
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Parent voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Target vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Assigned captain identifier.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Current mission status.
        /// </summary>
        public MissionStatusEnum Status { get; set; } = MissionStatusEnum.Pending;

        /// <summary>
        /// Mission priority.
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// Parent mission identifier.
        /// </summary>
        public string? ParentMissionId { get; set; } = null;

        /// <summary>
        /// Persona assigned to this mission.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Mission dependency identifier.
        /// </summary>
        public string? DependsOnMissionId { get; set; } = null;

        /// <summary>
        /// Branch name for this mission's work.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Assigned dock identifier.
        /// </summary>
        public string? DockId { get; set; } = null;

        /// <summary>
        /// Active OS process identifier.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Pull request URL if created.
        /// </summary>
        public string? PrUrl { get; set; } = null;

        /// <summary>
        /// Git commit hash captured when the mission completed.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// Failure reason when the mission fails.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// Structured classification of the failure (see <see cref="Mission.FailureKind"/>).
        /// </summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>
        /// Whether this mission requires explicit review approval.
        /// </summary>
        public bool RequiresReview { get; set; } = false;

        /// <summary>
        /// Action to take if review is denied.
        /// </summary>
        public ReviewDenyActionEnum ReviewDenyAction { get; set; } = ReviewDenyActionEnum.RetryStage;

        /// <summary>
        /// Reviewer comment from the most recent review decision.
        /// </summary>
        public string? ReviewComment { get; set; } = null;

        /// <summary>
        /// User identifier for the most recent reviewer.
        /// </summary>
        public string? ReviewedByUserId { get; set; } = null;

        /// <summary>
        /// Timestamp when the most recent review was requested.
        /// </summary>
        public DateTime? ReviewRequestedUtc { get; set; } = null;

        /// <summary>
        /// Timestamp when the most recent review was decided.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// Description length in characters.
        /// </summary>
        public int DescriptionLength { get; set; } = 0;

        /// <summary>
        /// Diff snapshot length in characters.
        /// </summary>
        public int DiffSnapshotLength { get; set; } = 0;

        /// <summary>
        /// Agent output length in characters.
        /// </summary>
        public int AgentOutputLength { get; set; } = 0;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Start timestamp in UTC.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Completion timestamp in UTC.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Total runtime in milliseconds.
        /// </summary>
        public long? TotalRuntimeMs { get; set; } = null;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
