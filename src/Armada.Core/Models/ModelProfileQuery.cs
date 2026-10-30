namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Filters and paging for listing model profiles.
    /// </summary>
    public class ModelProfileQuery
    {
        #region Public-Members

        /// <summary>
        /// Only profiles of this tenant, or null for every tenant (global admins only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Only profiles of this runtime (case-insensitive), or null.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Only profiles of this provider (case-insensitive), or null.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Only profiles whose model name contains this text (case-insensitive), or null.
        /// </summary>
        public string? ModelContains { get; set; } = null;

        /// <summary>
        /// 1-based page number. Values below 1 become 1.
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Page size, 1 to 1000. Out-of-range values are clamped.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = value < 1 ? 1 : (value > 1000 ? 1000 : value);
        }

        #endregion

        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileQuery()
        {
        }

        #endregion
    }
}
