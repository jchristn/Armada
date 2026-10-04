namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Result of pushing a vessel branch.
    /// </summary>
    public class PushBranchResult
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Branch.
        /// </summary>
        public string Branch { get; set; } = "";

        /// <summary>
        /// True when pushed.
        /// </summary>
        public bool Pushed { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushBranchResult()
        {
        }

        #endregion
    }
}
