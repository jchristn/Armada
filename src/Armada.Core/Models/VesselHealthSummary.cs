namespace Armada.Core.Models
{
    /// <summary>
    /// Tenant-wide vessel health counts for KPI tiles. Counts cover active vessels only and are based on the
    /// effective (override-aware) statuses stored on each vessel's health row, so they always agree with the
    /// health table.
    /// </summary>
    public class VesselHealthSummary
    {
        #region Public-Members

        /// <summary>
        /// Total number of active vessels in the tenant.
        /// </summary>
        public long TotalVessels { get; set; } = 0;

        /// <summary>
        /// Number of evaluated vessels whose overall status is Pass.
        /// </summary>
        public long Pass { get; set; } = 0;

        /// <summary>
        /// Number of evaluated vessels whose overall status is Warn.
        /// </summary>
        public long Warn { get; set; } = 0;

        /// <summary>
        /// Number of evaluated vessels whose overall status is Fail.
        /// </summary>
        public long Fail { get; set; } = 0;

        /// <summary>
        /// Number of evaluated vessels whose overall status is Unknown.
        /// </summary>
        public long Unknown { get; set; } = 0;

        /// <summary>
        /// Number of evaluated vessels whose overall status is NotApplicable.
        /// </summary>
        public long NotApplicable { get; set; } = 0;

        /// <summary>
        /// Number of active vessels that have never been evaluated (no health row).
        /// </summary>
        public long NotEvaluated { get; set; } = 0;

        /// <summary>
        /// Number of vessels with at least one dependency at major version drift.
        /// </summary>
        public long OutdatedMajorVessels { get; set; } = 0;

        /// <summary>
        /// Number of vessels whose maximum vulnerability severity is High or Critical.
        /// </summary>
        public long HighOrCriticalVulnerabilityVessels { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthSummary()
        {
        }

        #endregion
    }
}
