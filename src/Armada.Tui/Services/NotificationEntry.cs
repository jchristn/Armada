namespace Armada.Tui.Services
{
    using System;

    /// <summary>
    /// One persisted notification (the dashboard's notification history entry). Stores the semantic parts (asset type,
    /// name, status) so it can be re-rendered in another locale, plus the rendered English title and message.
    /// </summary>
    public class NotificationEntry
    {
        #region Public-Members

        /// <summary>
        /// Id.
        /// </summary>
        public string Id { get; set; } = "ntf_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>
        /// Severity.
        /// </summary>
        public NotificationSeverityEnum Severity { get; set; } = NotificationSeverityEnum.Info;

        /// <summary>
        /// Asset type (Mission, Voyage, Captain, Deployment, Objective, Incident), or null for free-form notices.
        /// </summary>
        public string? AssetType { get; set; } = null;

        /// <summary>
        /// Entity name or title.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Title (English source).
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Message (English source).
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Route opened by Enter, or null.
        /// </summary>
        public string? Route { get; set; } = null;

        /// <summary>
        /// Read state.
        /// </summary>
        public bool Read { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public NotificationEntry()
        {
        }

        #endregion
    }
}
