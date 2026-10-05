namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Body of POST /api/v1/credentials.
    /// </summary>
    public class AdminCredentialBody
    {
        #region Public-Members

        /// <summary>
        /// Credential name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// User id.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Tenant id.
        /// </summary>
        public string? TenantId { get; set; } = null;

        #endregion
    }
}
