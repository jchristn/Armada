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
        /// When missionId is provided, the dock path uses {vessel}/{missionId} for uniqueness.
        /// When omitted, falls back to {vessel}/{captain} (legacy behavior).
        /// </summary>
        /// <param name="vessel">Target vessel.</param>
        /// <param name="captain">Captain that will use the dock.</param>
        /// <param name="branchName">Branch name for the worktree.</param>
        /// <param name="missionId">Optional mission ID for per-mission dock paths.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created dock, or null if provisioning failed.</returns>
        Task<Dock?> ProvisionAsync(Vessel vessel, Captain captain, string branchName, string? missionId = null, CancellationToken token = default);

        /// <summary>
        /// Reclaim a dock by removing the worktree.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        Task ReclaimAsync(string dockId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Repair a dock's worktree.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        Task RepairAsync(string dockId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Delete a dock by ID, cleaning up its worktree.
        /// Blocked if an active mission is using the dock.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if deleted, false if blocked by an active mission.</returns>
        Task<bool> DeleteAsync(string dockId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Force purge a dock and its worktree, even if a mission references it.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        Task PurgeAsync(string dockId, string? tenantId = null, CancellationToken token = default);
    }
}
