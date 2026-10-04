namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Branches of a vessel.
    /// </summary>
    public class VesselBranchesResult
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Default branch, or null.
        /// </summary>
        public string? DefaultBranch { get; set; } = null;

        /// <summary>
        /// Branches. Never null.
        /// </summary>
        public List<Armada.Core.Models.BranchInfo> Branches { get; set; } = new List<Armada.Core.Models.BranchInfo>();

        /// <summary>
        /// Branch count.
        /// </summary>
        public int BranchCount { get; set; } = 0;

        /// <summary>
        /// Error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselBranchesResult()
        {
        }

        #endregion
    }
}
