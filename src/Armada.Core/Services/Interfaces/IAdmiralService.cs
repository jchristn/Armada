namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Top-level orchestration service for coordinating missions and captains.
    /// </summary>
    public interface IAdmiralService
    {
        /// <summary>
        /// Black box capture for the failures the Admiral writes (and operator cancellations through it), or null.
        /// </summary>
        IBlackBoxCapture? BlackBox => null;

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
        /// <param name="captainOverridesJson">Serialized per-persona captain overrides, persisted on the voyage
        /// before any mission is created and dispatched so assignment can honor the preferred captain.</param>
        /// <param name="approvalOptions">Per-run approval snapshot.</param>
        /// <param name="landingMode">Optional per-voyage landing mode, persisted before any mission is created; null
        /// inherits the vessel's or the global landing mode.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="isEvaluation">True for evaluation work (fixture battery, model evaluation): the voyage and every
        /// mission it creates are flagged IsEvaluation, so their stage outcomes never count as production data.</param>
        /// <returns>The created voyage.</returns>
        Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            string? pipelineId,
            List<SelectedPlaybook>? selectedPlaybooks,
            string? captainOverridesJson = null,
            LandingModeEnum? landingMode = null,
            CancellationToken token = default,
            RunApprovalOptions? approvalOptions = null,
            bool isEvaluation = false);

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
        /// Get aggregate status scoped to a caller: a global administrator sees every tenant (the same as
        /// <see cref="GetStatusAsync(CancellationToken)"/>); any other caller sees only its own tenant's captains,
        /// missions, voyages, and signals.
        /// </summary>
        /// <param name="caller">Caller identity.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status summary object.</returns>
        Task<ArmadaStatus> GetStatusAsync(AuthContext caller, CancellationToken token = default);

        /// <summary>
        /// Cancel a mission on every surface the same way: stop the captain's agent process first (on its Harbor when it
        /// runs there) and wait for the exit to be confirmed, then release the captain when it holds no other active
        /// mission, then mark the mission Cancelled. The dock is kept for inspection.
        /// </summary>
        /// <param name="mission">Mission to cancel, already read with the caller's scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated mission.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the agent process could not be stopped or its exit was not
        /// confirmed; the mission, captain and dock are left unchanged so nothing else is assigned while it may still run.</exception>
        Task<Mission> CancelMissionAsync(Mission mission, CancellationToken token = default);

        /// <summary>
        /// Stop a specific captain gracefully.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task RecallCaptainAsync(string captainId, CancellationToken token = default);

        /// <summary>
        /// Stop a specific captain on an operator's request, recording who stopped it and through which surface on the
        /// mission it was running.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="stop">Who stopped the captain and how; null records a generic recall.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        Task RecallCaptainAsync(string captainId, OperatorStopContext? stop, CancellationToken token = default)
            => RecallCaptainAsync(captainId, token);

        /// <summary>
        /// Emergency stop all captains.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task RecallAllAsync(CancellationToken token = default);

        /// <summary>
        /// Emergency stop all captains on an operator's request, recording who stopped them and through which surface
        /// on each mission they were running.
        /// </summary>
        /// <param name="stop">Who stopped the captains and how; null records a generic recall.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        Task RecallAllAsync(OperatorStopContext? stop, CancellationToken token = default)
            => RecallAllAsync(token);

        /// <summary>
        /// Stop Admiral-local agent processes without altering mission or dock state.
        /// Harbor-owned mission agents survive shutdown and are reattached before links are accepted
        /// on restart. Explicit RecallAllAsync still stops captains on every host.
        /// </summary>
        Task StopAllAgentProcessesAsync(CancellationToken token = default);

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

        /// <summary>
        /// Handle an agent process exit with its typed exit outcome (exit code plus the structured provider error the
        /// runtime reported). Quarantine, stall, and crash-loop policy read <see cref="RuntimeExitInfo.FailureKind"/>.
        /// </summary>
        /// <param name="processId">OS process ID that exited.</param>
        /// <param name="exitInfo">Typed exit outcome.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task HandleProcessExitAsync(int processId, RuntimeExitInfo exitInfo, string captainId, string missionId, CancellationToken token = default);

        /// <summary>
        /// Dry-run the vessel's auto-land predicate against a mission's captured diff without landing it.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The auto-land decision, or null when the mission/vessel is missing.</returns>
        Task<AutoLandDecision?> EvaluateAutoLandAsync(string missionId, CancellationToken token = default);

        /// <summary>
        /// Validate a voyage-dispatch request once for both the REST and MCP surfaces: check that a linked
        /// objective exists, resolve a pipeline by id or name, and decide whether the request is a bare
        /// voyage. Returns a typed result the caller maps to its own error shape.
        /// </summary>
        /// <param name="objectiveId">Optional objective id to link (must exist when supplied).</param>
        /// <param name="pipelineId">Optional explicit pipeline id.</param>
        /// <param name="pipelineName">Optional pipeline name to resolve (used when no id is supplied).</param>
        /// <param name="vesselId">Target vessel id.</param>
        /// <param name="missionCount">Number of missions in the request. At most CollectionLimits.MaxRequestItems.</param>
        /// <param name="allowBareVoyage">Whether a missing vessel / zero missions should be treated as a bare voyage rather than an error.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The typed validation result.</returns>
        /// <exception cref="Armada.Core.Services.CollectionLimitExceededException">Thrown when the request carries more than
        /// CollectionLimits.MaxRequestItems missions.</exception>
        Task<DispatchValidationResult> ValidateDispatchAsync(
            string? objectiveId,
            string? pipelineId,
            string? pipelineName,
            string? vesselId,
            int missionCount,
            bool allowBareVoyage,
            CancellationToken token = default);
    }
}
