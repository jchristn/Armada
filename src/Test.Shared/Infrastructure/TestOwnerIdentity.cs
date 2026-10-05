namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// A tenant and user pair that owns records seeded by a database test.
    /// </summary>
    public sealed class TestOwnerIdentity
    {
        #region Public-Members

        /// <summary>
        /// Owning tenant id.
        /// </summary>
        public string TenantId { get; set; } = "";

        /// <summary>
        /// Owning user id.
        /// </summary>
        public string UserId { get; set; } = "";

        #endregion
    }
}
