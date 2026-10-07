namespace Armada.Core.Services.Push
{
    using System;

    /// <summary>
    /// A user who should receive a push for an occurrence.
    /// </summary>
    public class PushRecipient
    {
        #region Public-Members

        /// <summary>
        /// Tenant of the user.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// User.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Whether the user is a global admin.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user is a tenant admin.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user owns the entity.
        /// </summary>
        public bool IsOwner { get; set; } = false;

        /// <summary>
        /// Whether the user may act on the item from the notification (approve or deny), so the push carries the
        /// actionable iOS category.
        /// </summary>
        public bool CanAct { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushRecipient()
        {
        }

        #endregion
    }
}
