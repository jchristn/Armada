namespace Armada.Client.Models
{
    using Armada.Core.Models;

    /// <summary>
    /// Body of <c>POST /api/v1/tenants</c>: the tenant plus an optional password for its seeded tenant admin
    /// (<c>admin@armada</c>). When <see cref="AdminPassword"/> is null the server generates one and returns it once in
    /// <see cref="TenantCreateResult.AdminPassword"/>.
    /// </summary>
    public class TenantCreateRequest : TenantMetadata
    {
        #region Public-Members

        /// <summary>
        /// Password for the seeded tenant admin (at least 8 characters, not the default password), or null to have the
        /// server generate one.
        /// </summary>
        public string? AdminPassword { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TenantCreateRequest()
        {
        }

        /// <summary>
        /// Instantiate with a name.
        /// </summary>
        /// <param name="name">Tenant name.</param>
        public TenantCreateRequest(string name)
            : base(name)
        {
        }

        #endregion
    }
}
