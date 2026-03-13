namespace Armada.Test.Unit.TestHelpers
{
    using Armada.Core.Models;

    /// <summary>
    /// Result of creating prerequisite entities for mission database tests.
    /// </summary>
    public class MissionTestPrerequisites
    {
        #region Public-Members

        /// <summary>
        /// The test fleet.
        /// </summary>
        public Fleet Fleet { get; set; } = null!;

        /// <summary>
        /// The test vessel.
        /// </summary>
        public Vessel Vessel { get; set; } = null!;

        /// <summary>
        /// The test voyage.
        /// </summary>
        public Voyage Voyage { get; set; } = null!;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public MissionTestPrerequisites()
        {
        }

        /// <summary>
        /// Instantiate with fleet, vessel, and voyage.
        /// </summary>
        /// <param name="fleet">The test fleet.</param>
        /// <param name="vessel">The test vessel.</param>
        /// <param name="voyage">The test voyage.</param>
        public MissionTestPrerequisites(Fleet fleet, Vessel vessel, Voyage voyage)
        {
            Fleet = fleet ?? throw new System.ArgumentNullException(nameof(fleet));
            Vessel = vessel ?? throw new System.ArgumentNullException(nameof(vessel));
            Voyage = voyage ?? throw new System.ArgumentNullException(nameof(voyage));
        }

        #endregion
    }
}
