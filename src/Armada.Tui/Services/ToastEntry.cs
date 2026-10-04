namespace Armada.Tui.Services
{
    using System;

    /// <summary>
    /// A transient toast (5 s by default) with an optional action key (for example <c>[Ctrl+O] Open</c>).
    /// </summary>
    public class ToastEntry
    {
        #region Public-Members

        /// <summary>
        /// Id.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Severity.
        /// </summary>
        public NotificationSeverityEnum Severity { get; set; } = NotificationSeverityEnum.Info;

        /// <summary>
        /// Text (already translated).
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Expiry time.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddSeconds(5);

        /// <summary>
        /// English label of the action, or null.
        /// </summary>
        public string? ActionLabel { get; set; } = null;

        /// <summary>
        /// Action, or null.
        /// </summary>
        public Action? Action { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ToastEntry()
        {
        }

        #endregion
    }
}
