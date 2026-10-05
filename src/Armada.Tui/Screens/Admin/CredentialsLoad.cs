namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Result of the Credentials screen load: credentials, users, and tenants.
    /// </summary>
    public class CredentialsLoad
    {
        #region Public-Members

        /// <summary>
        /// Credentials.
        /// </summary>
        public List<Credential> Credentials { get; set; } = new List<Credential>();

        /// <summary>
        /// Users.
        /// </summary>
        public List<UserMaster> Users { get; set; } = new List<UserMaster>();

        /// <summary>
        /// Tenants.
        /// </summary>
        public List<TenantMetadata> Tenants { get; set; } = new List<TenantMetadata>();

        #endregion
    }
}
