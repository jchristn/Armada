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
        /// Have a Harbor create a mission dock on its own host: a git worktree of the vessel's checkout there (or of the
        /// Harbor's own clone) under the Harbor's docks directory. The dock records the Harbor and the repository on that
        /// host, so every later operation on it runs on that Harbor.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="captain">Captain.</param>
        /// <param name="branchName">Mission branch.</param>
        /// <param name="missionId">Mission identifier (the dock folder name).</param>
        /// <param name="harborId">Harbor to create the dock on.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The dock.</returns>
        /// <exception cref="Armada.Core.Services.DockProvisioningException">Thrown when the Harbor could not create the dock;
        /// the message says why and what to change.</exception>
        Task<Dock> ProvisionOnHarborAsync(Vessel vessel, Captain captain, string branchName, string missionId, string harborId, CancellationToken token = default);

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
        /// Operator recovery for a wedged dock: release any captain still holding it back to Idle
        /// and reclaim the dock's worktree, so a dock stuck with a non-recoverable captain does not
        /// pin capacity. Committed branch history is preserved (only the worktree is removed).
        /// </summary>
        Task UnstickAsync(string dockId, string? tenantId = null, CancellationToken token = default);

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
