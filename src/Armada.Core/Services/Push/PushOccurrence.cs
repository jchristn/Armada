namespace Armada.Core.Services.Push
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Something that may need a user, turned into pushes by <see cref="PushNotificationService"/>: its category and
    /// kind, the entity, the tenant and owner used to pick recipients, and the already-sanitized title, body, and deep
    /// link.
    /// </summary>
    public class PushOccurrence
    {
        #region Public-Members

        /// <summary>
        /// Category.
        /// </summary>
        public PushCategoryEnum Category { get; set; } = PushCategoryEnum.MissionFailed;

        /// <summary>
        /// Kind (see <see cref="PushNotificationKinds"/>).
        /// </summary>
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// Entity identifier.
        /// </summary>
        public string EntityId { get; set; } = String.Empty;

        /// <summary>
        /// Tenant of the entity. Occurrences without a tenant reach nobody.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user of the entity (mission, voyage, captain, deployment, thread, or permission request owner).
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Title (sanitized and truncated).
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body (sanitized and truncated).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Deep link for the owner (dashboard-style path).
        /// </summary>
        public string Url { get; set; } = "/";

        /// <summary>
        /// Deep link for recipients other than the owner, or null to use <see cref="Url"/> (for example a thread CLI
        /// permission request opens the thread for its owner and the CLI permissions page for admins).
        /// </summary>
        public string? ApproverUrl { get; set; } = null;

        /// <summary>
        /// Ask thread, for Ask proposals and thread CLI permission requests.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// The permission request, for CLI permission occurrences (who may decide is computed per recipient).
        /// </summary>
        public CliPermissionRequest? CliPermission { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushOccurrence()
        {
        }

        #endregion
    }
}
