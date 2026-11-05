namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Service for mission lifecycle management.
    /// </summary>
    public interface IMissionService
    {
        /// <summary>Invalidate a mission's transient selection retry after an authorized recovery revision and notify clients.</summary>
        /// <param name="mission">The mission whose recovery authorization changed.</param>
        /// <exception cref="ArgumentNullException">Mission is null.</exception>
        void ReconsiderCaptainSelection(Mission mission);

        /// <summary>
        /// Delegate invoked synchronously at completion to capture the diff before the worktree can be reclaimed.
        /// </summary>
        Func<Mission, Dock, Task>? OnCaptureDiff { get; set; }

        /// <summary>
        /// Take the telemetry the lifecycle handler recorded for a mission's latest launch (runtime-reported usage and model), or null.
        /// </summary>
        Func<string, ChatTurnTelemetryRecorder?>? OnTakeMissionTelemetry { get; set; }

        /// <summary>
        /// Delegate invoked when a mission completes and branch should be pushed/PR created.
        /// </summary>
        Func<Mission, Dock, Task>? OnMissionComplete { get; set; }

        /// <summary>
        /// Delegate that retrieves and clears accumulated agent stdout output for a mission.
        /// Wired to AgentLifecycleHandler.GetAndClearMissionOutput at startup.
        /// </summary>
        Func<string, string?>? OnGetMissionOutput { get; set; }

        /// <summary>Read-only full preflight for observing a dispatch opportunity; never claims or launches work.</summary>
        Task<bool> IsAdmissionReadyForMonitoringAsync(Mission mission, CancellationToken token = default);

        /// <summary>Readmit a verified repair through current safety/capacity gates before relaunch.</summary>
        Task<bool> TryAdmitVerificationResumeAsync(Mission mission, Captain captain, CancellationToken token = default);

        /// <summary>
        /// Try to assign a mission to an available captain.
        /// </summary>
        /// <param name="mission">Mission to assign.</param>
        /// <param name="vessel">Target vessel.</param>
        /// <param name="token">Cancellation token.</param>
        Task<bool> TryAssignAsync(Mission mission, Vessel vessel, CancellationToken token = default);

        /// <summary>
        /// Try to assign a mission to an available captain. With <paramref name="launchInBackground"/>, returns once the
        /// mission is admitted (Assigned, captain slot claimed) and provisions the dock and launches the agent in the
        /// background; a failure there rolls the mission back to Pending for the next dispatch cycle.
        /// </summary>
        /// <param name="mission">Mission to assign.</param>
        /// <param name="vessel">Target vessel.</param>
        /// <param name="launchInBackground">Return after admission instead of after launch.</param>
        /// <param name="token">Cancellation token for admission; the background launch is not cancelled by it.</param>
        /// <returns>True when the mission was admitted (and, without background launch, launched).</returns>
        Task<bool> TryAssignAsync(Mission mission, Vessel vessel, bool launchInBackground, CancellationToken token = default);

        /// <summary>
        /// Close wait intervals that a previous Admiral process left open. Call once at startup, before dispatch.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of intervals closed.</returns>
        Task<int> CloseInterruptedWaitsAsync(CancellationToken token = default) => Task.FromResult(0);

        /// <summary>
        /// Close tracked waits for missions that are no longer Pending, given a Pending snapshot taken at
        /// <paramref name="snapshotUtc"/>.
        /// </summary>
        /// <param name="pendingMissionIds">Ids of the missions Pending in the snapshot.</param>
        /// <param name="snapshotUtc">When the snapshot was read.</param>
        /// <param name="token">Cancellation token.</param>
        Task ReconcileWaitsAsync(IEnumerable<string> pendingMissionIds, DateTime snapshotUtc, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Read-only authorized-mission preview of configurable captain selection; no claims, probes or history writes.</summary>
        Task<CaptainSelectionPreview> PreviewCaptainSelectionAsync(Mission mission, int pageNumber = 1, int pageSize = 25, CancellationToken token = default);

        /// <summary>
        /// Explain why a Pending mission is still waiting for a captain, using the same gates as
        /// <see cref="TryAssignAsync"/> without changing anything. Returns null when the mission is not Pending.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The blocker, or null.</returns>
        Task<MissionAssignmentBlocker?> GetAssignmentBlockerAsync(Mission mission, CancellationToken token = default);

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

        /// <summary>Complete only the row version owned by the agent exit handler.</summary>
        Task HandleClaimedCompletionAsync(Captain captain, string missionId, DateTime expectedVersion, CancellationToken token = default)
            => HandleCompletionAsync(captain, missionId, token);


        /// <summary>
        /// Whether a mission's completion is still being processed. Its status reads WorkProduced from the first step of
        /// completion, before the flow has decided what follows (a handoff, a rework, an output repair, landing), so voyage
        /// settlement must not treat such a mission as finished.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <returns>True while the mission's completion handler runs.</returns>
        bool IsCompletionInFlight(string missionId);

        /// <summary>
        /// Record how a run against a captain's model endpoint ended, for the endpoint circuit breaker. A healthy run
        /// closes the breaker; consecutive endpoint-level failures open it for a short window, during which captains on
        /// that endpoint get no new missions. Captains without a model endpoint are ignored.
        /// </summary>
        /// <param name="captain">Captain whose run ended.</param>
        /// <param name="healthy">True for a clean run; false for an endpoint-level failure.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        Task RecordEndpointOutcomeAsync(Captain captain, bool healthy, CancellationToken token = default);

        /// <summary>
        /// Why a Pending mission's dock could not be placed on the last dispatch attempt, or null when placement was not
        /// the obstacle.
        /// </summary>
        /// <param name="missionId">Mission ID.</param>
        /// <returns>The placement wait reason, or null.</returns>
        string? GetPlacementWaitReason(string missionId);

        /// <summary>
        /// Whether the mission's voyage binds its persona to one captain strictly ("wait for selected captain"), so no
        /// recovery path may hand it to a different captain.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True for a strict assignment.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        Task<bool> HasStrictCaptainAssignmentAsync(Mission mission, CancellationToken token = default);

        /// <summary>
        /// Re-drive pipeline handoffs that dangled: WorkProduced missions whose Pending downstream
        /// stage was never prepared. Returns the number of missions whose handoff was re-driven.
        /// </summary>
        Task<int> RecoverDanglingHandoffsAsync(CancellationToken token = default);

        /// <summary>
        /// Settle Pending pipeline stages whose dependency already ended Failed, LandingFailed or Cancelled but whose own
        /// state was never updated (a failure path that did not cascade, such as stopping the captain): they are
        /// cancelled, or Blocked when <c>BlockDependentsOfFailedStages</c> is on, exactly as the failure would have done.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of failed or cancelled missions whose waiting stages were settled.</returns>
        Task<int> SettleStagesBehindFailedDependenciesAsync(CancellationToken token = default) => Task.FromResult(0);

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
        /// Restart a Failed, LandingFailed, or Cancelled mission: back to Pending with its run state cleared. A later
        /// pipeline stage keeps the previous stage's branch (so it sees that work), a finished voyage is reopened, and
        /// the following stages that were cancelled because this one failed are restored to Pending. A Pending mission
        /// only takes the new title and description. Either way the mission is then dispatched at once when a captain
        /// can take it under the normal dispatch rules; otherwise it stays Pending for the regular dispatcher.
        /// </summary>
        /// <param name="mission">The mission (already read and access-checked by the caller).</param>
        /// <param name="title">New title, or null to keep it.</param>
        /// <param name="description">New description, or null to keep it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The restarted mission as stored after the dispatch attempt (Pending, Assigned, or InProgress).</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the mission is not Pending, Failed, LandingFailed, or Cancelled.</exception>
        Task<Mission> RestartAsync(Mission mission, string? title = null, string? description = null, CancellationToken token = default);

        /// <summary>
        /// Rewrite a mission's runtime instructions file in its dock (on the dock's Harbor for a Harbor-hosted dock), for
        /// a relaunch after the mission's description changed (for example a note about an earlier attempt).
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="captain">Captain that will run it.</param>
        /// <param name="dock">The mission's dock.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        Task RegenerateInstructionsAsync(Mission mission, Captain captain, Dock dock, CancellationToken token = default);

        /// <summary>
        /// Commit whatever a captain left uncommitted in a mission's dock (on the dock's Harbor for a Harbor-hosted dock),
        /// so the work survives when the dock is reclaimed. Judge and Architect docks are left alone.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="captain">Captain whose work it is.</param>
        /// <param name="dock">The mission's dock.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        Task CommitDockWorkAsync(Mission mission, Captain captain, Dock dock, CancellationToken token = default);

        /// <summary>
        /// Check the work a local-model captain (Mux, API endpoint) produced in a work stage before it moves on: run the
        /// vessel's workflow-profile build and unit-test commands in the mission's dock. Frontier runtimes, planning and
        /// review personas, and vessels without those commands are not checked.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="captain">Captain that just finished it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A description of the failure with the command output, or null when the check passed or did not apply.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission or captain is null.</exception>
        Task<string?> VerifyStageWorkAsync(Mission mission, Captain captain, CancellationToken token = default);

        /// <summary>
        /// Run the stage check for a mission that just finished and report what it established: passed, failed,
        /// skipped (with the reason) or could not run. Only <see cref="StageCheckStatusEnum.Passed"/> means the work
        /// was verified. <see cref="VerifyStageWorkAsync"/> returns this result's <see cref="StageCheckResult.FailureMessage"/>.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="captain">Captain that just finished it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The check result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission or captain is null.</exception>
        Task<StageCheckResult> CheckStageWorkAsync(Mission mission, Captain captain, CancellationToken token = default);

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
