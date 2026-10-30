namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Default resilience content per failure step, used when no matched rule carries its own (an uncovered step, the
    /// fallback rule) so the resilience section is never empty in handoff and landing scope: the handoff and landing
    /// services that own the step, a generic self-healing proposal, the detection gap and the suite a regression test
    /// belongs in. Pure and thread-safe.
    /// </summary>
    public static class BlackBoxResilienceDefaults
    {
        #region Public-Members

        /// <summary>Landing entry point.</summary>
        public const string LandingHandler = "src/Armada.Server/MissionLandingHandler.cs:HandleMissionCompleteAsync";

        /// <summary>Dock lease check before landing.</summary>
        public const string LandingDock = "src/Armada.Server/MissionLandingHandler.cs:EnsureLandingDockAsync";

        /// <summary>Retry landing.</summary>
        public const string RetryLanding = "src/Armada.Core/Services/LandingService.cs:RetryLandingAsync";

        /// <summary>Target synchronization.</summary>
        public const string TargetSync = "src/Armada.Core/Services/LandingTargetSynchronization.cs:SynchronizeAsync";

        /// <summary>Landing verification.</summary>
        public const string Verification = "src/Armada.Core/Services/LandingVerification.cs:VerifyAsync";

        /// <summary>Merge queue entry processing.</summary>
        public const string MergeQueue = "src/Armada.Core/Services/MergeQueueService.cs:ProcessEntryAsync";

        /// <summary>Merge queue branch merge.</summary>
        public const string MergeQueueMerge = "src/Armada.Core/Services/MergeQueueService.cs:MergeBranchAsync";

        /// <summary>Landing integrity sweep (deferrals, missing docks).</summary>
        public const string Integrity = "src/Armada.Core/Services/LandingIntegrityService.cs:SweepAsync";

        /// <summary>Deferred landing retries.</summary>
        public const string Deferrals = "src/Armada.Core/Services/LandingIntegrityService.cs:RetryDeferredLandingsAsync";

        /// <summary>Missing dock repair.</summary>
        public const string RepairDocks = "src/Armada.Core/Services/LandingIntegrityService.cs:RepairMissingDocksAsync";

        /// <summary>Dock provisioning.</summary>
        public const string Provision = "src/Armada.Core/Services/DockService.cs:ProvisionAsync";

        /// <summary>Dock reclaim.</summary>
        public const string Reclaim = "src/Armada.Core/Services/DockService.cs:ReclaimAsync";

        /// <summary>Worktree restore.</summary>
        public const string RestoreWorktree = "src/Armada.Core/Services/DockService.cs:RestoreWorktreeAsync";

        /// <summary>Dock repair.</summary>
        public const string RepairDock = "src/Armada.Core/Services/DockService.cs:RepairAsync";

        /// <summary>Mission completion handoff.</summary>
        public const string Completion = "src/Armada.Core/Services/MissionService.cs:HandleCompletionAsync";

        /// <summary>Output contract repair.</summary>
        public const string OutputRepair = "src/Armada.Core/Services/MissionService.cs:TryScheduleOutputRepairAsync";

        /// <summary>Pipeline stage handoff.</summary>
        public const string StageHandoff = "src/Armada.Core/Services/MissionService.Handoff.cs:TryHandoffToNextStageAsync";

        /// <summary>Stage branch hardening.</summary>
        public const string HardenStage = "src/Armada.Core/Services/MissionService.Handoff.cs:HardenStageBranchAsync";

        /// <summary>Dangling handoff recovery.</summary>
        public const string DanglingHandoffs = "src/Armada.Core/Services/MissionService.Handoff.cs:RecoverDanglingHandoffsAsync";

        /// <summary>Terminal process exit handling.</summary>
        public const string TerminalExit = "src/Armada.Core/Services/AdmiralService.cs:HandleTerminalProcessExitFailureAsync";

        /// <summary>Orphaned mission recovery.</summary>
        public const string Orphans = "src/Armada.Core/Services/AdmiralService.cs:RecoverOrphanedMissionAsync";

        /// <summary>Missing merge handoff detection.</summary>
        public const string MissingHandoffs = "src/Armada.Core/Services/AdmiralService.cs:FlagMissingMergeHandoffsAsync";

        /// <summary>Unconfirmed stop recording.</summary>
        public const string UnconfirmedStop = "src/Armada.Core/Services/AdmiralService.cs:RecordUnconfirmedStopAsync";

        #endregion

        #region Private-Members

        private static readonly Dictionary<BlackBoxPhaseEnum, string[]> _PhasePointers = new Dictionary<BlackBoxPhaseEnum, string[]>
        {
            { BlackBoxPhaseEnum.Landing, new[] { LandingHandler, RetryLanding, TargetSync, Integrity } },
            { BlackBoxPhaseEnum.MergeQueue, new[] { MergeQueue, MergeQueueMerge, LandingHandler } },
            { BlackBoxPhaseEnum.Handoff, new[] { StageHandoff, HardenStage, Completion } },
            { BlackBoxPhaseEnum.Recovery, new[] { TerminalExit, Orphans, MissingHandoffs } },
            { BlackBoxPhaseEnum.Provision, new[] { Provision, RestoreWorktree, RepairDock } },
            { BlackBoxPhaseEnum.Completion, new[] { Completion, OutputRepair } },
            { BlackBoxPhaseEnum.Review, new[] { Reclaim, LandingDock } }
        };

        private static readonly Dictionary<BlackBoxPhaseEnum, string> _PhaseSuites = new Dictionary<BlackBoxPhaseEnum, string>
        {
            { BlackBoxPhaseEnum.Landing, "Services.LandingPipeline" },
            { BlackBoxPhaseEnum.MergeQueue, "Services.MergeQueueProcessing" },
            { BlackBoxPhaseEnum.Handoff, "Services.HandoffResilience" },
            { BlackBoxPhaseEnum.Recovery, "Services.AdmiralService" },
            { BlackBoxPhaseEnum.Provision, "Services.DockService" },
            { BlackBoxPhaseEnum.Completion, "Services.ContractRepair" },
            { BlackBoxPhaseEnum.Review, "Services.DockLease" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The handoff and landing services that own a phase (file:method), for code pointers.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <returns>Pointers (empty for a phase outside handoff and landing).</returns>
        public static List<string> PointersFor(BlackBoxPhaseEnum phase)
        {
            return _PhasePointers.TryGetValue(phase, out string[]? pointers) ? new List<string>(pointers) : new List<string>();
        }

        /// <summary>
        /// The test suite a regression test for a phase belongs in.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <returns>The suite id.</returns>
        public static string SuiteFor(BlackBoxPhaseEnum phase)
        {
            return _PhaseSuites.TryGetValue(phase, out string? suite) ? suite : "Services.BlackBoxAdvisor";
        }

        /// <summary>
        /// The default resilience template for an in-scope step no rule covers: retry with backoff and a preflight check
        /// before the step, with the step's owning services as code pointers.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="step">Step.</param>
        /// <returns>The template (rendered by the advisor).</returns>
        public static BlackBoxRuleResilience ForStep(BlackBoxPhaseEnum phase, BlackBoxStepEnum step)
        {
            string stepName = step == BlackBoxStepEnum.Unknown ? "the failing step" : "step " + step;
            BlackBoxRuleResilience r = new BlackBoxRuleResilience
            {
                SelfHealable = BlackBoxSelfHealableEnum.Unknown,
                SelfHealing = "retried " + stepName + " with backoff after re-checking the dock, the branch and the target, and kept the work intact for a person if the retry failed too",
                GapKind = BlackBoxDetectionGapEnum.NoPreflightCheck,
                DetectionGap = "No preflight check or black box rule covers " + stepName + " in phase " + phase + ", so Armada learned of the problem only when the step failed.",
                Invariant = "Before " + stepName + " runs, the mission's dock, branch and target are verified, and a failure there leaves the mission recoverable (WorkProduced with its branch) rather than terminal.",
                RegressionSuite = SuiteFor(phase),
                RegressionTest = "Reproduce the {phase} / {step} failure of {missionId|the mission} and assert Armada retries once with backoff and keeps the branch and dock for recovery."
            };
            r.CodePointers.AddRange(PointersFor(phase));
            r.Changes.Add(BlackBoxRuleChange.Of(
                "Make " + phase + " step " + step + " retry and preflight instead of failing",
                "Armada's " + phase + " failed at " + stepName + " with no rule or preflight check covering it ({reason|no reason recorded}). Add a preflight check before the step (dock and worktree present, branch resolvable, target fetched), retry the step once with backoff when the failure is retryable, and when it still fails keep the mission recoverable with its branch and dock instead of ending it. Add a black box rule for this failure mode with resilience content.",
                SuiteFor(phase) + ": a mission that fails at " + stepName + " once is retried and lands; a second failure leaves it WorkProduced with its branch.",
                BlackBoxSeverityEnum.Medium,
                PointersFor(phase).ToArray()));
            return r;
        }

        #endregion
    }
}
