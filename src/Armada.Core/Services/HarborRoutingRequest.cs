namespace Armada.Core.Services
{
    using System.Collections.Generic;

    /// <summary>
    /// Inputs to a Harbor routing decision. Affinity and preference are optional; when a mission already
    /// owns a dock, <see cref="ExistingHarborId"/> pins the decision to that Harbor.
    /// </summary>
    public class HarborRoutingRequest
    {
        #region Public-Members

        /// <summary>
        /// The Harbor that already owns this mission's dock, if any. When set, routing is pinned to it
        /// (dock affinity): a worktree cannot move hosts.
        /// </summary>
        public string? ExistingHarborId { get; set; } = null;

        /// <summary>
        /// A preferred Harbor (from vessel or mission assignment). Honored when it is otherwise eligible.
        /// </summary>
        public string? PreferredHarborId { get; set; } = null;

        /// <summary>
        /// The runtime the mission needs (for example "claude"). When set, only Harbors advertising it as an
        /// available capability are eligible.
        /// </summary>
        public string? RequestedRuntime { get; set; } = null;

        /// <summary>
        /// Additional capabilities the vessel requires. All must be present and available on a candidate.
        /// </summary>
        public List<string> RequiredCapabilities { get; set; } = new List<string>();

        /// <summary>
        /// When true, only Harbors whose owning user equals <see cref="OwnerUserId"/> are candidates (shared and other
        /// users' Harbors are excluded). Set for launches under <c>requireHarborForLaunch</c>. Defaults to false.
        /// </summary>
        public bool RestrictToOwner { get; set; } = false;

        /// <summary>
        /// The owning user a Harbor must belong to when <see cref="RestrictToOwner"/> is true (null matches only
        /// Harbors with no owner).
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Harbors not to choose, for example ones already asked that cannot serve the vessel. An excluded pinned Harbor
        /// (<see cref="ExistingHarborId"/>) yields no choice.
        /// </summary>
        public List<string> ExcludedHarborIds { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborRoutingRequest()
        {
        }

        #endregion
    }
}
