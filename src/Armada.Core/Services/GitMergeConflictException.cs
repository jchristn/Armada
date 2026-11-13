namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Raised when git reports a real merge conflict while integrating a landing target or a mission branch. Landing maps
    /// only this exception to <see cref="Armada.Core.Enums.MissionFailureKindEnum.LandingConflict"/>; every other landing
    /// failure (git could not start, a missing dock, a refused push) is an environment failure. It is a
    /// <see cref="GitCommandException"/>, so callers that read the failed git command still can.
    /// </summary>
    public class GitMergeConflictException : GitCommandException
    {
        #region Public-Members

        /// <summary>
        /// Files git reported as conflicted.
        /// </summary>
        public IReadOnlyList<string> ConflictedFiles { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">What conflicted.</param>
        /// <param name="conflictedFiles">Conflicted files, or null.</param>
        /// <param name="command">The failed git merge, when known.</param>
        public GitMergeConflictException(string message, IEnumerable<string>? conflictedFiles = null, GitCommandException? command = null)
            : base(command?.Executable ?? "git", command?.Arguments ?? new List<string> { "merge" }, command?.ExitCode ?? 1, command?.StandardOutput, message)
        {
            ConflictedFiles = conflictedFiles == null ? new List<string>() : new List<string>(conflictedFiles);
        }

        #endregion
    }
}
