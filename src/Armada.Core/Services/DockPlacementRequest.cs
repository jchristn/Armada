namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// What to place a dock for: the captain that will work in it, its vessel, who the work is for, and, for work that
    /// continues an existing branch, the Harbor whose repository holds that branch. Used for mission docks and for the
    /// docks of planning sessions and Model Context builds, so all of them are placed on a Harbor the same way.
    /// </summary>
    public class DockPlacementRequest
    {
        #region Public-Members

        /// <summary>
        /// What the dock is for, for logs (for example "mission msn_..." or "planning session pss_...").
        /// </summary>
        public string Purpose { get; set; } = "dock";

        /// <summary>
        /// The captain that will work in the dock.
        /// </summary>
        public Captain Captain { get; }

        /// <summary>
        /// The vessel.
        /// </summary>
        public Vessel Vessel { get; }

        /// <summary>
        /// Tenant the work belongs to, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User the work is for (with requireHarborForLaunch only that user's Harbors count), or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// The branch the dock checks out, or null.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// The Harbor whose repository already holds the branch (the dock must go there), or null.
        /// </summary>
        public string? BranchHarborId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="vessel">Vessel.</param>
        public DockPlacementRequest(Captain captain, Vessel vessel)
        {
            Captain = captain ?? throw new ArgumentNullException(nameof(captain));
            Vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
        }

        #endregion
    }
}
