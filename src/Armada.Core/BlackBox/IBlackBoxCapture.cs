namespace Armada.Core.BlackBox
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Captures a black box entry at the moment a mission or voyage fails. Never throws: a capture failure is logged and
    /// emits blackbox.capture_failed, and the sweeper captures the subject later. Bounded by
    /// BlackBox.CaptureTimeoutMs beyond the index insert. Failure sites call it after the failing row update succeeded,
    /// usually through <see cref="BlackBoxCaptureExtensions.RecordMissionFailureAsync"/>. Thread-safe.
    /// </summary>
    public interface IBlackBoxCapture
    {
        /// <summary>
        /// Capture a mission failure: numbers the attempt and supersedes the previous entry, builds the record from the
        /// context, redacts and bounds it, writes the entry, record.json, the briefs and in-memory evidence, sets
        /// Mission.LastBlackBoxEntryId, emits blackbox.captured and queues enrichment.
        /// </summary>
        /// <param name="context">Failure context (Mission required).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry id, or null when disabled or the capture failed.</returns>
        Task<string?> CaptureMissionFailureAsync(BlackBoxCaptureContext context, CancellationToken token = default);

        /// <summary>
        /// Capture a voyage failure (roll-up of the voyage's missions, cascade root, settlement rule and halt reason).
        /// </summary>
        /// <param name="context">Failure context (Voyage required).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry id, or null when disabled or the capture failed.</returns>
        Task<string?> CaptureVoyageFailureAsync(BlackBoxVoyageCaptureContext context, CancellationToken token = default);
    }
}
