namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Service for mission lifecycle management.
    /// </summary>
    public interface IMissionService
    {
        /// <summary>
        /// Delegate invoked synchronously at completion to capture the diff before the worktree can be reclaimed.
        /// </summary>
        Func<Mission, Dock, Task>? OnCaptureDiff { get; set; }

        /// <summary>
        /// Delegate invoked when a mission completes and branch should be pushed/PR created.
        /// </summary>
        Func<Mission, Dock, Task>? OnMissionComplete { get; set; }

        /// <summary>
        /// Try to assign a mission to an available captain.
        /// </summary>
        /// <param name="mission">Mission to assign.</param>
        /// <param name="vessel">Target vessel.</param>
        /// <param name="token">Cancellation token.</param>
        Task<bool> TryAssignAsync(Mission mission, Vessel vessel, CancellationToken token = default);

        /// <summary>
        /// Handle mission completion for a captain whose agent process exited successfully.
        /// </summary>
        /// <param name="captain">Captain that completed the mission.</param>
        /// <param name="token">Cancellation token.</param>
        Task HandleCompletionAsync(Captain captain, CancellationToken token = default);

        /// <summary>
        /// Handle completion for a specific mission when the captain supports parallelism.
        /// </summary>
        /// <param name="captain">Captain that completed the mission.</param>
        /// <param name="missionId">Identifier of the completed mission.</param>
        /// <param name="token">Cancellation token.</param>
        Task HandleCompletionAsync(Captain captain, string missionId, CancellationToken token = default);

        /// <summary>
        /// Detect if a mission is broad-scope (likely to touch many files).
        /// </summary>
        /// <param name="mission">Mission to check.</param>
        /// <returns>True if the mission appears to be broad-scope.</returns>
        bool IsBroadScope(Mission mission);

        /// <summary>
        /// Generate mission CLAUDE.md into a worktree.
        /// </summary>
        /// <param name="worktreePath">Worktree directory path.</param>
        /// <param name="mission">Mission details.</param>
        /// <param name="vessel">Vessel details.</param>
        /// <param name="captain">Captain assigned to the mission, or null.</param>
        /// <param name="token">Cancellation token.</param>
        Task GenerateClaudeMdAsync(string worktreePath, Mission mission, Vessel vessel, Captain? captain = null, CancellationToken token = default);
    }
}
