namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Service for dock (worktree) lifecycle management.
    /// </summary>
    public interface IDockService
    {
        /// <summary>
        /// Provision a dock (git worktree) for a captain working on a vessel.
        /// </summary>
        /// <param name="vessel">Target vessel.</param>
        /// <param name="captain">Captain that will use the dock.</param>
        /// <param name="branchName">Branch name for the worktree.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created dock, or null if provisioning failed.</returns>
        Task<Dock?> ProvisionAsync(Vessel vessel, Captain captain, string branchName, CancellationToken token = default);

        /// <summary>
        /// Reclaim a dock by removing the worktree.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task ReclaimAsync(string dockId, CancellationToken token = default);

        /// <summary>
        /// Repair a dock's worktree.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task RepairAsync(string dockId, CancellationToken token = default);
    }
}
