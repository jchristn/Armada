namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Body of the setup wizard's POST /api/v1/fleets.
    /// </summary>
    public class SetupWizardFleetBody
    {
        #region Public-Members

        /// <summary>
        /// Fleet name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Fleet description.
        /// </summary>
        public string? Description { get; set; } = null;

        #endregion
    }
}
