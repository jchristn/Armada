namespace Armada.Test.Unit.TestHelpers
{
    using Armada.Core.Models;

    /// <summary>
    /// Result of creating test entities for mission status transition tests.
    /// </summary>
    public class TestEntitiesResult
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

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public TestEntitiesResult()
        {
        }

        /// <summary>
        /// Instantiate with captain, mission, and dock.
        /// </summary>
        /// <param name="captain">The test captain.</param>
        /// <param name="mission">The test mission.</param>
        /// <param name="dock">The test dock.</param>
        public TestEntitiesResult(Captain captain, Mission mission, Dock dock)
        {
            Captain = captain ?? throw new System.ArgumentNullException(nameof(captain));
            Mission = mission ?? throw new System.ArgumentNullException(nameof(mission));
            Dock = dock ?? throw new System.ArgumentNullException(nameof(dock));
        }

        #endregion
    }
}
