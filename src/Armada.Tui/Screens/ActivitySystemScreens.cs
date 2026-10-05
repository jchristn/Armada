namespace Armada.Tui.Screens
{
    using System;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Screens.Admin;

    /// <summary>
    /// Registers the ACTIVITY and SYSTEM screens (TUI_APP_PLAN.md workstream W7, plus Jobs) with the screen factory.
    /// </summary>
    public static class ActivitySystemScreens
    {
        #region Public-Methods

        /// <summary>
        /// Register every Activity and System screen builder.
        /// </summary>
        /// <param name="screens">Screen factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screens"/> is null.</exception>
        public static void Register(ScreenFactory screens)
        {
            if (screens == null) throw new ArgumentNullException(nameof(screens));
            screens.Register("ActivityScreen", (m, c) => new ActivityScreen(m, c));
            screens.Register("RequestHistoryScreen", (m, c) => new RequestHistoryScreen(m, c));
            screens.Register("EventsScreen", (m, c) => new EventsScreen(m, c));
            screens.Register("EventScreen", (m, c) => new EventScreen(m, c));
            screens.Register("SignalsScreen", (m, c) => new SignalsScreen(m, c));
            screens.Register("SignalScreen", (m, c) => new SignalScreen(m, c));
            screens.Register("TokenUsageScreen", (m, c) => new TokenUsageScreen(m, c));
            screens.Register("ApiExplorerScreen", (m, c) => new ApiExplorerScreen(m, c));
            screens.Register("ServerSettingsScreen", (m, c) => new ServerSettingsScreen(m, c));
            screens.Register("DiagnosticsScreen", (m, c) => new DiagnosticsScreen(m, c));
            screens.Register("TenantsScreen", (m, c) => new TenantsScreen(m, c));
            screens.Register("UsersScreen", (m, c) => new UsersScreen(m, c));
            screens.Register("CredentialsScreen", (m, c) => new CredentialsScreen(m, c));
            screens.Register("CliPermissionRequestsScreen", (m, c) => new CliPermissionRequestsScreen(m, c));
            screens.Register("CliPermissionRulesScreen", (m, c) => new CliPermissionRulesScreen(m, c));
            screens.Register("SetupWizard", (m, c) => new SetupWizardScreen(m, c));
        }

        #endregion
    }
}
