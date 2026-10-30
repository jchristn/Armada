namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// The black box phase and step taxonomy (docs/BLACKBOX.md): maps every BlackBoxStepEnum value to its BlackBoxPhaseEnum.
    /// Thread-safe (immutable).
    /// </summary>
    public static class BlackBoxTaxonomy
    {
        private static readonly Dictionary<BlackBoxStepEnum, BlackBoxPhaseEnum> _Phases = new Dictionary<BlackBoxStepEnum, BlackBoxPhaseEnum>
        {
            { BlackBoxStepEnum.Unknown, BlackBoxPhaseEnum.Unknown },
            { BlackBoxStepEnum.VoyageParentNotRunnable, BlackBoxPhaseEnum.Dispatch },
            { BlackBoxStepEnum.AssignmentFailed, BlackBoxPhaseEnum.Dispatch },
            { BlackBoxStepEnum.NoEligibleCaptain, BlackBoxPhaseEnum.Dispatch },
            { BlackBoxStepEnum.HarborUnavailable, BlackBoxPhaseEnum.Dispatch },
            { BlackBoxStepEnum.MemoryPressure, BlackBoxPhaseEnum.Dispatch },
            { BlackBoxStepEnum.DockProvision, BlackBoxPhaseEnum.Provision },
            { BlackBoxStepEnum.WorktreeCreate, BlackBoxPhaseEnum.Provision },
            { BlackBoxStepEnum.BranchCreate, BlackBoxPhaseEnum.Provision },
            { BlackBoxStepEnum.StartCommitRecord, BlackBoxPhaseEnum.Provision },
            { BlackBoxStepEnum.ProcessCrash, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.UsageLimit, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.AuthFailure, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.ModelUnavailable, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.Stall, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.MaxRuntime, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.RecoveryExhausted, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.Interrupted, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.OperatorStop, BlackBoxPhaseEnum.Execution },
            { BlackBoxStepEnum.NoOp, BlackBoxPhaseEnum.Completion },
            { BlackBoxStepEnum.Unfinished, BlackBoxPhaseEnum.Completion },
            { BlackBoxStepEnum.CaptainBlocked, BlackBoxPhaseEnum.Completion },
            { BlackBoxStepEnum.InvalidOutput, BlackBoxPhaseEnum.Completion },
            { BlackBoxStepEnum.ScopeViolation, BlackBoxPhaseEnum.Completion },
            { BlackBoxStepEnum.BoundaryViolation, BlackBoxPhaseEnum.Completion },
            { BlackBoxStepEnum.DefinitionOfDone, BlackBoxPhaseEnum.Completion },
            { BlackBoxStepEnum.StageCheck, BlackBoxPhaseEnum.Completion },
            { BlackBoxStepEnum.JudgeRejected, BlackBoxPhaseEnum.Review },
            { BlackBoxStepEnum.ReviewDenied, BlackBoxPhaseEnum.Review },
            { BlackBoxStepEnum.ReviewTimeout, BlackBoxPhaseEnum.Review },
            { BlackBoxStepEnum.ReviewApprovedDockMissing, BlackBoxPhaseEnum.Review },
            { BlackBoxStepEnum.PlanParse, BlackBoxPhaseEnum.Handoff },
            { BlackBoxStepEnum.StageBranchHarden, BlackBoxPhaseEnum.Handoff },
            { BlackBoxStepEnum.DependencyCancelled, BlackBoxPhaseEnum.Handoff },
            { BlackBoxStepEnum.IntegrationBarrier, BlackBoxPhaseEnum.Handoff },
            { BlackBoxStepEnum.DockMissing, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.TargetIntegration, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.Verification, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.DirtyCheckout, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.UnpublishedAncestry, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.Push, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.PullRequest, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.FastForward, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.LocalMerge, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.RetryLanding, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.DeferralExpired, BlackBoxPhaseEnum.Landing },
            { BlackBoxStepEnum.QueueResolveRepo, BlackBoxPhaseEnum.MergeQueue },
            { BlackBoxStepEnum.QueueMergeConflict, BlackBoxPhaseEnum.MergeQueue },
            { BlackBoxStepEnum.QueueTests, BlackBoxPhaseEnum.MergeQueue },
            { BlackBoxStepEnum.QueueProcessing, BlackBoxPhaseEnum.MergeQueue },
            { BlackBoxStepEnum.QueuePush, BlackBoxPhaseEnum.MergeQueue },
            { BlackBoxStepEnum.RescueFailed, BlackBoxPhaseEnum.Recovery },
            { BlackBoxStepEnum.RedispatchExhausted, BlackBoxPhaseEnum.Recovery },
            { BlackBoxStepEnum.OrphanedExit, BlackBoxPhaseEnum.Recovery },
            { BlackBoxStepEnum.UnconfirmedStop, BlackBoxPhaseEnum.Recovery },
            { BlackBoxStepEnum.MissingMergeHandoff, BlackBoxPhaseEnum.Recovery },
            { BlackBoxStepEnum.SettlementFailed, BlackBoxPhaseEnum.Voyage },
            { BlackBoxStepEnum.Halted, BlackBoxPhaseEnum.Voyage },
            { BlackBoxStepEnum.Cancelled, BlackBoxPhaseEnum.Voyage },
            { BlackBoxStepEnum.StageFailed, BlackBoxPhaseEnum.Voyage },
            { BlackBoxStepEnum.VesselDeleted, BlackBoxPhaseEnum.External },
            { BlackBoxStepEnum.OperatorTransition, BlackBoxPhaseEnum.External }
        };

        /// <summary>
        /// The phase a step belongs to. Unknown for an unmapped value.
        /// </summary>
        /// <param name="step">Step.</param>
        /// <returns>The phase.</returns>
        public static BlackBoxPhaseEnum PhaseOf(BlackBoxStepEnum step)
        {
            return _Phases.TryGetValue(step, out BlackBoxPhaseEnum phase) ? phase : BlackBoxPhaseEnum.Unknown;
        }

        /// <summary>
        /// Every step of a phase, in declaration order.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <returns>The steps.</returns>
        public static List<BlackBoxStepEnum> StepsOf(BlackBoxPhaseEnum phase)
        {
            List<BlackBoxStepEnum> steps = new List<BlackBoxStepEnum>();
            foreach (BlackBoxStepEnum step in Enum.GetValues(typeof(BlackBoxStepEnum)))
            {
                if (PhaseOf(step) == phase) steps.Add(step);
            }
            return steps;
        }
    }
}
