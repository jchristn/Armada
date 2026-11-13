namespace Armada.Core.Services
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Receipt for one capture request: which dedicated job the attempt runs on (so it is visible and
    /// pollable on Jobs), whether it joined an existing flight rather than starting one, when the group
    /// is next eligible, and the stable skip reason when nothing was started. The
    /// <see cref="Completion"/> task completes when the attempt finishes, with the persisted snapshot
    /// (or the discard reason when the result was not applied); callers that only need the receipt, such
    /// as a manual capture returning its job id, do not have to await it.
    /// </summary>
    public class UsageCaptureAttempt
    {
        #region Public-Members

        /// <summary>
        /// Identifier of the dedicated UsageProbe job the attempt runs on, or null when nothing was
        /// started or joined.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// True when the request joined an attempt that was already running (an in-flight flight, or the
        /// group's unexpired persisted lease), rather than starting a new one.
        /// </summary>
        public bool JoinedExisting { get; set; } = false;

        /// <summary>
        /// True when this request started a new attempt.
        /// </summary>
        public bool Started { get; set; } = false;

        /// <summary>
        /// When the group is next eligible for a capture, or null when it is not scheduled again (an
        /// Unsupported source retries only on a runtime, version or configuration change).
        /// </summary>
        public DateTime? NextEligibleUtc { get; set; } = null;

        /// <summary>
        /// Stable reason nothing was started: CaptureDisabled (the server switch), Cooldown (the interval
        /// or a provider Retry-After), LeaseHeld, NoMembers, or HostUnavailable. Null when an attempt was
        /// started or joined.
        /// </summary>
        public string? SkippedReason { get; set; } = null;

        /// <summary>
        /// The attempt's completion. Already completed when nothing was started.
        /// </summary>
        public Task<UsageCaptureAttemptResult> Completion { get; set; } = Task.FromResult(new UsageCaptureAttemptResult());

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageCaptureAttempt()
        {
        }

        #endregion
    }
}