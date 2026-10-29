namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The specific failure step a capture site reports. Each step belongs to exactly one BlackBoxPhaseEnum value (BlackBoxTaxonomy.PhaseOf).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxStepEnum
    {
        /// <summary>
        /// The step could not be determined. Phase: Unknown.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// The mission's voyage could not run, so the mission was failed at dispatch. Phase: Dispatch.
        /// </summary>
        [EnumMember(Value = "VoyageParentNotRunnable")]
        VoyageParentNotRunnable,

        /// <summary>
        /// Assignment to a captain failed. Phase: Dispatch.
        /// </summary>
        [EnumMember(Value = "AssignmentFailed")]
        AssignmentFailed,

        /// <summary>
        /// No captain was eligible for the mission. Phase: Dispatch.
        /// </summary>
        [EnumMember(Value = "NoEligibleCaptain")]
        NoEligibleCaptain,

        /// <summary>
        /// The Harbor the mission needed was unavailable. Phase: Dispatch.
        /// </summary>
        [EnumMember(Value = "HarborUnavailable")]
        HarborUnavailable,

        /// <summary>
        /// Dispatch was refused or deferred for memory pressure. Phase: Dispatch.
        /// </summary>
        [EnumMember(Value = "MemoryPressure")]
        MemoryPressure,

        /// <summary>
        /// Dock provisioning failed. Phase: Provision.
        /// </summary>
        [EnumMember(Value = "DockProvision")]
        DockProvision,

        /// <summary>
        /// Worktree creation failed. Phase: Provision.
        /// </summary>
        [EnumMember(Value = "WorktreeCreate")]
        WorktreeCreate,

        /// <summary>
        /// Branch creation failed. Phase: Provision.
        /// </summary>
        [EnumMember(Value = "BranchCreate")]
        BranchCreate,

        /// <summary>
        /// Recording the start commit failed. Phase: Provision.
        /// </summary>
        [EnumMember(Value = "StartCommitRecord")]
        StartCommitRecord,

        /// <summary>
        /// The captain process crashed or exited with a failure. Phase: Execution.
        /// </summary>
        [EnumMember(Value = "ProcessCrash")]
        ProcessCrash,

        /// <summary>
        /// The runtime account hit its usage limit. Phase: Execution.
        /// </summary>
        [EnumMember(Value = "UsageLimit")]
        UsageLimit,

        /// <summary>
        /// The runtime rejected its credentials. Phase: Execution.
        /// </summary>
        [EnumMember(Value = "AuthFailure")]
        AuthFailure,

        /// <summary>
        /// The model was unavailable. Phase: Execution.
        /// </summary>
        [EnumMember(Value = "ModelUnavailable")]
        ModelUnavailable,

        /// <summary>
        /// The captain stalled (no activity). Phase: Execution.
        /// </summary>
        [EnumMember(Value = "Stall")]
        Stall,

        /// <summary>
        /// The mission exceeded its maximum runtime. Phase: Execution.
        /// </summary>
        [EnumMember(Value = "MaxRuntime")]
        MaxRuntime,

        /// <summary>
        /// Captain recovery attempts were exhausted. Phase: Execution.
        /// </summary>
        [EnumMember(Value = "RecoveryExhausted")]
        RecoveryExhausted,

        /// <summary>
        /// The captain process was interrupted. Phase: Execution.
        /// </summary>
        [EnumMember(Value = "Interrupted")]
        Interrupted,

        /// <summary>
        /// An operator stopped the captain. Phase: Execution.
        /// </summary>
        [EnumMember(Value = "OperatorStop")]
        OperatorStop,

        /// <summary>
        /// The captain finished without producing changes. Phase: Completion.
        /// </summary>
        [EnumMember(Value = "NoOp")]
        NoOp,

        /// <summary>
        /// The captain reported the work unfinished. Phase: Completion.
        /// </summary>
        [EnumMember(Value = "Unfinished")]
        Unfinished,

        /// <summary>
        /// The captain reported it was blocked. Phase: Completion.
        /// </summary>
        [EnumMember(Value = "CaptainBlocked")]
        CaptainBlocked,

        /// <summary>
        /// The captain's output did not meet the output contract. Phase: Completion.
        /// </summary>
        [EnumMember(Value = "InvalidOutput")]
        InvalidOutput,

        /// <summary>
        /// The mission changed files outside its scoped file list. Phase: Completion.
        /// </summary>
        [EnumMember(Value = "ScopeViolation")]
        ScopeViolation,

        /// <summary>
        /// The mission changed protected paths. Phase: Completion.
        /// </summary>
        [EnumMember(Value = "BoundaryViolation")]
        BoundaryViolation,

        /// <summary>
        /// A definition-of-done gate failed. Phase: Completion.
        /// </summary>
        [EnumMember(Value = "DefinitionOfDone")]
        DefinitionOfDone,

        /// <summary>
        /// A stage check failed. Phase: Completion.
        /// </summary>
        [EnumMember(Value = "StageCheck")]
        StageCheck,

        /// <summary>
        /// The judge rejected the work. Phase: Review.
        /// </summary>
        [EnumMember(Value = "JudgeRejected")]
        JudgeRejected,

        /// <summary>
        /// A reviewer denied the work. Phase: Review.
        /// </summary>
        [EnumMember(Value = "ReviewDenied")]
        ReviewDenied,

        /// <summary>
        /// Review timed out. Phase: Review.
        /// </summary>
        [EnumMember(Value = "ReviewTimeout")]
        ReviewTimeout,

        /// <summary>
        /// Review was approved but the dock was missing. Phase: Review.
        /// </summary>
        [EnumMember(Value = "ReviewApprovedDockMissing")]
        ReviewApprovedDockMissing,

        /// <summary>
        /// The architect plan could not be parsed. Phase: Handoff.
        /// </summary>
        [EnumMember(Value = "PlanParse")]
        PlanParse,

        /// <summary>
        /// Hardening the stage branch failed. Phase: Handoff.
        /// </summary>
        [EnumMember(Value = "StageBranchHarden")]
        StageBranchHarden,

        /// <summary>
        /// A dependency was cancelled. Phase: Handoff.
        /// </summary>
        [EnumMember(Value = "DependencyCancelled")]
        DependencyCancelled,

        /// <summary>
        /// The integration barrier failed. Phase: Handoff.
        /// </summary>
        [EnumMember(Value = "IntegrationBarrier")]
        IntegrationBarrier,

        /// <summary>
        /// The dock was missing at landing. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "DockMissing")]
        DockMissing,

        /// <summary>
        /// Integrating the target branch failed (for example a merge conflict). Phase: Landing.
        /// </summary>
        [EnumMember(Value = "TargetIntegration")]
        TargetIntegration,

        /// <summary>
        /// Post-merge verification failed. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "Verification")]
        Verification,

        /// <summary>
        /// The target checkout had uncommitted changes. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "DirtyCheckout")]
        DirtyCheckout,

        /// <summary>
        /// The branch had unpublished ancestry. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "UnpublishedAncestry")]
        UnpublishedAncestry,

        /// <summary>
        /// Pushing failed. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "Push")]
        Push,

        /// <summary>
        /// Creating or merging the pull request failed. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "PullRequest")]
        PullRequest,

        /// <summary>
        /// Fast-forwarding the target failed. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "FastForward")]
        FastForward,

        /// <summary>
        /// The local merge failed. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "LocalMerge")]
        LocalMerge,

        /// <summary>
        /// A landing retry failed. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "RetryLanding")]
        RetryLanding,

        /// <summary>
        /// A landing deferral expired. Phase: Landing.
        /// </summary>
        [EnumMember(Value = "DeferralExpired")]
        DeferralExpired,

        /// <summary>
        /// The merge queue could not resolve the repository. Phase: MergeQueue.
        /// </summary>
        [EnumMember(Value = "QueueResolveRepo")]
        QueueResolveRepo,

        /// <summary>
        /// The merge queue hit a merge conflict. Phase: MergeQueue.
        /// </summary>
        [EnumMember(Value = "QueueMergeConflict")]
        QueueMergeConflict,

        /// <summary>
        /// Merge-queue tests failed. Phase: MergeQueue.
        /// </summary>
        [EnumMember(Value = "QueueTests")]
        QueueTests,

        /// <summary>
        /// Merge-queue processing failed. Phase: MergeQueue.
        /// </summary>
        [EnumMember(Value = "QueueProcessing")]
        QueueProcessing,

        /// <summary>
        /// The merge queue could not push. Phase: MergeQueue.
        /// </summary>
        [EnumMember(Value = "QueuePush")]
        QueuePush,

        /// <summary>
        /// A rescue failed. Phase: Recovery.
        /// </summary>
        [EnumMember(Value = "RescueFailed")]
        RescueFailed,

        /// <summary>
        /// Redispatch attempts were exhausted. Phase: Recovery.
        /// </summary>
        [EnumMember(Value = "RedispatchExhausted")]
        RedispatchExhausted,

        /// <summary>
        /// A captain process exited while orphaned. Phase: Recovery.
        /// </summary>
        [EnumMember(Value = "OrphanedExit")]
        OrphanedExit,

        /// <summary>
        /// A stop could not be confirmed. Phase: Recovery.
        /// </summary>
        [EnumMember(Value = "UnconfirmedStop")]
        UnconfirmedStop,

        /// <summary>
        /// The merge handoff was missing. Phase: Recovery.
        /// </summary>
        [EnumMember(Value = "MissingMergeHandoff")]
        MissingMergeHandoff,

        /// <summary>
        /// Voyage settlement decided the voyage failed. Phase: Voyage.
        /// </summary>
        [EnumMember(Value = "SettlementFailed")]
        SettlementFailed,

        /// <summary>
        /// The voyage was halted. Phase: Voyage.
        /// </summary>
        [EnumMember(Value = "Halted")]
        Halted,

        /// <summary>
        /// The voyage was cancelled. Phase: Voyage.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled,

        /// <summary>
        /// A voyage stage failed. Phase: Voyage.
        /// </summary>
        [EnumMember(Value = "StageFailed")]
        StageFailed,

        /// <summary>
        /// The mission's vessel was deleted. Phase: External.
        /// </summary>
        [EnumMember(Value = "VesselDeleted")]
        VesselDeleted,

        /// <summary>
        /// An operator transitioned the mission's status by hand. Phase: External.
        /// </summary>
        [EnumMember(Value = "OperatorTransition")]
        OperatorTransition
    }
}
