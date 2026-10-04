namespace Armada.Server
{
    /// <summary>
    /// Outcome of authenticating a Harbor link upgrade: the tenant and user the Harbor registers under, or the reason
    /// the link is refused.
    /// </summary>
    public sealed class HarborLinkIdentity
    {
        #region Public-Members

        /// <summary>
        /// Owning tenant, or null for a shared (local) Harbor.
        /// </summary>
        public string? TenantId { get; private set; } = null;

        /// <summary>
        /// Owning user, or null.
        /// </summary>
        public string? UserId { get; private set; } = null;

        /// <summary>
        /// Reason the link is refused, or null when it is accepted.
        /// </summary>
        public string? DenyReason { get; private set; } = null;

        #endregion

        #region Constructors-and-Factories

        private HarborLinkIdentity()
        {
        }

        /// <summary>
        /// An accepted link.
        /// </summary>
        /// <param name="tenantId">Owning tenant.</param>
        /// <param name="userId">Owning user.</param>
        /// <returns>Identity.</returns>
        public static HarborLinkIdentity Allow(string? tenantId, string? userId)
        {
            return new HarborLinkIdentity { TenantId = tenantId, UserId = userId };
        }

        /// <summary>
        /// A refused link.
        /// </summary>
        /// <param name="reason">Reason sent to the Harbor.</param>
        /// <returns>Identity.</returns>
        public static HarborLinkIdentity Deny(string reason)
        {
            return new HarborLinkIdentity { DenyReason = reason };
        }

        #endregion
    }
}
