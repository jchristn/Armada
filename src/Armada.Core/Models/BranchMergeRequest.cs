namespace Armada.Core.Models
{
    /// <summary>
    /// Request body for merging one branch into another within a vessel's repository.
    /// </summary>
    public class BranchMergeRequest
    {
        #region Public-Members

        /// <summary>
        /// Branch to merge from.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Branch to merge into.
        /// </summary>
        public string? Target { get; set; } = null;

        /// <summary>
        /// Whether to push the target branch to the remote after a successful merge.
        /// </summary>
        public bool Push { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BranchMergeRequest()
        {
        }

        #endregion
    }
}
