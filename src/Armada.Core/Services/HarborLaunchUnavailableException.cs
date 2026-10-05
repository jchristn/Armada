namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when <c>requireHarborForLaunch</c> is on and no eligible Harbor owned by the mission's user can take a
    /// captain launch (for a pinned dock, its Harbor is offline or no longer registered). The launch is refused
    /// instead of falling back to the Admiral host. A first launch returns the mission to Pending; a stall-recovery
    /// relaunch spends one recovery attempt and waits for the next stall check, and the mission fails as
    /// StallRecoveryExhausted once the attempts run out.
    /// </summary>
    public class HarborLaunchUnavailableException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// The Harbor the mission's dock is pinned to, or null when the dock is not pinned.
        /// </summary>
        public string? PinnedHarborId { get; }

        /// <summary>
        /// True when the refusal comes from the <c>requireHarborForLaunch</c> policy.
        /// </summary>
        public bool RequiredByPolicy { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="pinnedHarborId">The Harbor the dock is pinned to, or null.</param>
        /// <param name="requiredByPolicy">True when <c>requireHarborForLaunch</c> requires a Harbor.</param>
        /// <param name="message">Human-readable message.</param>
        public HarborLaunchUnavailableException(string? pinnedHarborId, bool requiredByPolicy, string message)
            : base(message)
        {
            PinnedHarborId = String.IsNullOrWhiteSpace(pinnedHarborId) ? null : pinnedHarborId;
            RequiredByPolicy = requiredByPolicy;
        }

        #endregion
    }
}
