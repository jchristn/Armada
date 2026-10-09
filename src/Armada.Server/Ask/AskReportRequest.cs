namespace Armada.Server.Ask
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// A request to report the outcome of finished tracked work in its Ask Armada thread: the work, its final snapshot and
    /// result, and the final milestone message that was posted for it.
    /// </summary>
    public class AskReportRequest
    {
        #region Public-Members

        /// <summary>
        /// Thread the work belongs to.
        /// </summary>
        public AskThread Thread { get; }

        /// <summary>
        /// The finished tracked work.
        /// </summary>
        public AskTrackedWork Work { get; }

        /// <summary>
        /// The work's final snapshot.
        /// </summary>
        public AskWorkSnapshot Snapshot { get; }

        /// <summary>
        /// The work's result, or null when it could not be built.
        /// </summary>
        public AskWorkResult? Result { get; }

        /// <summary>
        /// Sequence of the final milestone message; a user message after it means the user already asked.
        /// </summary>
        public int MilestoneSequence { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="work">Tracked work.</param>
        /// <param name="snapshot">Final snapshot.</param>
        /// <param name="result">Result, or null.</param>
        /// <param name="milestoneSequence">Sequence of the final milestone message.</param>
        /// <exception cref="ArgumentNullException">Thrown when thread, work, or snapshot is null.</exception>
        public AskReportRequest(AskThread thread, AskTrackedWork work, AskWorkSnapshot snapshot, AskWorkResult? result, int milestoneSequence)
        {
            Thread = thread ?? throw new ArgumentNullException(nameof(thread));
            Work = work ?? throw new ArgumentNullException(nameof(work));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Result = result;
            MilestoneSequence = milestoneSequence;
        }

        #endregion
    }
}
