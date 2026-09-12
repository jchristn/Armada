namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Describes a single git branch in a vessel's repository, including its position relative to the
    /// repository's default branch. Used by the dashboard branch-management view.
    /// </summary>
    public class BranchInfo
    {
        #region Public-Members

        /// <summary>
        /// Short branch name (for example "main" or "armada/claude-code-1/msn_...").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Whether this branch is the repository's currently checked-out branch (HEAD).
        /// </summary>
        public bool IsCurrent { get; set; } = false;

        /// <summary>
        /// Whether this branch is the repository's default branch.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Short hash of the branch tip commit, or null when unknown.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// Subject line of the branch tip commit, or null when unknown.
        /// </summary>
        public string? CommitSubject { get; set; } = null;

        /// <summary>
        /// Commit date of the branch tip, or null when unknown.
        /// </summary>
        public DateTime? CommitDate { get; set; } = null;

        /// <summary>
        /// Number of commits this branch is ahead of the default branch.
        /// </summary>
        public int Ahead { get; set; } = 0;

        /// <summary>
        /// Number of commits this branch is behind the default branch.
        /// </summary>
        public int Behind { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BranchInfo()
        {
        }

        #endregion
    }
}
