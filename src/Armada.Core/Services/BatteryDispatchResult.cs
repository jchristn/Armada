namespace Armada.Core.Services
{
    using Armada.Core.Models;

    /// <summary>
    /// What happened to a dispatched battery task: the voyage and mission, and the worktree to grade.
    /// </summary>
    public class BatteryDispatchResult
    {
        /// <summary>
        /// Voyage dispatched, or null when dispatch failed.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// The mission as it finished (or as it was when the wait timed out), or null.
        /// </summary>
        public Mission? Mission { get; set; } = null;

        /// <summary>
        /// The mission's worktree on the host that ran it, or null when it has none.
        /// </summary>
        public IBatteryWorkspace? Workspace { get; set; } = null;

        /// <summary>
        /// Whether the wait timed out before the mission finished.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Why dispatch failed, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// Whether the wait was cancelled (a Full Evaluate was cancelled or timed out); the open missions were cancelled,
        /// which releases their docks and captains.
        /// </summary>
        public bool Cancelled { get; set; } = false;
    }
}
