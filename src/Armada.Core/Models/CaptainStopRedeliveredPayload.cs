namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Typed payload of a <c>captain.stop_redelivered</c> event: a stop that could not be confirmed while the Harbor was
    /// disconnected was sent again after the Harbor reconnected, because the captain's job was still running and had
    /// reported no activity since the stop was requested.
    /// </summary>
    public class CaptainStopRedeliveredPayload
    {
        #region Public-Members

        /// <summary>
        /// Captain being stopped.
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
        /// When the stop was first requested (UTC).
        /// </summary>
        public DateTime StopRequestedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the Harbor's current link was established (UTC).
        /// </summary>
        public DateTime ReconnectedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the Harbor confirmed the stop.
        /// </summary>
        public bool Confirmed { get; set; } = false;

        #endregion
    }
}
