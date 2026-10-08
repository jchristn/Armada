namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a mission's dock (a git worktree the Admiral created on its own host) does not exist on the Harbor
    /// chosen to run the mission, and the mission cannot run anywhere else: <c>requireHarborForLaunch</c> keeps it off
    /// the Admiral host, or the Admiral host cannot run the captain's runtime. Docks are provisioned on the Admiral, so a
    /// Harbor can run a mission only when it sees the Admiral's docks directory at the same path. Retrying cannot make the
    /// dock appear on the Harbor, so the mission fails with this message instead of returning to Pending.
    /// </summary>
    public class HarborDockNotFoundException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// The Harbor that does not have the dock.
        /// </summary>
        public string HarborId { get; }

        /// <summary>
        /// The dock's worktree path on the Admiral host.
        /// </summary>
        public string WorktreePath { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harborId">The Harbor that does not have the dock.</param>
        /// <param name="worktreePath">The dock's worktree path on the Admiral host.</param>
        /// <param name="message">Human-readable, actionable message.</param>
        /// <param name="inner">The failure that ended the launch, or null.</param>
        public HarborDockNotFoundException(string harborId, string worktreePath, string message, Exception? inner = null)
            : base(message, inner)
        {
            HarborId = harborId ?? throw new ArgumentNullException(nameof(harborId));
            WorktreePath = worktreePath ?? throw new ArgumentNullException(nameof(worktreePath));
        }

        #endregion
    }
}
