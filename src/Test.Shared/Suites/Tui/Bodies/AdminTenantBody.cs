namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Body of POST /api/v1/tenants and PUT /api/v1/tenants/{id}.
    /// </summary>
    public class AdminTenantBody
    {
        #region Public-Members

        /// <summary>
        /// Tenant name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Active flag.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Password for the seeded tenant admin, or null.
        /// </summary>
        public string? AdminPassword { get; set; } = null;

        #endregion
    }
}
