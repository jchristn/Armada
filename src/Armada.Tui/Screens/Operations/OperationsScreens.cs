namespace Armada.Tui.Screens.Operations
{
    using System;

    /// <summary>
    /// Registers the OPERATIONS screens (W3: Home, Needs You, Planning, Dispatch, Backlog, Fleet Actions, Missions,
    /// Voyages, Merge Queue, and Jobs) with the screen factory, keeping the composition root's change to one line.
    /// </summary>
    public static class OperationsScreens
    {
        #region Public-Methods

        /// <summary>
        /// Register every OPERATIONS screen builder.
        /// </summary>
        /// <param name="screens">Screen factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screens"/> is null.</exception>
        public static void Register(ScreenFactory screens)
        {
            if (screens == null) throw new ArgumentNullException(nameof(screens));
            screens.Register("HomeScreen", (m, c) => new HomeScreen(m, c));
            screens.Register("JobsScreen", (m, c) => new JobsScreen(m, c));
            screens.Register("PlanningScreen", (m, c) => new PlanningScreen(m, c));
            screens.Register("DispatchScreen", (m, c) => new DispatchScreen(m, c));
            screens.Register("FleetActionsScreen", (m, c) => new FleetActionsScreen(m, c));
            screens.Register("FleetActionRunsScreen", (m, c) => new FleetActionRunsScreen(m, c));
            screens.Register("FleetActionRunScreen", (m, c) => new FleetActionRunScreen(m, c));
            screens.Register("BacklogScreen", (m, c) => new BacklogScreen(m, c));
            screens.Register("BacklogItemScreen", (m, c) => new BacklogItemScreen(m, c));
            screens.Register("InboxScreen", (m, c) => new InboxScreen(m, c));
            screens.Register("MissionsScreen", (m, c) => new MissionsScreen(m, c));
            screens.Register("MissionScreen", (m, c) => new MissionScreen(m, c));
            screens.Register("VoyagesScreen", (m, c) => new VoyagesScreen(m, c));
            screens.Register("VoyageScreen", (m, c) => new VoyageScreen(m, c));
            screens.Register("VoyageCreateScreen", (m, c) => new VoyageCreateScreen(m, c));
            screens.Register("MergeQueueScreen", (m, c) => new MergeQueueScreen(m, c));
            screens.Register("MergeEntryScreen", (m, c) => new MergeEntryScreen(m, c));
        }

        #endregion
    }
}
