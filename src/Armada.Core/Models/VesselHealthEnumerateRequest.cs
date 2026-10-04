namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// Typed filter, sort, and paging request for vessel health enumeration. Filtering, sorting, and paging all run
    /// in SQL against indexed columns; sorting is restricted to the whitelisted <see cref="VesselHealthSortEnum"/>
    /// columns. Every active vessel in the tenant is a candidate row, including vessels that have never been
    /// evaluated (those report Unknown statuses, so they match an Unknown status filter).
    /// </summary>
    public class VesselHealthEnumerateRequest
    {
        #region Public-Members

        /// <summary>
        /// Page number, starting from 1. Default 1; values below 1 are clamped to 1.
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Number of rows per page. Default 25, minimum 1, maximum 500; out-of-range values are clamped.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = value < 1 ? 1 : (value > 500 ? 500 : value);
        }

        /// <summary>
        /// Sort column. Default VesselName. Status columns sort by rank (Fail, Warn, Pass, then Unknown and
        /// NotApplicable lowest); null measurements always sort last. Ties break on vessel name, then identifier.
        /// </summary>
        public VesselHealthSortEnum SortBy { get; set; } = VesselHealthSortEnum.VesselName;

        /// <summary>
        /// Whether to sort descending. Default false (ascending).
        /// </summary>
        public bool SortDescending { get; set; } = false;

        /// <summary>
        /// Case-insensitive substring filter on the vessel name, or null for no filter.
        /// </summary>
        public string? NameContains { get; set; } = null;

        /// <summary>
        /// Fleet identifier filter, or null for no filter.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Case-insensitive exact primary-language filter, or null for no filter.
        /// </summary>
        public string? PrimaryLanguage { get; set; } = null;

        /// <summary>
        /// Case-insensitive substring filter on the current branch, or null for no filter.
        /// </summary>
        public string? CurrentBranchContains { get; set; } = null;

        /// <summary>
        /// Overall status filter (any of). Empty means no filter. Never null.
        /// </summary>
        public List<VesselHealthStatusEnum> OverallStatus
        {
            get => _OverallStatus;
            set => _OverallStatus = value ?? new List<VesselHealthStatusEnum>();
        }

        /// <summary>
        /// Dependency status filter (any of). Empty means no filter. Never null.
        /// </summary>
        public List<VesselHealthStatusEnum> DependencyStatus
        {
            get => _DependencyStatus;
            set => _DependencyStatus = value ?? new List<VesselHealthStatusEnum>();
        }

        /// <summary>
        /// Test infrastructure status filter (any of). Empty means no filter. Never null.
        /// </summary>
        public List<VesselHealthStatusEnum> TestInfraStatus
        {
            get => _TestInfraStatus;
            set => _TestInfraStatus = value ?? new List<VesselHealthStatusEnum>();
        }

        /// <summary>
        /// Dirty working tree filter, or null for no filter.
        /// </summary>
        public bool? IsDirty { get; set; } = null;

        /// <summary>
        /// Continuous integration configuration presence filter, or null for no filter.
        /// </summary>
        public bool? HasCiConfig { get; set; } = null;

        /// <summary>
        /// Divergence filter relative to the default branch, or null for no filter.
        /// </summary>
        public VesselDivergenceFilterEnum? Divergence { get; set; } = null;

        /// <summary>
        /// Minimum branch count (inclusive), or null for no lower bound. Negative values are clamped to 0.
        /// </summary>
        public int? MinBranchCount
        {
            get => _MinBranchCount;
            set => _MinBranchCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Maximum branch count (inclusive), or null for no upper bound. Negative values are clamped to 0.
        /// </summary>
        public int? MaxBranchCount
        {
            get => _MaxBranchCount;
            set => _MaxBranchCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Only include vessels whose last commit is after this UTC timestamp (exclusive), or null.
        /// </summary>
        public DateTime? LastCommitAfterUtc { get; set; } = null;

        /// <summary>
        /// Only include vessels whose last commit is before this UTC timestamp (exclusive), or null.
        /// </summary>
        public DateTime? LastCommitBeforeUtc { get; set; } = null;

        /// <summary>
        /// Whether to include inactive vessels. Default false.
        /// </summary>
        public bool IncludeInactive { get; set; } = false;

        /// <summary>
        /// Row offset for the requested page.
        /// </summary>
        [JsonIgnore]
        public int Offset => (PageNumber - 1) * PageSize;

        #endregion

        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 25;
        private int? _MinBranchCount = null;
        private int? _MaxBranchCount = null;
        private List<VesselHealthStatusEnum> _OverallStatus = new List<VesselHealthStatusEnum>();
        private List<VesselHealthStatusEnum> _DependencyStatus = new List<VesselHealthStatusEnum>();
        private List<VesselHealthStatusEnum> _TestInfraStatus = new List<VesselHealthStatusEnum>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public VesselHealthEnumerateRequest()
        {
        }

        #endregion
    }
}
