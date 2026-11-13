namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Maps a merge queue failure to the mission failure kind it records, by the step that failed. Only a merge git
    /// reported as conflicted is a <see cref="MissionFailureKindEnum.LandingConflict"/>; a landing check that ran and
    /// failed is a <see cref="MissionFailureKindEnum.TestFail"/> (or <see cref="MissionFailureKindEnum.Timeout"/> when it
    /// ran out of time); everything else (an unresolvable repository, a failed fetch or worktree, a check that could not
    /// run or changed the tree, a refused push) is <see cref="MissionFailureKindEnum.Infra"/>.
    /// </summary>
    public static class MergeQueueFailureClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Classify a merge step that did not merge.
        /// </summary>
        /// <param name="conflictedFileCount">Number of files git reported as conflicted.</param>
        /// <returns>LandingConflict when git reported conflicted files, otherwise Infra.</returns>
        public static MissionFailureKindEnum ClassifyMerge(int conflictedFileCount)
        {
            return conflictedFileCount > 0 ? MissionFailureKindEnum.LandingConflict : MissionFailureKindEnum.Infra;
        }

        /// <summary>
        /// Classify an exception raised while processing a merge queue entry.
        /// </summary>
        /// <param name="step">The step that was running.</param>
        /// <param name="exception">The failure.</param>
        /// <returns>The failure kind to record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when exception is null.</exception>
        public static MissionFailureKindEnum Classify(MergeQueueFailureStepEnum step, Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is GitMergeConflictException) return MissionFailureKindEnum.LandingConflict;
            }

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

        #endregion
    }
}
