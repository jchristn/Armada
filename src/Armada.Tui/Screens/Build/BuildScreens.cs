namespace Armada.Tui.Screens.Build
{
    using System;

    /// <summary>
    /// Registers the BUILD screens (W4: Vessels, Import wizard, Vessel Health, Vessel detail and onboarding, Fleets,
    /// Workspace, Captains, and Docks) with the screen factory, keeping the composition root's change to one line.
    /// </summary>
    public static class BuildScreens
    {
        #region Public-Methods

        /// <summary>
        /// Register every BUILD screen builder.
        /// </summary>
        /// <param name="screens">Screen factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screens"/> is null.</exception>
        public static void Register(ScreenFactory screens)
        {
            if (screens == null) throw new ArgumentNullException(nameof(screens));
            screens.Register("FleetsScreen", (m, c) => new FleetsScreen(m, c));
            screens.Register("FleetScreen", (m, c) => new FleetScreen(m, c));
            screens.Register("DocksScreen", (m, c) => new DocksScreen(m, c));
            screens.Register("DockScreen", (m, c) => new DockScreen(m, c));
            screens.Register("CaptainsScreen", (m, c) => new CaptainsScreen(m, c));
            screens.Register("CaptainScreen", (m, c) => new CaptainScreen(m, c));
            screens.Register("VesselsScreen", (m, c) => new VesselsScreen(m, c));
            screens.Register("VesselHealthScreen", (m, c) => new VesselHealthScreen(m, c));
            screens.Register("VesselScreen", (m, c) => new VesselScreen(m, c));
            screens.Register("VesselOnboardingScreen", (m, c) => new VesselOnboardingScreen(m, c));
            screens.Register("ImportWizard", (m, c) => new ImportWizard(m, c));
            screens.Register("WorkspaceScreen", (m, c) => String.IsNullOrEmpty(m.Param("vesselId")) ? new WorkspacePickerScreen(m, c) : new WorkspaceScreen(m, c));
        }

        #endregion
    }
}
