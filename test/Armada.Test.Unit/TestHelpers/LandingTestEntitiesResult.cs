namespace Armada.Test.Unit.TestHelpers
{
    using Armada.Core.Models;

    /// <summary>
    /// Result of creating test entities for landing pipeline tests.
    /// </summary>
    public class LandingTestEntitiesResult
    {
        #region Public-Members

        /// <summary>
        /// The test captain.
        /// </summary>
        public Captain Captain { get; set; } = null!;

        /// <summary>
        /// The test mission.
        /// </summary>
        public Mission Mission { get; set; } = null!;

        /// <summary>
        /// The test dock.
        /// </summary>
        public Dock Dock { get; set; } = null!;

        /// <summary>
        /// The test vessel.
        /// </summary>
        public Vessel Vessel { get; set; } = null!;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public LandingTestEntitiesResult()
        {
        }

        /// <summary>
        /// Instantiate with captain, mission, dock, and vessel.
        /// </summary>
        /// <param name="captain">The test captain.</param>
        /// <param name="mission">The test mission.</param>
        /// <param name="dock">The test dock.</param>
        /// <param name="vessel">The test vessel.</param>
        public LandingTestEntitiesResult(Captain captain, Mission mission, Dock dock, Vessel vessel)
        {
            Captain = captain ?? throw new System.ArgumentNullException(nameof(captain));
            Mission = mission ?? throw new System.ArgumentNullException(nameof(mission));
            Dock = dock ?? throw new System.ArgumentNullException(nameof(dock));
            Vessel = vessel ?? throw new System.ArgumentNullException(nameof(vessel));
        }

        #endregion
    }
}
