namespace Armada.Tui.Screens.Admin
{
    /// <summary>
    /// Whether a setup wizard step reuses an existing record or creates a new one.
    /// </summary>
    public enum SetupWizardModeEnum
    {
        /// <summary>
        /// Use an existing record.
        /// </summary>
        Existing = 0,

        /// <summary>
        /// Create a new record.
        /// </summary>
        New = 1
    }
}
