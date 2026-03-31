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
        /// Attempt auto-recovery of a failed captain.
        /// </summary>
        /// <param name="captain">Captain to recover.</param>
        /// <param name="token">Cancellation token.</param>
        Task TryRecoverAsync(Captain captain, CancellationToken token = default);

        /// <summary>
        /// Release a captain after mission completion and reset recovery counter.
        /// </summary>
        /// <param name="captain">Captain to release.</param>
        /// <param name="token">Cancellation token.</param>
        Task ReleaseAsync(Captain captain, CancellationToken token = default);
    }
}
