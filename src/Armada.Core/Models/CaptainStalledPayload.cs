namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Typed payload of a <c>captain.stalled</c> event: the captain produced no output for longer than the stall
    /// threshold and automatic recovery is attempted (or is exhausted). Recorded once per stall episode and once per
    /// later attempt while a stop is waiting to be confirmed.
    /// </summary>
    public class CaptainStalledPayload
    {
        #region Public-Members

        /// <summary>
        /// Stalled captain.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// The captain's mission, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Harbor that runs the captain's process, or null.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Admiral process handle of the captain's job, or null.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Minutes since the captain's last output.
        /// </summary>
        public double IdleMinutes { get; set; } = 0;

        /// <summary>
        /// StallThresholdMinutes in effect.
        /// </summary>
        public int StallThresholdMinutes { get; set; } = 0;

        /// <summary>
        /// The automatic recovery attempt this stall starts (1-based), or 0 when recovery is exhausted.
        /// </summary>
        public int Attempt { get; set; } = 0;

        /// <summary>
        /// MaxRecoveryAttempts in effect.
        /// </summary>
        public int MaxAttempts { get; set; } = 0;

        /// <summary>
        /// Whether an earlier stop of this captain is still waiting to be confirmed (this is a retry after backoff).
        /// </summary>
        public bool WaitingOnStop { get; set; } = false;

        /// <summary>
        /// Backoff that applies if this attempt's stop is not confirmed either, in minutes.
        /// </summary>
        public int RetryDelayMinutes { get; set; } = 1;

        /// <summary>
        /// When the next attempt is due (UTC) if this attempt's stop is not confirmed.
        /// </summary>
        public DateTime NextRetryUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
