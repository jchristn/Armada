namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One vessel import request: the discovery results an operator reviews, and later the outcome of
    /// importing the candidates they kept. Candidates are persisted as <see cref="VesselImportItem"/> child rows.
    /// </summary>
    public class VesselImportBatch
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (vib_ prefix). Defaults to a new identifier; a create backfills an empty value.
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
        /// Identifier of the user who requested discovery.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Lifecycle status. Defaults to Discovered.
        /// </summary>
        public VesselImportBatchStatusEnum Status { get; set; } = VesselImportBatchStatusEnum.Discovered;

        /// <summary>
        /// Harbor that performed discovery, or null when discovery ran on the Admiral host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Fleet that imported vessels are assigned to, or null for none.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Background job identifier when the import ran as a job, or null when it ran inline.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// Number of directories and roots the operator supplied. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int RequestedPathCount
        {
            get => _RequestedPathCount;
            set => _RequestedPathCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of candidates discovery produced. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int CandidateCount
        {
            get => _CandidateCount;
            set => _CandidateCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of vessels created by the import. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int CreatedCount
        {
            get => _CreatedCount;
            set => _CreatedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of candidates skipped by the import. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int SkippedCount
        {
            get => _SkippedCount;
            set => _SkippedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of candidates that failed to import. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int FailedCount
        {
            get => _FailedCount;
            set => _FailedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp in UTC when the import finished, or null while it has not.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselImportBatchIdPrefix, 24);
        private int _RequestedPathCount = 0;
        private int _CandidateCount = 0;
        private int _CreatedCount = 0;
        private int _SkippedCount = 0;
        private int _FailedCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportBatch()
        {
        }

        #endregion
    }
}
