namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Filters for listing push devices (oldest first). Every filter is optional.
    /// </summary>
    public class PushDeviceQuery
    {
        #region Public-Members

        /// <summary>
        /// Tenant, or null for every tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User, or null for every user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// When true, only active devices are listed.
        /// </summary>
        public bool ActiveOnly { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushDeviceQuery()
        {
        }

        #endregion
    }
}
