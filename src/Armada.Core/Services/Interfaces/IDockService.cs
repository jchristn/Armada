namespace Armada.Core.Services.Interfaces
{
    using System.Runtime.CompilerServices;
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
        /// Reclaim a dock by removing the worktree. A dock is leased while a mission that references it is WorkProduced,
        /// Testing, Review, Verifying, PullRequestOpen or LandingFailed, or while a merge queue entry for such a mission is
        /// Queued or Testing; a leased dock is kept (the call is a no-op, logged and recorded as a dock.reclaim_refused
        /// event) unless <paramref name="forceReason"/> says why it may go. Every reclaim is recorded as a dock.reclaimed
        /// event naming the caller and the mission's status.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="forceReason">Why a leased dock may be removed anyway (for example a pipeline handoff that moves the
        /// branch to the next stage), or null to respect the lease.</param>
        /// <param name="caller">Code path asking for the reclaim; defaults to the calling member.</param>
        /// <returns>False when the reclaim was refused because the dock is leased; true otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when dockId is null or empty.</exception>
        Task<bool> ReclaimAsync(string dockId, string? tenantId = null, CancellationToken token = default, string? forceReason = null, [CallerMemberName] string caller = "");

        /// <summary>
        /// Whether a dock's worktree directory exists on the dock's host (the Admiral's disk, or its Harbor's).
        /// </summary>
        /// <param name="dock">Dock.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True or false, or null when it cannot be told (for example the dock's Harbor does not answer).</returns>
        Task<bool?> WorktreeExistsAsync(Dock dock, CancellationToken token = default) => Task.FromResult<bool?>(null);

        /// <summary>
        /// Take (and forget) why the last provisioning for a mission failed, or null when none is recorded. ProvisionAsync
        /// returns null on failure; this says which step failed and with what exception.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <returns>The failure, or null.</returns>
        DockProvisionFailure? TakeProvisionFailure(string missionId) => null;

        /// <summary>
        /// Recreate a dock's missing worktree from its branch, on the dock's own host, at the dock's recorded path. Never
        /// creates a branch: when the branch is missing from the dock's repository the restore fails. A restored dock is
        /// recorded as a dock.repaired event.
        /// </summary>
        /// <param name="dockId">Dock identifier.</param>
        /// <param name="trigger">What asked for the repair, for the event.</param>
        /// <param name="reactivate">Whether a dock already marked reclaimed may be restored (and marked active again).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The outcome.</returns>
        Task<DockRestoreResult> RestoreWorktreeAsync(string dockId, string trigger, bool reactivate = false, CancellationToken token = default)
            => Task.FromResult(DockRestoreResult.Failed(dockId, "This dock service cannot restore worktrees."));

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
