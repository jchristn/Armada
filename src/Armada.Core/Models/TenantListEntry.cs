namespace Armada.Core.Models
{
    /// <summary>
    /// Lightweight tenant reference for lookup results.
    /// </summary>
    public class TenantListEntry
    {
        #region Public-Members

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Tenant name.
        /// </summary>
        public string Name { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TenantListEntry()
        {
        }

        /// <summary>
        /// Instantiate with id and name.
        /// </summary>
        /// <param name="id">Tenant identifier.</param>
        /// <param name="name">Tenant name.</param>
        public TenantListEntry(string id, string name)
        {
            Id = id;
            Name = name;
        }

        #endregion
    }
}
