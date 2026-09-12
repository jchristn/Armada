namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Enums;
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
        /// Delegate that retrieves and clears accumulated agent stdout output for a mission.
        /// Wired to AgentLifecycleHandler.GetAndClearMissionOutput at startup.
        /// </summary>
        Func<string, string?>? OnGetMissionOutput { get; set; }

        /// <summary>
        /// Try to assign a mission to an available captain.
        /// </summary>
        /// <param name="mission">Mission to assign.</param>
        /// <param name="vessel">Target vessel.</param>
        /// <param name="token">Cancellation token.</param>
        Task<bool> TryAssignAsync(Mission mission, Vessel vessel, CancellationToken token = default);

        /// <summary>
        /// Evaluate the vessel's auto-land predicate against a mission's captured diff without landing it
        /// (a dry run). Returns the decision, or null when the mission or its vessel cannot be found.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The auto-land decision, or null when the mission/vessel is missing.</returns>
        Task<AutoLandDecision?> EvaluateAutoLandAsync(string missionId, CancellationToken token = default);

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
        /// Re-drive pipeline handoffs that dangled: WorkProduced missions whose Pending downstream
        /// stage was never prepared. Returns the number of missions whose handoff was re-driven.
        /// </summary>
        Task<int> RecoverDanglingHandoffsAsync(CancellationToken token = default);

        /// <summary>
        /// Approve a mission that is waiting at a review gate.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="reviewedByUserId">Reviewer user identifier, if known.</param>
        /// <param name="comment">Optional review comment.</param>
        /// <param name="conditional">When true, attach the comment to the next pipeline stage as guidance the
        /// next captain must consider ("Conditionally Approve"). Ignored when there is no downstream stage.</param>
        /// <param name="token">Cancellation token.</param>
        Task<Mission> ApproveReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, bool conditional = false, CancellationToken token = default);

        /// <summary>
        /// Deny a mission that is waiting at a review gate.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="reviewedByUserId">Reviewer user identifier, if known.</param>
        /// <param name="comment">Optional review comment.</param>
        /// <param name="actionOverride">Overrides the mission's configured deny action. RetryStage revisits the
        /// same stage with the feedback ("More Work Required"); FailPipeline rejects the stage ("Deny"). When
        /// null the mission's configured deny action is used.</param>
        /// <param name="token">Cancellation token.</param>
        Task<Mission> DenyReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, ReviewDenyActionEnum? actionOverride = null, CancellationToken token = default);

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
