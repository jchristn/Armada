namespace Armada.Core.Models
{
    /// <summary>
    /// Payload of the dock.reclaimed and dock.reclaim_refused events: which dock, which mission referenced it and in what
    /// status, which code path asked for the reclaim, and why it was forced or refused.
    /// </summary>
    public class DockReclaimRecord
    {
        #region Public-Members

        /// <summary>
        /// Dock identifier.
        /// </summary>
        public string DockId { get; set; } = "";

        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Harbor that hosts the dock, or null for the Admiral's host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Worktree path on the dock's host.
        /// </summary>
        public string? WorktreePath { get; set; } = null;

        /// <summary>
        /// Branch checked out in the dock.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Mission most closely tied to the dock (the lease holder, else the latest mission that references it).
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// That mission's status when the reclaim was requested.
        /// </summary>
        public string? MissionStatus { get; set; } = null;

        /// <summary>
        /// Code path that requested the reclaim.
        /// </summary>
        public string Caller { get; set; } = "";

        /// <summary>
        /// Explicit reason the caller gave to reclaim a leased dock, or null.
        /// </summary>
        public string? ForceReason { get; set; } = null;

        /// <summary>
        /// What held the lease, or null when nothing did.
        /// </summary>
        public string? LeaseHolder { get; set; } = null;

        /// <summary>
        /// Whether the reclaim was refused.
        /// </summary>
        public bool Refused { get; set; } = false;

        #endregion
    }
}
