namespace Armada.Tui.Ask
{
    /// <summary>
    /// The inline form a quick action opens above the composer.
    /// </summary>
    public enum AskQuickActionFormEnum
    {
        /// <summary>
        /// No form: the action runs immediately with no arguments.
        /// </summary>
        None = 0,

        /// <summary>
        /// The Dispatch form (vessel, pipeline, voyage title, missions).
        /// </summary>
        Dispatch = 1,

        /// <summary>
        /// The Fleet action form (action, vessels).
        /// </summary>
        FleetAction = 2,

        /// <summary>
        /// The Import wizard.
        /// </summary>
        Import = 3
    }
}
