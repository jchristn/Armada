namespace Armada.Core.Models
{
    /// <summary>
    /// Summary of a working tree's status, as reported by git status --porcelain.
    /// </summary>
    public class GitWorkingTreeStatus
    {
        #region Public-Members

        /// <summary>
        /// Number of tracked files with staged or unstaged modifications (including deletions, renames, and
        /// conflicts). Negative values are clamped to 0.
        /// </summary>
        public int ModifiedCount
        {
            get => _ModifiedCount;
            set => _ModifiedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of untracked files. Negative values are clamped to 0.
        /// </summary>
        public int UntrackedCount
        {
            get => _UntrackedCount;
            set => _UntrackedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// True when there are no modified tracked files and no untracked files.
        /// </summary>
        public bool IsClean => _ModifiedCount == 0 && _UntrackedCount == 0;

        #endregion

        #region Private-Members

        private int _ModifiedCount = 0;
        private int _UntrackedCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GitWorkingTreeStatus()
        {
        }

        #endregion
    }
}
