namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Classified cause of a failed mission, used to decide whether autonomous recovery should open an
    /// incident and dispatch a bounded rescue mission.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionFailureKindEnum
    {
        /// <summary>
        /// The change did not compile (Definition-of-Done build failure).
        /// </summary>
        Compile,

        /// <summary>
        /// The build succeeded but unit tests failed (Definition-of-Done test failure).
        /// </summary>
        TestFail,

        /// <summary>
        /// A gate or command exceeded its time budget.
        /// </summary>
        Timeout,

        /// <summary>
        /// Landing failed for a mechanical reason (merge conflict, push failure).
        /// </summary>
        LandingConflict,

        /// <summary>
        /// The captain's runtime crashed or exited abnormally.
        /// </summary>
        Crash,

        /// <summary>
        /// The captain repeatedly completed with no changes (handled by re-dispatch, not rescue).
        /// </summary>
        NoOp,

        /// <summary>
        /// The change tripped the dock-boundary scanner (secrets, protected paths). Needs a human.
        /// </summary>
        Boundary,

        /// <summary>
        /// The change modified files outside the mission's scope. Needs a human.
        /// </summary>
        ScopeViolation,

        /// <summary>
        /// A Judge blocked the work. Needs a human or a re-implementation, not a mechanical rescue.
        /// </summary>
        JudgeRejected,

        /// <summary>
        /// Infrastructure could not run the work (missing toolchain, vanished checkout). Needs ops.
        /// </summary>
        Infra,

        /// <summary>
        /// The failure could not be classified.
        /// </summary>
        Unknown,

        /// <summary>
        /// A reviewer denied the work. Needs a human, not a mechanical rescue.
        /// </summary>
        ReviewDenied,

        /// <summary>
        /// The mission was not run or was stopped because something it depends on failed: a failed dependency
        /// mission, or its voyage was halted or cancelled after another mission failed.
        /// </summary>
        DependencyFailed,

        /// <summary>
        /// The mission ran past the configured maximum mission runtime and was stopped.
        /// </summary>
        MaxRuntimeExceeded,

        /// <summary>
        /// The captain stalled and stall recovery was exhausted.
        /// </summary>
        StallRecoveryExhausted,

        /// <summary>
        /// The mission was failed by an operator or by removal of the resource it targets (for example its
        /// vessel was deleted).
        /// </summary>
        OperatorAction,

        /// <summary>
        /// The captain's output did not satisfy the mission's output contract (for example an architect that
        /// produced no mission plan).
        /// </summary>
        InvalidOutput
    }
}
