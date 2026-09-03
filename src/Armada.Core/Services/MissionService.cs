namespace Armada.Core.Services
{
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for mission lifecycle management.
    /// </summary>
    public class MissionService : IMissionService
    {
        #region Public-Members

        /// <inheritdoc />
        public Func<Mission, Dock, Task>? OnCaptureDiff { get; set; }

        /// <inheritdoc />
        public Func<Mission, Dock, Task>? OnMissionComplete { get; set; }

        /// <inheritdoc />
        public Func<string, string?>? OnGetMissionOutput { get; set; }

        /// <summary>
        /// Optional callback invoked when a mission enters an approval-required review state.
        /// </summary>
        public Action<Mission>? OnReviewRequested { get; set; }

        #endregion

        #region Private-Members

        private string _Header = "[MissionService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService? _Git;
        private IDockService _Docks;
        private ICaptainService _Captains;
        private IPromptTemplateService? _PromptTemplates;
        private IDefinitionOfDoneGate? _DefinitionOfDone;
        private const string ArchitectHandoffMarker = "<!-- ARMADA:ARCHITECT-HANDOFF -->";
        private const string ReviewFeedbackMarker = "<!-- ARMADA:REVIEW-FEEDBACK -->";
        private const string ReviewerGuidanceMarker = "<!-- ARMADA:REVIEWER-GUIDANCE -->";
        private static readonly System.Text.RegularExpressions.Regex _ScopedFilesDirectiveRegex =
            new System.Text.RegularExpressions.Regex(@"^\s*(?:Touch|Edit|Modify)\s+only\s+(?<files>.+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Multiline |
                System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _ScopedFileTokenRegex =
            new System.Text.RegularExpressions.Regex(
                @"(?<path>(?:[A-Za-z0-9_.-]+[\\/])+[A-Za-z0-9_.-]+|[A-Za-z0-9_.-]+\.(?:cs|csproj|sln|md|json|yaml|yml|ts|tsx|js|jsx|css|html|sh|bat))",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly HashSet<string> _IgnoredMissionArtifactFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CODEX.md",
            "CLAUDE.md",
            "MUX.md"
        };

        /// <summary>
        /// Tracks in-flight mission assignment operations by mission ID.
        /// Prevents duplicate provisioning/launch when multiple dispatch paths
        /// race on the same mission.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, byte> _InFlightAssignments =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        /// <summary>
        /// Tracks in-flight mission complete handler operations by mission ID.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, Task> _InFlightCompletions = new System.Collections.Concurrent.ConcurrentDictionary<string, Task>();

        /// <summary>
        /// Parsed mission definition extracted from an architect's output.
        /// </summary>
        private class ParsedArchitectMission
        {
            /// <summary>
            /// Mission title.
            /// </summary>
            public string Title { get; set; } = "";

            /// <summary>
            /// Mission description.
            /// </summary>
            public string Description { get; set; } = "";

            /// <summary>
            /// Optional dependency reference emitted by the architect.
            /// </summary>
            public string? DependsOnReference { get; set; } = null;
        }

        private sealed class WorktreePlaybookLocation
        {
            public string ResolvedPath { get; set; } = String.Empty;

            public string RelativePath { get; set; } = String.Empty;
        }

        private sealed class ArchitectDependencyExtractionResult
        {
            public string Description { get; set; } = String.Empty;

            public string? DependsOnReference { get; set; } = null;
        }

        private sealed class MissionOutcomeSignalDefinition
        {
            public SignalTypeEnum Type { get; set; }

            public string Payload { get; set; } = String.Empty;
        }

        private sealed class MissionOutcomeEventDefinition
        {
            public string EventType { get; set; } = String.Empty;

            public string EventMessage { get; set; } = String.Empty;
        }

        /// <summary>
        /// Verdict extracted from a judge mission's output.
        /// </summary>
        private enum JudgeVerdict
        {
            None,
            Pass,
            Fail,
            NeedsRevision
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="docks">Dock service.</param>
        /// <param name="captains">Captain service.</param>
        /// <param name="promptTemplates">Prompt template service (optional for backward compatibility).</param>
        /// <param name="git">Git service used for branch cleanup on non-landed intermediate stages.</param>
        public MissionService(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            IDockService docks,
            ICaptainService captains,
            IPromptTemplateService? promptTemplates = null,
            IGitService? git = null,
            IDefinitionOfDoneGate? definitionOfDone = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Git = git;
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
            _Captains = captains ?? throw new ArgumentNullException(nameof(captains));
            _PromptTemplates = promptTemplates;
            _DefinitionOfDone = definitionOfDone;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> TryAssignAsync(Mission mission, Vessel vessel, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            if (!_InFlightAssignments.TryAdd(mission.Id, 0))
            {
                _Logging.Debug(_Header + "mission " + mission.Id + " assignment already in flight -- skipping duplicate");
                return false;
            }

            try
            {
                Mission? latestMission = null;
                if (!String.IsNullOrEmpty(mission.TenantId))
                {
                    latestMission = await _Database.Missions.ReadAsync(mission.TenantId, mission.Id, token).ConfigureAwait(false);
                }
                if (latestMission == null)
                {
                    latestMission = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                }
                if (latestMission != null)
                {
                    mission = latestMission;
                }

                if (mission.Status != MissionStatusEnum.Pending)
                {
                    _Logging.Debug(_Header + "mission " + mission.Id + " is " + mission.Status +
                        " in the database -- skipping assignment");
                    return false;
                }

                if (!String.IsNullOrEmpty(mission.VoyageId))
                {
                    Voyage? voyage = await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
                    if (voyage != null &&
                        (voyage.Status == VoyageStatusEnum.Cancelled ||
                         voyage.Status == VoyageStatusEnum.Failed ||
                         voyage.Status == VoyageStatusEnum.Complete))
                    {
                        mission.Status = MissionStatusEnum.Cancelled;
                        mission.ProcessId = null;
                        mission.CompletedUtc = DateTime.UtcNow;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        if (String.IsNullOrWhiteSpace(mission.FailureReason))
                        {
                            mission.FailureReason = "Parent voyage " + voyage.Id + " is " + voyage.Status + ".";
                        }

                        await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                        _Logging.Info(_Header + "mission " + mission.Id + " belongs to terminal voyage " + voyage.Id +
                            " (" + voyage.Status + ") -- cancelling instead of assigning");
                        return false;
                    }
                }

                if (UsesSharedLocalAndWorkingDirectory(vessel))
                {
                    _Logging.Warn(_Header + "vessel " + vessel.Id +
                        " uses the same path for LocalPath and WorkingDirectory (" +
                        (vessel.WorkingDirectory ?? vessel.LocalPath ?? "unknown") +
                        ") -- skipping mission assignment because dock provisioning requires a separate bare repository path");
                    return false;
                }

            // Check pipeline dependency -- skip if the mission depends on another that hasn't completed
            // or if the downstream handoff has not yet populated the mission's branch/context.
            if (!String.IsNullOrEmpty(mission.DependsOnMissionId))
            {
                Mission? dependency = await _Database.Missions.ReadAsync(mission.DependsOnMissionId, token).ConfigureAwait(false);
                if (dependency == null)
                {
                    _Logging.Warn(_Header + "mission " + mission.Id + " depends on " + mission.DependsOnMissionId + " which was not found -- skipping assignment");
                    return false;
                }

                if (dependency.Status != MissionStatusEnum.Complete &&
                    dependency.Status != MissionStatusEnum.WorkProduced)
                {
                    // Dependency not yet satisfied -- don't assign
                    return false;
                }

                // A dependency in WorkProduced means the upstream agent finished, but the
                // synchronous completion flow may still be preparing the downstream mission.
                // Do not launch the next stage until it has been explicitly handed the same
                // branch as the dependency. This prevents workers from starting with the
                // original top-level dispatch prompt before architect/test/judge handoff runs.
                if (dependency.Status == MissionStatusEnum.WorkProduced &&
                    !IsPipelineHandoffPrepared(mission, dependency))
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " depends on " + dependency.Id +
                        " which is WorkProduced, but handoff is not prepared yet -- deferring assignment");
                    return false;
                }
            }

            if (await ShouldDeferArchitectSequencedMissionAsync(mission, token).ConfigureAwait(false))
            {
                _Logging.Info(_Header + "mission " + mission.Id +
                    " is architect-marked as sequential after implementation work -- deferring assignment");
                return false;
            }

            // Check for vessel-level lock (broad-scope missions block new assignments)
            List<Mission> activeMissions = await _Database.Missions.EnumerateByVesselAsync(vessel.Id, token).ConfigureAwait(false);
            List<Mission> broadMissions = activeMissions.Where(m =>
                (m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Assigned) &&
                IsBroadScope(m)).ToList();

            if (broadMissions.Count > 0)
            {
                _Logging.Warn(_Header + "vessel " + vessel.Id + " has a broad-scope mission in progress — deferring assignment of " + mission.Id);
                return false;
            }

            // Check if this mission is broad-scope and vessel already has active work
            // Only count truly active missions (agent running or assigned).
            // WorkProduced and PullRequestOpen are post-agent states where the agent
            // has finished -- they should NOT block new mission dispatch.
            int concurrentCount = activeMissions.Count(m =>
                m.Status == MissionStatusEnum.Assigned ||
                m.Status == MissionStatusEnum.InProgress);

            if (IsBroadScope(mission) && concurrentCount > 0)
            {
                _Logging.Warn(_Header + "broad-scope mission " + mission.Id + " deferred — vessel " + vessel.Id + " has " + concurrentCount + " active mission(s)");
                return false;
            }

            // Enforce per-vessel serialization unless explicitly allowed
            if (!vessel.AllowConcurrentMissions && concurrentCount > 0)
            {
                _Logging.Info(_Header + "vessel " + vessel.Id + " already has " + concurrentCount + " active mission(s); deferring " + mission.Id + " (AllowConcurrentMissions=false)");
                return false;
            }

            // Warn about concurrent missions on same vessel when allowed
            if (vessel.AllowConcurrentMissions && concurrentCount > 0)
            {
                _Logging.Warn(_Header + "vessel " + vessel.Id + " already has " + concurrentCount + " active mission(s) — potential for conflicts (AllowConcurrentMissions=true)");
            }

            // Resolve the mission's preferred captain + fallback tier (per-persona default / voyage override)
            // before assignment, so per-step captain selection applies to every mission of a step, including
            // fan-out missions that were created without an explicit dictated captain.
            await ResolvePreferredCaptainAsync(mission, token).ConfigureAwait(false);

            // Find an idle captain: the preferred captain when idle, otherwise fall back by persona/tier.
            Captain? captain = await FindAvailableCaptainAsync(mission.Persona, mission.Tier, mission.RequestedCaptainId, token).ConfigureAwait(false);
            if (captain == null)
            {
                _Logging.Warn(_Header + "no idle captains available for mission " + mission.Id +
                    (mission.Persona != null ? " (persona: " + mission.Persona + ")" : ""));
                return false;
            }

            // Missions with an existing branch continue work on that branch. This covers
            // downstream pipeline stages, review rework loops, and resumed missions.
            bool preserveExistingBranch = !String.IsNullOrEmpty(mission.BranchName);
            string branchName = preserveExistingBranch
                ? mission.BranchName!
                : BuildMissionBranchName(captain, mission);
            mission.BranchName = branchName;
            mission.CaptainId = captain.Id;
            mission.Status = MissionStatusEnum.Assigned;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            // Provision dock (worktree) and launch agent
            Dock? dock;
            try
            {
                _Logging.Info(_Header + "provisioning dock for mission " + mission.Id + " on vessel " + vessel.Id + " with captain " + captain.Id);
                dock = await _Docks.ProvisionAsync(vessel, captain, branchName, mission.Id, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "dock provisioning threw for mission " + mission.Id + " vessel " + vessel.Id + " captain " + captain.Id + ": " + ex.Message);

                // Revert mission to Pending
                mission.Status = MissionStatusEnum.Pending;
                mission.CaptainId = null;
                if (!preserveExistingBranch)
                    mission.BranchName = null;
                mission.DockId = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                // Release captain back to Idle
                await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);

                return false;
            }

            if (dock == null)
            {
                // Provisioning failed — revert mission assignment
                _Logging.Warn(_Header + "dock provisioning failed for captain " + captain.Id + " vessel " + vessel.Id + " mission " + mission.Id + " — reverting to Pending");
                mission.Status = MissionStatusEnum.Pending;
                mission.CaptainId = null;
                if (!preserveExistingBranch)
                    mission.BranchName = null;
                mission.DockId = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                return false;
            }

            // Track dock on the mission for per-mission dock tracking
            mission.DockId = dock.Id;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            // Atomically claim the captain — only succeeds if captain is still Idle.
            // This prevents a race where two concurrent TryAssignAsync calls both find
            // the same idle captain and overwrite each other's mission assignment.
            bool claimed = await _Database.Captains.TryClaimAsync(captain.Id, mission.Id, dock.Id, token).ConfigureAwait(false);
            if (!claimed)
            {
                _Logging.Warn(_Header + "captain " + captain.Id + " was claimed by another mission before we could assign " + mission.Id + " — reverting to Pending");

                mission.Status = MissionStatusEnum.Pending;
                mission.CaptainId = null;
                if (!preserveExistingBranch)
                    mission.BranchName = null;
                mission.DockId = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                // Reclaim the dock we provisioned since we can't use it
                await _Docks.ReclaimAsync(dock.Id, token: token).ConfigureAwait(false);

                return false;
            }

            // Refresh in-memory captain state to match the atomic update
            captain.State = CaptainStateEnum.Working;
            captain.CurrentMissionId = mission.Id;
            captain.CurrentDockId = dock.Id;
            captain.LastHeartbeatUtc = DateTime.UtcNow;
            captain.LastUpdateUtc = DateTime.UtcNow;

            // Create assignment signal
            Signal signal = new Signal(SignalTypeEnum.Assignment, mission.Title);
            signal.TenantId = mission.TenantId;
            signal.UserId = mission.UserId;
            signal.ToCaptainId = captain.Id;
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

            // Generate mission CLAUDE.md into worktree
            await GenerateClaudeMdAsync(dock.WorktreePath!, mission, vessel, captain, token).ConfigureAwait(false);
            await EnsureMissionInstructionsPresentAsync(dock.WorktreePath!, mission, captain, token).ConfigureAwait(false);

            // Launch agent process via captain service
            if (_Captains.OnLaunchAgent != null)
            {
                try
                {
                    int processId = await _Captains.OnLaunchAgent.Invoke(captain, mission, dock).ConfigureAwait(false);
                    captain.ProcessId = processId;
                    await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);

                    mission.ProcessId = processId;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.StartedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                    _Logging.Info(_Header + "launched agent process " + processId + " for captain " + captain.Id);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "failed to launch agent for captain " + captain.Id + ": " + ex.Message);

                    // Rollback captain state — release back to idle so it can accept future work
                    await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);

                    // Rollback mission state — revert to Pending for re-dispatch
                    mission.Status = MissionStatusEnum.Pending;
                    mission.CaptainId = null;
                    if (!preserveExistingBranch)
                        mission.BranchName = null;
                    mission.DockId = null;
                    mission.ProcessId = null;
                    mission.StartedUtc = null;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                    try
                    {
                        await _Docks.ReclaimAsync(dock.Id, token: token).ConfigureAwait(false);
                    }
                    catch (Exception reclaimEx)
                    {
                        _Logging.Warn(_Header + "failed to reclaim dock " + dock.Id +
                            " after launch failure for mission " + mission.Id + ": " + reclaimEx.Message);
                    }

                    Signal errorSignal = new Signal(SignalTypeEnum.Error, "Failed to launch agent: " + ex.Message);
                    errorSignal.TenantId = mission.TenantId;
                    errorSignal.UserId = mission.UserId;
                    errorSignal.FromCaptainId = captain.Id;
                    await _Database.Signals.CreateAsync(errorSignal, token).ConfigureAwait(false);

                    return false;
                }
            }
            else
            {
                // No launch handler configured — rollback assignment
                _Logging.Warn(_Header + "no OnLaunchAgent handler configured — cannot launch agent for captain " + captain.Id);

                await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);

                mission.Status = MissionStatusEnum.Pending;
                mission.CaptainId = null;
                if (!preserveExistingBranch)
                    mission.BranchName = null;
                mission.DockId = null;
                mission.ProcessId = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                try
                {
                    await _Docks.ReclaimAsync(dock.Id, token: token).ConfigureAwait(false);
                }
                catch (Exception reclaimEx)
                {
                    _Logging.Warn(_Header + "failed to reclaim dock " + dock.Id +
                        " after missing launch handler for mission " + mission.Id + ": " + reclaimEx.Message);
                }

                return false;
            }

            _Logging.Info(_Header + "assigned mission " + mission.Id + " to captain " + captain.Id + " at " + dock.WorktreePath);
            return true;
            }
            finally
            {
                _InFlightAssignments.TryRemove(mission.Id, out _);
            }
        }

        /// <inheritdoc />
        public async Task HandleCompletionAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(captain.CurrentMissionId)) return;

            await HandleCompletionAsync(captain, captain.CurrentMissionId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task HandleCompletionAsync(Captain captain, string missionId, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(missionId)) return;

            // In-flight deduplication: ensure only one completion handler runs per mission.
            // Both the process exit callback and the health check can trigger completion
            // concurrently for the same mission. TryAdd returns false if another caller
            // is already processing this mission.
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            if (!_InFlightCompletions.TryAdd(missionId, gate.Task))
            {
                _Logging.Debug(_Header + "mission " + missionId + " completion already in flight -- skipping duplicate");
                return;
            }

            try
            {
                await HandleCompletionCoreAsync(captain, missionId, token).ConfigureAwait(false);
            }
            finally
            {
                gate.TrySetResult(true);
                // Remove after a delay so late-arriving duplicate calls still see the entry
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    _InFlightCompletions.TryRemove(missionId, out _);
                });
            }
        }

        /// <inheritdoc />
        public async Task<Mission> ApproveReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, bool conditional = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission mission = await RequireReviewMissionAsync(missionId, token).ConfigureAwait(false);
            bool hasDependentPipelineStages = await HasDependentPipelineStages(mission.VoyageId, mission.Id, token).ConfigureAwait(false);

            mission.ReviewComment = NormalizeReviewComment(comment);
            mission.ReviewedByUserId = reviewedByUserId;
            mission.ReviewedUtc = DateTime.UtcNow;

            if (hasDependentPipelineStages)
            {
                mission.Status = MissionStatusEnum.Complete;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                await TryHandoffToNextStageAsync(mission, token).ConfigureAwait(false);

                // Conditionally Approve: fold the reviewer's guidance into the freshly-prepared next stage(s)
                // before they dispatch, so the next captain is required to take that feedback into account.
                if (conditional && !String.IsNullOrWhiteSpace(mission.ReviewComment))
                {
                    await ApplyConditionalFeedbackToNextStagesAsync(mission, mission.ReviewComment!, token).ConfigureAwait(false);
                }

                await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);

                Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                return refreshed ?? mission;
            }

            Dock? dock = await ReadMissionDockAsync(mission, token).ConfigureAwait(false);
            mission.Status = MissionStatusEnum.WorkProduced;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            if (dock == null)
            {
                mission.Status = MissionStatusEnum.LandingFailed;
                mission.FailureReason = "Review approved but the mission dock was unavailable for landing.";
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
                return mission;
            }

            if (OnMissionComplete != null)
            {
                await OnMissionComplete.Invoke(mission, dock).ConfigureAwait(false);
            }

            await ReclaimMissionDockAsync(dock.Id, token).ConfigureAwait(false);
            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);

            Mission? landed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            return landed ?? mission;
        }

        /// <inheritdoc />
        public async Task<Mission> DenyReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, ReviewDenyActionEnum? actionOverride = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission mission = await RequireReviewMissionAsync(missionId, token).ConfigureAwait(false);
            Dock? dock = await ReadMissionDockAsync(mission, token).ConfigureAwait(false);
            string reviewComment = NormalizeReviewComment(comment) ?? "Review denied. Rework is required before this stage can continue.";

            // "More Work Required" and "Deny" surface as an explicit action from the reviewer; fall back to the
            // mission's configured deny action when the caller does not specify one.
            ReviewDenyActionEnum effectiveAction = actionOverride ?? mission.ReviewDenyAction;

            mission.ReviewComment = reviewComment;
            mission.ReviewedByUserId = reviewedByUserId;
            mission.ReviewedUtc = DateTime.UtcNow;

            if (dock != null)
            {
                await ReclaimMissionDockAsync(dock.Id, token).ConfigureAwait(false);
            }

            if (effectiveAction == ReviewDenyActionEnum.FailPipeline)
            {
                mission.Status = MissionStatusEnum.Failed;
                mission.FailureReason = BuildReviewDeniedFailureReason(reviewComment);
                mission.CompletedUtc = DateTime.UtcNow;
                mission.ProcessId = null;
                mission.DockId = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
                return mission;
            }

            mission.Status = MissionStatusEnum.Pending;
            mission.Description = ApplyReviewFeedback(mission.Description, reviewComment);
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.PrUrl = null;
            mission.CommitHash = null;
            mission.DiffSnapshot = null;
            mission.AgentOutput = null;
            mission.FailureReason = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.TotalRuntimeMs = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel != null)
                {
                    await TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                    Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (refreshed != null) return refreshed;
                }
            }

            return mission;
        }

        /// <summary>
        /// Core completion logic, called under in-flight deduplication guard.
        /// </summary>
        /// <summary>
        /// If a completion looks like a no-op false-complete, re-dispatch the mission to a fresh captain
        /// (bounded by <see cref="ArmadaSettings.MaxNoOpRedispatchAttempts"/>) or, once exhausted, fail it
        /// so it surfaces in the operator inbox. Returns true when the completion was handled as a no-op
        /// (the caller must not proceed to landing).
        /// </summary>
        /// <summary>
        /// Run the pre-land dock-boundary scanner for the mission's vessel. On a finding, fail the mission
        /// with a redaction-safe reason (which surfaces it in the operator inbox) and return true so the
        /// caller does not land. Returns false when the vessel has no boundary rules or the dock is clean.
        /// </summary>
        private async Task<bool> TryFailMissionForBoundaryViolationAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.VesselId)) return false;
            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null) return false;

            DockBoundaryPolicy policy = new DockBoundaryPolicy
            {
                SecretScanEnabled = vessel.SecretScanEnabled,
                ProtectedPathGlobs = vessel.ProtectedPathPatterns ?? new List<string>(),
                PrivateIdentifiers = vessel.PrivateIdentifierDenylist ?? new List<string>(),
            };
            if (!policy.HasAnyRule()) return false;

            List<string> changedPaths = ExtractChangedPathsFromDiff(mission.DiffSnapshot);
            List<BoundaryFinding> findings = DockBoundaryScanner.Scan(mission.DiffSnapshot, changedPaths, policy);
            if (findings.Count == 0) return false;

            if (dock != null)
            {
                try { await ReclaimMissionDockAsync(dock.Id, token).ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "boundary reclaim error for mission " + mission.Id + ": " + ex.Message); }
            }

            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = DockBoundaryScanner.Summarize(findings);
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "mission " + mission.Id + " blocked by dock-boundary scanner (" + findings.Count + " finding(s))");
            await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            return true;
        }

        private static List<string> ExtractChangedPathsFromDiff(string? diff)
        {
            List<string> paths = new List<string>();
            if (String.IsNullOrEmpty(diff)) return paths;
            foreach (string raw in diff!.Replace("\r\n", "\n").Split('\n'))
            {
                if (!raw.StartsWith("+++ ", StringComparison.Ordinal)) continue;
                string body = raw.Length > 4 ? raw.Substring(4).Trim() : String.Empty;
                if (body == "/dev/null") continue;
                if (body.StartsWith("b/", StringComparison.Ordinal) || body.StartsWith("a/", StringComparison.Ordinal))
                    body = body.Substring(2);
                body = body.Replace('\\', '/').TrimStart('/').Trim();
                if (!String.IsNullOrEmpty(body)) paths.Add(body);
            }
            return paths;
        }

        /// <summary>
        /// Count changed lines (added + removed) in a unified diff, ignoring the +++/--- file headers.
        /// </summary>
        private static int CountChangedDiffLines(string? diff)
        {
            if (String.IsNullOrEmpty(diff)) return 0;
            int count = 0;
            foreach (string raw in diff!.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.StartsWith("+++", StringComparison.Ordinal) || raw.StartsWith("---", StringComparison.Ordinal)) continue;
                if (raw.StartsWith("+", StringComparison.Ordinal) || raw.StartsWith("-", StringComparison.Ordinal)) count++;
            }
            return count;
        }

        /// <summary>
        /// Evaluate the vessel's auto-land predicate against a mission that would otherwise land. When the
        /// change violates the vessel's file/line/path rules, route the mission to Review and return true so
        /// the caller holds instead of landing. Returns false when auto-land is disabled or the change is
        /// within the rules.
        /// </summary>
        /// <summary>
        /// Run the vessel's in-dock Definition-of-Done gate for a mission about to land. On a classified
        /// failure (Compile/TestFail/Timeout/Infra), fail the mission with the classified reason, cancel
        /// dependent pipeline stages, and return true so the caller does not land. Returns false when the
        /// gate is disabled/unconfigured or the change passed.
        /// </summary>
        private async Task<bool> TryFailForDefinitionOfDoneAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            if (_DefinitionOfDone == null) return false;
            if (String.IsNullOrEmpty(mission.VesselId)) return false;
            if (dock == null || String.IsNullOrEmpty(dock.WorktreePath)) return false;

            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null || !vessel.DefinitionOfDoneEnabled) return false;

            DefinitionOfDoneResult result = await _DefinitionOfDone.EvaluateAsync(vessel, dock.WorktreePath!, token).ConfigureAwait(false);
            if (result.Passed) return false;

            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = "definition_of_done_" + result.Outcome.ToString().ToLowerInvariant() + ": " + result.Detail;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "mission " + mission.Id + " failed the Definition-of-Done gate (" + result.Outcome + ")");

            await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<AutoLandDecision?> EvaluateAutoLandAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null || String.IsNullOrEmpty(mission.VesselId)) return null;

            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId!, token).ConfigureAwait(false);
            if (vessel == null) return null;

            List<string> changedPaths = ExtractChangedPathsFromDiff(mission.DiffSnapshot);
            AutoLandPolicy policy = new AutoLandPolicy
            {
                Enabled = vessel.AutoLandEnabled,
                MaxFiles = vessel.AutoLandMaxFiles,
                MaxLines = vessel.AutoLandMaxLines,
                PathAllowGlobs = vessel.AutoLandPathAllowGlobs ?? new List<string>(),
                PathDenyGlobs = vessel.AutoLandPathDenyGlobs ?? new List<string>(),
            };

            return AutoLandPredicate.Evaluate(changedPaths.Count, CountChangedDiffLines(mission.DiffSnapshot), changedPaths, policy);
        }

        private async Task<bool> TryHoldForAutoLandAsync(Mission mission, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.VesselId)) return false;
            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null || !vessel.AutoLandEnabled) return false;

            List<string> changedPaths = ExtractChangedPathsFromDiff(mission.DiffSnapshot);
            int filesChanged = changedPaths.Count;
            int linesChanged = CountChangedDiffLines(mission.DiffSnapshot);

            AutoLandPolicy policy = new AutoLandPolicy
            {
                Enabled = true,
                MaxFiles = vessel.AutoLandMaxFiles,
                MaxLines = vessel.AutoLandMaxLines,
                PathAllowGlobs = vessel.AutoLandPathAllowGlobs ?? new List<string>(),
                PathDenyGlobs = vessel.AutoLandPathDenyGlobs ?? new List<string>(),
            };

            AutoLandDecision decision = AutoLandPredicate.Evaluate(filesChanged, linesChanged, changedPaths, policy);
            if (decision.Land) return false;

            mission.Status = MissionStatusEnum.Review;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.ReviewRequestedUtc = DateTime.UtcNow;
            mission.ReviewDeadlineUtc = _Settings.ReviewTimeoutMinutes > 0
                ? DateTime.UtcNow.AddMinutes(_Settings.ReviewTimeoutMinutes)
                : (DateTime?)null;
            mission.ReviewComment = "Auto-land held: " + (decision.HoldReason ?? "change exceeds auto-land rules");
            mission.ReviewedByUserId = null;
            mission.ReviewedUtc = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            OnReviewRequested?.Invoke(mission);
            _Logging.Info(_Header + "auto-land held mission " + mission.Id + " for review: " + (decision.HoldReason ?? "rules"));
            return true;
        }

        private async Task<bool> TryRejectNoOpCompletionAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            if (!NoOpCompletionDetector.IsNoOp(mission.DiffSnapshot, mission.AgentOutput, mission.TotalRuntimeMs))
                return false;

            // Release the dock/captain before re-dispatching or failing.
            if (dock != null)
            {
                try { await ReclaimMissionDockAsync(dock.Id, token).ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "no-op reclaim error for mission " + mission.Id + ": " + ex.Message); }
            }

            int maxAttempts = _Settings.MaxNoOpRedispatchAttempts;
            if (mission.RedispatchAttempts < maxAttempts)
            {
                mission.RedispatchAttempts++;
                mission.Status = MissionStatusEnum.Pending;
                mission.CaptainId = null;
                mission.DockId = null;
                mission.ProcessId = null;
                mission.PrUrl = null;
                mission.CommitHash = null;
                mission.DiffSnapshot = null;
                mission.AgentOutput = null;
                mission.FailureReason = null;
                mission.StartedUtc = null;
                mission.CompletedUtc = null;
                mission.TotalRuntimeMs = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                _Logging.Warn(_Header + "no-op completion detected for mission " + mission.Id +
                    "; re-dispatching to a fresh captain (attempt " + mission.RedispatchAttempts + "/" + maxAttempts + ")");

                if (!String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                    if (vessel != null) await TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                }
                return true;
            }

            // Retries exhausted: fail and surface to the operator inbox (LandingFailed/Failed feed it).
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = "no_op_completion_detected: captain repeatedly completed with no changes after " +
                mission.RedispatchAttempts + " re-dispatch attempt(s). Likely an impossible or mis-specified mission.";
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "no-op completion for mission " + mission.Id +
                " exhausted re-dispatch attempts; failing to operator inbox");
            await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            return true;
        }

        private async Task HandleCompletionCoreAsync(Captain captain, string missionId, CancellationToken token)
        {
            Mission? mission = null;
            if (!String.IsNullOrEmpty(captain.TenantId))
            {
                mission = await _Database.Missions.ReadAsync(captain.TenantId, missionId, token).ConfigureAwait(false);
            }
            if (mission == null)
            {
                mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            }
            if (mission == null) return;

            // Idempotency guard: if the mission has already been processed (e.g. by a concurrent
            // health check or process exit handler), skip to avoid double-processing.
            if (mission.Status == MissionStatusEnum.WorkProduced ||
                mission.Status == MissionStatusEnum.Complete ||
                mission.Status == MissionStatusEnum.Failed ||
                mission.Status == MissionStatusEnum.Cancelled ||
                mission.Status == MissionStatusEnum.LandingFailed ||
                mission.Status == MissionStatusEnum.PullRequestOpen)
            {
                _Logging.Debug(_Header + "mission " + missionId + " already in post-work state " + mission.Status + " -- skipping completion handler");
                return;
            }

            // Read-only modes (Audit/Research) produce a written report, not a commit. Their empty diff is a
            // successful outcome, so they skip no-op rejection and never attempt landing.
            bool isReadOnlyMission = MissionModeContract.IsReadOnly(mission.Mode);

            // Mark mission as work produced (agent finished, landing not yet attempted)
            mission.Status = MissionStatusEnum.WorkProduced;
            mission.ProcessId = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Info(_Header + "mission " + mission.Id + " work produced by captain " + captain.Id);

            // Get dock for diff capture (prefer mission-level DockId, fall back to captain-level)
            Dock? dock = null;
            string? dockId = mission.DockId ?? captain.CurrentDockId;
            if (!String.IsNullOrEmpty(dockId))
            {
                dock = !String.IsNullOrEmpty(mission.TenantId)
                    ? await _Database.Docks.ReadAsync(mission.TenantId, dockId, token).ConfigureAwait(false)
                    : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            }

            if (dock != null && String.IsNullOrEmpty(mission.BranchName) && !String.IsNullOrEmpty(dock.BranchName))
            {
                mission.BranchName = dock.BranchName;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                _Logging.Info(_Header + "backfilled branch " + dock.BranchName + " onto mission " + mission.Id +
                    " from dock " + dock.Id + " before pipeline handoff");
            }

            // Capture diff BEFORE pipeline handoff so the next stage gets the actual diff
            if (dock != null && OnCaptureDiff != null)
            {
                try
                {
                    await OnCaptureDiff.Invoke(mission, dock).ConfigureAwait(false);
                    // Re-read mission to get the persisted DiffSnapshot
                    Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (refreshed != null && !String.IsNullOrEmpty(refreshed.DiffSnapshot))
                    {
                        mission.DiffSnapshot = refreshed.DiffSnapshot;
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error capturing diff for mission " + mission.Id + ": " + ex.Message);
                }
            }

            bool failedForScopeViolation = false;
            if (dock != null)
            {
                failedForScopeViolation = await TryFailMissionForScopeViolationAsync(mission, dock, token).ConfigureAwait(false);
            }

            // Capture accumulated agent stdout output before pipeline handoff
            if (OnGetMissionOutput != null)
            {
                string? agentOutput = OnGetMissionOutput(mission.Id);
                if (!String.IsNullOrEmpty(agentOutput))
                {
                    mission.AgentOutput = agentOutput;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                    // Best-effort token accounting for the mission run. Mission output is human-readable
                    // (no provider usage report), so both counts are estimated from text length and flagged.
                    await TokenUsageCapture.CaptureAsync(
                        _Database, _Logging, "mission",
                        model: captain.Model,
                        runtime: captain.Runtime.ToString(),
                        tenantId: mission.TenantId,
                        userId: null,
                        vesselId: String.IsNullOrEmpty(mission.VesselId) ? null : mission.VesselId,
                        captainId: captain.Id,
                        sourceId: mission.Id,
                        inputTokens: null,
                        outputTokens: null,
                        cachedTokens: null,
                        inputText: mission.Description,
                        outputText: agentOutput,
                        token: token).ConfigureAwait(false);
                }
            }

            // Reject a no-op false-complete (fast exit, empty diff, trivial output): re-dispatch to a
            // fresh captain up to a bounded number of times, then fail to the operator inbox. Skipped
            // when the mission already failed scope validation.
            if (!failedForScopeViolation && !isReadOnlyMission &&
                await TryRejectNoOpCompletionAsync(mission, dock, token).ConfigureAwait(false))
            {
                return;
            }

            // Pre-land dock-boundary scan: secrets, protected paths, private-identifier leaks. A finding
            // blocks landing and surfaces the mission (with a redaction-safe reason) in the operator inbox.
            if (!failedForScopeViolation &&
                await TryFailMissionForBoundaryViolationAsync(mission, dock, token).ConfigureAwait(false))
            {
                return;
            }

            if (!failedForScopeViolation && String.Equals(mission.Persona, "Judge", StringComparison.OrdinalIgnoreCase))
            {
                JudgeVerdict verdict = ParseJudgeVerdict(mission.AgentOutput);
                string? verdictFailureReason = null;
                bool rejectedPass = false;

                // A PASS must be substantiated (three lenses + real narrative). A rejected PASS is not
                // silently re-run: it is downgraded to a blocking verdict and the mission fails terminally
                // with an explicit reason so an operator sees it rather than a quiet re-dispatch.
                if (verdict == JudgeVerdict.Pass && !TryValidateJudgePassOutput(mission.AgentOutput, out verdictFailureReason))
                {
                    verdict = JudgeVerdict.NeedsRevision;
                    rejectedPass = true;
                }

                if (verdict != JudgeVerdict.Pass)
                {
                    // To block, the Judge must exhibit a concrete affected case. A block without one is a
                    // contract violation: the mission still fails terminally, but the reason makes clear the
                    // Judge did not substantiate the block rather than the work being definitively wrong.
                    bool isBlockingVerdict = verdict == JudgeVerdict.Fail || verdict == JudgeVerdict.NeedsRevision;
                    bool exhibitsAffectedCase = JudgeContract.ExhibitsAffectedCase(mission.AgentOutput);

                    string blockingReason;
                    if (rejectedPass)
                    {
                        blockingReason = "Judge PASS rejected (" + (verdictFailureReason ?? "unsubstantiated approval") + ")";
                    }
                    else if (isBlockingVerdict && !exhibitsAffectedCase)
                    {
                        blockingReason = "Judge verdict: " + (verdict == JudgeVerdict.Fail ? "FAIL" : "NEEDS_REVISION") +
                            " but did not exhibit a concrete affected case; a block must cite a real affected file, line, or scenario";
                    }
                    else
                    {
                        blockingReason = verdict switch
                        {
                            JudgeVerdict.Fail => "Judge verdict: FAIL",
                            JudgeVerdict.NeedsRevision => "Judge verdict: NEEDS_REVISION",
                            _ => "Judge mission did not emit an explicit PASS verdict"
                        };
                    }

                    mission.Status = MissionStatusEnum.Failed;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    mission.FailureReason = blockingReason;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    _Logging.Warn(_Header + "judge mission " + mission.Id + " blocked landing: " + blockingReason);
                }
            }

            bool hasDependentPipelineStages = await HasDependentPipelineStages(mission.VoyageId, mission.Id, token).ConfigureAwait(false);
            bool awaitingManualReview = false;
            if (!failedForScopeViolation &&
                mission.Status == MissionStatusEnum.WorkProduced &&
                mission.RequiresReview)
            {
                mission.Status = MissionStatusEnum.Review;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.ReviewRequestedUtc = DateTime.UtcNow;
                mission.ReviewDeadlineUtc = _Settings.ReviewTimeoutMinutes > 0
                    ? DateTime.UtcNow.AddMinutes(_Settings.ReviewTimeoutMinutes)
                    : (DateTime?)null;
                mission.ReviewComment = null;
                mission.ReviewedByUserId = null;
                mission.ReviewedUtc = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                OnReviewRequested?.Invoke(mission);
                awaitingManualReview = true;
            }

            // Pipeline handoff: if missions in the same voyage depend on this one, prepare them
            bool preparedDownstreamStages = false;
            if (!failedForScopeViolation && !awaitingManualReview)
            {
                preparedDownstreamStages = await TryHandoffToNextStageAsync(mission, token).ConfigureAwait(false);
            }

            Mission? missionAfterHandoff = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            if (missionAfterHandoff != null)
            {
                mission = missionAfterHandoff;
            }

            if (mission.Status == MissionStatusEnum.Failed ||
                mission.Status == MissionStatusEnum.Cancelled ||
                mission.Status == MissionStatusEnum.LandingFailed)
            {
                await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            }

            await EmitMissionOutcomeTelemetryAsync(mission, captain, token).ConfigureAwait(false);

            bool shouldAttemptLanding =
                !awaitingManualReview &&
                !preparedDownstreamStages &&
                !hasDependentPipelineStages &&
                !isReadOnlyMission &&
                (mission.Status == MissionStatusEnum.WorkProduced ||
                mission.Status == MissionStatusEnum.PullRequestOpen);

            // Read-only missions (Audit/Research) that are not held for review or feeding a pipeline stage
            // complete directly: there is nothing to land, and an empty diff is the expected outcome.
            if (isReadOnlyMission &&
                !awaitingManualReview &&
                !preparedDownstreamStages &&
                !hasDependentPipelineStages &&
                mission.Status == MissionStatusEnum.WorkProduced)
            {
                mission.Status = MissionStatusEnum.Complete;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
                _Logging.Info(_Header + "read-only " + mission.Mode + " mission " + mission.Id +
                    " completed without landing (no commit expected)");
            }

            // In-dock Definition-of-Done gate: run the vessel's build + unit tests inside the mission's own
            // checkout before acceptance. A classified failure (Compile/TestFail/Timeout/Infra) blocks
            // landing and fails the mission so it surfaces (and can be recovered).
            if (shouldAttemptLanding && await TryFailForDefinitionOfDoneAsync(mission, dock, token).ConfigureAwait(false))
            {
                shouldAttemptLanding = false;
            }

            // Per-vessel auto-land predicate: a change that is too large or touches denied/out-of-scope
            // paths holds for review instead of landing unattended.
            if (shouldAttemptLanding && await TryHoldForAutoLandAsync(mission, token).ConfigureAwait(false))
            {
                shouldAttemptLanding = false;
                awaitingManualReview = true;
            }

            if (!shouldAttemptLanding)
            {
                _Logging.Info(_Header + "skipping landing for mission " + mission.Id +
                    " because it is not a terminal landed stage yet (status: " + mission.Status + ")");
            }

            // Invoke OnMissionComplete synchronously (Phase A: push branch, create PR, or enqueue).
            // Captain stays in Working state until the handoff completes, preventing the captain
            // from being reassigned while git operations are still in progress.
            if (shouldAttemptLanding && dock != null && OnMissionComplete != null)
            {
                _Logging.Info(_Header + "executing synchronous landing handoff for mission " + mission.Id);
                try
                {
                    await OnMissionComplete.Invoke(mission, dock).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error in mission complete handler for " + mission.Id + ": " + ex.Message);
                }
            }

            // Reclaim the dock after the handoff completes (or if no handler was set)
            string? completionDockId = dock?.Id;
            bool cleanupArchitectBranch =
                preparedDownstreamStages &&
                String.Equals(mission.Persona, "Architect", StringComparison.OrdinalIgnoreCase);
            bool retainDockForReview =
                awaitingManualReview &&
                !hasDependentPipelineStages &&
                !String.IsNullOrEmpty(completionDockId);

            if (!String.IsNullOrEmpty(completionDockId) && !retainDockForReview)
            {
                try
                {
                    await _Docks.ReclaimAsync(completionDockId, token: token).ConfigureAwait(false);
                }
                catch (Exception reclaimEx)
                {
                    _Logging.Warn(_Header + "error reclaiming dock " + completionDockId + " after mission " + mission.Id + ": " + reclaimEx.Message);
                }
            }

            if (cleanupArchitectBranch)
            {
                await CleanupArchitectBranchAsync(mission, dock, token).ConfigureAwait(false);
            }

            MissionOutcomeSignalDefinition missionOutcomeSignal = BuildMissionOutcomeSignal(mission);
            Signal signal = new Signal(missionOutcomeSignal.Type, missionOutcomeSignal.Payload);
            signal.FromCaptainId = captain.Id;
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

            // Release the captain to idle only AFTER the handoff and dock reclaim are done,
            // and only if the captain is still assigned to this mission. Orphan recovery can
            // finalize an older mission using a captain record that has already moved on.
            Captain? latestCaptain = await _Database.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false);
            if (latestCaptain != null && latestCaptain.CurrentMissionId == mission.Id)
            {
                await _Captains.ReleaseAsync(latestCaptain, token).ConfigureAwait(false);
            }
            else
            {
                _Logging.Info(_Header + "skipping captain release for mission " + mission.Id +
                    " because captain " + captain.Id + " is now assigned to " + (latestCaptain?.CurrentMissionId ?? "nothing"));
            }

            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public bool IsBroadScope(Mission mission)
        {
            if (mission == null) return false;

            string text = ((mission.Title ?? "") + " " + (mission.Description ?? "")).ToLowerInvariant();

            string[] broadIndicators = new[]
            {
                "refactor entire",
                "refactor all",
                "rename across",
                "migrate project",
                "upgrade framework",
                "restructure",
                "rewrite",
                "overhaul",
                "global search and replace",
                "update all",
                "format all",
                "lint entire"
            };

            foreach (string indicator in broadIndicators)
            {
                if (text.Contains(indicator)) return true;
            }

            return false;
        }

        /// <inheritdoc />
        public async Task GenerateClaudeMdAsync(string worktreePath, Mission mission, Vessel vessel, Captain? captain = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            string instructionsFileName = MissionPromptBuilder.GetInstructionsFileName(captain != null ? captain.Runtime.ToString() : null);
            string instructionsPath = Path.Combine(worktreePath, instructionsFileName);

            Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(mission, vessel, captain);
            List<MissionPlaybookSnapshot> playbookSnapshots = await LoadMissionPlaybookSnapshotsAsync(mission, token).ConfigureAwait(false);

            string content = "";

            // Captain instructions
            if (captain != null && !String.IsNullOrEmpty(captain.SystemInstructions))
            {
                content += await ResolveSectionAsync("mission.captain_instructions_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            // Vessel context sections
            if (!String.IsNullOrEmpty(vessel.ProjectContext))
            {
                content += await ResolveSectionAsync("mission.project_context_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            if (!String.IsNullOrEmpty(vessel.StyleGuide))
            {
                content += await ResolveSectionAsync("mission.code_style_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            if (vessel.EnableModelContext && !String.IsNullOrEmpty(vessel.ModelContext))
            {
                content += await ResolveSectionAsync("mission.model_context_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            if (playbookSnapshots.Count > 0)
            {
                templateParams["SelectedPlaybooksMarkdown"] = await RenderSelectedPlaybooksMarkdownAsync(
                    worktreePath,
                    mission,
                    playbookSnapshots,
                    token).ConfigureAwait(false);
                content += await ResolveSectionAsync("mission.playbooks_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            // Inject the vessel's project-profile skills (best-effort) as a Skills section.
            string skillsMarkdown = await ResolveSkillsMarkdownAsync(vessel, token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(skillsMarkdown))
            {
                content += "## Skills\n\n" + skillsMarkdown + "\n";
            }

            // Mission preamble and metadata -- resolve persona prompt first, then inject into metadata template.
            // Apply the vessel's project-profile persona override (if any) so per-project customization takes effect.
            PersonaOverride? personaOverride = await ResolvePersonaOverrideAsync(vessel, mission.Persona, token).ConfigureAwait(false);
            string personaPrompt = await ResolvePersonaPromptAsync(mission.Persona, templateParams, personaOverride, token).ConfigureAwait(false);
            templateParams["PersonaPrompt"] = personaPrompt;
            content += await ResolveSectionAsync("mission.metadata", templateParams, token).ConfigureAwait(false);
            content += "\n";

            // Mission-mode contract: read-only modes (Audit/Research) get a report-shaped brief that forbids
            // repository changes and states that an empty diff is a successful outcome. Write missions add nothing.
            string missionModeSection = MissionModeContract.BuildBriefSection(mission.Mode);
            if (!String.IsNullOrEmpty(missionModeSection))
            {
                content += missionModeSection;
                content += "\n";
            }

            // Resolved git anchors (start commit, target branch, working branch) so the captain does not
            // burn opening turns deriving them. Best-effort: a git failure degrades to no section.
            string? headCommit = null;
            if (_Git != null)
            {
                try { headCommit = await _Git.GetHeadCommitHashAsync(worktreePath, token).ConfigureAwait(false); }
                catch { headCommit = null; }
            }
            string gitAnchors = GitAnchorsFormatter.Render(headCommit, vessel.DefaultBranch, mission.BranchName);
            if (!String.IsNullOrEmpty(gitAnchors))
            {
                content += gitAnchors;
                content += "\n";
            }

            // Rules, context conservation, merge conflicts, progress signals -- from templates or hardcoded fallback
            content += await ResolveSectionAsync("mission.rules", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.context_conservation", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.merge_conflict_avoidance", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.progress_signals", templateParams, token).ConfigureAwait(false);

            // Papercuts apply to every mission mode: an audit meets stale docs and dead links exactly as
            // an implementation does. Judge and Architect are excluded -- a judge reports what it finds
            // through its verdict, and an architect emits mission blocks only.
            if (!PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge) &&
                !PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect))
            {
                content += "\n";
                content += BuildPapercutsSection();
            }

            // Model context updates
            if (vessel.EnableModelContext)
            {
                content += "\n";
                content += await ResolveSectionAsync("mission.model_context_updates", templateParams, token).ConfigureAwait(false);
            }

            // If there's an existing runtime instruction file, preserve it and prepend our instructions
            if (File.Exists(instructionsPath))
            {
                string existing = await File.ReadAllTextAsync(instructionsPath).ConfigureAwait(false);
                string sanitizedExisting = SanitizeExistingInstructions(existing);

                if (!String.IsNullOrWhiteSpace(sanitizedExisting))
                {
                    if (!String.Equals(existing, sanitizedExisting, StringComparison.Ordinal))
                    {
                        _Logging.Info(_Header + "sanitized generated mission sections from existing instructions at " + instructionsPath);
                    }

                    templateParams["ExistingClaudeMd"] = sanitizedExisting;
                    content += await ResolveSectionAsync("mission.existing_instructions_wrapper", templateParams, token).ConfigureAwait(false);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(instructionsPath)!);
            await File.WriteAllTextAsync(instructionsPath, content).ConfigureAwait(false);

            // Persist a stable copy outside the dock so the dashboard and APIs can still
            // show the generated mission instructions after the worktree is reclaimed.
            try
            {
                string instructionsSnapshotDir = Path.Combine(_Settings.LogDirectory, "instructions");
                Directory.CreateDirectory(instructionsSnapshotDir);
                string snapshotPath = Path.Combine(instructionsSnapshotDir, mission.Id + "." + instructionsFileName);
                await File.WriteAllTextAsync(snapshotPath, content).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not persist mission instructions snapshot for " + mission.Id + ": " + ex.Message);
            }

            // Ensure the generated instruction file is ignored locally so agents don't commit it.
            // Mission instructions are ephemeral and should not alter tracked repository files.
            try
            {
                string? excludePath = ResolveGitInfoExcludePath(worktreePath);
                if (!String.IsNullOrEmpty(excludePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(excludePath)!);
                    string excludeContent = File.Exists(excludePath)
                        ? await File.ReadAllTextAsync(excludePath).ConfigureAwait(false)
                        : "";
                    bool hasEntry = excludeContent
                        .Split('\n')
                        .Select(l => l.Trim())
                        .Any(l => String.Equals(l, instructionsFileName, StringComparison.Ordinal));
                    if (!hasEntry)
                    {
                        string entry = (excludeContent.Length > 0 && !excludeContent.EndsWith("\n") ? "\n" : "") + instructionsFileName + "\n";
                        await File.AppendAllTextAsync(excludePath, entry).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not update git exclude for " + instructionsFileName + ": " + ex.Message);
            }

            _Logging.Info(_Header + "generated mission instructions at " + instructionsPath);
        }

        private async Task<List<MissionPlaybookSnapshot>> LoadMissionPlaybookSnapshotsAsync(Mission mission, CancellationToken token)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.PlaybookSnapshots != null && mission.PlaybookSnapshots.Count > 0)
                return mission.PlaybookSnapshots;

            if (String.IsNullOrEmpty(mission.Id))
                return new List<MissionPlaybookSnapshot>();

            List<MissionPlaybookSnapshot> snapshots = await _Database.Playbooks
                .GetMissionSnapshotsAsync(mission.Id, token)
                .ConfigureAwait(false);
            mission.PlaybookSnapshots = snapshots;
            return snapshots;
        }

        private async Task<string> RenderSelectedPlaybooksMarkdownAsync(
            string worktreePath,
            Mission mission,
            List<MissionPlaybookSnapshot> snapshots,
            CancellationToken token)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (snapshots == null || snapshots.Count == 0) return String.Empty;

            List<string> sections = new List<string>();
            bool snapshotStateChanged = false;

            for (int i = 0; i < snapshots.Count; i++)
            {
                MissionPlaybookSnapshot snapshot = snapshots[i];
                string header = "### " + snapshot.FileName;
                string? description = String.IsNullOrWhiteSpace(snapshot.Description) ? null : snapshot.Description.Trim();

                switch (snapshot.DeliveryMode)
                {
                    case PlaybookDeliveryModeEnum.InstructionWithReference:
                        string resolvedPath = await MaterializeReferencePlaybookAsync(mission, snapshot, i, token).ConfigureAwait(false);
                        if (!String.Equals(snapshot.ResolvedPath, resolvedPath, StringComparison.Ordinal))
                        {
                            snapshot.ResolvedPath = resolvedPath;
                            snapshot.WorktreeRelativePath = null;
                            snapshotStateChanged = true;
                        }

                        sections.Add(
                            header + "\n" +
                            (description != null ? description + "\n" : "") +
                            "Read and follow this playbook at `" + resolvedPath + "`.");
                        break;

                    case PlaybookDeliveryModeEnum.AttachIntoWorktree:
                        WorktreePlaybookLocation attachedPlaybook = await MaterializeWorktreePlaybookAsync(
                            worktreePath,
                            snapshot,
                            i,
                            token).ConfigureAwait(false);
                        if (!String.Equals(snapshot.ResolvedPath, attachedPlaybook.ResolvedPath, StringComparison.Ordinal) ||
                            !String.Equals(snapshot.WorktreeRelativePath, attachedPlaybook.RelativePath, StringComparison.Ordinal))
                        {
                            snapshot.ResolvedPath = attachedPlaybook.ResolvedPath;
                            snapshot.WorktreeRelativePath = attachedPlaybook.RelativePath;
                            snapshotStateChanged = true;
                        }

                        sections.Add(
                            header + "\n" +
                            (description != null ? description + "\n" : "") +
                            "Read and follow this attached playbook at `" + attachedPlaybook.RelativePath.Replace("\\", "/") + "`.");
                        break;

                    default:
                        sections.Add(
                            header + "\n" +
                            (description != null ? description + "\n\n" : "") +
                            snapshot.Content.TrimEnd());
                        break;
                }
            }

            if (snapshotStateChanged)
            {
                await _Database.Playbooks.SetMissionSnapshotsAsync(mission.Id, snapshots, token).ConfigureAwait(false);
            }

            return String.Join("\n\n", sections);
        }

        private async Task<string> MaterializeReferencePlaybookAsync(
            Mission mission,
            MissionPlaybookSnapshot snapshot,
            int selectionOrder,
            CancellationToken token)
        {
            string playbookDir = Path.Combine(_Settings.LogDirectory, "playbooks", mission.Id);
            Directory.CreateDirectory(playbookDir);

            string fileName = BuildMaterializedPlaybookFileName(selectionOrder, snapshot.FileName);
            string resolvedPath = Path.Combine(playbookDir, fileName);
            await File.WriteAllTextAsync(resolvedPath, snapshot.Content, token).ConfigureAwait(false);
            return resolvedPath;
        }

        private async Task<WorktreePlaybookLocation> MaterializeWorktreePlaybookAsync(
            string worktreePath,
            MissionPlaybookSnapshot snapshot,
            int selectionOrder,
            CancellationToken token)
        {
            string relativeDir = Path.Combine(".armada", "playbooks");
            string absoluteDir = Path.Combine(worktreePath, relativeDir);
            Directory.CreateDirectory(absoluteDir);

            string fileName = BuildMaterializedPlaybookFileName(selectionOrder, snapshot.FileName);
            string absolutePath = Path.Combine(absoluteDir, fileName);
            await File.WriteAllTextAsync(absolutePath, snapshot.Content, token).ConfigureAwait(false);

            string? excludePath = ResolveGitInfoExcludePath(worktreePath);
            if (!String.IsNullOrEmpty(excludePath))
            {
                await EnsureGitExcludeEntryAsync(excludePath, ".armada/playbooks/", token).ConfigureAwait(false);
            }

            return new WorktreePlaybookLocation
            {
                ResolvedPath = absolutePath,
                RelativePath = Path.Combine(relativeDir, fileName)
            };
        }

        private async Task EnsureGitExcludeEntryAsync(string excludePath, string entry, CancellationToken token)
        {
            if (String.IsNullOrEmpty(excludePath)) return;
            if (String.IsNullOrEmpty(entry)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(excludePath)!);
            string excludeContent = File.Exists(excludePath)
                ? await File.ReadAllTextAsync(excludePath, token).ConfigureAwait(false)
                : "";
            bool hasEntry = excludeContent
                .Split('\n')
                .Select(l => l.Trim())
                .Any(l => String.Equals(l, entry, StringComparison.Ordinal));
            if (hasEntry) return;

            string suffix = excludeContent.Length > 0 && !excludeContent.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "";
            await File.AppendAllTextAsync(excludePath, suffix + entry + "\n", token).ConfigureAwait(false);
        }

        private static string BuildMaterializedPlaybookFileName(int selectionOrder, string? originalFileName)
        {
            string safeName = String.IsNullOrWhiteSpace(originalFileName) ? "PLAYBOOK.md" : originalFileName.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalid, '_');
            }

            return (selectionOrder + 1).ToString("D2") + "_" + safeName;
        }

        /// <summary>
        /// Resolve a persona prompt template by persona name. Falls back to default worker preamble.
        /// </summary>
        private async Task<string> ResolvePersonaPromptAsync(string? persona, Dictionary<string, string> templateParams, PersonaOverride? personaOverride, CancellationToken token)
        {
            return await MissionPromptBuilder.ResolvePersonaPromptAsync(persona, templateParams, _PromptTemplates, personaOverride, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolve the effective per-project persona override for a mission's persona by selecting the
        /// vessel's project profile (vessel -> fleet -> global). Best-effort: returns null on any error so
        /// a profile lookup never blocks dispatch.
        /// </summary>
        private async Task<PersonaOverride?> ResolvePersonaOverrideAsync(Vessel vessel, string? persona, CancellationToken token)
        {
            if (vessel == null || String.IsNullOrWhiteSpace(persona)) return null;

            try
            {
                List<ProjectProfile> profiles = await _Database.ProjectProfiles.EnumerateAllAsync(
                    new ProjectProfileQuery
                    {
                        TenantId = vessel.TenantId,
                        Active = true,
                        PageNumber = 1,
                        PageSize = 1000
                    },
                    token).ConfigureAwait(false);

                if (profiles.Count == 0) return null;

                ProjectProfile? profile = ProjectProfileService.SelectForVessel(profiles, vessel);
                return ProjectProfileService.ResolvePersonaOverride(profile, persona);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error resolving persona override for vessel " + vessel.Id + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Resolve the vessel's project-profile skills into a combined markdown block for prompt
        /// injection. Best-effort: returns an empty string on any error or when no profile/skills apply.
        /// Skill references in the profile may be skill ids (skl_...) or skill names.
        /// </summary>
        private async Task<string> ResolveSkillsMarkdownAsync(Vessel vessel, CancellationToken token)
        {
            if (vessel == null) return String.Empty;

            try
            {
                List<ProjectProfile> profiles = await _Database.ProjectProfiles.EnumerateAllAsync(
                    new ProjectProfileQuery { TenantId = vessel.TenantId, Active = true, PageNumber = 1, PageSize = 1000 },
                    token).ConfigureAwait(false);
                if (profiles.Count == 0) return String.Empty;

                ProjectProfile? profile = ProjectProfileService.SelectForVessel(profiles, vessel);
                if (profile == null || profile.Skills == null || profile.Skills.Count == 0) return String.Empty;

                List<Skill> skills = await _Database.Skills.EnumerateAllAsync(
                    new SkillQuery { TenantId = vessel.TenantId, Active = true, PageNumber = 1, PageSize = 1000 },
                    token).ConfigureAwait(false);
                if (skills.Count == 0) return String.Empty;

                Dictionary<string, Skill> byId = new Dictionary<string, Skill>(StringComparer.Ordinal);
                Dictionary<string, Skill> byName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
                foreach (Skill skill in skills)
                {
                    byId[skill.Id] = skill;
                    if (!String.IsNullOrWhiteSpace(skill.Name)) byName[skill.Name.Trim()] = skill;
                }

                List<string> blocks = new List<string>();
                foreach (string reference in profile.Skills)
                {
                    if (String.IsNullOrWhiteSpace(reference)) continue;
                    Skill? resolved = null;
                    if (byId.TryGetValue(reference.Trim(), out Skill? byIdMatch)) resolved = byIdMatch;
                    else if (byName.TryGetValue(reference.Trim(), out Skill? byNameMatch)) resolved = byNameMatch;
                    if (resolved == null || String.IsNullOrWhiteSpace(resolved.Content)) continue;
                    blocks.Add("### " + resolved.Name + "\n\n" + resolved.Content.Trim());
                }

                return String.Join("\n\n", blocks);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error resolving skills for vessel " + vessel.Id + ": " + ex.Message);
                return String.Empty;
            }
        }

        /// <summary>
        /// Preserve only stable project instructions from an existing runtime instruction file.
        /// Generated Armada mission blocks are stripped to avoid recursively injecting stale
        /// mission objectives into future captain prompts.
        /// </summary>
        private static string SanitizeExistingInstructions(string existing)
        {
            if (String.IsNullOrWhiteSpace(existing)) return String.Empty;

            int generatedSectionIndex = existing.IndexOf("# Mission Instructions", StringComparison.Ordinal);
            if (generatedSectionIndex < 0)
            {
                generatedSectionIndex = existing.IndexOf("## Mission Instructions", StringComparison.Ordinal);
            }

            if (generatedSectionIndex >= 0)
            {
                return existing.Substring(0, generatedSectionIndex).TrimEnd();
            }

            return existing.TrimEnd();
        }

        /// <summary>
        /// Builds the papercut directive. A captain that meets broken friction -- a stale doc, a dead
        /// link, a brief that contradicts itself, a missing sibling repository -- works around it and
        /// says nothing, so the next captain on the same vessel pays the same cost again. This asks for
        /// one line per problem and forbids fixing it out of scope, which keeps the report cheap and the
        /// diff clean.
        ///
        /// Built in code rather than added to a template, so an existing stored template row that
        /// predates this module cannot silently drop it.
        /// </summary>
        /// <returns>The papercut section.</returns>
        internal static string BuildPapercutsSection()
        {
            return
                "## Papercuts\n" +
                "\n" +
                "Report friction you meet. Do not work around it silently. One line each, on its own line:\n" +
                "\n" +
                "`[ARMADA:PAPERCUT] {\"category\":\"MissingDoc\",\"severity\":\"Low\",\"title\":\"one line\",\"detail\":\"optional\",\"path\":\"optional/file.cs\"}`\n" +
                "\n" +
                "- Categories: BriefContradiction, ToolFailure, MissingDoc, BrokenLink, RepoFriction, " +
                "TestFlake, EnvSetup, PlatformBug, Other. Severity: Low, Medium, High.\n" +
                "- Report it, do not fix it. A fix outside your mission scope is a merge conflict for another captain.\n" +
                "- Never block on a papercut. Report it and continue the mission.\n" +
                "- Your own assigned work is not a papercut. Report what made the work harder than it needed to be.\n" +
                "- Include no credentials, tokens, or absolute host paths.\n" +
                "- Ten per mission is the limit. Report the ones that cost you time.\n";
        }

        /// <summary>
        /// Resolve a named template section. Falls back to empty string if no template service or template not found.
        /// </summary>
        private async Task<string> ResolveSectionAsync(string templateName, Dictionary<string, string> templateParams, CancellationToken token)
        {
            if (_PromptTemplates != null)
            {
                string rendered = await _PromptTemplates.RenderAsync(templateName, templateParams, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(rendered))
                    return rendered;
            }

            // Hardcoded fallbacks for backward compatibility when template service is unavailable
            string fallback = GetHardcodedFallback(templateName);
            if (!String.IsNullOrEmpty(fallback))
            {
                foreach (KeyValuePair<string, string> kvp in templateParams)
                {
                    fallback = fallback.Replace("{" + kvp.Key + "}", kvp.Value ?? "");
                }
            }
            return fallback;
        }

        /// <summary>
        /// Returns hardcoded prompt section content as a fallback when the template service is unavailable.
        /// </summary>
        private string GetHardcodedFallback(string templateName)
        {
            switch (templateName)
            {
                case "mission.rules":
                    return
                        "## Rules\n" +
                        "- Work only within this worktree directory\n" +
                        "- Commit all changes to the current branch\n" +
                        "- Commit and push your changes -- the Admiral will also push if needed\n" +
                        "- If you encounter a blocking issue, commit what you have and exit\n" +
                        "- Exit with code 0 on success\n" +
                        "- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages\n" +
                        "- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text\n";

                case "mission.context_conservation":
                    return
                        "## Context Conservation (CRITICAL)\n" +
                        "\n" +
                        "You have a limited context window. Exceeding it will crash your process and fail the mission. " +
                        "Follow these rules to stay within limits:\n" +
                        "\n" +
                        "1. **NEVER read entire large files.** If a file is over 200 lines, read only the specific " +
                        "section you need using line offsets. Use grep/search to find the right section first.\n" +
                        "\n" +
                        "2. **Read before you write, but read surgically.** Read only the 10-30 lines around the code " +
                        "you need to change, not the whole file.\n" +
                        "\n" +
                        "3. **Do not explore the codebase broadly.** Only read files explicitly mentioned in your " +
                        "mission description. If the mission says to edit README.md, read only the section you need " +
                        "to edit, not the entire README.\n" +
                        "\n" +
                        "4. **Make your changes and finish.** Do not re-read files to verify your changes, do not " +
                        "read files for 'context' that isn't directly needed for your edit, and do not explore related " +
                        "files out of curiosity.\n" +
                        "\n" +
                        "5. **If the mission scope feels too large** (more than 8 files, or files with 500+ lines to " +
                        "read), commit what you have, report progress, and exit with code 0. Partial progress is " +
                        "better than crashing.\n";

                case "mission.merge_conflict_avoidance":
                    return
                        "## Avoiding Merge Conflicts (CRITICAL)\n" +
                        "\n" +
                        "You are one of several captains working on this repository. Other captains may be working on " +
                        "other missions in parallel on separate branches. To prevent merge conflicts and landing failures, " +
                        "you MUST follow these rules:\n" +
                        "\n" +
                        "1. **Only modify files explicitly mentioned in your mission description.** If the description says " +
                        "to edit `src/routes/users.ts`, do NOT also refactor `src/routes/orders.ts` even if you notice " +
                        "improvements. Another captain may be working on that file.\n" +
                        "\n" +
                        "2. **Do not make \"helpful\" changes outside your scope.** Do not rename shared variables, " +
                        "reorganize imports in files you were not asked to touch, reformat code in unrelated files, " +
                        "update documentation files unless instructed, or modify configuration/project files " +
                        "(e.g., .csproj, package.json, tsconfig.json) unless your mission specifically requires it.\n" +
                        "\n" +
                        "3. **Do not modify barrel/index export files** (e.g., index.ts, mod.rs) unless your mission " +
                        "explicitly requires it. These are high-conflict files that many missions may need to touch.\n" +
                        "\n" +
                        "4. **Keep changes minimal and focused.** The fewer files you touch, the lower the risk of " +
                        "conflicts. If your mission can be completed by editing 2 files, do not edit 5.\n" +
                        "\n" +
                        "5. **If you must create new files**, prefer names that are specific to your mission's feature " +
                        "rather than generic names that another captain might also choose.\n" +
                        "\n" +
                        "6. **Do not modify or delete files created by another mission's branch.** You are working in " +
                        "an isolated worktree -- if you see files that seem unrelated to your mission, leave them alone.\n" +
                        "\n" +
                        "Violating these rules will cause your branch to conflict with other captains' branches during " +
                        "landing, resulting in a LandingFailed status and wasted work.\n";

                case "mission.progress_signals":
                    return
                        "## Progress Signals (Optional)\n" +
                        "You can report progress to the Admiral by printing these lines to stdout:\n" +
                        "- `[ARMADA:PROGRESS] 50` -- report completion percentage (0-100)\n" +
                        "- `[ARMADA:STATUS] Testing` -- transition mission to Testing status\n" +
                        "- `[ARMADA:STATUS] Review` -- transition mission to Review status\n" +
                        "- `[ARMADA:MESSAGE] your message here` -- send a progress message\n" +
                        "- `[ARMADA:TOKENS] input=1234 output=567 cached=0` -- report the tokens you consumed this session (input/prompt, output/completion, and cache-read). Emit this once near the end if your runtime can determine the counts; it lets the Admiral record real token usage instead of an estimate.\n" +
                        "- `[ARMADA:RESULT] COMPLETE` -- worker/test engineer mission finished successfully\n" +
                        "- `[ARMADA:VERDICT] PASS` -- judge approves the mission\n" +
                        "- `[ARMADA:VERDICT] FAIL` -- judge rejects the mission\n" +
                        "- `[ARMADA:VERDICT] NEEDS_REVISION` -- judge requests follow-up changes\n" +
                        "Architect missions must not emit `[ARMADA:RESULT]` or `[ARMADA:VERDICT]`; they must output only real `[ARMADA:MISSION]` blocks.\n";

                case "mission.model_context_updates":
                    return
                        "## Model Context Updates\n" +
                        "\n" +
                        "Model context accumulation is enabled for this vessel. Before you finish your mission, " +
                        "review the existing model context above (if any) and consider whether you have discovered " +
                        "key information that would help future agents work on this repository more effectively. " +
                        "Examples include: architectural insights, code style conventions, naming conventions, " +
                        "logging patterns, error handling patterns, testing patterns, build quirks, common pitfalls, " +
                        "important dependencies, interdependencies between modules, concurrency patterns, " +
                        "and performance considerations.\n" +
                        "\n" +
                        "If you have useful additions, call `update_vessel_context` with the `modelContext` " +
                        "parameter set to the COMPLETE updated model context (not just your additions -- include " +
                        "the existing content with your additions merged in). Be thorough -- this context is a " +
                        "goldmine for future agents. Focus on information that is not obvious from reading the code, " +
                        "and organize it clearly with sections or headings.\n" +
                        "\n" +
                        "If you have nothing to add, skip this step.\n";

                case "mission.playbooks_wrapper":
                    return
                        "## Playbooks\n" +
                        "These playbooks are part of the required instructions for this mission. Read and follow them.\n" +
                        "\n" +
                        "{SelectedPlaybooksMarkdown}\n";

                case "mission.captain_instructions_wrapper":
                    return
                        "## Captain Instructions\n" +
                        "{CaptainInstructions}\n";

                case "mission.project_context_wrapper":
                    return
                        "## Project Context\n" +
                        "{ProjectContext}\n";

                case "mission.code_style_wrapper":
                    return
                        "## Code Style\n" +
                        "{StyleGuide}\n";

                case "mission.model_context_wrapper":
                    return
                        "## Model Context\n" +
                        "The following context was accumulated by AI agents during previous missions on this repository. " +
                        "Use this information to work more effectively.\n" +
                        "\n" +
                        "{ModelContext}\n";

                case "mission.metadata":
                    return
                        "# Mission Instructions\n" +
                        "\n" +
                        "{PersonaPrompt}\n" +
                        "\n" +
                        "## Mission\n" +
                        "- **Title:** {MissionTitle}\n" +
                        "- **ID:** {MissionId}\n" +
                        "- **Voyage:** {VoyageId}\n" +
                        "\n" +
                        "## Description\n" +
                        "{MissionDescription}\n" +
                        "\n" +
                        "## Repository\n" +
                        "- **Name:** {VesselName}\n" +
                        "- **Branch:** {BranchName}\n" +
                        "- **Default Branch:** {DefaultBranch}\n";

                case "mission.existing_instructions_wrapper":
                    return
                        "\n## Existing Project Instructions\n" +
                        "\n" +
                        "{ExistingClaudeMd}";

                default:
                    return "";
            }
        }

        #endregion

        #region Private-Methods

        private static string BuildMissionBranchName(Captain captain, Mission mission)
        {
            return Constants.BranchPrefix + SanitizeBranchPathSegment(captain.Name) + "/" + mission.Id;
        }

        private static string SanitizeBranchPathSegment(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "captain";

            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
            bool previousDash = false;

            foreach (char current in value.Trim().ToLowerInvariant())
            {
                bool isAsciiLetter = current >= 'a' && current <= 'z';
                bool isAsciiDigit = current >= '0' && current <= '9';
                bool isSafeSeparator = current == '-' || current == '_';

                if (isAsciiLetter || isAsciiDigit || isSafeSeparator)
                {
                    builder.Append(current);
                    previousDash = false;
                }
                else if (!previousDash)
                {
                    builder.Append('-');
                    previousDash = true;
                }
            }

            string sanitized = builder.ToString().Trim('-', '_');
            if (String.IsNullOrEmpty(sanitized)) return "captain";

            const int maxSegmentLength = 64;
            if (sanitized.Length > maxSegmentLength)
            {
                sanitized = sanitized.Substring(0, maxSegmentLength).Trim('-', '_');
                if (String.IsNullOrEmpty(sanitized)) return "captain";
            }

            return sanitized;
        }

        private async Task CleanupArchitectBranchAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            if (_Git == null)
            {
                _Logging.Debug(_Header + "git service unavailable -- skipping architect branch cleanup for mission " + mission.Id);
                return;
            }

            string? branchName = mission.BranchName ?? dock?.BranchName;
            if (String.IsNullOrEmpty(branchName) || String.IsNullOrEmpty(mission.VesselId))
            {
                return;
            }

            Vessel? vessel = !String.IsNullOrEmpty(mission.TenantId)
                ? await _Database.Vessels.ReadAsync(mission.TenantId, mission.VesselId, token).ConfigureAwait(false)
                : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);

            if (vessel == null || String.IsNullOrEmpty(vessel.LocalPath))
            {
                _Logging.Warn(_Header + "unable to clean architect branch " + branchName +
                    " for mission " + mission.Id + " because vessel metadata is incomplete");
                return;
            }

            BranchCleanupPolicyEnum cleanupPolicy = vessel.BranchCleanupPolicy ?? _Settings.BranchCleanupPolicy;
            if (cleanupPolicy == BranchCleanupPolicyEnum.None)
            {
                _Logging.Info(_Header + "branch cleanup policy is None - retaining architect branch " + branchName + " after handoff");
                return;
            }

            try
            {
                await _Git.DeleteLocalBranchAsync(vessel.LocalPath, branchName, token).ConfigureAwait(false);
                _Logging.Info(_Header + "deleted architect branch " + branchName + " from bare repo after successful handoff");
            }
            catch (Exception branchEx)
            {
                _Logging.Warn(_Header + "failed to delete architect branch " + branchName + " from bare repo: " + branchEx.Message);
            }

            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
            {
                if (String.IsNullOrEmpty(vessel.WorkingDirectory))
                {
                    _Logging.Warn(_Header + "cannot delete remote architect branch " + branchName +
                        " because vessel working directory is not configured");
                    return;
                }

                try
                {
                    await _Git.DeleteRemoteBranchAsync(vessel.WorkingDirectory, branchName, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "deleted remote architect branch " + branchName + " after successful handoff");
                }
                catch (Exception remoteBranchEx)
                {
                    _Logging.Warn(_Header + "failed to delete remote architect branch " + branchName + ": " + remoteBranchEx.Message);
                }
            }
        }

        /// <summary>
        /// After a mission produces work, check if any missions in the same voyage depend on it
        /// and prepare them for assignment (inject prior stage context into description).
        /// </summary>
        /// <summary>
        /// Recover pipeline handoffs that dangled: a mission reached WorkProduced but its Pending
        /// downstream stage was never prepared (branch/context not copied), so it can never be
        /// dispatched. Re-drives the handoff for any such mission so the next stage becomes
        /// dispatchable. Architect stages are skipped here because re-driving their fan-out is not
        /// idempotent; their handoff is handled on completion. Returns the number re-driven.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count of missions whose handoff was re-driven.</returns>
        public async Task<int> RecoverDanglingHandoffsAsync(CancellationToken token = default)
        {
            int redriven = 0;
            List<Mission> workProduced = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.WorkProduced, token).ConfigureAwait(false);

            foreach (Mission produced in workProduced)
            {
                if (String.IsNullOrEmpty(produced.VoyageId)) continue;
                if (String.Equals(produced.Persona, "Architect", StringComparison.OrdinalIgnoreCase)) continue;

                List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(produced.VoyageId, token).ConfigureAwait(false);
                List<Mission> pendingDependents = voyageMissions
                    .Where(m => m.DependsOnMissionId == produced.Id && m.Status == MissionStatusEnum.Pending)
                    .ToList();
                if (pendingDependents.Count == 0) continue;

                bool anyUnprepared = pendingDependents.Any(dep => !IsPipelineHandoffPrepared(dep, produced));
                if (!anyUnprepared) continue;

                _Logging.Warn(_Header + "re-driving dangling pipeline handoff for WorkProduced mission " + produced.Id +
                    " (" + pendingDependents.Count + " pending dependent(s) not prepared)");
                try
                {
                    await TryHandoffToNextStageAsync(produced, token).ConfigureAwait(false);
                    redriven++;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error re-driving handoff for mission " + produced.Id + ": " + ex.Message);
                }
            }

            return redriven;
        }

        private async Task<bool> TryHandoffToNextStageAsync(Mission completedMission, CancellationToken token)
        {
            if (String.IsNullOrEmpty(completedMission.VoyageId)) return false;

            // Find missions that depend on this completed mission
            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(completedMission.VoyageId, token).ConfigureAwait(false);
            List<Mission> dependentMissions = voyageMissions.Where(m =>
                m.DependsOnMissionId == completedMission.Id &&
                m.Status == MissionStatusEnum.Pending).ToList();

            if (dependentMissions.Count == 0) return false;

            // Special handling for Architect stage: parse output into new missions
            if (String.Equals(completedMission.Persona, "Architect", StringComparison.OrdinalIgnoreCase))
            {
                List<ParsedArchitectMission> parsed = ParseArchitectOutput(completedMission);
                if (parsed.Count > 0)
                {
                    await ProjectArchitectMissionsToLogAsync(completedMission, parsed, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "architect produced " + parsed.Count + " mission definitions");

                    foreach (Mission nextMission in dependentMissions)
                    {
                if (String.Equals(nextMission.Persona, "Worker", StringComparison.OrdinalIgnoreCase))
                {
                    // Update the first parsed mission into this existing Worker mission slot
                    ParsedArchitectMission first = parsed[0];
                    nextMission.Title = first.Title + " [Worker]";
                    nextMission.Description = ArchitectHandoffMarker + "\n" + first.Description;
                    nextMission.BranchName = null;
                    nextMission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(nextMission, token).ConfigureAwait(false);
                    await RetitleDependentChainAsync(voyageMissions, nextMission, first.Title, first.Description, token).ConfigureAwait(false);

                    // Find what depends on this worker mission (Judge, Test Engineer stages)
                    // Create additional worker missions for remaining parsed items
                    for (int i = 1; i < parsed.Count; i++)
                    {
                                Mission additionalWorker = new Mission(parsed[i].Title + " [Worker]", ArchitectHandoffMarker + "\n" + parsed[i].Description);
                                additionalWorker.TenantId = completedMission.TenantId;
                                additionalWorker.UserId = completedMission.UserId;
                                additionalWorker.VoyageId = completedMission.VoyageId;
                                additionalWorker.VesselId = completedMission.VesselId;
                        additionalWorker.Persona = "Worker";
                        additionalWorker.DependsOnMissionId = completedMission.Id;
                        additionalWorker.RequiresReview = nextMission.RequiresReview;
                        additionalWorker.ReviewDenyAction = nextMission.ReviewDenyAction;
                        additionalWorker.BranchName = null;
                        additionalWorker = await _Database.Missions.CreateAsync(additionalWorker, token).ConfigureAwait(false);
                        _Logging.Info(_Header + "architect created additional worker mission " + additionalWorker.Id + ": " + parsed[i].Title);

                        await CloneDependentChainAsync(voyageMissions, nextMission, additionalWorker, parsed[i].Title, parsed[i].Description, token).ConfigureAwait(false);
                    }
                }
            }

                    await ApplyArchitectMissionDependenciesAsync(completedMission, parsed, token).ConfigureAwait(false);
                    return true; // Architect special handling complete, skip normal handoff
                }

                bool hadArchitectMarkers = !String.IsNullOrEmpty(completedMission.AgentOutput) &&
                    completedMission.AgentOutput.Contains("[ARMADA:MISSION]", StringComparison.Ordinal);

                string failureReason = hadArchitectMarkers
                    ? "Architect produced no valid [ARMADA:MISSION] definitions in output"
                    : "Architect produced no [ARMADA:MISSION] markers in output";

                _Logging.Warn(_Header + "architect mission " + completedMission.Id +
                    " produced no valid mission definitions -- marking as failed");
                completedMission.Status = MissionStatusEnum.Failed;
                completedMission.FailureReason = failureReason;
                completedMission.CompletedUtc = DateTime.UtcNow;
                completedMission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(completedMission, token).ConfigureAwait(false);
                return false;
            }

            // Stage-lag hardening: the just-completed stage's dock may have committed on a detached HEAD,
            // leaving its produced commit off the shared branch ref. The next stage reuses that same branch,
            // so resolve the dock's live HEAD and force-advance the branch ref before handing off, otherwise
            // the next stage would check out stale code. Best-effort: a git failure is logged, not fatal.
            await HardenStageBranchAsync(completedMission, token).ConfigureAwait(false);

            foreach (Mission nextMission in dependentMissions)
            {
                // Build persona-specific preamble for the next stage
                string personaPreamble = "";
                switch (PersonaCatalog.NormalizeName(nextMission.Persona))
                {
                    case PersonaCatalog.ProductManager:
                        personaPreamble = "## Your Role: Product Manager (Frame the Product)\n\n" +
                            "You are turning the dispatched work into a concrete product picture before implementation. " +
                            "Clarify user value, experience expectations, validation signals, and future-facing constraints.\n\n";
                        break;
                    case PersonaCatalog.Architect:
                        personaPreamble = "## Your Role: Architect (Decompose)\n\n" +
                            "You are translating the dispatched work and prior product framing into right-sized missions. " +
                            "Carry forward the user outcomes, validation plan, and future-readiness constraints from the prior stage.\n\n";
                        break;
                    case PersonaCatalog.Worker:
                        personaPreamble = "## Your Role: Worker (Implement)\n\n" +
                            "You are implementing code changes based on the Architect's plan. " +
                            "Review the prior stage output below and implement the described changes.\n\n";
                        break;
                    case PersonaCatalog.UsabilityEngineer:
                        personaPreamble = "## Your Role: Usability Engineer (Refine Experience)\n\n" +
                            "You are improving the completed implementation for user experience, consistency, and edge-case handling. " +
                            "Review the prior diff below and make the capability easier and safer to use.\n\n";
                        break;
                    case PersonaCatalog.TestEngineer:
                        personaPreamble = "## Your Role: Test Engineer (Write Tests)\n\n" +
                            "You are writing tests for code changes made by the Worker. " +
                            "Review the diff below and write unit tests, integration tests, or test harness updates " +
                            "that cover the changes. Follow existing test patterns in the repository. " +
                            "Scope yourself only to this mission, not sibling missions in the same voyage. Cover the " +
                            "happy path, but also add negative or edge-path coverage for validation, timeout, cancellation, " +
                            "retry, cleanup, and error-handling branches when they are in scope. Include short " +
                            "`## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections. " +
                            "End with a standalone `[ARMADA:RESULT] COMPLETE` line and a short summary.\n\n";
                        break;
                    case PersonaCatalog.Judge:
                        personaPreamble = "## Your Role: Judge (Review)\n\n" +
                            "You are reviewing the completed work through three lenses: correctness, blast radius, and " +
                            "source fidelity. Examine the diff below against the current mission description only, not " +
                            "sibling missions in the same voyage. Assume there may be at least one hidden defect. " +
                            "Your response must include `## Correctness`, `## Blast Radius`, `## Source Fidelity`, and " +
                            "`## Verdict` sections. To block (FAIL or NEEDS_REVISION) you MUST add a `## Affected Case` " +
                            "section exhibiting one concrete affected case (a specific file, line, or scenario); a block " +
                            "without a concrete affected case is not accepted. End with a standalone line " +
                            "`[ARMADA:VERDICT] PASS`, `[ARMADA:VERDICT] FAIL`, or `[ARMADA:VERDICT] NEEDS_REVISION`.\n\n";
                        break;
                }

                // Inject context from the completed stage into the next stage's description
                string handoffContext = "\n\n---\n" +
                    "## Prior Stage Output\n" +
                    "The previous pipeline stage (" + PersonaCatalog.NormalizeName(completedMission.Persona ?? PersonaCatalog.Worker) + ") " +
                    "completed mission \"" + completedMission.Title + "\" (" + completedMission.Id + ").\n" +
                    "Branch: " + (completedMission.BranchName ?? "unknown") + "\n";

                // Use the canonical persisted AgentOutput for handoff instead of reparsing
                // the mission log file. AgentOutput is captured from accumulated stdout by
                // HandleCompletionAsync and is the single source of truth for agent output.
                if (!String.IsNullOrEmpty(completedMission.AgentOutput))
                {
                    string agentOutput = completedMission.AgentOutput.Trim();
                    int maxOutputChars = 8000;
                    if (agentOutput.Length > maxOutputChars)
                    {
                        // Truncate from the end (keep the beginning which typically contains
                        // the plan/structure) rather than the beginning
                        agentOutput = agentOutput.Substring(0, maxOutputChars) + "\n...(truncated)";
                    }
                    handoffContext += "\n### Agent Output (from " + PersonaCatalog.NormalizeName(completedMission.Persona) + " stage)\n```\n" + agentOutput + "\n```\n";
                }

                // Include the diff snapshot if available
                if (!String.IsNullOrEmpty(completedMission.DiffSnapshot))
                {
                    handoffContext += "\n### Diff from prior stage\n```diff\n" + completedMission.DiffSnapshot + "\n```\n";
                }
                else
                {
                    handoffContext += "\n*No diff available from prior stage. The work is on the branch above.*\n";
                }

                nextMission.Description = personaPreamble.Length > 0
                    ? personaPreamble + (nextMission.Description ?? "") + handoffContext
                    : (nextMission.Description ?? "") + handoffContext;
                nextMission.BranchName = completedMission.BranchName;
                nextMission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(nextMission, token).ConfigureAwait(false);

                _Logging.Info(_Header + "pipeline handoff: prepared mission " + nextMission.Id +
                    " (" + nextMission.Persona + ") with context from " + completedMission.Id +
                    " (" + completedMission.Persona + ")");

            }

            return true;
        }

        /// <summary>
        /// Force-advance the shared pipeline branch to the just-completed stage's produced commit. A prior
        /// stage's dock can end on a detached HEAD (its commit is not on the branch ref), so the next stage --
        /// which reuses the same branch -- would otherwise check out stale code. Resolve the dock's live HEAD
        /// and lift it onto the branch ref. Best-effort: any failure is logged and the handoff continues.
        /// </summary>
        private async Task HardenStageBranchAsync(Mission completedMission, CancellationToken token)
        {
            if (_Git == null) return;
            if (String.IsNullOrEmpty(completedMission.BranchName) || String.IsNullOrEmpty(completedMission.DockId)) return;

            Dock? dock = !String.IsNullOrEmpty(completedMission.TenantId)
                ? await _Database.Docks.ReadAsync(completedMission.TenantId, completedMission.DockId!, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(completedMission.DockId!, token).ConfigureAwait(false);
            if (dock == null || String.IsNullOrEmpty(dock.WorktreePath) || !Directory.Exists(dock.WorktreePath)) return;

            try
            {
                string? headCommit = await _Git.GetHeadCommitHashAsync(dock.WorktreePath!, token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(headCommit)) return;

                bool advanced = await _Git.ForceAdvanceBranchAsync(dock.WorktreePath!, completedMission.BranchName!, headCommit!, token).ConfigureAwait(false);
                if (advanced)
                {
                    _Logging.Info(_Header + "stage-lag hardening: advanced branch " + completedMission.BranchName +
                        " to " + headCommit + " from dock " + dock.Id + " before handoff from mission " + completedMission.Id);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "stage-lag hardening failed for mission " + completedMission.Id + ": " + ex.Message);
            }
        }

        private async Task CancelDependentPipelineStagesAsync(Mission failedMission, CancellationToken token)
        {
            if (failedMission == null) throw new ArgumentNullException(nameof(failedMission));
            if (String.IsNullOrEmpty(failedMission.VoyageId)) return;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(failedMission.VoyageId, token).ConfigureAwait(false);
            List<Mission> directDependents = voyageMissions.Where(m =>
                m.DependsOnMissionId == failedMission.Id &&
                (m.Status == MissionStatusEnum.Pending ||
                 m.Status == MissionStatusEnum.Assigned ||
                 m.Status == MissionStatusEnum.InProgress ||
                 m.Status == MissionStatusEnum.Testing ||
                 m.Status == MissionStatusEnum.Review)).ToList();

            foreach (Mission dependent in directDependents)
            {
                dependent.Status = MissionStatusEnum.Cancelled;
                dependent.FailureReason = "Blocked by failed dependency " + failedMission.Id;
                dependent.CompletedUtc = DateTime.UtcNow;
                dependent.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(dependent, token).ConfigureAwait(false);
                _Logging.Info(_Header + "cancelled dependent mission " + dependent.Id +
                    " because upstream mission " + failedMission.Id + " ended in " + failedMission.Status);
                await CancelDependentPipelineStagesAsync(dependent, token).ConfigureAwait(false);
            }
        }

        private async Task UpdateVoyageTerminalStatusAsync(string? voyageId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(voyageId)) return;

            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return;

            List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            if (missions.Count == 0) return;

            bool anyActive = missions.Any(m =>
                m.Status == MissionStatusEnum.Pending ||
                m.Status == MissionStatusEnum.Assigned ||
                m.Status == MissionStatusEnum.InProgress ||
                m.Status == MissionStatusEnum.Testing ||
                m.Status == MissionStatusEnum.Review ||
                m.Status == MissionStatusEnum.PullRequestOpen);

            if (anyActive) return;

            bool allDone = missions.All(m =>
                m.Status == MissionStatusEnum.Complete ||
                m.Status == MissionStatusEnum.Failed ||
                m.Status == MissionStatusEnum.Cancelled ||
                m.Status == MissionStatusEnum.LandingFailed ||
                m.Status == MissionStatusEnum.WorkProduced);

            if (!allDone) return;

            bool anyFailed = missions.Any(m =>
                m.Status == MissionStatusEnum.Failed ||
                m.Status == MissionStatusEnum.LandingFailed);

            voyage.Status = anyFailed ? VoyageStatusEnum.Failed : VoyageStatusEnum.Complete;
            voyage.CompletedUtc = DateTime.UtcNow;
            voyage.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);
            _Logging.Info(_Header + "voyage " + voyage.Id + " reached terminal status " + voyage.Status + " during mission completion");
        }

        private async Task CloneDependentChainAsync(
            List<Mission> voyageMissions,
            Mission templateMission,
            Mission newDependency,
            string parsedTitle,
            string parsedDescription,
            CancellationToken token)
        {
            List<Mission> directDependents = voyageMissions
                .Where(m => m.DependsOnMissionId == templateMission.Id)
                .OrderBy(m => m.CreatedUtc)
                .ToList();

            foreach (Mission templateChild in directDependents)
            {
                string childPersona = PersonaCatalog.NormalizeName(templateChild.Persona);
                if (String.IsNullOrEmpty(childPersona)) childPersona = templateChild.Persona ?? PersonaCatalog.Worker;

                Mission clonedStage = new Mission(
                    parsedTitle + " [" + childPersona + "]",
                    parsedDescription);
                clonedStage.TenantId = templateChild.TenantId;
                clonedStage.UserId = templateChild.UserId;
                clonedStage.VoyageId = templateChild.VoyageId;
                clonedStage.VesselId = templateChild.VesselId;
                clonedStage.Persona = childPersona;
                clonedStage.DependsOnMissionId = newDependency.Id;
                clonedStage.RequiresReview = templateChild.RequiresReview;
                clonedStage.ReviewDenyAction = templateChild.ReviewDenyAction;
                clonedStage.BranchName = null;
                clonedStage = await _Database.Missions.CreateAsync(clonedStage, token).ConfigureAwait(false);
                _Logging.Info(_Header + "architect created chained stage " + clonedStage.Id +
                    " (" + clonedStage.Persona + ") depending on " + newDependency.Id);
                await CloneDependentChainAsync(voyageMissions, templateChild, clonedStage, parsedTitle, parsedDescription, token).ConfigureAwait(false);
            }
        }

        private async Task RetitleDependentChainAsync(
            List<Mission> voyageMissions,
            Mission dependency,
            string parsedTitle,
            string parsedDescription,
            CancellationToken token)
        {
            List<Mission> directDependents = voyageMissions
                .Where(m => m.DependsOnMissionId == dependency.Id)
                .OrderBy(m => m.CreatedUtc)
                .ToList();

            foreach (Mission dependent in directDependents)
            {
                string dependentPersona = PersonaCatalog.NormalizeName(dependent.Persona);
                if (String.IsNullOrEmpty(dependentPersona)) dependentPersona = dependent.Persona ?? PersonaCatalog.Worker;

                dependent.Persona = dependentPersona;
                dependent.Title = parsedTitle + " [" + dependentPersona + "]";
                dependent.Description = parsedDescription;
                dependent.BranchName = null;
                dependent.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(dependent, token).ConfigureAwait(false);
                await RetitleDependentChainAsync(voyageMissions, dependent, parsedTitle, parsedDescription, token).ConfigureAwait(false);
            }
        }

        private async Task ApplyArchitectMissionDependenciesAsync(
            Mission architectMission,
            List<ParsedArchitectMission> parsed,
            CancellationToken token)
        {
            if (architectMission == null) throw new ArgumentNullException(nameof(architectMission));
            if (String.IsNullOrEmpty(architectMission.VoyageId)) return;
            if (parsed == null || parsed.Count == 0) return;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(architectMission.VoyageId, token).ConfigureAwait(false);
            Dictionary<int, Mission> workerRootsByIndex = new Dictionary<int, Mission>();
            Dictionary<int, Mission> terminalStagesByIndex = new Dictionary<int, Mission>();
            Dictionary<string, Mission> terminalStagesByTitle = new Dictionary<string, Mission>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < parsed.Count; i++)
            {
                string workerTitle = parsed[i].Title + " [Worker]";
                Mission? workerRoot = voyageMissions.FirstOrDefault(m =>
                    String.Equals(m.Persona, "Worker", StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(m.Title, workerTitle, StringComparison.OrdinalIgnoreCase));
                if (workerRoot == null) continue;

                Mission terminalStage = FindTerminalPipelineStage(voyageMissions, workerRoot);
                workerRootsByIndex[i + 1] = workerRoot;
                terminalStagesByIndex[i + 1] = terminalStage;
                terminalStagesByTitle[parsed[i].Title] = terminalStage;
            }

            for (int i = 0; i < parsed.Count; i++)
            {
                string? dependencyReference = parsed[i].DependsOnReference;
                if (String.IsNullOrWhiteSpace(dependencyReference)) continue;
                if (!workerRootsByIndex.TryGetValue(i + 1, out Mission? workerRoot)) continue;

                Mission? resolvedDependency = ResolveArchitectDependencyTerminalStage(
                    terminalStagesByIndex,
                    terminalStagesByTitle,
                    i + 1,
                    dependencyReference);
                if (resolvedDependency == null)
                {
                    _Logging.Warn(_Header + "could not resolve architect dependency '" + dependencyReference +
                        "' for mission '" + parsed[i].Title + "' -- leaving dependency on architect");
                    continue;
                }

                if (resolvedDependency.Id == workerRoot.Id)
                {
                    _Logging.Warn(_Header + "ignoring self-referential architect dependency '" + dependencyReference +
                        "' for mission '" + parsed[i].Title + "'");
                    continue;
                }

                workerRoot.DependsOnMissionId = resolvedDependency.Id;
                workerRoot.BranchName = null;
                workerRoot.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(workerRoot, token).ConfigureAwait(false);

                _Logging.Info(_Header + "architect sequenced worker mission " + workerRoot.Id +
                    " to depend on terminal stage " + resolvedDependency.Id +
                    " from reference '" + dependencyReference + "'");
            }
        }

        private static Mission FindTerminalPipelineStage(IEnumerable<Mission> voyageMissions, Mission root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            Mission current = root;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);

            while (!String.IsNullOrEmpty(current.Id) && visited.Add(current.Id))
            {
                Mission? next = voyageMissions
                    .Where(m => m.DependsOnMissionId == current.Id)
                    .OrderBy(m => m.CreatedUtc)
                    .FirstOrDefault();
                if (next == null) break;
                current = next;
            }

            return current;
        }

        private async Task<bool> HasDependentPipelineStages(string? voyageId, string missionId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(voyageId)) return false;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            return voyageMissions.Any(m => m.DependsOnMissionId == missionId);
        }

        /// <summary>
        /// Parse structured mission definitions from an architect's output.
        /// Looks for [ARMADA:MISSION] markers in the mission diff snapshot or description.
        /// </summary>
        private List<ParsedArchitectMission> ParseArchitectOutput(Mission architectMission)
        {
            List<ParsedArchitectMission> results = new List<ParsedArchitectMission>();
            HashSet<string> seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string?[] candidateSources =
            {
                architectMission.AgentOutput,
                architectMission.DiffSnapshot,
                architectMission.Description
            };

            foreach (string? candidateSource in candidateSources)
            {
                if (String.IsNullOrWhiteSpace(candidateSource)) continue;

                string source = candidateSource.Replace("\r\n", "\n");
                ParseArchitectMissionMarkers(source, results, seenTitles);
                if (results.Count > 0) break;

                ParseArchitectSummaryLines(source, results, seenTitles);
                if (results.Count > 0) break;
            }

            return results;
        }

        private void ParseArchitectMissionMarkers(
            string source,
            List<ParsedArchitectMission> results,
            HashSet<string> seenTitles)
        {
            if (String.IsNullOrWhiteSpace(source)) return;

            string[] segments = System.Text.RegularExpressions.Regex.Split(source, @"(?m)^\[ARMADA:MISSION\][ \t]*");

            for (int i = 1; i < segments.Length; i++)
            {
                string segment = segments[i].Trim();
                if (String.IsNullOrEmpty(segment)) continue;

                int closingTagIndex = segment.IndexOf("[/ARMADA:MISSION]", StringComparison.Ordinal);
                if (closingTagIndex >= 0)
                    segment = segment.Substring(0, closingTagIndex).Trim();

                if (String.IsNullOrEmpty(segment)) continue;

                int newlineIndex = segment.IndexOf('\n');
                string title;
                string description;

                if (newlineIndex >= 0)
                {
                    title = segment.Substring(0, newlineIndex).Trim();
                    description = segment.Substring(newlineIndex + 1).Trim();
                }
                else
                {
                    title = segment.Trim();
                    description = "";
                }

                TryAddParsedArchitectMission(results, seenTitles, title, description);
            }
        }

        private void ParseArchitectSummaryLines(
            string source,
            List<ParsedArchitectMission> results,
            HashSet<string> seenTitles)
        {
            if (String.IsNullOrWhiteSpace(source)) return;

            string[] lines = source.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (String.IsNullOrEmpty(line)) continue;
                if (IsAgentTelemetryLine(line)) continue;
                if (IsArchitectSummaryPreambleOrFooter(line)) continue;

                if (TryParseArchitectSummaryLine(line, out string? title, out string? description))
                {
                    List<string> descriptionLines = new List<string>();
                    if (!String.IsNullOrWhiteSpace(description))
                    {
                        descriptionLines.Add(description);
                    }

                    int nextIndex = i + 1;
                    while (nextIndex < lines.Length)
                    {
                        string nextLine = lines[nextIndex].Trim();
                        if (String.IsNullOrEmpty(nextLine))
                        {
                            nextIndex++;
                            continue;
                        }

                        if (IsAgentTelemetryLine(nextLine) || IsArchitectSummaryPreambleOrFooter(nextLine))
                        {
                            nextIndex++;
                            continue;
                        }

                        if (TryParseArchitectSummaryLine(nextLine, out _, out _))
                        {
                            break;
                        }

                        descriptionLines.Add(nextLine);
                        nextIndex++;
                    }

                    TryAddParsedArchitectMission(results, seenTitles, title, String.Join("\n", descriptionLines));
                    i = nextIndex - 1;
                }
            }
        }

        private void TryAddParsedArchitectMission(
            List<ParsedArchitectMission> results,
            HashSet<string> seenTitles,
            string? title,
            string? description)
        {
            if (String.IsNullOrWhiteSpace(title)) return;

            string normalizedTitle = title.Trim();
            string normalizedDescription = NormalizeArchitectDescription(description);
            ArchitectDependencyExtractionResult dependencyExtraction = ExtractArchitectDependencyReference(normalizedDescription);
            normalizedDescription = dependencyExtraction.Description;
            string? dependencyReference = dependencyExtraction.DependsOnReference;
            if (String.IsNullOrWhiteSpace(normalizedDescription))
            {
                // Title-only architect blocks are still actionable; preserve the title as
                // the downstream mission description so worker/test/judge prompts are not empty.
                normalizedDescription = normalizedTitle;
            }

            if (IsArchitectPlaceholderTitle(normalizedTitle))
                return;

            if (IsArchitectPlaceholderDescription(normalizedDescription))
                return;

            if (seenTitles.Add(normalizedTitle))
            {
                ParsedArchitectMission parsed = new ParsedArchitectMission();
                parsed.Title = normalizedTitle;
                parsed.Description = normalizedDescription;
                parsed.DependsOnReference = dependencyReference;
                results.Add(parsed);
            }
        }

        private static string NormalizeArchitectDescription(string? description)
        {
            if (String.IsNullOrWhiteSpace(description)) return "";

            List<string> descriptionLines = description
                .Split('\n')
                .Select(l => l.Trim('\r'))
                .ToList();

            while (descriptionLines.Count > 0)
            {
                string firstLine = descriptionLines[0].Trim();
                if (IsAgentTelemetryLine(firstLine))
                {
                    descriptionLines.RemoveAt(0);
                    continue;
                }

                break;
            }

            while (descriptionLines.Count > 0)
            {
                string lastLine = descriptionLines[descriptionLines.Count - 1].Trim();
                if (IsAgentTelemetryLine(lastLine))
                {
                    descriptionLines.RemoveAt(descriptionLines.Count - 1);
                    continue;
                }

                break;
            }

            return String.Join("\n", descriptionLines).Trim();
        }

        private static ArchitectDependencyExtractionResult ExtractArchitectDependencyReference(string description)
        {
            if (String.IsNullOrWhiteSpace(description))
            {
                return new ArchitectDependencyExtractionResult();
            }

            List<string> keptLines = new List<string>();
            string? dependencyReference = null;

            foreach (string rawLine in description.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = rawLine.Trim();
                if (TryExtractArchitectDependency(trimmed, out string? dependencyCandidate, out string? remainingDescription))
                {
                    if (String.IsNullOrWhiteSpace(dependencyReference))
                    {
                        dependencyReference = NormalizeArchitectDependencyReference(dependencyCandidate ?? String.Empty);
                    }

                    if (!String.IsNullOrWhiteSpace(remainingDescription))
                    {
                        keptLines.Add(remainingDescription);
                    }

                    continue;
                }

                keptLines.Add(rawLine.TrimEnd('\r'));
            }

            return new ArchitectDependencyExtractionResult
            {
                Description = String.Join("\n", keptLines).Trim(),
                DependsOnReference = String.IsNullOrWhiteSpace(dependencyReference) ? null : dependencyReference
            };
        }

        private static bool TryExtractArchitectDependency(string line, out string? dependencyReference, out string? remainingDescription)
        {
            dependencyReference = null;
            remainingDescription = null;

            if (String.IsNullOrWhiteSpace(line)) return false;

            string trimmed = line.Trim();
            if (!trimmed.StartsWith("depends on", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string remainder = trimmed.Substring("depends on".Length).TrimStart();
            if (remainder.StartsWith(":", StringComparison.Ordinal))
            {
                remainder = remainder.Substring(1).TrimStart();
            }

            if (String.IsNullOrWhiteSpace(remainder)) return false;

            int sentenceBoundary = remainder.IndexOf(". ", StringComparison.Ordinal);
            if (sentenceBoundary >= 0)
            {
                dependencyReference = remainder.Substring(0, sentenceBoundary).Trim().TrimEnd('.');
                remainingDescription = remainder.Substring(sentenceBoundary + 2).Trim();
            }
            else
            {
                dependencyReference = remainder.Trim().TrimEnd('.');
                remainingDescription = "";
            }

            return !String.IsNullOrWhiteSpace(dependencyReference);
        }

        private static string NormalizeArchitectDependencyReference(string dependencyReference)
        {
            if (String.IsNullOrWhiteSpace(dependencyReference)) return "";

            string normalized = dependencyReference.Trim().Trim('"', '\'', '`');
            int commentIndex = normalized.IndexOf(" (", StringComparison.Ordinal);
            if (commentIndex > 0)
            {
                normalized = normalized.Substring(0, commentIndex).Trim();
            }

            return normalized.Trim().TrimEnd('.', ';', ',');
        }

        private static Mission? ResolveArchitectDependencyTerminalStage(
            IReadOnlyDictionary<int, Mission> terminalStagesByIndex,
            IReadOnlyDictionary<string, Mission> terminalStagesByTitle,
            int currentMissionIndex,
            string dependencyReference)
        {
            string normalizedReference = NormalizeArchitectDependencyReference(dependencyReference);
            if (String.IsNullOrWhiteSpace(normalizedReference)) return null;

            System.Text.RegularExpressions.Match numericMatch =
                System.Text.RegularExpressions.Regex.Match(
                    normalizedReference,
                    @"^(?:mission\s+)?(?<index>\d+)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (numericMatch.Success &&
                Int32.TryParse(numericMatch.Groups["index"].Value, out int dependencyIndex) &&
                dependencyIndex > 0 &&
                dependencyIndex < currentMissionIndex &&
                terminalStagesByIndex.TryGetValue(dependencyIndex, out Mission? terminalStage))
            {
                return terminalStage;
            }

            if (terminalStagesByTitle.TryGetValue(normalizedReference, out Mission? byTitle))
            {
                return byTitle;
            }

            return null;
        }

        private static bool TryParseArchitectSummaryLine(string line, out string? title, out string? description)
        {
            title = null;
            description = null;

            if (String.IsNullOrWhiteSpace(line)) return false;

            string trimmedLine = line.Trim();
            System.Text.RegularExpressions.Match missionHeadingMatch =
                System.Text.RegularExpressions.Regex.Match(
                    trimmedLine,
                    @"^(?:\*\*)?Mission\s+\d+\s*:\s*(?<title>.+?)(?:\*\*)?(?<tail>.*)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (missionHeadingMatch.Success)
            {
                title = TrimArchitectSummaryMetadata(missionHeadingMatch.Groups["title"].Value.Trim());
                description = ParseArchitectSummaryTail(missionHeadingMatch.Groups["tail"].Value);
                return !String.IsNullOrEmpty(title);
            }

            System.Text.RegularExpressions.Match numberedMatch =
                System.Text.RegularExpressions.Regex.Match(trimmedLine, @"^\d+\.\s+(?<rest>.+)$");
            if (!numberedMatch.Success) return false;

            string rest = numberedMatch.Groups["rest"].Value.Trim();
            if (String.IsNullOrEmpty(rest)) return false;

            if (rest.StartsWith("**", StringComparison.Ordinal))
            {
                System.Text.RegularExpressions.Match boldTitleMatch =
                    System.Text.RegularExpressions.Regex.Match(rest, @"^\*\*(?<title>.+?)\*\*(?<tail>.*)$");
                if (!boldTitleMatch.Success) return false;

                title = boldTitleMatch.Groups["title"].Value.Trim();
                description = ParseArchitectSummaryTail(boldTitleMatch.Groups["tail"].Value);
                return !String.IsNullOrEmpty(title);
            }

            if (TrySplitArchitectSummaryTitleAndDescription(rest, out string? parsedTitle, out string? parsedDescription))
            {
                title = parsedTitle;
                description = parsedDescription;
                return !String.IsNullOrEmpty(title);
            }

            title = rest;
            description = "";
            return true;
        }

        private static bool TrySplitArchitectSummaryTitleAndDescription(
            string summary,
            out string? title,
            out string? description)
        {
            title = null;
            description = null;

            if (String.IsNullOrWhiteSpace(summary)) return false;

            string[] separators = { " -- ", ": " };
            foreach (string separator in separators)
            {
                int separatorIndex = summary.IndexOf(separator, StringComparison.Ordinal);
                if (separatorIndex < 0) continue;

                string titlePart = summary.Substring(0, separatorIndex).Trim();
                string descriptionPart = summary.Substring(separatorIndex + separator.Length).Trim();
                if (String.IsNullOrEmpty(titlePart) || String.IsNullOrEmpty(descriptionPart)) continue;

                title = TrimArchitectSummaryMetadata(titlePart);
                description = descriptionPart;
                return !String.IsNullOrEmpty(title);
            }

            title = TrimArchitectSummaryMetadata(summary.Trim());
            description = "";
            return !String.IsNullOrEmpty(title);
        }

        private static string ParseArchitectSummaryTail(string tail)
        {
            if (String.IsNullOrWhiteSpace(tail)) return "";

            string remaining = tail.Trim();
            while (remaining.StartsWith("(", StringComparison.Ordinal))
            {
                int closingIndex = remaining.IndexOf(')');
                if (closingIndex < 0) break;
                remaining = remaining.Substring(closingIndex + 1).TrimStart();
            }

            if (remaining.StartsWith("--", StringComparison.Ordinal))
            {
                remaining = remaining.Substring(2).TrimStart();
            }
            else if (remaining.StartsWith(":", StringComparison.Ordinal))
            {
                remaining = remaining.Substring(1).TrimStart();
            }

            return remaining.Trim();
        }

        private static string TrimArchitectSummaryMetadata(string title)
        {
            if (String.IsNullOrWhiteSpace(title)) return "";

            string trimmed = title.Trim();
            while (trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                int openIndex = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
                if (openIndex < 0) break;
                trimmed = trimmed.Substring(0, openIndex).TrimEnd();
            }

            return trimmed.Trim();
        }

        private static bool IsArchitectSummaryPreambleOrFooter(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return true;

            string normalized = line.Trim().Trim('*', '_', '`').ToLowerInvariant();
            return normalized.StartsWith("vessel context updated", StringComparison.Ordinal) ||
                normalized.StartsWith("the architect mission is complete", StringComparison.Ordinal) ||
                normalized.StartsWith("here's a summary of", StringComparison.Ordinal) ||
                normalized.StartsWith("here is a summary of", StringComparison.Ordinal) ||
                normalized.StartsWith("missions ", StringComparison.Ordinal);
        }

        private static bool IsAgentTelemetryLine(string? line)
        {
            if (String.IsNullOrWhiteSpace(line)) return true;

            string trimmed = line.Trim();
            if (ProgressParser.TryParse(trimmed) != null) return true;

            return trimmed.Equals("tokens used", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\d,]+$");
        }

        private static bool IsArchitectPlaceholderTitle(string? title)
        {
            if (String.IsNullOrWhiteSpace(title)) return true;

            string trimmed = title.Trim();
            if (trimmed.Equals("...", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("goal:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("inputs:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("deliverables:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("dependencies:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("risks:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("done_when:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("status:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("reason:", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool IsArchitectPlaceholderDescription(string? description)
        {
            if (String.IsNullOrWhiteSpace(description)) return false;

            string[] lines = description
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !String.IsNullOrEmpty(l))
                .ToArray();

            if (lines.Length == 0) return false;

            string[] placeholderPrefixes =
            {
                "goal:",
                "inputs:",
                "deliverables:",
                "dependencies:",
                "risks:",
                "done_when:"
            };

            return lines.All(line =>
                placeholderPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                line.Contains("...", StringComparison.Ordinal));
        }

        private async Task<bool> TryFailMissionForScopeViolationAsync(Mission mission, Dock dock, CancellationToken token)
        {
            if (_Git == null ||
                String.IsNullOrEmpty(mission.Description) ||
                String.IsNullOrEmpty(dock.Id) ||
                String.IsNullOrEmpty(dock.WorktreePath))
            {
                return false;
            }

            HashSet<string> allowedFiles = ParseMissionScopedFiles(mission.Description);
            if (allowedFiles.Count < 1)
            {
                return false;
            }

            string? startCommit = TryReadDockStartCommit(dock.Id);
            if (String.IsNullOrEmpty(startCommit))
            {
                _Logging.Warn(_Header + "scope validation skipped for mission " + mission.Id +
                    " because dock start commit metadata is missing for " + dock.Id);
                return false;
            }

            IReadOnlyList<string> changedFiles;
            try
            {
                changedFiles = await _Git.GetChangedFilesSinceAsync(dock.WorktreePath, startCommit, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "scope validation failed for mission " + mission.Id +
                    " while reading changed files: " + ex.Message);
                return false;
            }

            List<string> outOfScopeFiles = new List<string>();
            foreach (string changedFile in changedFiles)
            {
                string normalizedPath = NormalizeMissionPath(changedFile);
                if (_IgnoredMissionArtifactFiles.Contains(normalizedPath))
                {
                    continue;
                }

                if (!allowedFiles.Contains(normalizedPath))
                {
                    outOfScopeFiles.Add(normalizedPath);
                }
            }

            if (outOfScopeFiles.Count < 1)
            {
                return false;
            }

            mission.Status = MissionStatusEnum.Failed;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            mission.FailureReason = "Mission modified files outside its scoped file list: " + String.Join(", ", outOfScopeFiles);
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "mission " + mission.Id + " failed scope validation: " + mission.FailureReason);
            return true;
        }

        private HashSet<string> ParseMissionScopedFiles(string description)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(description))
            {
                return files;
            }

            foreach (System.Text.RegularExpressions.Match directiveMatch in _ScopedFilesDirectiveRegex.Matches(description))
            {
                string fileSegment = directiveMatch.Groups["files"].Value;
                foreach (System.Text.RegularExpressions.Match pathMatch in _ScopedFileTokenRegex.Matches(fileSegment))
                {
                    string normalizedPath = NormalizeMissionPath(pathMatch.Groups["path"].Value);
                    if (!String.IsNullOrEmpty(normalizedPath))
                    {
                        files.Add(normalizedPath);
                    }
                }
            }

            return files;
        }

        private string? TryReadDockStartCommit(string dockId)
        {
            try
            {
                string metadataPath = Path.Combine(_Settings.LogDirectory, "docks", dockId + ".start");
                if (!File.Exists(metadataPath))
                {
                    return null;
                }

                return File.ReadAllText(metadataPath).Trim();
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not read dock start commit metadata for " + dockId + ": " + ex.Message);
                return null;
            }
        }

        private static string NormalizeMissionPath(string path)
        {
            return (path ?? String.Empty).Trim().Replace('\\', '/');
        }

        private async Task EnsureMissionInstructionsPresentAsync(
            string worktreePath,
            Mission mission,
            Captain captain,
            CancellationToken token)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            string instructionsFileName = MissionPromptBuilder.GetInstructionsFileName(captain.Runtime.ToString());
            string instructionsPath = Path.Combine(worktreePath, instructionsFileName);
            if (File.Exists(instructionsPath)) return;

            string snapshotPath = Path.Combine(_Settings.LogDirectory, "instructions", mission.Id + "." + instructionsFileName);
            if (!File.Exists(snapshotPath))
            {
                _Logging.Warn(_Header + "mission instructions missing at " + instructionsPath +
                    " and no snapshot exists at " + snapshotPath);
                return;
            }

            Directory.CreateDirectory(worktreePath);
            await File.WriteAllTextAsync(instructionsPath, await File.ReadAllTextAsync(snapshotPath, token).ConfigureAwait(false), token).ConfigureAwait(false);
            _Logging.Warn(_Header + "restored missing mission instructions from snapshot to " + instructionsPath);
        }

        private JudgeVerdict ParseJudgeVerdict(string? agentOutput)
        {
            if (String.IsNullOrEmpty(agentOutput)) return JudgeVerdict.None;

            string[] lines = agentOutput.Replace("\r\n", "\n").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim().Trim('\r');
                if (String.IsNullOrEmpty(line)) continue;

                JudgeVerdict? signalVerdict = ParseStructuredJudgeVerdictSignal(line);
                if (signalVerdict.HasValue) return signalVerdict.Value;

                if (IsAgentTelemetryLine(line)) continue;

                string normalized = line.Trim().Trim('*', '_', '`', '#', '>', '-', ' ');
                JudgeVerdict? explicitVerdict = ParseExplicitJudgeVerdictLine(normalized);
                if (explicitVerdict.HasValue) return explicitVerdict.Value;
            }

            return JudgeVerdict.None;
        }

        private bool TryValidateJudgePassOutput(string? agentOutput, out string? failureReason)
        {
            // Delegate the bounded three-lens PASS contract to the pure JudgeContract so the prompt builders,
            // this gate, and the tests share one definition.
            string narrative = ExtractJudgeNarrative(agentOutput ?? String.Empty);
            if (JudgeContract.ValidatePass(agentOutput, narrative, out failureReason))
            {
                return true;
            }

            return false;
        }

        private static string ExtractJudgeNarrative(string agentOutput)
        {
            if (String.IsNullOrWhiteSpace(agentOutput)) return String.Empty;

            List<string> lines = new List<string>();
            string[] split = agentOutput.Replace("\r\n", "\n").Split('\n');

            foreach (string rawLine in split)
            {
                string line = rawLine.Trim();
                if (String.IsNullOrWhiteSpace(line)) continue;
                if (IsAgentTelemetryLine(line)) continue;
                if (ParseStructuredJudgeVerdictSignal(line).HasValue) continue;

                string normalized = line.Trim('*', '_', '`', '#', '>', '-', ' ');
                if (ParseExplicitJudgeVerdictLine(normalized).HasValue) continue;

                lines.Add(line);
            }

            return String.Join(" ", lines);
        }

        private static JudgeVerdict? ParseStructuredJudgeVerdictSignal(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return null;

            System.Text.RegularExpressions.Match signal = System.Text.RegularExpressions.Regex.Match(
                line.Trim(),
                @"^\[ARMADA:VERDICT\]\s+(?<verdict>PASS|FAIL|NEEDS_REVISION)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!signal.Success) return null;

            return signal.Groups["verdict"].Value.ToUpperInvariant() switch
            {
                "PASS" => JudgeVerdict.Pass,
                "FAIL" => JudgeVerdict.Fail,
                "NEEDS_REVISION" => JudgeVerdict.NeedsRevision,
                _ => null
            };
        }

        private static JudgeVerdict? ParseExplicitJudgeVerdictLine(string normalizedLine)
        {
            if (String.IsNullOrEmpty(normalizedLine)) return null;

            const System.Text.RegularExpressions.RegexOptions options = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            string candidate = normalizedLine.Trim();
            const string verdictSuffixPattern = @"(?:\s*$|\s*[\.,:;!?](?:\s+.+)?$|\s*-(?!-)\s+.+$)";

            System.Text.RegularExpressions.Match labeledVerdict = System.Text.RegularExpressions.Regex.Match(
                candidate,
                @"^VERDICT\s*(?::|=|-|IS)?\s*(?:\*\*|__|`)?(?<verdict>PASS|FAIL|NEEDS_REVISION)(?:\*\*|__|`)?"
                + verdictSuffixPattern,
                options);
            if (labeledVerdict.Success)
                return labeledVerdict.Groups["verdict"].Value.ToUpperInvariant() switch
                {
                    "PASS" => JudgeVerdict.Pass,
                    "FAIL" => JudgeVerdict.Fail,
                    "NEEDS_REVISION" => JudgeVerdict.NeedsRevision,
                    _ => null
                };

            System.Text.RegularExpressions.Match inlineLabeledVerdict = System.Text.RegularExpressions.Regex.Match(
                candidate,
                @"\bVERDICT\s*(?::|=|-|IS)?\s*(?:\*\*|__|`)?(?<verdict>PASS|FAIL|NEEDS_REVISION)(?:\*\*|__|`)?"
                + verdictSuffixPattern,
                options);
            if (inlineLabeledVerdict.Success)
                return inlineLabeledVerdict.Groups["verdict"].Value.ToUpperInvariant() switch
                {
                    "PASS" => JudgeVerdict.Pass,
                    "FAIL" => JudgeVerdict.Fail,
                    "NEEDS_REVISION" => JudgeVerdict.NeedsRevision,
                    _ => null
                };

            System.Text.RegularExpressions.Match bareVerdict = System.Text.RegularExpressions.Regex.Match(
                candidate,
                @"^(?:\*\*|__|`)?(?<verdict>PASS|FAIL|NEEDS_REVISION)(?:\*\*|__|`)?"
                + verdictSuffixPattern,
                options);
            if (bareVerdict.Success)
                return bareVerdict.Groups["verdict"].Value.ToUpperInvariant() switch
                {
                    "PASS" => JudgeVerdict.Pass,
                    "FAIL" => JudgeVerdict.Fail,
                    "NEEDS_REVISION" => JudgeVerdict.NeedsRevision,
                    _ => null
                };

            return null;
        }

        private async Task EmitMissionOutcomeTelemetryAsync(Mission mission, Captain captain, CancellationToken token)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            MissionOutcomeEventDefinition missionOutcomeEvent = BuildMissionOutcomeEvent(mission);

            try
            {
                ArmadaEvent outcomeEvent = new ArmadaEvent(missionOutcomeEvent.EventType, missionOutcomeEvent.EventMessage);
                outcomeEvent.TenantId = mission.TenantId;
                outcomeEvent.UserId = mission.UserId;
                outcomeEvent.EntityType = "mission";
                outcomeEvent.EntityId = mission.Id;
                outcomeEvent.CaptainId = captain.Id;
                outcomeEvent.MissionId = mission.Id;
                outcomeEvent.VesselId = mission.VesselId;
                outcomeEvent.VoyageId = mission.VoyageId;
                await _Database.Events.CreateAsync(outcomeEvent, token).ConfigureAwait(false);
            }
            catch (Exception evtEx)
            {
                _Logging.Warn(_Header + "error emitting mission outcome event for " + mission.Id + ": " + evtEx.Message);
            }
        }

        private static MissionOutcomeSignalDefinition BuildMissionOutcomeSignal(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            return mission.Status switch
            {
                MissionStatusEnum.Complete => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Completion, Payload = "Mission completed: " + mission.Title },
                MissionStatusEnum.PullRequestOpen => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Completion, Payload = "Pull request open: " + mission.Title },
                MissionStatusEnum.Failed => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Error, Payload = BuildFailurePayload("Mission failed: ", mission) },
                MissionStatusEnum.LandingFailed => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Error, Payload = BuildFailurePayload("Landing failed: ", mission) },
                MissionStatusEnum.Cancelled => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Error, Payload = BuildFailurePayload("Mission cancelled: ", mission) },
                _ => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Completion, Payload = "Work produced: " + mission.Title }
            };
        }

        private static MissionOutcomeEventDefinition BuildMissionOutcomeEvent(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            return mission.Status switch
            {
                MissionStatusEnum.Failed => new MissionOutcomeEventDefinition { EventType = "mission.failed", EventMessage = BuildFailurePayload("Mission failed: ", mission) },
                MissionStatusEnum.LandingFailed => new MissionOutcomeEventDefinition { EventType = "mission.landing_failed", EventMessage = BuildFailurePayload("Landing failed: ", mission) },
                MissionStatusEnum.Cancelled => new MissionOutcomeEventDefinition { EventType = "mission.cancelled", EventMessage = BuildFailurePayload("Mission cancelled: ", mission) },
                _ => new MissionOutcomeEventDefinition { EventType = "mission.work_produced", EventMessage = "Work produced: " + mission.Title }
            };
        }

        private static string BuildFailurePayload(string prefix, Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            string payload = prefix + mission.Title;
            if (!String.IsNullOrWhiteSpace(mission.FailureReason))
            {
                payload += " (" + mission.FailureReason + ")";
            }

            return payload;
        }

        /// <summary>
        /// Ensure parsed architect mission definitions are visible in the mission log even when
        /// the source was a diff snapshot or other non-stdout artifact.
        /// </summary>
        private async Task ProjectArchitectMissionsToLogAsync(Mission architectMission, List<ParsedArchitectMission> parsed, CancellationToken token)
        {
            if (architectMission == null) throw new ArgumentNullException(nameof(architectMission));
            if (parsed == null || parsed.Count == 0) return;

            try
            {
                string missionLogDir = Path.Combine(_Settings.LogDirectory, "missions");
                Directory.CreateDirectory(missionLogDir);
                string logFilePath = Path.Combine(missionLogDir, architectMission.Id + ".log");

                string existing = File.Exists(logFilePath)
                    ? await File.ReadAllTextAsync(logFilePath, token).ConfigureAwait(false)
                    : String.Empty;

                if (existing.Contains("[ARMADA:MISSION]"))
                {
                    return;
                }

                using (StreamWriter writer = new StreamWriter(logFilePath, append: true))
                {
                    await writer.WriteLineAsync(String.Empty).ConfigureAwait(false);
                    await writer.WriteLineAsync("[Armada] Parsed architect mission definitions:").ConfigureAwait(false);
                    foreach (ParsedArchitectMission mission in parsed)
                    {
                        await writer.WriteLineAsync("[ARMADA:MISSION] " + mission.Title).ConfigureAwait(false);
                        if (!String.IsNullOrEmpty(mission.DependsOnReference))
                        {
                            await writer.WriteLineAsync("Depends on: " + mission.DependsOnReference).ConfigureAwait(false);
                        }
                        if (!String.IsNullOrEmpty(mission.Description))
                        {
                            await writer.WriteLineAsync(mission.Description).ConfigureAwait(false);
                        }
                        await writer.WriteLineAsync(String.Empty).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not project architect mission definitions into log for " + architectMission.Id + ": " + ex.Message);
            }
        }

        private async Task<Mission> RequireReviewMissionAsync(string missionId, CancellationToken token)
        {
            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null) throw new InvalidOperationException("Mission not found: " + missionId);
            if (mission.Status != MissionStatusEnum.Review || !mission.RequiresReview)
            {
                throw new InvalidOperationException("Mission " + missionId + " is not waiting for an explicit review decision.");
            }

            return mission;
        }

        private async Task<Dock?> ReadMissionDockAsync(Mission mission, CancellationToken token)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (String.IsNullOrEmpty(mission.DockId)) return null;

            return !String.IsNullOrEmpty(mission.TenantId)
                ? await _Database.Docks.ReadAsync(mission.TenantId, mission.DockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
        }

        private async Task ReclaimMissionDockAsync(string dockId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(dockId)) return;

            try
            {
                await _Docks.ReclaimAsync(dockId, token: token).ConfigureAwait(false);
            }
            catch (Exception reclaimEx)
            {
                _Logging.Warn(_Header + "error reclaiming dock " + dockId + ": " + reclaimEx.Message);
            }
        }

        private async Task DispatchPendingMissionsAsync(CancellationToken token)
        {
            List<Mission> pendingMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false);
            if (!pendingMissions.Any()) return;

            Mission nextMission = pendingMissions.OrderBy(m => m.Priority).ThenBy(m => m.CreatedUtc).First();
            if (String.IsNullOrEmpty(nextMission.VesselId)) return;

            Vessel? vessel = await _Database.Vessels.ReadAsync(nextMission.VesselId, token).ConfigureAwait(false);
            if (vessel == null) return;

            await TryAssignAsync(nextMission, vessel, token).ConfigureAwait(false);
        }

        private static string? NormalizeReviewComment(string? comment)
        {
            if (String.IsNullOrWhiteSpace(comment)) return null;
            return comment.Trim();
        }

        private static string BuildReviewDeniedFailureReason(string comment)
        {
            return "Review denied: " + comment;
        }

        private static string ApplyReviewFeedback(string? description, string reviewComment)
        {
            string existing = description ?? String.Empty;
            int markerIndex = existing.IndexOf(ReviewFeedbackMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                existing = existing.Substring(0, markerIndex).TrimEnd();
            }

            string feedbackSection =
                ReviewFeedbackMarker + "\n" +
                "## Review Feedback\n" +
                reviewComment.Trim() + "\n\n" +
                "Address this feedback and continue the mission on the existing branch.\n";

            if (String.IsNullOrWhiteSpace(existing))
            {
                return feedbackSection;
            }

            return existing.TrimEnd() + "\n\n---\n\n" + feedbackSection;
        }

        /// <summary>
        /// Append reviewer guidance from a conditional approval to a downstream stage description, so the next
        /// captain must take the prior-stage feedback into account. Idempotent: an existing guidance block is
        /// replaced rather than stacked.
        /// </summary>
        private static string ApplyReviewerGuidance(string? description, string reviewComment)
        {
            string existing = description ?? String.Empty;
            int markerIndex = existing.IndexOf(ReviewerGuidanceMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                existing = existing.Substring(0, markerIndex).TrimEnd();
            }

            string guidanceSection =
                ReviewerGuidanceMarker + "\n" +
                "## Reviewer Guidance (from prior stage approval)\n" +
                reviewComment.Trim() + "\n\n" +
                "The previous stage was conditionally approved with the guidance above. Take it into account as you carry out this stage.\n";

            if (String.IsNullOrWhiteSpace(existing))
            {
                return guidanceSection;
            }

            return existing.TrimEnd() + "\n\n---\n\n" + guidanceSection;
        }

        /// <summary>
        /// After a conditional approval, fold the reviewer's guidance into the pending next-stage mission(s)
        /// that depend on the just-approved mission, before they dispatch.
        /// </summary>
        private async Task ApplyConditionalFeedbackToNextStagesAsync(Mission mission, string reviewComment, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.VoyageId)) return;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId, token).ConfigureAwait(false);
            foreach (Mission next in voyageMissions)
            {
                if (next.DependsOnMissionId != mission.Id) continue;
                if (next.Status != MissionStatusEnum.Pending) continue;

                next.Description = ApplyReviewerGuidance(next.Description, reviewComment);
                next.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(next, token).ConfigureAwait(false);
            }
        }

        private async Task<Captain?> FindAvailableCaptainAsync(string? persona, CaptainTierEnum? requiredTier, string? requestedCaptainId, CancellationToken token)
        {
            // Only idle captains are eligible for assignment
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            if (idleCaptains.Count == 0)
                return null;

            // Preferred (dictated) captain: honor it when it is idle, bypassing the AllowedPersonas fence and
            // tier routing since the operator explicitly chose it. When it is busy (or has been deleted), fall
            // through to persona/tier routing using requiredTier as the fallback tier.
            if (!String.IsNullOrEmpty(requestedCaptainId))
            {
                Captain? preferred = idleCaptains.FirstOrDefault(c => c.Id == requestedCaptainId);
                if (preferred != null)
                    return preferred;

                Captain? requested = await _Database.Captains.ReadAsync(requestedCaptainId, token).ConfigureAwait(false);
                if (requested == null)
                    _Logging.Warn(_Header + "requested captain " + requestedCaptainId + " not found; falling back to normal persona/tier routing");
                else
                    _Logging.Info(_Header + "requested captain " + requestedCaptainId + " is busy; falling back by tier for this dispatch tick");
            }

            // Filter by AllowedPersonas (null persona = any; null AllowedPersonas = fills any persona).
            List<Captain> eligible;
            if (String.IsNullOrEmpty(persona))
            {
                eligible = new List<Captain>(idleCaptains);
            }
            else
            {
                eligible = new List<Captain>();
                foreach (Captain captain in idleCaptains)
                {
                    if (String.IsNullOrEmpty(captain.AllowedPersonas) || CaptainAllowsPersona(captain, persona))
                        eligible.Add(captain);
                }
            }

            if (eligible.Count == 0)
            {
                return null;
            }

            // Tier routing -- only when the mission explicitly requires a tier (so default dispatch is
            // unchanged). Narrow to captains at/above the required tier, preferring the lowest eligible
            // tier so strong captains are not consumed by cheaper work (upward-only fallback).
            if (requiredTier.HasValue)
            {
                List<Captain> tierEligible = eligible
                    .Where(c => CaptainTierSelector.EffectiveTier(c) >= requiredTier.Value)
                    .ToList();
                if (tierEligible.Count == 0)
                {
                    // No idle captain is strong enough for this mission's tier; leave it pending.
                    return null;
                }
                CaptainTierEnum bestTier = tierEligible.Min(CaptainTierSelector.EffectiveTier);
                eligible = tierEligible.Where(c => CaptainTierSelector.EffectiveTier(c) == bestTier).ToList();
            }

            // Prefer captains whose PreferredPersona matches
            if (!String.IsNullOrEmpty(persona))
            {
                foreach (Captain captain in eligible)
                {
                    if (!String.IsNullOrEmpty(captain.PreferredPersona) &&
                        PersonaCatalog.Matches(captain.PreferredPersona, persona))
                    {
                        return captain;
                    }
                }
            }

            // No preferred match -- return first eligible
            return eligible[0];
        }

        /// <summary>
        /// Resolve and persist the preferred captain and fallback tier for a mission from (in order) an
        /// explicit value already set, the voyage-level override for the mission's persona, or the persona's
        /// default captain. A dangling captain reference is left as-is and degrades to normal routing at
        /// assignment time. Best-effort: never throws for a missing persona/voyage.
        /// </summary>
        private async Task ResolvePreferredCaptainAsync(Mission mission, CancellationToken token)
        {
            if (mission == null) return;
            if (!String.IsNullOrEmpty(mission.RequestedCaptainId)) return;
            if (String.IsNullOrEmpty(mission.Persona)) return;

            string? resolvedCaptainId = null;
            CaptainTierEnum? resolvedTier = null;

            List<CaptainAssignmentOverride> overrides = await ReadVoyageCaptainOverridesAsync(mission.VoyageId, token).ConfigureAwait(false);
            foreach (CaptainAssignmentOverride ov in overrides)
            {
                if (!PersonaCatalog.Matches(ov.Persona, mission.Persona)) continue;
                resolvedCaptainId = String.IsNullOrEmpty(ov.CaptainId) ? null : ov.CaptainId;
                resolvedTier = ov.FallbackTier;
                break;
            }

            if (String.IsNullOrEmpty(resolvedCaptainId))
            {
                Persona? persona = await ReadPersonaByNameAsync(mission.TenantId, mission.Persona, token).ConfigureAwait(false);
                if (persona != null && !String.IsNullOrEmpty(persona.DefaultCaptainId))
                    resolvedCaptainId = persona.DefaultCaptainId;
            }

            if (String.IsNullOrEmpty(resolvedCaptainId) && resolvedTier == null) return;

            bool changed = false;
            if (!String.IsNullOrEmpty(resolvedCaptainId))
            {
                mission.RequestedCaptainId = resolvedCaptainId;
                changed = true;
            }

            if (mission.Tier == null)
            {
                if (resolvedTier != null)
                {
                    mission.Tier = resolvedTier;
                    changed = true;
                }
                else if (!String.IsNullOrEmpty(mission.RequestedCaptainId))
                {
                    CaptainTierEnum? preferredTier = await CaptainEffectiveTierAsync(mission.RequestedCaptainId, token).ConfigureAwait(false);
                    if (preferredTier != null)
                    {
                        mission.Tier = preferredTier;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            }
        }

        private async Task<Persona?> ReadPersonaByNameAsync(string? tenantId, string personaName, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(tenantId))
            {
                Persona? scoped = await _Database.Personas.ReadByNameAsync(tenantId, personaName, token).ConfigureAwait(false);
                if (scoped != null) return scoped;
            }

            return await _Database.Personas.ReadByNameAsync(personaName, token).ConfigureAwait(false);
        }

        private async Task<CaptainTierEnum?> CaptainEffectiveTierAsync(string captainId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(captainId)) return null;
            Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null) return null;
            return CaptainTierSelector.EffectiveTier(captain);
        }

        private async Task<List<CaptainAssignmentOverride>> ReadVoyageCaptainOverridesAsync(string? voyageId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(voyageId)) return new List<CaptainAssignmentOverride>();
            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return new List<CaptainAssignmentOverride>();
            return DeserializeCaptainOverrides(voyage.CaptainOverridesJson);
        }

        /// <summary>
        /// Deserialize a voyage's captain-override JSON into a list. Returns an empty list for null, empty, or
        /// malformed input rather than throwing.
        /// </summary>
        /// <param name="json">Serialized override array, or null.</param>
        /// <returns>The parsed overrides, or an empty list.</returns>
        public static List<CaptainAssignmentOverride> DeserializeCaptainOverrides(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new List<CaptainAssignmentOverride>();
            try
            {
                List<CaptainAssignmentOverride>? parsed = JsonSerializer.Deserialize<List<CaptainAssignmentOverride>>(json);
                return parsed ?? new List<CaptainAssignmentOverride>();
            }
            catch (JsonException)
            {
                return new List<CaptainAssignmentOverride>();
            }
        }

        /// <summary>
        /// Serialize a list of captain overrides for voyage persistence. Returns null when the list is null or
        /// empty so the column stays null rather than storing "[]".
        /// </summary>
        /// <param name="overrides">The overrides to serialize, or null.</param>
        /// <returns>The serialized JSON array, or null.</returns>
        public static string? SerializeCaptainOverrides(List<CaptainAssignmentOverride>? overrides)
        {
            if (overrides == null || overrides.Count == 0) return null;
            return JsonSerializer.Serialize(overrides);
        }

        private static bool CaptainAllowsPersona(Captain captain, string persona)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(persona)) return true;
            if (String.IsNullOrEmpty(captain.AllowedPersonas)) return true;

            try
            {
                List<string>? allowedPersonas = JsonSerializer.Deserialize<List<string>>(captain.AllowedPersonas);
                if (allowedPersonas != null)
                {
                    foreach (string allowedPersona in allowedPersonas)
                    {
                        if (PersonaCatalog.Matches(allowedPersona, persona))
                            return true;
                    }

                    return false;
                }
            }
            catch
            {
            }

            string normalizedPersona = PersonaCatalog.NormalizeName(persona);
            if (captain.AllowedPersonas.Contains("\"" + normalizedPersona + "\"", StringComparison.OrdinalIgnoreCase))
                return true;

            if (PersonaCatalog.Matches(persona, PersonaCatalog.TestEngineer) &&
                captain.AllowedPersonas.Contains("\"" + PersonaCatalog.LegacyTestEngineer + "\"", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool UsesSharedLocalAndWorkingDirectory(Vessel vessel)
        {
            if (vessel == null) return false;
            if (String.IsNullOrWhiteSpace(vessel.LocalPath) || String.IsNullOrWhiteSpace(vessel.WorkingDirectory))
                return false;

            try
            {
                string localPath = Path.GetFullPath(vessel.LocalPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string workingDirectory = Path.GetFullPath(vessel.WorkingDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return String.Equals(localPath, workingDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return String.Equals(vessel.LocalPath.Trim(), vessel.WorkingDirectory.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Determine whether a dependent pipeline mission has had upstream handoff context applied.
        /// The handoff path stamps the downstream mission with the dependency's branch name,
        /// so branch equality is used as the minimum readiness signal before launch.
        /// </summary>
        private static bool IsPipelineHandoffPrepared(Mission mission, Mission dependency)
        {
            if (mission == null || dependency == null) return false;
            if (String.IsNullOrEmpty(mission.DependsOnMissionId)) return true;

            if (String.Equals(dependency.Persona, "Architect", StringComparison.OrdinalIgnoreCase))
            {
                return !String.IsNullOrEmpty(mission.Description) &&
                    mission.Description.Contains(ArchitectHandoffMarker, StringComparison.Ordinal);
            }

            // If the dependency never had a branch, there is no stronger handoff signal
            // available here; fall back to allowing assignment.
            if (String.IsNullOrEmpty(dependency.BranchName)) return true;

            return String.Equals(mission.BranchName, dependency.BranchName, StringComparison.Ordinal);
        }

        private async Task<bool> ShouldDeferArchitectSequencedMissionAsync(Mission mission, CancellationToken token)
        {
            if (mission == null) return false;
            if (!String.Equals(mission.Persona, "Worker", StringComparison.OrdinalIgnoreCase)) return false;
            if (String.IsNullOrEmpty(mission.VoyageId)) return false;
            if (String.IsNullOrEmpty(mission.Description)) return false;

            string description = mission.Description.ToLowerInvariant();
            bool requestsDeferredExecution =
                description.Contains("after both implementation missions complete") ||
                description.Contains("sequential after both implementation missions") ||
                description.Contains("after the implementation missions land") ||
                description.Contains("after the implementation details are settled") ||
                description.Contains("after implementation details are settled") ||
                description.Contains("after the implementation details are finalized");

            if (!requestsDeferredExecution) return false;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId, token).ConfigureAwait(false);
            return voyageMissions.Any(m =>
                m.Id != mission.Id &&
                String.Equals(m.Persona, "Worker", StringComparison.OrdinalIgnoreCase) &&
                m.Status != MissionStatusEnum.Complete &&
                m.Status != MissionStatusEnum.WorkProduced &&
                m.Status != MissionStatusEnum.Failed &&
                m.Status != MissionStatusEnum.Cancelled &&
                m.Status != MissionStatusEnum.LandingFailed);
        }

        private static string? ResolveGitInfoExcludePath(string worktreePath)
        {
            if (String.IsNullOrEmpty(worktreePath)) return null;

            string gitPath = Path.Combine(worktreePath, ".git");
            if (Directory.Exists(gitPath))
            {
                return Path.Combine(gitPath, "info", "exclude");
            }

            if (!File.Exists(gitPath))
            {
                return null;
            }

            string gitPointer = File.ReadAllText(gitPath).Trim();
            const string prefix = "gitdir:";
            if (!gitPointer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string gitDir = gitPointer.Substring(prefix.Length).Trim();
            if (!Path.IsPathRooted(gitDir))
            {
                gitDir = Path.GetFullPath(Path.Combine(worktreePath, gitDir));
            }

            return Path.Combine(gitDir, "info", "exclude");
        }

        #endregion
    }
}
