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
        /// Delegate invoked when a voyage completes (all missions done).
        /// The handler receives the completed voyage.
        /// </summary>
        Func<Voyage, Task>? OnVoyageComplete { get; set; }

        /// <summary>
        /// Delegate invoked during health check to reconcile PullRequestOpen missions.
        /// The handler receives a mission and should check if its PR has been merged,
        /// returning true if the mission was reconciled (transitioned to Complete or LandingFailed).
        /// </summary>
        Func<Mission, Task<bool>>? OnReconcilePullRequest { get; set; }

        /// <summary>
        /// Delegate that checks whether a process exit has already been received and is being
        /// handled by the async exit callback. The health check uses this to avoid triggering
        /// recovery for a process whose exit handler is still in progress.
        /// </summary>
        Func<int, bool>? OnIsProcessExitHandled { get; set; }

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
        /// Dispatch a voyage with one or more missions and an ordered set of playbooks.
        /// </summary>
        /// <param name="title">Voyage title.</param>
        /// <param name="description">Voyage description.</param>
        /// <param name="vesselId">Target vessel identifier.</param>
        /// <param name="missionDescriptions">List of mission title/description pairs.</param>
        /// <param name="selectedPlaybooks">Ordered playbooks to apply to every mission in the voyage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created voyage.</returns>
        Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            List<SelectedPlaybook>? selectedPlaybooks,
            CancellationToken token = default);

        /// <summary>
        /// Dispatch a new voyage with pipeline support.
        /// When a pipelineId is provided, each mission is wrapped in the pipeline's persona stages.
        /// </summary>
        /// <param name="title">Voyage title.</param>
        /// <param name="description">Voyage description.</param>
        /// <param name="vesselId">Target vessel identifier.</param>
        /// <param name="missionDescriptions">List of mission title/description pairs.</param>
        /// <param name="pipelineId">Optional pipeline ID. Resolved: explicit > vessel default > fleet default > WorkerOnly.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created voyage.</returns>
        Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            string? pipelineId,
            CancellationToken token = default);

        /// <summary>
        /// Dispatch a new voyage with pipeline support and an ordered set of playbooks.
        /// </summary>
        /// <param name="title">Voyage title.</param>
        /// <param name="description">Voyage description.</param>
        /// <param name="vesselId">Target vessel identifier.</param>
        /// <param name="missionDescriptions">List of mission title/description pairs.</param>
        /// <param name="pipelineId">Optional pipeline ID. Resolved: explicit > vessel default > fleet default > WorkerOnly.</param>
        /// <param name="selectedPlaybooks">Ordered playbooks to apply to every mission in the voyage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created voyage.</returns>
        Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            string? pipelineId,
            List<SelectedPlaybook>? selectedPlaybooks,
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

        /// <summary>
        /// Reset captains left in Working state with dead processes after a server restart.
        /// Resets orphaned captains to Idle, transitions their active missions back to Pending,
        /// and dispatches any pending missions.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task CleanupStaleCaptainsAsync(CancellationToken token = default);

        /// <summary>
        /// Handle an agent process exit detected via the OnProcessExited event.
        /// Transitions the mission to the appropriate state, releases the captain,
        /// and reclaims the dock.
        /// </summary>
        /// <param name="processId">OS process ID that exited.</param>
        /// <param name="exitCode">Exit code, or null if unavailable.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task HandleProcessExitAsync(int processId, int? exitCode, string captainId, string missionId, CancellationToken token = default);
    }
}
