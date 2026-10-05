namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Pure classifier that maps a failed mission's status and persisted failure kind to a
    /// <see cref="MissionFailureKindEnum"/>, and decides whether that kind warrants an autonomous, bounded
    /// rescue mission. Side-effect free so it unit-tests without a database.
    /// </summary>
    public static class MissionFailureClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Classify a failed mission from its status and its persisted failure kind. The kind is decided and stored
        /// where the failure happens (<see cref="Mission.FailureKind"/>); this method never reads
        /// <see cref="Mission.FailureReason"/>, which is human-readable text only. A LandingFailed mission without a
        /// recorded kind is a landing conflict (the status itself says landing failed); any other mission without a
        /// recorded kind (for example a failure recorded before the column existed) is
        /// <see cref="MissionFailureKindEnum.Unknown"/> and is therefore never auto-rescued.
        /// </summary>
        /// <param name="status">Mission status.</param>
        /// <param name="failureKind">The mission's persisted failure kind, when recorded.</param>
        /// <returns>The failure kind.</returns>
        public static MissionFailureKindEnum Classify(MissionStatusEnum status, MissionFailureKindEnum? failureKind)
        {
            if (failureKind.HasValue) return failureKind.Value;
            if (status == MissionStatusEnum.LandingFailed) return MissionFailureKindEnum.LandingConflict;
            return MissionFailureKindEnum.Unknown;
        }

        /// <summary>
        /// Classify a failed mission (see <see cref="Classify(MissionStatusEnum, MissionFailureKindEnum?)"/>).
        /// </summary>
        /// <param name="mission">The mission.</param>
        /// <returns>The failure kind.</returns>
        public static MissionFailureKindEnum Classify(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return Classify(mission.Status, mission.FailureKind);
        }

        /// <summary>
        /// Map a Definition-of-Done gate outcome to the mission failure kind it records.
        /// </summary>
        /// <param name="outcome">The gate outcome.</param>
        /// <returns>The failure kind.</returns>
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
