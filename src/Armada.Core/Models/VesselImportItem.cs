namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One candidate path within a <see cref="VesselImportBatch"/>, with its discovery classification and,
    /// after import, its outcome. Items are history: deleting a vessel created from an item does not delete the item.
    /// </summary>
    public class VesselImportItem
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (vii_ prefix). Defaults to a new identifier; a create backfills an empty value.
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
        /// Owning import batch identifier (vib_ prefix).
        /// </summary>
        public string? BatchId { get; set; } = null;

        /// <summary>
        /// Normalized absolute path of the candidate directory. Never null.
        /// </summary>
        public string Path
        {
            get => _Path;
            set => _Path = value ?? String.Empty;
        }

        /// <summary>
        /// Proposed vessel name, made unique within the tenant. Never null.
        /// </summary>
        public string ProposedName
        {
            get => _ProposedName;
            set => _ProposedName = value ?? String.Empty;
        }

        /// <summary>
        /// Origin remote URL, or null when the repository has no origin.
        /// </summary>
        public string? RemoteUrl { get; set; } = null;

        /// <summary>
        /// Inferred default branch, or null when unknown.
        /// </summary>
        public string? DefaultBranch { get; set; } = null;

        /// <summary>
        /// Discovery classification. Defaults to New.
        /// </summary>
        public VesselImportCandidateStatusEnum CandidateStatus { get; set; } = VesselImportCandidateStatusEnum.New;

        /// <summary>
        /// Identifier of an existing vessel that matches this candidate, or null.
        /// </summary>
        public string? ExistingVesselId { get; set; } = null;

        /// <summary>
        /// Import outcome. Defaults to Pending.
        /// </summary>
        public VesselImportOutcomeEnum Outcome { get; set; } = VesselImportOutcomeEnum.Pending;

        /// <summary>
        /// Stable reason code for the outcome (for example a failure code), or null. Clients localize it.
        /// </summary>
        public string? OutcomeReason { get; set; } = null;

        /// <summary>
        /// Diagnostic message for the outcome, or null.
        /// </summary>
        public string? OutcomeMessage { get; set; } = null;

        /// <summary>
        /// Identifier of the vessel created for this candidate, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselImportItemIdPrefix, 24);
        private string _Path = String.Empty;
        private string _ProposedName = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportItem()
        {
        }

        #endregion
    }
}
