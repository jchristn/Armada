namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Full health of one vessel: the health row (effective statuses), the raw findings, the outdated or vulnerable
    /// dependencies, and the manual overrides. For a vessel never evaluated, Health has a null Id and Unknown statuses
    /// and the lists are empty.
    /// </summary>
    public class VesselHealthDetail
    {
        #region Public-Members

        /// <summary>
        /// Health row. Never null.
        /// </summary>
        public VesselHealth Health { get; set; } = new VesselHealth();

        /// <summary>
        /// Raw findings, one per criterion. Never null.
        /// </summary>
        public List<VesselHealthFinding> Findings { get; set; } = new List<VesselHealthFinding>();

        /// <summary>
        /// Outdated or vulnerable dependencies. Never null.
        /// </summary>
        public List<VesselDependency> Dependencies { get; set; } = new List<VesselDependency>();

        /// <summary>
        /// Manual overrides. Never null.
        /// </summary>
        public List<VesselHealthOverride> Overrides { get; set; } = new List<VesselHealthOverride>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthDetail()
        {
        }

        #endregion
    }
}
