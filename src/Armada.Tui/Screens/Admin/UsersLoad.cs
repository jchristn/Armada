namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Result of the Users screen load: users and the tenants used for names, filters, and the form.
    /// </summary>
    public class UsersLoad
    {
        #region Public-Members

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
