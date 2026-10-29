namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Typed payload of a <c>mission.dispatch_deferred</c> event: a Pending mission was not dispatched because the host's
    /// resource admission gate refused new captains (memory pressure). Recorded once per mission and reason, so a later
    /// failure's black box entry can show the mission waited and why.
    /// </summary>
    public class MissionDispatchDeferredPayload
    {
        #region Public-Members

        /// <summary>
        /// Mission that was not dispatched.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Why dispatch was deferred.
        /// </summary>
        public string Reason { get; set; } = "";

        /// <summary>
        /// Available physical memory when the gate refused (bytes), or null when unknown.
        /// </summary>
        public long? AvailableBytes { get; set; } = null;

        /// <summary>
        /// Total physical memory (bytes), or null when unknown.
        /// </summary>
        public long? TotalBytes { get; set; } = null;

        /// <summary>
        /// Configured floor of available memory for a launch (bytes).
        /// </summary>
        public long MinimumAvailableBytes { get; set; } = 0;

        /// <summary>
        /// When dispatch was deferred (UTC).
        /// </summary>
        public DateTime DeferredUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
