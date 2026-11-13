namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Service for captain lifecycle management.
    /// </summary>
    public interface ICaptainService
    {
        /// <summary>
        /// Delegate invoked when a captain's agent process should be stopped.
        /// </summary>
        Func<Captain, Task>? OnStopAgent { get; set; }

        /// <summary>
        /// Delegate invoked when a captain needs an agent process started.
        /// The handler receives (captain, mission, dock) and should return the process ID.
        /// </summary>
        Func<Captain, Mission, Dock, Task<int>>? OnLaunchAgent { get; set; }

        /// <summary>
        /// Stop a specific captain gracefully.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        Task RecallAsync(string captainId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Stop a specific captain gracefully on an operator's request. A mission the captain was running
        /// (InProgress or Assigned) is cancelled with FailureKind OperatorAction and a FailureReason naming who stopped
        /// it, how and when; a mission.stopped_by_operator event is emitted; and uncommitted work in its dock is
        /// committed to the mission branch as a WIP commit and pushed (best effort) before the dock is reclaimed.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="stop">Who stopped the captain and through which surface; null records a generic recall.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when captainId is null or empty.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the captain does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the agent process did not confirm it stopped.</exception>
        Task RecallAsync(string captainId, string? tenantId, OperatorStopContext? stop, CancellationToken token = default);

        /// <summary>
        /// Attempt auto-recovery of a failed captain.
        /// </summary>
        /// <param name="captain">Captain to recover.</param>
        /// <param name="token">Cancellation token.</param>
        Task TryRecoverAsync(Captain captain, CancellationToken token = default);

        /// <summary>
        /// Relaunch captains whose recovery was refused because their dock's Harbor was offline, once that Harbor is
        /// connected again, instead of leaving them until the next stall check.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of captains relaunched.</returns>
        Task<int> RetryRelaunchesAwaitingHarborAsync(CancellationToken token = default) => Task.FromResult(0);

        /// <summary>
        /// Release a captain after mission completion and reset recovery counter.
        /// </summary>
        /// <param name="captain">Captain to release.</param>
        /// <param name="token">Cancellation token.</param>
        Task ReleaseAsync(Captain captain, CancellationToken token = default);
    }
}
