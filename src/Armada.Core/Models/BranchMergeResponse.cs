namespace Armada.Core.Models
{
    /// <summary>
    /// Response for a branch merge action on a vessel repository.
    /// </summary>
    public class BranchMergeResponse
    {
        #region Public-Members

        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// The branch that was merged from.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// The branch that was merged into.
        /// </summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>
        /// Whether the merge completed.
        /// </summary>
        public bool Merged { get; set; } = false;

        /// <summary>
        /// Whether the target branch was pushed after the merge.
        /// </summary>
        public bool Pushed { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BranchMergeResponse()
        {
        }

        #endregion
    }
}
