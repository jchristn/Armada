namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Body of the setup wizard's POST /api/v1/vessels; nullable so an absent field reads as null.
    /// </summary>
    public class SetupWizardVesselBody
    {
        #region Public-Members

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Repository URL.
        /// </summary>
        public string? RepoUrl { get; set; } = null;

        /// <summary>
        /// Fleet id.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Default branch.
        /// </summary>
        public string? DefaultBranch { get; set; } = null;

        /// <summary>
        /// Landing mode as sent.
        /// </summary>
        public string? LandingMode { get; set; } = null;

        /// <summary>
        /// Model context flag.
        /// </summary>
        public bool? EnableModelContext { get; set; } = null;

        /// <summary>
        /// Concurrency flag.
        /// </summary>
        public bool? AllowConcurrentMissions { get; set; } = null;

        #endregion
    }
}
