namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of POST /api/v1/tenants/lookup.
    /// </summary>
    public class TenantLookupResult
    {
        #region Public-Members

        /// <summary>
        /// List of tenants matching the email (empty if not found).
        /// </summary>
        public List<TenantListEntry> Tenants { get; set; } = new List<TenantListEntry>();

        #endregion
    }
}
