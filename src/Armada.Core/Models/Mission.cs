namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Services;

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
        /// Identifier of the Harbor (host runner) this mission was routed to when its dock was provisioned,
        /// or null when it runs on the Admiral's own host (Local mode) or has not yet been routed.
        /// </summary>
        public string? AssignedHarborId { get; set; } = null;

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
        /// Number of automatic re-dispatches or attempted process recovery launches, including interruptions
        /// and no-op completions. Reattaching a still-running Harbor job does not increment this count.
        /// </summary>
        public int RedispatchAttempts { get; set; } = 0;

        /// <summary>
        /// Number of output-contract repair turns used: the stage ran again, with the exact contract errors quoted,
        /// after it finished with output that broke its contract (an invalid or missing Architect plan, a missing or
        /// conflicting Judge verdict, an unsubstantiated PASS, or a block without an affected case). Bounded by
        /// <c>ArmadaSettings.MaxOutputRepairAttempts</c>; counted separately from <see cref="RedispatchAttempts"/>.
        /// </summary>
        public int OutputRepairAttempts { get; set; } = 0;

        /// <summary>
        /// Number of re-dispatches after a detected no-op completion (finished with no changes). Bounded by
        /// <c>ArmadaSettings.MaxNoOpRedispatchAttempts</c> and counted separately from <see cref="RedispatchAttempts"/>, so
        /// earlier resumes or interruptions do not use up the no-op budget.
        /// </summary>
        public int NoOpRedispatchAttempts { get; set; } = 0;

        /// <summary>
        /// True for a mission that autonomous recovery dispatched to rescue a failed one. A rescue's failure is handled by
        /// the incident that dispatched it and never opens a rescue of its own, even after that incident or the original
        /// mission is purged.
        /// </summary>
        public bool IsRescue { get; set; } = false;

        /// <summary>
        /// True for evaluation work (fixture battery tasks and other model evaluations). Set at dispatch, inherited from
        /// the owning voyage and by every mission derived from this one. Stage outcomes of evaluation missions are tagged
        /// IsEvaluation and never count as production data.
        /// </summary>
        public bool IsEvaluation { get; set; } = false;

        /// <summary>
        /// For a Worker the Judge sent back for rework, its rework round (1 for the first); 0 for any other mission. Stored
        /// so rework state never depends on the title, which an operator can change.
        /// </summary>
        public int ReworkRound
        {
            get => _ReworkRound;
            set => _ReworkRound = value < 0 ? 0 : value;
        }

        /// <summary>
        /// True for a rework Worker that went to a different captain because the earlier rounds did not resolve the Judge's
        /// findings (the escalated attempt, after which the rework budget is spent).
        /// </summary>
        public bool ReworkEscalated { get; set; } = false;

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
        /// Structured classification of the failure, set at the point the mission fails (null while the mission
        /// has not failed, or for failures recorded before the column existed). Recovery decisions switch on this
        /// value; <see cref="FailureReason"/> is human-readable text only and is never parsed.
        /// </summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>
        /// The mission's current black box entry (bbx_ prefix), or null when it has none. Set when a failure is captured
        /// and kept when a restart clears <see cref="FailureReason"/> and <see cref="FailureKind"/>, so the evidence of
        /// the earlier failure stays reachable.
        /// </summary>
        public string? LastBlackBoxEntryId { get; set; } = null;

        /// <summary>
        /// Overlap warnings found when this mission was dispatched (black box prevention, at most 50), or null. Returned by
        /// the dispatch responses only; never stored.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [CollectionBound(Armada.Core.BlackBox.BlackBoxInsightLimits.MaxOverlapWarnings, Reason = "Dispatch keeps the strongest 50")]
        public List<MissionOverlapWarning>? OverlapWarnings { get; set; } = null;

        /// <summary>
        /// Brief lint findings found when this mission was dispatched (at most 50), or null. Returned by the dispatch
        /// responses only; never stored. Lint never blocks a dispatch.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [CollectionBound(Armada.Core.BlackBox.BlackBoxInsightLimits.MaxLintFindings, Reason = "Lint stops at 50")]
        public List<BriefLintFinding>? BriefLintWarnings { get; set; } = null;

        /// <summary>
        /// The target-branch commit the mission's dock started from (recorded at assignment), or null when unknown.
        /// The base for "what moved on the target since this mission started".
        /// </summary>
        public string? StartCommit { get; set; } = null;

        /// <summary>
        /// When true, a Worker mission waits for other implementation pipelines in the voyage, including reviews
        /// and checks, to settle (Complete, WorkProduced, Failed, Cancelled, or LandingFailed). Its own downstream
        /// stages and the Architect are excluded to avoid a circular wait. Reworks inherit this barrier. Set from
        /// the structured architect plan (waitForOtherMissions), not inferred from description wording.
        /// </summary>
        public bool WaitForVoyageWorkers { get; set; } = false;

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
        /// This is request metadata and is not stored directly on the mission row. At most
        /// <see cref="CollectionLimits.MaxConfigItems"/>; dispatch rejects more.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();

        /// <summary>
        /// Immutable snapshots of playbooks used for this mission: one per selected playbook.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One snapshot per selected playbook; selections are limited to MaxConfigItems on every dispatch path")]
        public List<MissionPlaybookSnapshot> PlaybookSnapshots { get; set; } = new List<MissionPlaybookSnapshot>();

        /// <summary>
        /// Why this Pending mission is still waiting for a captain. Computed by the server when a single mission or a
        /// voyage's missions are read (not stored); null for missions that are not Pending and in list responses.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MissionAssignmentBlocker? AssignmentBlocker { get; set; } = null;

        /// <summary>
        /// What the mission's captain is doing right now (its latest tool call, text, or reasoning, read from its
        /// runtime's structured output). Computed by the server when a single mission is read and carried by the
        /// mission.changed and mission.activity WebSocket events (not stored); null when the captain is not running, its
        /// runtime has no structured output, and in list responses.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RuntimeActivity? Activity { get; set; } = null;

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

        private int _ReworkRound = 0;
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
