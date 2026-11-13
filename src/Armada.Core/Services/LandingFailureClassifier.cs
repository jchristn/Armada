namespace Armada.Core.Services
{
    using System;
    using System.ComponentModel;
    using System.IO;
    using Armada.Core.Enums;

    /// <summary>
    /// Maps a landing exception to the mission failure kind it records. Only a merge conflict git actually reported
    /// (<see cref="GitMergeConflictException"/>) is a <see cref="MissionFailureKindEnum.LandingConflict"/>, which an
    /// autonomous rescue can fix. Everything else, including git failing to start because the dock directory is gone, a
    /// dirty developer checkout, unpublished checkout commits or a refused push, is <see cref="MissionFailureKindEnum.Infra"/>.
    /// </summary>
    public static class LandingFailureClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Classify a landing failure.
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <returns>The failure kind to record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when exception is null.</exception>
        public static MissionFailureKindEnum Classify(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (IsProcessStartFailure(exception)) return MissionFailureKindEnum.Infra;
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is GitMergeConflictException) return MissionFailureKindEnum.LandingConflict;
            }
            return MissionFailureKindEnum.Infra;
        }

        /// <summary>
        /// Whether a failure is a process that could not start (missing working directory, missing executable) rather than
        /// a git command that ran and failed.
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
    }
}
