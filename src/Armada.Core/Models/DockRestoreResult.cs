namespace Armada.Core.Models
{
    /// <summary>
    /// Outcome of recreating a dock's missing worktree from its branch on the dock's own host. Also the payload of the
    /// dock.repaired event.
    /// </summary>
    public class DockRestoreResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the worktree exists again.
        /// </summary>
        public bool Restored { get; set; } = false;

        /// <summary>
        /// Dock identifier.
        /// </summary>
        public string DockId { get; set; } = "";

        /// <summary>
        /// Harbor that hosts the dock, or null for the Admiral's host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Worktree path on the dock's host.
        /// </summary>
        public string? WorktreePath { get; set; } = null;

        /// <summary>
        /// Repository the worktree was recreated from.
        /// </summary>
        public string? RepositoryPath { get; set; } = null;

        /// <summary>
        /// Branch the worktree was recreated on.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// HEAD of the recreated worktree.
        /// </summary>
        public string? HeadCommit { get; set; } = null;

        /// <summary>
        /// What asked for the repair (for example a landing or the integrity sweep).
        /// </summary>
        public string? Trigger { get; set; } = null;

        /// <summary>
        /// Why the worktree could not be recreated, or null when it was.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A failed restore.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="reason">Why it failed.</param>
        /// <returns>The result.</returns>
        public static DockRestoreResult Failed(string dockId, string reason)
        {
            return new DockRestoreResult { DockId = dockId ?? "", Restored = false, Reason = reason };
        }

        #endregion
    }
}
