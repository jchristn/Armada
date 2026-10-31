namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Typed payload of a <c>captain.unreachable</c> event: the captain's process runs on a Harbor whose link to the
    /// Admiral is down, so missing output is not judged a stall. Recorded once per episode; the episode ends with a
    /// <c>captain.reconciled</c> event.
    /// </summary>
    public class CaptainUnreachablePayload
    {
        #region Public-Members

        /// <summary>
        /// Unreachable captain.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// The captain's mission, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Harbor that runs the captain's process.
        /// </summary>
        public string HarborId { get; set; } = "";

        /// <summary>
        /// Admiral process handle of the captain's job, or null.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// When the Harbor was last heard from (UTC), the best estimate of when the link was lost.
        /// </summary>
        public DateTime Since { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the Admiral first held the captain as unreachable (UTC); the grace is counted from here.
        /// </summary>
        public DateTime DetectedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The captain's last reported activity (UTC), or null when it has reported none.
        /// </summary>
        public DateTime? LastActivityUtc { get; set; } = null;

        /// <summary>
        /// HarborDisconnectGraceMinutes in effect (0 means no grace).
        /// </summary>
        public int GraceMinutes { get; set; } = 0;

        #endregion
    }
}
