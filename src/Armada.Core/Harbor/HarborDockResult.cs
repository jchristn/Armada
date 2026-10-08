namespace Armada.Core.Harbor
{
    /// <summary>
    /// Harbor-to-server reply to a <see cref="HarborDockRequest"/>.
    /// </summary>
    public class HarborDockResult : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Request identifier this result answers.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Whether the operation succeeded. For Resolve, whether the Harbor can serve the vessel.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Why the operation failed, written for a person: what is missing and which Harbor setting fixes it. Null on
        /// success.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Where the repository came from.
        /// </summary>
        public HarborRepositorySourceEnum Source { get; set; } = HarborRepositorySourceEnum.None;

        /// <summary>
        /// The repository mission branches live in, on the Harbor host: the user's checkout, or the Harbor's bare clone
        /// (for a Clone source that does not exist yet, where it will be created).
        /// </summary>
        public string? RepositoryPath { get; set; } = null;

        /// <summary>
        /// The user's checkout (working directory) on the Harbor host when the source is Mapped or Discovered; null for
        /// a Clone source.
        /// </summary>
        public string? CheckoutPath { get; set; } = null;

        /// <summary>
        /// The dock's worktree path on the Harbor host (Provision).
        /// </summary>
        public string? WorktreePath { get; set; } = null;

        /// <summary>
        /// The commit the new dock's HEAD points at (Provision).
        /// </summary>
        public string? HeadCommit { get; set; } = null;

        #endregion
    }
}
