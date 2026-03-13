namespace Armada.Test.Automated
{
    /// <summary>
    /// Result of creating prerequisites for merge queue tests (fleet, vessel, and mission).
    /// </summary>
    public class MergeQueuePrerequisiteResult
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

        /// <summary>
        /// The created mission ID.
        /// </summary>
        public string MissionId { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public MergeQueuePrerequisiteResult()
        {
        }

        /// <summary>
        /// Instantiate with fleet, vessel, and mission IDs.
        /// </summary>
        /// <param name="fleetId">The created fleet ID.</param>
        /// <param name="vesselId">The created vessel ID.</param>
        /// <param name="missionId">The created mission ID.</param>
        public MergeQueuePrerequisiteResult(string fleetId, string vesselId, string missionId)
        {
            FleetId = fleetId ?? "";
            VesselId = vesselId ?? "";
            MissionId = missionId ?? "";
        }

        #endregion
    }
}
