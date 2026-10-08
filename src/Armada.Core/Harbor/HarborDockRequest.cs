namespace Armada.Core.Harbor
{
    /// <summary>
    /// Server-to-Harbor request to resolve, create, or remove a mission dock (git worktree) on the Harbor host. The
    /// Harbor owns where repositories and docks live on its machine: it maps the vessel to a checkout (its settings) or
    /// a clone of its own, and creates docks under its own docks directory. Answered by <see cref="HarborDockResult"/>.
    /// </summary>
    public class HarborDockRequest : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Request identifier tying the result back to this call.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// The operation to perform.
        /// </summary>
        public HarborDockOperationEnum Operation { get; set; } = HarborDockOperationEnum.Resolve;

        /// <summary>
        /// Vessel identifier (vsl_ prefix). Harbor settings may name a vessel by its identifier or its name.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string VesselName { get; set; } = string.Empty;

        /// <summary>
        /// The vessel's repository URL, matched against the remotes of checkouts under the Harbor's root folders and
        /// cloned when the Harbor has no checkout. Null when the vessel has none.
        /// </summary>
        public string? RepoUrl { get; set; } = null;

        /// <summary>
        /// The vessel's default branch; new mission branches start from it (Provision).
        /// </summary>
        public string DefaultBranch { get; set; } = "main";

        /// <summary>
        /// The mission branch the dock checks out (Provision). An existing branch is reused.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Name of the dock directory under the vessel's docks directory, normally the mission identifier (Provision).
        /// </summary>
        public string? DockName { get; set; } = null;

        /// <summary>
        /// The dock's worktree path on the Harbor host (Reclaim).
        /// </summary>
        public string? WorktreePath { get; set; } = null;

        /// <summary>
        /// The repository the worktree belongs to, on the Harbor host (Reclaim), so its worktree list can be pruned.
        /// </summary>
        public string? RepositoryPath { get; set; } = null;

        #endregion
    }
}
