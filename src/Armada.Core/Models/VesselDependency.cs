namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One outdated or vulnerable package dependency of a vessel.
    /// </summary>
    public class VesselDependency
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (vdp_ prefix). Defaults to a new identifier; a write backfills an empty value.
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
        /// Package ecosystem (for example NuGet or npm). Never null.
        /// </summary>
        public string Ecosystem
        {
            get => _Ecosystem;
            set => _Ecosystem = value ?? String.Empty;
        }

        /// <summary>
        /// Project or manifest path relative to the repository root, or null.
        /// </summary>
        public string? ProjectPath { get; set; } = null;

        /// <summary>
        /// Package name. Never null.
        /// </summary>
        public string PackageName
        {
            get => _PackageName;
            set => _PackageName = value ?? String.Empty;
        }

        /// <summary>
        /// Currently referenced version, or null.
        /// </summary>
        public string? CurrentVersion { get; set; } = null;

        /// <summary>
        /// Latest available version, or null.
        /// </summary>
        public string? LatestVersion { get; set; } = null;

        /// <summary>
        /// Version drift. Defaults to None.
        /// </summary>
        public DependencyDriftEnum Drift { get; set; } = DependencyDriftEnum.None;

        /// <summary>
        /// Whether the referenced version has a known vulnerability.
        /// </summary>
        public bool IsVulnerable { get; set; } = false;

        /// <summary>
        /// Highest vulnerability severity. Defaults to None.
        /// </summary>
        public VulnerabilitySeverityEnum Severity { get; set; } = VulnerabilitySeverityEnum.None;

        /// <summary>
        /// Advisory URL for the vulnerability, or null.
        /// </summary>
        public string? AdvisoryUrl { get; set; } = null;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselDependencyIdPrefix, 24);
        private string _VesselId = String.Empty;
        private string _Ecosystem = String.Empty;
        private string _PackageName = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselDependency()
        {
        }

        #endregion
    }
}
