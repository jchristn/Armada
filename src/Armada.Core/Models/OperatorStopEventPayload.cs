namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Payload of the mission.stopped_by_operator event: who stopped the mission, how, and what happened to the
    /// work left in its dock.
    /// </summary>
    public class OperatorStopEventPayload
    {
        /// <summary>
        /// The surface the stop came through.
        /// </summary>
        public string Source { get; set; } = OperatorStopContext.SourceRecall;

        /// <summary>
        /// The user who asked for the stop, when known.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// When the stop was requested (UTC).
        /// </summary>
        public DateTime StoppedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The mission's status before the stop.
        /// </summary>
        public string? PreviousStatus { get; set; } = null;

        /// <summary>
        /// The dock the mission ran in, when it had one.
        /// </summary>
        public string? DockId { get; set; } = null;

        /// <summary>
        /// The mission branch, when known.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// The commit that preserved uncommitted work, or null when there was nothing to preserve or it could not be
        /// committed.
        /// </summary>
        public string? WipCommitHash { get; set; } = null;

        /// <summary>
        /// Whether the WIP commit was pushed to the remote.
        /// </summary>
        public bool WipPushed { get; set; } = false;

        /// <summary>
        /// Why work could not be preserved, when it could not.
        /// </summary>
        public string? WipError { get; set; } = null;
    }
}
