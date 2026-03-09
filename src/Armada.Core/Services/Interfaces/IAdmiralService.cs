namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Top-level orchestration service for coordinating missions and captains.
    /// </summary>
    public interface IAdmiralService
    {
        /// <summary>
        /// Delegate invoked when a captain needs an agent process started.
        /// The handler receives (captain, mission, dock) and should return the process ID.
        /// </summary>
        Func<Captain, Mission, Dock, Task<int>>? OnLaunchAgent { get; set; }

        /// <summary>
        /// Delegate invoked when a captain's agent process should be stopped.
        /// </summary>
        Func<Captain, Task>? OnStopAgent { get; set; }

        /// <summary>
        /// Delegate invoked synchronously at completion to capture the diff before the worktree can be reclaimed.
        /// </summary>
        Func<Mission, Dock, Task>? OnCaptureDiff { get; set; }

        /// <summary>
        /// Delegate invoked when a mission completes and branch should be pushed/PR created.
        /// The handler receives (mission, dock).
        /// </summary>
        Func<Mission, Dock, Task>? OnMissionComplete { get; set; }

        /// <summary>
        /// Dispatch a voyage with one or more missions.
        /// Creates the voyage, creates missions, and auto-assigns available captains.
        /// </summary>
        /// <param name="title">Voyage title.</param>
        /// <param name="description">Voyage description.</param>
        /// <param name="vesselId">Target vessel identifier.</param>
        /// <param name="missionDescriptions">List of mission title/description pairs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created voyage.</returns>
        Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            CancellationToken token = default);

        /// <summary>
        /// Dispatch a single mission.
        /// </summary>
        /// <param name="mission">Mission to dispatch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created and potentially assigned mission.</returns>
        Task<Mission> DispatchMissionAsync(Mission mission, CancellationToken token = default);

        /// <summary>
        /// Get aggregate status across all active work.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status summary object.</returns>
        Task<ArmadaStatus> GetStatusAsync(CancellationToken token = default);

        /// <summary>
        /// Stop a specific captain gracefully.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task RecallCaptainAsync(string captainId, CancellationToken token = default);

        /// <summary>
        /// Emergency stop all captains.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task RecallAllAsync(CancellationToken token = default);

        /// <summary>
        /// Run a single health check cycle across all active captains.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task HealthCheckAsync(CancellationToken token = default);
    }
}
