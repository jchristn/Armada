namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a captain launch must run on a Harbor and no acceptable Harbor can take it: the mission's dock is
    /// pinned to a Harbor that is offline or no longer registered (a worktree cannot move hosts), or
    /// <c>requireHarborForLaunch</c> is on and no eligible Harbor owned by the mission's user is connected. The launch
    /// is refused instead of falling back to the Admiral host. Callers treat it like any other launch failure: a
    /// first launch returns the mission to Pending, and a stall-recovery relaunch counts as a spent recovery attempt
    /// and fails the mission with a typed failure kind.
    /// </summary>
    public class HarborLaunchUnavailableException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// The Harbor the mission's dock is pinned to, or null when the launch was refused by the
        /// <c>requireHarborForLaunch</c> policy for an unpinned dock.
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
