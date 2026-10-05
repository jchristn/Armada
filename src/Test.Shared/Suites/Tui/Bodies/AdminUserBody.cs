namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Body of POST /api/v1/users and PUT /api/v1/users/{id}.
    /// </summary>
    public class AdminUserBody
    {
        #region Public-Members

        /// <summary>
        /// Email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Password.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// First name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Tenant id.
        /// </summary>
        public string? TenantId { get; set; } = null;

        #endregion
    }
}
