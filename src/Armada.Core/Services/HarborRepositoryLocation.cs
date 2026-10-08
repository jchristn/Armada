namespace Armada.Core.Services
{
    using Armada.Core.Harbor;

    /// <summary>
    /// Where a Harbor serves a vessel's repository from on its own machine.
    /// </summary>
    public class HarborRepositoryLocation
    {
        #region Public-Members

        /// <summary>
        /// Where the repository came from; None when the Harbor cannot serve the vessel.
        /// </summary>
        public HarborRepositorySourceEnum Source { get; set; } = HarborRepositorySourceEnum.None;

        /// <summary>
        /// The repository mission branches and worktrees are created in: the checkout, or the Harbor's bare clone.
        /// </summary>
        public string? RepositoryPath { get; set; } = null;

        /// <summary>
        /// The user's checkout (Mapped or Discovered); null for a Clone source.
        /// </summary>
        public string? CheckoutPath { get; set; } = null;

        /// <summary>
        /// Why the Harbor cannot serve the vessel, and which setting fixes it; null when it can.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborRepositoryLocation()
        {
        }

        #endregion
    }
}
