namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Armada's handoff and landing scope, where the black box is biased toward finding problems in Armada itself: the
    /// phases Handoff, Landing, MergeQueue, Recovery and Provision; the Completion steps that are Armada handoff mechanics
    /// (output contract parsing, a check harness error rather than a real test failure, a scope check that misread the
    /// scoped files or ran without a start commit); and the dock, worktree, branch and target-sync facts of any phase. In
    /// scope the advisor adds the <see cref="PriorRuleId"/> vote for ArmadaDefect and always fills the resilience section.
    /// Pure and thread-safe.
    /// </summary>
    public static class BlackBoxResilienceScope
    {
        #region Public-Members

        /// <summary>
        /// Rule id of the ArmadaDefect prior vote cast for every record in scope.
        /// </summary>
        public const string PriorRuleId = "BBX-PRIOR-ARMADA";

        /// <summary>
        /// Phases that are wholly in scope.
        /// </summary>
        public static IReadOnlyList<BlackBoxPhaseEnum> Phases { get; } = new List<BlackBoxPhaseEnum>
        {
            BlackBoxPhaseEnum.Handoff, BlackBoxPhaseEnum.Landing, BlackBoxPhaseEnum.MergeQueue, BlackBoxPhaseEnum.Recovery, BlackBoxPhaseEnum.Provision
        };

        /// <summary>
        /// Steps outside the scope phases that are always in scope (the index-level scope filter uses them too).
        /// </summary>
        public static IReadOnlyList<BlackBoxStepEnum> Steps { get; } = new List<BlackBoxStepEnum>
        {
            BlackBoxStepEnum.InvalidOutput, BlackBoxStepEnum.ReviewApprovedDockMissing
        };

        /// <summary>
        /// Failure kinds that are always in scope (the index-level scope filter uses them too).
        /// </summary>
        public static IReadOnlyList<MissionFailureKindEnum> Kinds { get; } = new List<MissionFailureKindEnum>
        {
            MissionFailureKindEnum.InvalidOutput
        };

        /// <summary>
        /// Check steps whose harness errors (rather than real test failures) are in scope.
        /// </summary>
        public static IReadOnlyList<BlackBoxStepEnum> CheckSteps { get; } = new List<BlackBoxStepEnum>
        {
            BlackBoxStepEnum.StageCheck, BlackBoxStepEnum.DefinitionOfDone, BlackBoxStepEnum.Verification, BlackBoxStepEnum.QueueTests
        };

        #endregion

        #region Private-Members

        private static readonly HashSet<int> _HarnessExitCodes = new HashSet<int> { 124, 126, 127, 134, 137, 139, 143 };
        private static readonly Regex _Harness = new Regex(
            @"command not found|is not recognized as an internal or external command|could not start|failed to start|unable to start|cannot execute|exec format error|"
            + @"timed out|timeout expired|killed by signal|MSB1009|MSB1003|project file does not exist|no test is available|no tests? (?:were )?found|"
            + @"the system cannot find the file specified|working directory .{0,120}(?:does not exist|not found)|harness error|check harness|stage check could not run",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a phase is wholly in scope.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <returns>True for Handoff, Landing, MergeQueue, Recovery and Provision.</returns>
        public static bool IsScopePhase(BlackBoxPhaseEnum phase)
        {
            return Phases.Contains(phase);
        }

        /// <summary>
        /// The scope as far as the index row can tell (phase, step and failure kind): the condition behind the
        /// ResilienceScope enumerate filter. Records can be in scope for more reasons (<see cref="InScope(BlackBoxRecord)"/>).
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="step">Step.</param>
        /// <param name="kind">Failure kind, or null.</param>
        /// <returns>True when in scope.</returns>
        public static bool IndexInScope(BlackBoxPhaseEnum phase, BlackBoxStepEnum step, MissionFailureKindEnum? kind)
        {
            if (IsScopePhase(phase) || IsScopePhase(BlackBoxTaxonomy.PhaseOf(step))) return true;
            if (Steps.Contains(step)) return true;
            return kind.HasValue && Kinds.Contains(kind.Value);
        }

        /// <summary>
        /// Whether a record is in Armada's handoff and landing scope.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <returns>True when in scope.</returns>
        public static bool InScope(BlackBoxRecord record)
        {
            return InScope(record, out string? _);
        }

        /// <summary>
        /// Whether a record is in Armada's handoff and landing scope, and why.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="reason">Why it is in scope, or null.</param>
        /// <returns>True when in scope.</returns>
        public static bool InScope(BlackBoxRecord record, out string? reason)
        {
            reason = null;
            if (record == null) return false;
            if (record.Subject?.SubjectType == BlackBoxSubjectTypeEnum.Voyage) return false;
            BlackBoxPhaseEnum phase = BlackBoxRuleFacts.Phase(record);
            BlackBoxStepEnum step = BlackBoxRuleFacts.Step(record);
            MissionFailureKindEnum? kind = BlackBoxRuleFacts.Kind(record);
            if (IsScopePhase(phase))
            {
                reason = "phase " + phase + " (step " + step + ") is Armada's handoff and landing";
                return true;
            }
            if (step == BlackBoxStepEnum.InvalidOutput || kind == MissionFailureKindEnum.InvalidOutput)
            {
                reason = "Armada's output contract parsing (markers, sections, plan block) rejected the captain's report";
                return true;
            }
            if (step == BlackBoxStepEnum.ReviewApprovedDockMissing)
            {
                reason = "the approved mission's dock was gone before Armada landed it";
                return true;
            }
            if (CheckSteps.Contains(step) && HarnessError(record))
            {
                reason = "the " + step + " harness failed to run the check, rather than the code failing it";
                return true;
            }
            bool scopeFailure = step == BlackBoxStepEnum.ScopeViolation || kind == MissionFailureKindEnum.ScopeViolation;
            if (scopeFailure && BlackBoxRuleFacts.FalseScopeViolation(record))
            {
                reason = "Armada's scope check flagged files that were blank or in the scoped file list";
                return true;
            }
            if (scopeFailure && BlackBoxRuleFacts.IsMission(record) && record.Workspace != null && String.IsNullOrEmpty(record.Workspace.StartCommit))
            {
                reason = "Armada did not record the start commit its scope check needs";
                return true;
            }
            BlackBoxWorkspace? w = record.Workspace;
            if (w?.WorktreeExists == false)
            {
                reason = "the dock worktree was missing";
                return true;
            }
            if (w?.BranchExists == false)
            {
                reason = "the mission branch was missing";
                return true;
            }
            if (BlackBoxRuleFacts.MissingWorkingDirectory(record))
            {
                reason = "a working directory Armada manages was gone";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a check failure (stage check, definition of done, landing verification, merge-queue tests) is a harness
        /// error: the check could not run (timeout, command not found, missing project or directory, killed) rather than the
        /// code failing it.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <returns>True for a harness error.</returns>
        public static bool HarnessError(BlackBoxRecord record)
        {
            if (record == null) return false;
            if (record.Evidence != null && record.Evidence.CheckResults.Any(c => c.Status == StageCheckStatusEnum.CouldNotRun)) return true;
            int? exit = record.Mechanism?.CommandExitCode;
            if (exit.HasValue && _HarnessExitCodes.Contains(exit.Value)) return true;
            string text = BlackBoxRuleFacts.AllText(record);
            return text.Length > 0 && _Harness.IsMatch(text);
        }

        /// <summary>
        /// Whether the record's check results show the same checks both passing and failing (a flaky check rather than a
        /// deterministic failure).
        /// </summary>
        /// <param name="record">Record.</param>
        /// <returns>True when the checks flaked.</returns>
        public static bool CheckFlaked(BlackBoxRecord record)
        {
            if (record?.Evidence == null) return false;
            List<StageCheckResult> results = record.Evidence.CheckResults;
            return results.Any(c => c.Status == StageCheckStatusEnum.Passed) && results.Any(c => c.Status == StageCheckStatusEnum.Failed);
        }

        /// <summary>
        /// Whether a check failure is a concrete failure of the code (a non-zero exit that is not a harness error, or a
        /// Compile or TestFail kind): strong evidence the mission's change broke it.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <returns>True for a concrete failure.</returns>
        public static bool ConcreteCheckFailure(BlackBoxRecord record)
        {
            if (record == null || HarnessError(record)) return false;
            int? exit = record.Mechanism?.CommandExitCode;
            if (exit.HasValue && exit.Value != 0) return true;
            MissionFailureKindEnum? kind = BlackBoxRuleFacts.Kind(record);
            return kind == MissionFailureKindEnum.TestFail || kind == MissionFailureKindEnum.Compile;
        }

        #endregion
    }
}
