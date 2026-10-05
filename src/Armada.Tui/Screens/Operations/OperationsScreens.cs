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
            screens.Register("JobsScreen", (m, c) => new JobsScreen(m, c));
            screens.Register("MissionsScreen", (m, c) => new MissionsScreen(m, c));
        }

        #endregion
    }
}
