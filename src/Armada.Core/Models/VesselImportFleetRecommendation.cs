namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One fleet a captain recommended for the vessels of an import batch. Persisted as a row with its vessel links as
    /// child rows; recommendations are replaced as a whole each time categorization runs.
    /// </summary>
    public class VesselImportFleetRecommendation
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (vfr_ prefix). Defaults to a new identifier; a create backfills an empty value.
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? String.Empty;
        }

        /// <summary>
        /// Owning tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Import batch identifier (vib_ prefix) the recommendation belongs to. Never null.
        /// </summary>
        public string BatchId
        {
            get => _BatchId;
            set => _BatchId = value ?? String.Empty;
        }

        /// <summary>
        /// Recommended fleet name. Never null; trimmed and limited to 256 characters.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = Clip(value, 256) ?? String.Empty;
        }

        /// <summary>
        /// Short description of what the fleet holds, or null. Limited to 4096 characters.
        /// </summary>
        public string? Description
        {
            get => _Description;
            set => _Description = Clip(value, 4096);
        }

        /// <summary>
        /// The captain's reasoning for grouping these repositories, or null. Limited to 8192 characters.
        /// </summary>
        public string? Rationale
        {
            get => _Rationale;
            set => _Rationale = Clip(value, 8192);
        }

        /// <summary>
        /// Display order within the batch, starting at 0. Negative values are clamped to 0.
        /// </summary>
        public int SortOrder
        {
            get => _SortOrder;
            set => _SortOrder = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Fleet (flt_ prefix) the recommendation was applied to, or null while it has not been applied.
        /// </summary>
        public string? AppliedFleetId { get; set; } = null;

        /// <summary>
        /// Vessel identifiers (vsl_ prefix) assigned to this fleet, in order. Never null.
        /// </summary>
        public List<string> VesselIds
        {
            get => _VesselIds;
            set => _VesselIds = value ?? new List<string>();
        }

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselImportFleetRecommendationIdPrefix, 24);
        private string _BatchId = String.Empty;
        private string _Name = String.Empty;
        private string? _Description = null;
        private string? _Rationale = null;
        private int _SortOrder = 0;
        private List<string> _VesselIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportFleetRecommendation()
        {
        }

        #endregion

        #region Private-Methods

        private static string? Clip(string? value, int max)
        {
            if (value == null) return null;
            string trimmed = value.Trim();
            return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
        }

        #endregion
    }
}
