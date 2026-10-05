namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Body of the setup wizard's POST /api/v1/captains.
    /// </summary>
    public class SetupWizardCaptainBody
    {
        #region Public-Members

        /// <summary>
        /// Captain name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Runtime as sent.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Tier as sent.
        /// </summary>
        public string? Tier { get; set; } = null;

        /// <summary>
        /// System instructions.
        /// </summary>
        public string? SystemInstructions { get; set; } = null;

        /// <summary>
        /// Mux runtime options JSON.
        /// </summary>
        public string? RuntimeOptionsJson { get; set; } = null;

        #endregion
    }
}
