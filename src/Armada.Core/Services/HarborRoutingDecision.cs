namespace Armada.Core.Services
{
    /// <summary>
    /// The outcome of a Harbor routing decision: the chosen Harbor, or a reason no eligible Harbor could be
    /// selected (so the caller can surface a clear "waiting for a runner" state rather than stalling
    /// silently).
    /// </summary>
    public class HarborRoutingDecision
    {
        #region Public-Members

        /// <summary>
        /// Whether a Harbor was selected.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// The chosen Harbor identifier when <see cref="Success"/> is true, otherwise null.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Whether the decision was pinned by dock affinity (the mission already owns a dock on a Harbor).
        /// </summary>
        public bool PinnedByAffinity { get; set; } = false;

        /// <summary>
        /// Human-readable reason no Harbor was selected, or an explanation of the choice.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborRoutingDecision()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a successful decision.
        /// </summary>
        /// <param name="harborId">Chosen Harbor identifier.</param>
        /// <param name="pinnedByAffinity">Whether the choice was pinned by dock affinity.</param>
        /// <param name="reason">Optional explanation.</param>
        /// <returns>A successful decision.</returns>
        public static HarborRoutingDecision Chosen(string harborId, bool pinnedByAffinity, string? reason)
        {
            return new HarborRoutingDecision { Success = true, HarborId = harborId, PinnedByAffinity = pinnedByAffinity, Reason = reason };
        }

        /// <summary>
        /// Build a failed decision.
        /// </summary>
        /// <param name="reason">Reason no Harbor could be selected.</param>
        /// <returns>A failed decision.</returns>
        public static HarborRoutingDecision None(string reason)
        {
            return new HarborRoutingDecision { Success = false, HarborId = null, PinnedByAffinity = false, Reason = reason };
        }

        #endregion
    }
}
