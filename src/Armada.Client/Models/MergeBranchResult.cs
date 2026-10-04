namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Result of merging one vessel branch into another.
    /// </summary>
    public class MergeBranchResult
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Source branch.
        /// </summary>
        public string Source { get; set; } = "";

        /// <summary>
        /// Target branch.
        /// </summary>
        public string Target { get; set; } = "";

        /// <summary>
        /// True when merged.
        /// </summary>
        public bool Merged { get; set; } = false;

        /// <summary>
        /// True when pushed after merge.
        /// </summary>
        public bool Pushed { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MergeBranchResult()
        {
        }

        #endregion
    }
}
