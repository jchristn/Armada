namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Filters for listing CLI permission requests (newest first). Every filter is optional.
    /// </summary>
    public class CliPermissionRequestQuery
    {
        #region Public-Members

        /// <summary>
        /// Tenant, or null for every tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owner (thread or mission owner), or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Status, or null.
        /// </summary>
        public CliPermissionRequestStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Mission, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Ask thread, or null.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Vessel, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Maximum rows. Default 100, minimum 1, maximum 1000.
        /// </summary>
        public int Limit
        {
            get => _Limit;
            set => _Limit = value < 1 ? 1 : (value > 1000 ? 1000 : value);
        }

        #endregion

        #region Private-Members

        private int _Limit = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionRequestQuery()
        {
        }

        #endregion
    }
}
