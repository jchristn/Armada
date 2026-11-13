namespace Armada.Core.Services
{
    using Armada.Core.Models;

    /// <summary>
    /// The outcome of one capture attempt: the sanitized snapshot that was committed (one per attempt,
    /// including a failure with its stable error code), whether it was applied at all, and why not when
    /// it was not. A result is never applied after its lease expired or its generation changed, so a
    /// stale attempt reports <see cref="DiscardedReason"/> rather than overwriting fresh state.
    /// </summary>
    public class UsageCaptureAttemptResult
    {
        #region Public-Members

        /// <summary>
        /// The committed snapshot, or null when nothing was persisted.
        /// </summary>
        public UsageSnapshot? Snapshot { get; set; } = null;

        /// <summary>
        /// True when the sanitized snapshot was committed with its member links.
        /// </summary>
        public bool Persisted { get; set; } = false;

        /// <summary>
        /// The attempt's capture status, or null when nothing ran.
        /// </summary>
        public Enums.UsageSnapshotStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Stable error code of the attempt, or null on success.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// When the group is next eligible after this attempt, or null when it is not scheduled again.
        /// </summary>
        public System.DateTime? NextEligibleUtc { get; set; } = null;

        /// <summary>
        /// Why the result was discarded instead of applied: SettingsGenerationChanged (a settings or
        /// configuration change invalidated the scheduling generation) or LeaseExpired. Null when the
        /// result was applied or nothing ran.
        /// </summary>
        public string? DiscardedReason { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageCaptureAttemptResult()
        {
        }

        #endregion
    }
}