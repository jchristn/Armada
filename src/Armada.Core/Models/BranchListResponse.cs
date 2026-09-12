namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Response for a vessel branch listing: the branches with their divergence from the default branch.
    /// </summary>
    public class BranchListResponse
    {
        #region Public-Members

        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// The repository's default branch.
        /// </summary>
        public string DefaultBranch { get; set; } = "main";

        /// <summary>
        /// The branches in the vessel repository.
        /// </summary>
        public List<BranchInfo> Branches { get; set; } = new List<BranchInfo>();

        /// <summary>
        /// Total number of branches.
        /// </summary>
        public int BranchCount { get; set; } = 0;

        /// <summary>
        /// Error message when the listing could not be produced, or null on success.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BranchListResponse()
        {
        }

        #endregion
    }
}
