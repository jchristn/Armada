namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One criterion result for one vessel. Findings carry stable codes and typed values (not
    /// rendered sentences) so clients can localize them, for example DetailCode = OutdatedPackages, ValueA = 7,
    /// ValueB = 2. Findings hold the RAW evaluated status (before manual overrides).
    /// </summary>
    public class VesselHealthFinding
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (vhf_ prefix). Defaults to a new identifier; a write backfills an empty value.
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? String.Empty;
        }

        /// <summary>
        /// Owning tenant identifier. Every row carries a tenant; reads and enumerations are scoped by it.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Vessel identifier. Never null.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set => _VesselId = value ?? String.Empty;
        }

        /// <summary>
        /// Criterion this finding grades.
        /// </summary>
        public VesselHealthCriterionEnum Criterion { get; set; } = VesselHealthCriterionEnum.Overall;

        /// <summary>
        /// Raw evaluated status. Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum Status { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Stable detail code, or null.
        /// </summary>
        public string? DetailCode { get; set; } = null;

        /// <summary>
        /// First typed value for the detail code, or null.
        /// </summary>
        public long? ValueA { get; set; } = null;

        /// <summary>
        /// Second typed value for the detail code, or null.
        /// </summary>
        public long? ValueB { get; set; } = null;

        /// <summary>
        /// Timestamp in UTC when the finding was evaluated.
        /// </summary>
        public DateTime EvaluatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselHealthFindingIdPrefix, 24);
        private string _VesselId = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthFinding()
        {
        }

        #endregion
    }
}
