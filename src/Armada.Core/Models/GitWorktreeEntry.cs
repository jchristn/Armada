namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One record of git worktree list --porcelain.
    /// </summary>
    public class GitWorktreeEntry
    {
        #region Public-Members

        /// <summary>
        /// Absolute worktree path as git reports it.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// HEAD commit, when reported.
        /// </summary>
        public string? Head { get; set; } = null;

        /// <summary>
        /// Full branch ref (for example refs/heads/main), or null when detached or bare.
        /// </summary>
        public string? BranchRef { get; set; } = null;

        /// <summary>
        /// True for the bare repository entry.
        /// </summary>
        public bool IsBare { get; set; } = false;

        /// <summary>
        /// True when HEAD is detached.
        /// </summary>
        public bool IsDetached { get; set; } = false;

        #endregion
    }
}
