namespace Armada.Test.Automated
{
    /// <summary>
    /// Result of creating prerequisite fleet and vessel for tests.
    /// </summary>
    public class PrerequisiteResult
    {
        #region Public-Members

        /// <summary>
        /// The created fleet ID.
        /// </summary>
        public string FleetId { get; set; } = "";

        /// <summary>
        /// The created vessel ID.
        /// </summary>
        public string VesselId { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public PrerequisiteResult()
        {
        }

        /// <summary>
        /// Instantiate with fleet and vessel IDs.
        /// </summary>
        /// <param name="fleetId">The created fleet ID.</param>
        /// <param name="vesselId">The created vessel ID.</param>
        public PrerequisiteResult(string fleetId, string vesselId)
        {
            FleetId = fleetId ?? "";
            VesselId = vesselId ?? "";
        }

        #endregion
    }
}
