namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Typed payload of a <c>captain.stop_unconfirmed</c> event: recovery asked for the captain's process to stop and the
    /// owning host did not confirm it, so no replacement was launched and recovery waits. Recorded once per episode; the
    /// stop is re-sent when the Harbor reconnects, and otherwise retried after <see cref="NextRetryUtc"/>. The episode
    /// ends with a <c>captain.reconciled</c> event.
    /// </summary>
    public class CaptainStopUnconfirmedPayload
    {
        #region Public-Members

        /// <summary>
        /// Captain whose stop was not confirmed.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// The captain's mission, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Harbor that runs the captain's process, or null when it runs on the Admiral host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Admiral process handle of the captain's job, or null.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Why the stop was requested; the same step continues once the stop is confirmed.
        /// </summary>
        public CaptainStopPurposeEnum Purpose { get; set; } = CaptainStopPurposeEnum.Recovery;

        /// <summary>
        /// Automatic recovery attempts counted so far.
        /// </summary>
        public int Attempt { get; set; } = 0;

        /// <summary>
        /// MaxRecoveryAttempts in effect.
        /// </summary>
        public int MaxAttempts { get; set; } = 0;

        /// <summary>
        /// When the stop was first requested (UTC).
        /// </summary>
        public DateTime StopRequestedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Backoff before the next attempt, in minutes.
        /// </summary>
        public int RetryDelayMinutes { get; set; } = 1;

        /// <summary>
        /// Earliest time of the next attempt (UTC) when the Harbor has not reconnected first.
        /// </summary>
        public DateTime NextRetryUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Why the stop could not be confirmed.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
