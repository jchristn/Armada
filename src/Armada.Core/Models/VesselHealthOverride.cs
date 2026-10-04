namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A manual status override for one criterion of one vessel, with an optional note. Unique per
    /// tenant, vessel, and criterion. The evaluator applies overrides when computing the effective statuses stored on
    /// <see cref="VesselHealth"/>.
    /// </summary>
    public class VesselHealthOverride
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (vho_ prefix). Defaults to a new identifier; an upsert keeps the existing identifier for the same tenant, vessel, and criterion.
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
        /// Identifier of the user who set the override.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Criterion being overridden.
        /// </summary>
        public VesselHealthCriterionEnum Criterion { get; set; } = VesselHealthCriterionEnum.Overall;

        /// <summary>
        /// Overriding status. Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum Status { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Operator note explaining the override, or null.
        /// </summary>
        public string? Note { get; set; } = null;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselHealthOverrideIdPrefix, 24);
        private string _VesselId = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthOverride()
        {
        }

        #endregion
    }
}
