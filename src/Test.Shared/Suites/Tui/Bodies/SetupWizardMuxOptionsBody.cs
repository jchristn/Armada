namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Mux runtime options JSON the setup wizard builds.
    /// </summary>
    public class SetupWizardMuxOptionsBody
    {
        #region Public-Members

        /// <summary>
        /// Schema version.
        /// </summary>
        public int? SchemaVersion { get; set; } = null;

        /// <summary>
        /// Mux endpoint name.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Temperature.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Max tokens.
        /// </summary>
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Approval policy.
        /// </summary>
        public string? ApprovalPolicy { get; set; } = null;

        #endregion
    }
}
