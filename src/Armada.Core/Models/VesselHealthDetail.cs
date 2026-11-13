namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Full health of one vessel: the health row (effective statuses), the raw findings, the number of outdated or
    /// vulnerable dependencies, and the manual overrides. The dependency rows are not embedded; page them with
    /// GET /api/v1/vessels/{id}/health/dependencies (POST .../enumerate) or MCP list_vessel_dependencies. For a vessel
    /// never evaluated, Health has a null Id and Unknown statuses and the lists are empty.
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
        [CollectionBound(16, Reason = "One per VesselHealthCriterionEnum value (11), replaced on each evaluation")]
        public List<VesselHealthFinding> Findings { get; set; } = new List<VesselHealthFinding>();

        /// <summary>
        /// Number of stored outdated or vulnerable dependency rows (the TotalRecords of the dependencies enumeration).
        /// </summary>
        public long DependencyCount { get; set; } = 0;

        /// <summary>
        /// Manual overrides. Never null.
        /// </summary>
        [CollectionBound(16, Reason = "Unique per vessel and criterion (unique index); one per VesselHealthCriterionEnum value")]
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
