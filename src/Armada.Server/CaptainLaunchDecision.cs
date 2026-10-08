namespace Armada.Server
{
    /// <summary>
    /// Where a captain launch should run, as decided by <see cref="CaptainLaunchRouter.DecideAsync"/>: on a Harbor
    /// (<see cref="HarborId"/> set) or on the Admiral host. When no Harbor is chosen and <see cref="HarborRequired"/> is
    /// true, the launch must be refused instead of running on the Admiral host.
    /// </summary>
    public class CaptainLaunchDecision
    {
        #region Public-Members

        /// <summary>
        /// The chosen Harbor, or null to run on the Admiral host (or refuse, see <see cref="HarborRequired"/>).
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Why this Harbor was chosen, or why none was.
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// True when requireHarborForLaunch is on, so a launch with no Harbor must be refused.
        /// </summary>
        public bool HarborRequired { get; set; } = false;

        /// <summary>
        /// True when at least one Harbor was connected when the decision was made.
        /// </summary>
        public bool AnyHarborConnected { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainLaunchDecision()
        {
        }

        #endregion
    }
}
