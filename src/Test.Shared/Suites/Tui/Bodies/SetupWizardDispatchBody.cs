namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Body of the setup wizard's POST /api/v1/missions.
    /// </summary>
    public class SetupWizardDispatchBody
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Priority.
        /// </summary>
        public int? Priority { get; set; } = null;

        #endregion
    }
}
