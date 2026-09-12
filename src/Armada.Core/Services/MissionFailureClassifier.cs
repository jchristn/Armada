namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Pure classifier that maps a failed mission's status and failure reason to a
    /// <see cref="MissionFailureKindEnum"/>, and decides whether that kind warrants an autonomous, bounded
    /// rescue mission. Side-effect free so it unit-tests without a database.
    /// </summary>
    public static class MissionFailureClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Classify a failed mission from its status and failure reason.
        /// </summary>
        /// <param name="status">Mission status (only failure states classify meaningfully).</param>
        /// <param name="failureReason">The mission's failure reason text.</param>
        /// <returns>The classified failure kind.</returns>
        public static MissionFailureKindEnum Classify(MissionStatusEnum status, string? failureReason)
        {
            string reason = (failureReason ?? String.Empty).ToLowerInvariant();

            if (reason.Contains("definition_of_done_compile")) return MissionFailureKindEnum.Compile;
            if (reason.Contains("definition_of_done_testfail")) return MissionFailureKindEnum.TestFail;
            if (reason.Contains("definition_of_done_timeout")) return MissionFailureKindEnum.Timeout;
            if (reason.Contains("definition_of_done_infra")) return MissionFailureKindEnum.Infra;

            if (reason.Contains("no_op_completion_detected")) return MissionFailureKindEnum.NoOp;

            if (reason.Contains("judge verdict") || reason.Contains("judge pass rejected") || reason.Contains("judge mission"))
                return MissionFailureKindEnum.JudgeRejected;

            if (reason.Contains("outside its scoped file list") || reason.Contains("out-of-scope") || reason.Contains("out of scope"))
                return MissionFailureKindEnum.ScopeViolation;

            if (reason.Contains("secret") || reason.Contains("protected path") || reason.Contains("private identifier") || reason.Contains("boundary"))
                return MissionFailureKindEnum.Boundary;

            if (status == MissionStatusEnum.LandingFailed
                || reason.Contains("merge conflict") || reason.Contains("conflict") || reason.Contains("push failed") || reason.Contains("landing"))
                return MissionFailureKindEnum.LandingConflict;

            if (reason.Contains("usage limit") || reason.Contains("quota") || reason.Contains("crash")
                || reason.Contains("segmentation") || reason.Contains("exited") || reason.Contains("killed"))
                return MissionFailureKindEnum.Crash;

            if (reason.Contains("timeout") || reason.Contains("timed out")) return MissionFailureKindEnum.Timeout;

            return MissionFailureKindEnum.Unknown;
        }

        /// <summary>
        /// Whether a failure kind warrants an autonomous, bounded rescue mission. Mechanical, fix-forward
        /// failures (compile, tests, landing conflicts, timeouts, transient crashes) are recoverable; failures
        /// that need a human judgment or an environment fix (boundary, scope, judge, no-op, infra, unknown)
        /// are not auto-rescued and are left to existing escalation.
        /// </summary>
        /// <param name="kind">The classified failure kind.</param>
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

        #endregion
    }
}
