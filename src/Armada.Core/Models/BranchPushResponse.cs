namespace Armada.Core.Models
{
    /// <summary>
    /// Response for a branch push action on a vessel repository.
    /// </summary>
    public class BranchPushResponse
    {
        #region Public-Members

        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// The branch that was pushed.
        /// </summary>
        public string Branch { get; set; } = string.Empty;

        /// <summary>
        /// Whether the push completed.
        /// </summary>
        public bool Pushed { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BranchPushResponse()
        {
        }

        #endregion
    }
}
