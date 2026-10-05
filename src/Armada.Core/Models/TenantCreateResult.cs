namespace Armada.Core.Models
{
    /// <summary>
    /// Response of <c>POST /api/v1/tenants</c>: the created tenant plus the seeded tenant admin's sign-in. The admin
    /// password is returned only here, only once, and only when the server generated it.
    /// </summary>
    public class TenantCreateResult : TenantMetadata
    {
        #region Public-Members

        /// <summary>
        /// Email of the seeded tenant admin account.
        /// </summary>
        public string? AdminEmail { get; set; } = null;

        /// <summary>
        /// The generated password of the seeded tenant admin (shown once), or null when the creator supplied one.
        /// </summary>
        public string? AdminPassword { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TenantCreateResult()
        {
        }

        /// <summary>
        /// Copy a tenant into a result.
        /// </summary>
        /// <param name="tenant">Created tenant.</param>
        /// <returns>Result.</returns>
        public static TenantCreateResult From(TenantMetadata tenant)
        {
            TenantCreateResult result = new TenantCreateResult();
            result.Id = tenant.Id;
            result.Name = tenant.Name;
            result.Active = tenant.Active;
            result.IsProtected = tenant.IsProtected;
            result.CreatedUtc = tenant.CreatedUtc;
            result.LastUpdateUtc = tenant.LastUpdateUtc;
            return result;
        }

        #endregion
    }
}
