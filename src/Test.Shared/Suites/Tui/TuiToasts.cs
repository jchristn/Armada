namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Linq;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// Toast checks on the notification model (severity and text) instead of the rendered frame, so an error toast
    /// that happens to contain the expected words does not count as success.
    /// </summary>
    internal static class TuiToasts
    {
        #region Public-Methods

        /// <summary>
        /// True when an active toast has this severity and its text contains the fragment.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="severity">Expected severity.</param>
        /// <param name="fragment">Text fragment (user-visible copy).</param>
        /// <returns>True when one matches.</returns>
        public static bool Has(TuiTestHost host, NotificationSeverityEnum severity, string fragment)
        {
            return host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == severity && t.Text.Contains(fragment, StringComparison.Ordinal));
        }

        /// <summary>
        /// Pump until a success toast containing the fragment is active.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="fragment">Text fragment.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>True when seen.</returns>
        public static bool WaitForSuccess(TuiTestHost host, string fragment, int timeoutMs = 5000)
        {
            return host.PumpUntil(() => Has(host, NotificationSeverityEnum.Success, fragment), timeoutMs);
        }

        #endregion
    }
}
