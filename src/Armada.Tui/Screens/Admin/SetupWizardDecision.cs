namespace Armada.Tui.Screens.Admin
{
    /// <summary>
    /// Result of the setup wizard auto-open rule (the dashboard's <c>Layout.tsx</c> visibility check).
    /// </summary>
    public class SetupWizardDecision
    {
        #region Public-Members

        /// <summary>
        /// True when the wizard should open.
        /// </summary>
        public bool Open { get; set; } = false;

        /// <summary>
        /// True when the stored "setup completed" flag should be cleared (a completely empty deployment).
        /// </summary>
        public bool ClearCompleted { get; set; } = false;

        #endregion
    }
}
