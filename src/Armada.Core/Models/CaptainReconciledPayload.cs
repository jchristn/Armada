namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Typed payload of a <c>captain.reconciled</c> event: an unreachable or stop-pending captain was settled against its
    /// real state (it resumed, it was stopped, or it had exited). Ends the episode a <c>captain.unreachable</c> or
    /// <c>captain.stop_unconfirmed</c> event started.
    /// </summary>
    public class CaptainReconciledPayload
    {
        #region Public-Members

        /// <summary>
        /// Reconciled captain.
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
        /// How the captain was settled.
        /// </summary>
        public CaptainReconcileOutcomeEnum Outcome { get; set; } = CaptainReconcileOutcomeEnum.Resumed;

        /// <summary>
        /// When the captain was first held as unreachable (UTC), or null when it was not.
        /// </summary>
        public DateTime? UnreachableSinceUtc { get; set; } = null;

        /// <summary>
        /// When a stop was first requested and not confirmed (UTC), or null when none was pending.
        /// </summary>
        public DateTime? StopRequestedUtc { get; set; } = null;

        /// <summary>
        /// When the Harbor's current link was established (UTC), or null.
        /// </summary>
        public DateTime? ReconnectedUtc { get; set; } = null;

        /// <summary>
        /// The captain's last reported activity (UTC), or null.
        /// </summary>
        public DateTime? LastActivityUtc { get; set; } = null;

        #endregion
    }
}
