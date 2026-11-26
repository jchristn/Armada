namespace Armada.Core.Services
{
    using System;
    using System.ComponentModel;
    using System.IO;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The one place a failure source becomes a <see cref="MissionFailureKindEnum"/> (RS-5.1). Every source (runtime exit,
    /// Definition of Done, landing, merge queue, lost handoff, mission status) maps here, and the recovery coordinator
    /// decides from the result whether an autonomous rescue applies. BlackBox and FailureCause rules consume the kind;
    /// they never produce one. Pure and thread safe. The mapping table is documented in docs/MODEL_HETEROGENEITY.md Part 4.
    /// </summary>
    public static class FailureKindMapper
    {
        #region Public-Methods

        /// <summary>
        /// Map a runtime exit that ended a mission. A negative exit code means Armada stopped or lost the process (redispatch
        /// exhausted, Admiral interruption) and is <see cref="MissionFailureKindEnum.Infra"/>. Otherwise a task failure is
        /// <see cref="MissionFailureKindEnum.InvalidOutput"/>, a transient endpoint failure that outlasted its resumes and the
        /// outage grace is <see cref="MissionFailureKindEnum.Infra"/> (the environment, not the captain), and everything else
        /// is <see cref="MissionFailureKindEnum.Crash"/>.
        /// </summary>
        /// <param name="runtimeKind">The runtime failure kind.</param>
        /// <param name="exitCode">The process exit code, when known.</param>
        /// <returns>The mission failure kind.</returns>
        public static MissionFailureKindEnum FromRuntimeExit(RuntimeFailureKindEnum runtimeKind, int? exitCode)
        {
            if (exitCode.HasValue && exitCode.Value < 0) return MissionFailureKindEnum.Infra;
            switch (runtimeKind)
            {
                case RuntimeFailureKindEnum.TaskFailure: return MissionFailureKindEnum.InvalidOutput;
                case RuntimeFailureKindEnum.Transient: return MissionFailureKindEnum.Infra;
                default: return MissionFailureKindEnum.Crash;
            }
        }

        /// <summary>
        /// Map a Definition-of-Done gate outcome.
        /// </summary>
        /// <param name="outcome">The gate outcome.</param>
        /// <returns>The mission failure kind.</returns>
        public static MissionFailureKindEnum FromDefinitionOfDone(DefinitionOfDoneOutcomeEnum outcome)
        {
            switch (outcome)
            {
                case DefinitionOfDoneOutcomeEnum.Compile: return MissionFailureKindEnum.Compile;
                case DefinitionOfDoneOutcomeEnum.TestFail: return MissionFailureKindEnum.TestFail;
                case DefinitionOfDoneOutcomeEnum.Timeout: return MissionFailureKindEnum.Timeout;
                case DefinitionOfDoneOutcomeEnum.Infra: return MissionFailureKindEnum.Infra;
                default: return MissionFailureKindEnum.Unknown;
            }
        }

        /// <summary>
        /// Map a landing exception. Only a merge conflict git actually reported (<see cref="GitMergeConflictException"/>) is
        /// a <see cref="MissionFailureKindEnum.LandingConflict"/>, which an autonomous rescue can fix. Everything else,
        /// including git failing to start because the dock directory is gone, a dirty developer checkout, unpublished
        /// checkout commits or a refused push, is <see cref="MissionFailureKindEnum.Infra"/>.
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <returns>The mission failure kind.</returns>
        /// <exception cref="ArgumentNullException">Thrown when exception is null.</exception>
        public static MissionFailureKindEnum FromLandingException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (IsProcessStartFailure(exception)) return MissionFailureKindEnum.Infra;
            return HasMergeConflict(exception) ? MissionFailureKindEnum.LandingConflict : MissionFailureKindEnum.Infra;
        }

        /// <summary>
        /// Map a merge queue merge step that did not merge.
        /// </summary>
        /// <param name="conflictedFileCount">Number of files git reported as conflicted.</param>
        /// <returns>LandingConflict when git reported conflicted files, otherwise Infra.</returns>
        public static MissionFailureKindEnum FromMergeResult(int conflictedFileCount)
        {
            return conflictedFileCount > 0 ? MissionFailureKindEnum.LandingConflict : MissionFailureKindEnum.Infra;
        }

        /// <summary>
        /// Map an exception raised while processing a merge queue entry, by the step that failed. Only a merge git reported
        /// as conflicted is a <see cref="MissionFailureKindEnum.LandingConflict"/>; a landing check that ran and failed is a
        /// <see cref="MissionFailureKindEnum.TestFail"/> (or <see cref="MissionFailureKindEnum.Timeout"/> when it ran out of
        /// time); everything else (an unresolvable repository, a failed fetch or worktree, a check that could not run or
        /// changed the tree, a refused push) is <see cref="MissionFailureKindEnum.Infra"/>.
        /// </summary>
        /// <param name="step">The step that was running.</param>
        /// <param name="exception">The failure.</param>
        /// <returns>The mission failure kind.</returns>
        /// <exception cref="ArgumentNullException">Thrown when exception is null.</exception>
        public static MissionFailureKindEnum FromMergeQueueException(MergeQueueFailureStepEnum step, Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (HasMergeConflict(exception)) return MissionFailureKindEnum.LandingConflict;
            if (step != MergeQueueFailureStepEnum.Verification) return MissionFailureKindEnum.Infra;
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is LandingVerificationException verification)
                {
                    if (verification.TimedOut) return MissionFailureKindEnum.Timeout;
                    // A check that rewrote the commit or tree did not judge the work; the check itself needs attention.
                    if (verification.Mutated) return MissionFailureKindEnum.Infra;
                    return MissionFailureKindEnum.TestFail;
                }
                if (current is TimeoutException) return MissionFailureKindEnum.Timeout;
            }
            return MissionFailureKindEnum.Infra;
        }

        /// <summary>
        /// Map a lost landing handoff: a nonblocking stage check passed but no merge queue entry was recorded. This is an
        /// Armada handoff failure, not a merge conflict an autonomous rescue can fix.
        /// </summary>
        /// <returns><see cref="MissionFailureKindEnum.Infra"/>.</returns>
        public static MissionFailureKindEnum FromLostLandingHandoff()
        {
            return MissionFailureKindEnum.Infra;
        }

        /// <summary>
        /// Classify a failed mission from its status and its persisted failure kind. The kind is decided and stored where the
        /// failure happens (<see cref="Mission.FailureKind"/>); this method never reads <see cref="Mission.FailureReason"/>,
        /// which is human-readable text only. A LandingFailed mission without a recorded kind is a landing conflict; any
        /// other mission without a recorded kind is <see cref="MissionFailureKindEnum.Unknown"/> and is never auto-rescued.
        /// </summary>
        /// <param name="status">Mission status.</param>
        /// <param name="failureKind">The mission's persisted failure kind, when recorded.</param>
        /// <returns>The mission failure kind.</returns>
        public static MissionFailureKindEnum FromStatus(MissionStatusEnum status, MissionFailureKindEnum? failureKind)
        {
            if (failureKind.HasValue) return failureKind.Value;
            if (status == MissionStatusEnum.LandingFailed) return MissionFailureKindEnum.LandingConflict;
            return MissionFailureKindEnum.Unknown;
        }

        /// <summary>
        /// Classify a failed mission (see <see cref="FromStatus(MissionStatusEnum, MissionFailureKindEnum?)"/>).
        /// </summary>
        /// <param name="mission">The mission.</param>
        /// <returns>The mission failure kind.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public static MissionFailureKindEnum FromMission(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return FromStatus(mission.Status, mission.FailureKind);
        }

        /// <summary>
        /// Whether a failure kind warrants an autonomous, bounded rescue mission. Mechanical, fix-forward failures (compile,
        /// tests, landing conflicts, timeouts, crashes) are recoverable; failures that need a human judgment or an
        /// environment fix (boundary, scope, judge, no-op, infra, unknown) are left to existing escalation.
        /// </summary>
        /// <param name="kind">The mission failure kind.</param>
        /// <returns>True when a bounded rescue mission should be dispatched.</returns>
        public static bool IsRecoverable(MissionFailureKindEnum kind)
        {
            switch (kind)
            {
                case MissionFailureKindEnum.Compile:
                case MissionFailureKindEnum.TestFail:
                case MissionFailureKindEnum.LandingConflict:
                case MissionFailureKindEnum.Timeout:
                case MissionFailureKindEnum.Crash:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a failure is a process that could not start (missing working directory, missing executable) rather than a
        /// git command that ran and failed.
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <returns>True for a process start failure.</returns>
        /// <exception cref="ArgumentNullException">Thrown when exception is null.</exception>
        public static bool IsProcessStartFailure(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is Win32Exception || current is IOException) return true;
                if (current is InvalidOperationException
                    && current.Message.Contains("trying to start process", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        #endregion

        #region Private-Methods

        private static bool HasMergeConflict(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is GitMergeConflictException) return true;
            }
            return false;
        }

        #endregion
    }
}
