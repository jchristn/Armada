namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The health check's view of one captain that is unreachable (its Harbor lost the link) or whose stop is waiting to
    /// be confirmed. Kept in memory and rebuilt from the episode's events (captain.unreachable, captain.stop_unconfirmed,
    /// captain.stalled, captain.reconciled) after an Admiral restart, so a restart neither re-announces the episode nor
    /// restarts its retries.
    /// </summary>
    internal sealed class CaptainLinkEpisode
    {
        #region Public-Members

        /// <summary>
        /// Captain the episode is about.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// The captain's mission when the episode began, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Process handle the episode is about, or null. A captain relaunched under another handle ends the episode.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Harbor that runs the process, or null.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// When the captain was first held as unreachable (UTC), or null when it is not.
        /// </summary>
        public DateTime? UnreachableSinceUtc { get; set; } = null;

        /// <summary>
        /// Whether the unreachable grace ran out and the captain was handed to stall recovery.
        /// </summary>
        public bool Escalated { get; set; } = false;

        /// <summary>
        /// When a stop was first requested and not confirmed (UTC), or null when no stop is pending.
        /// </summary>
        public DateTime? StopRequestedUtc { get; set; } = null;

        /// <summary>
        /// Why the pending stop was requested.
        /// </summary>
        public CaptainStopPurposeEnum Purpose { get; set; } = CaptainStopPurposeEnum.Recovery;

        /// <summary>
        /// The captain's recovery attempts before the first unconfirmed stop of this episode, or null. Restored when the
        /// captain turns out to be working, since those attempts never relaunched anything.
        /// </summary>
        public int? RecoveryAttemptsBefore { get; set; } = null;

        /// <summary>
        /// Current backoff between stop attempts, in minutes.
        /// </summary>
        public int RetryDelayMinutes { get; set; } = 0;

        /// <summary>
        /// Earliest time of the next stop attempt (UTC) when the Harbor has not reconnected first.
        /// </summary>
        public DateTime NextStopRetryUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Whether a stop is pending.
        /// </summary>
        public bool StopPending => StopRequestedUtc.HasValue;

        /// <summary>
        /// Whether the episode holds anything (an unreachable captain or a pending stop).
        /// </summary>
        public bool IsOpen => UnreachableSinceUtc.HasValue || StopRequestedUtc.HasValue;

        #endregion
    }
}
