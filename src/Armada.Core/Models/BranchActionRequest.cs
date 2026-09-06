namespace Armada.Core.Models
{
    /// <summary>
    /// Request body for a single-branch action (for example push) on a vessel's repository.
    /// </summary>
    public class BranchActionRequest
    {
        #region Public-Members

        /// <summary>
        /// Branch name to act on.
        /// </summary>
        public string? Branch { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BranchActionRequest()
        {
        }

        #endregion
    }
}
