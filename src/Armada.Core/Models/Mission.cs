namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// An atomic unit of work assigned to a captain.
    /// </summary>
    public class Mission
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
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
        /// Parent voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Target vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Assigned captain identifier. Set by dispatch when the mission is actually assigned to a captain;
        /// this is the captain that ran (or is running) the mission, which may differ from
        /// <see cref="RequestedCaptainId"/> when the preferred captain was busy and dispatch fell back by tier.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Optional preferred (dictated) captain identifier for this mission, referenced by captain id
        /// (cpt_ prefix). Resolved at creation from the dispatch payload, the voyage override, or the persona
        /// default. When set and that captain is idle, dispatch assigns it (bypassing the persona fence);
        /// when it is busy, dispatch falls back to an idle captain at or above <see cref="Tier"/>. Null means
        /// no preference (normal persona/tier routing).
        /// </summary>
        public string? RequestedCaptainId { get; set; } = null;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Title));
                _Title = value;
            }
        }

        /// <summary>
        /// Detailed mission description and instructions.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Current mission status.
        /// </summary>
        public MissionStatusEnum Status { get; set; } = MissionStatusEnum.Pending;

        /// <summary>
        /// Execution mode. Implementation (default) is a write mission that lands its diff; Audit and
        /// Research are read-only modes that produce a written report and whose empty diff is treated as
        /// success rather than a no-op failure.
        /// </summary>
        public MissionModeEnum Mode { get; set; } = MissionModeEnum.Implementation;

        /// <summary>
        /// Mission priority (lower is higher priority).
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// Number of times this mission has been automatically re-dispatched after a detected no-op
        /// completion. Bounds the auto-retry before the mission is failed and surfaced to the operator.
        /// </summary>
        public int RedispatchAttempts { get; set; } = 0;

        /// <summary>
        /// Optional required capability tier. Dispatch routes the mission to an idle captain at or above
        /// this tier (preferring the lowest eligible tier). Null means Standard.
        /// </summary>
        public CaptainTierEnum? Tier { get; set; } = null;

        /// <summary>
        /// Parent mission identifier for sub-tasks.
        /// </summary>
        public string? ParentMissionId { get; set; } = null;

        /// <summary>
        /// Git branch name for this mission's work.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Assigned dock identifier for this mission's worktree.
        /// </summary>
        public string? DockId { get; set; } = null;

        /// <summary>
        /// Operating system process identifier for the agent working this mission.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Pull request URL if created.
        /// </summary>
        public string? PrUrl { get; set; } = null;

        /// <summary>
        /// Git commit hash (HEAD) captured when the mission completed.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// Saved git diff snapshot captured at mission completion, before worktree reclamation.
        /// </summary>
        public string? DiffSnapshot { get; set; } = null;

        /// <summary>
        /// Accumulated agent stdout output captured during mission execution.
        /// Used by architect missions for [ARMADA:MISSION] marker parsing
        /// and by pipeline handoff to pass context to the next stage.
        /// </summary>
        public string? AgentOutput { get; set; } = null;

        /// <summary>
        /// Persona assigned to this mission (e.g. "Worker", "Architect", "Judge").
        /// Null defaults to "Worker" for backward compatibility.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Mission ID that this mission depends on.
        /// When set, this mission cannot be assigned until the dependency completes successfully.
        /// Used by pipelines to chain persona stages.
        /// </summary>
        public string? DependsOnMissionId { get; set; } = null;

        /// <summary>
        /// Human-readable reason for failure or landing failure.
        /// Set when a mission transitions to Failed or LandingFailed status.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// Whether this mission requires an explicit review approval before the pipeline may continue.
        /// Copied from the owning pipeline stage when the mission is created.
        /// </summary>
        public bool RequiresReview { get; set; } = false;

        /// <summary>
        /// Action to take if the review gate for this mission is denied.
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
        /// Timestamp when this mission most recently entered the review gate.
        /// </summary>
        public DateTime? ReviewRequestedUtc { get; set; } = null;

        /// <summary>
        /// UTC deadline by which a mission parked in Review must be actioned. When elapsed, the
        /// review watchdog escalates and frees the retained dock and captain so a forgotten
        /// review cannot pin capacity indefinitely. Null when the mission is not awaiting review.
        /// </summary>
        public DateTime? ReviewDeadlineUtc { get; set; } = null;

        /// <summary>
        /// Timestamp when this mission's most recent review decision was made.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// Selected playbooks supplied when creating or updating the mission.
        /// This is request metadata and is not stored directly on the mission row.
        /// </summary>
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();

        /// <summary>
        /// Immutable snapshots of playbooks used for this mission.
        /// </summary>
        public List<MissionPlaybookSnapshot> PlaybookSnapshots { get; set; } = new List<MissionPlaybookSnapshot>();

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when work started in UTC.
        /// </summary>
        public DateTime? StartedUtc
        {
            get => _StartedUtc;
            set
            {
                _StartedUtc = value;
                RecalculateTotalRuntimeMs();
            }
        }

        /// <summary>
        /// Timestamp when work completed in UTC.
        /// </summary>
        public DateTime? CompletedUtc
        {
            get => _CompletedUtc;
            set
            {
                _CompletedUtc = value;
                RecalculateTotalRuntimeMs();
            }
        }

        /// <summary>
        /// Total runtime in milliseconds, computed from StartedUtc and CompletedUtc.
        /// </summary>
        public long? TotalRuntimeMs
        {
            get => _TotalRuntimeMs;
            set => _TotalRuntimeMs = value;
        }

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.MissionIdPrefix, 24);
        private string _Title = "New Mission";
        private DateTime? _StartedUtc = null;
        private DateTime? _CompletedUtc = null;
        private long? _TotalRuntimeMs = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Mission()
        {
        }

        /// <summary>
        /// Instantiate with title.
        /// </summary>
        /// <param name="title">Mission title.</param>
        /// <param name="description">Mission description.</param>
        public Mission(string title, string? description = null)
        {
            Title = title;
            Description = description;
        }

        #endregion

        #region Private-Methods

        private void RecalculateTotalRuntimeMs()
        {
            if (_StartedUtc.HasValue && _CompletedUtc.HasValue)
            {
                double totalMilliseconds = (_CompletedUtc.Value - _StartedUtc.Value).TotalMilliseconds;
                _TotalRuntimeMs = totalMilliseconds >= 0 ? Convert.ToInt64(totalMilliseconds) : null;
            }
            else
            {
                _TotalRuntimeMs = null;
            }
        }

        #endregion
    }
}
