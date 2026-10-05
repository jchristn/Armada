namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Services;

    /// <summary>
    /// One Home-page alert: a severity, an English message template and hint with placeholder arguments (translated
    /// as keyed messages, never by matching rendered text), and an optional route.
    /// </summary>
    public class HomeAlert
    {
        #region Public-Members

        /// <summary>
        /// Severity (<see cref="NotificationSeverityEnum.Error"/> or <see cref="NotificationSeverityEnum.Warning"/>).
        /// </summary>
        public NotificationSeverityEnum Severity { get; set; } = NotificationSeverityEnum.Warning;

        /// <summary>
        /// English message template with <c>{{name}}</c> placeholders.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// English hint template with <c>{{name}}</c> placeholders.
        /// </summary>
        public string Hint { get; set; } = "";

        /// <summary>
        /// Placeholder values for <see cref="Message"/> and <see cref="Hint"/>. Never null.
        /// </summary>
        public Dictionary<string, object?> Args { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>
        /// Route to open, or null.
        /// </summary>
        public string? Route { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HomeAlert()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="severity">Severity.</param>
        /// <param name="message">English message template.</param>
        /// <param name="hint">English hint template.</param>
        /// <param name="route">Route, or null.</param>
        /// <param name="args">Placeholder values, or null.</param>
        public HomeAlert(NotificationSeverityEnum severity, string message, string hint, string? route, Dictionary<string, object?>? args)
        {
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Hint = hint ?? throw new ArgumentNullException(nameof(hint));
            Route = route;
            if (args != null) Args = args;
        }

        #endregion
    }
}
