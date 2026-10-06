namespace Armada.Tui.Services
{
    using System;

    /// <summary>
    /// A toast as Armada draws it (5 s by default, with an optional action key such as <c>[Ctrl+O] Open</c>): a read-only
    /// view of one active TUIKit <c>Notification</c> in <see cref="NotificationService.Toasts"/>, taken when
    /// <see cref="NotificationService.ActiveToasts"/> is called.
    /// </summary>
    public class ToastEntry
    {
        #region Public-Members

        /// <summary>
        /// Id (the TUIKit notification id).
        /// </summary>
        public long Id { get; }

        /// <summary>
        /// Severity.
        /// </summary>
        public NotificationSeverityEnum Severity { get; }

        /// <summary>
        /// Text (already translated).
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Expiry time (the last raise plus the timeout).
        /// </summary>
        public DateTime ExpiresUtc { get; }

        /// <summary>
        /// English label of the action, or null.
        /// </summary>
        public string? ActionLabel { get; }

        /// <summary>
        /// Action (the one passed with the latest raise), or null.
        /// </summary>
        public Action? Action { get; }

        /// <summary>
        /// How many times the toast was raised while showing (1 for a single raise).
        /// </summary>
        public int Repeat { get; }

        /// <summary>
        /// The suffix shown after the text for a repeated toast (for example <c> (x3)</c>), or empty.
        /// </summary>
        public string RepeatSuffix { get; }

        /// <summary>
        /// The severity spelled as text, drawn before the text so severity never depends on color (TUIKit's
        /// <c>NotificationCenter.SeverityLabels</c>: <c>[i]</c>, <c>[ok]</c>, <c>[!]</c>, <c>[x]</c>), or empty.
        /// </summary>
        public string SeverityLabel { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="severity">Severity.</param>
        /// <param name="text">Text.</param>
        /// <param name="expiresUtc">Expiry time.</param>
        /// <param name="actionLabel">English action label, or null.</param>
        /// <param name="action">Action, or null.</param>
        /// <param name="repeat">Raise count (at least 1).</param>
        /// <param name="repeatSuffix">Repeat suffix, or empty.</param>
        /// <param name="severityLabel">Severity label, or empty.</param>
        public ToastEntry(long id, NotificationSeverityEnum severity, string text, DateTime expiresUtc, string? actionLabel, Action? action, int repeat, string repeatSuffix, string severityLabel)
        {
            Id = id;
            Severity = severity;
            Text = text ?? "";
            ExpiresUtc = expiresUtc;
            ActionLabel = actionLabel;
            Action = action;
            Repeat = Math.Max(1, repeat);
            RepeatSuffix = repeatSuffix ?? "";
            SeverityLabel = severityLabel ?? "";
        }

        #endregion
    }
}
