namespace Armada.Core.Services
{
    using System.Text;
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
    public partial class MissionService : IMissionService
    {
        #region Public-Members

        /// <inheritdoc />
        public Func<Mission, Dock, Task>? OnCaptureDiff { get; set; }

        /// <summary>
        /// Take the telemetry the lifecycle handler recorded for a mission's latest launch (runtime-reported usage and model), or null.
        /// </summary>
        public Func<string, ChatTurnTelemetryRecorder?>? OnTakeMissionTelemetry { get; set; }

        /// <inheritdoc />
        public Func<Mission, Dock, Task>? OnMissionComplete { get; set; }

        /// <inheritdoc />
        public Func<string, string?>? OnGetMissionOutput { get; set; }

        /// <summary>
        /// Read a mission's output without consuming it (the stage check uses it to see a verifier's reported outcome).
        /// </summary>
        public Func<string, string?>? OnPeekMissionOutput { get; set; }

        /// <summary>
        /// Optional callback invoked when a mission enters an approval-required review state.
        /// </summary>
        public Action<Mission>? OnReviewRequested { get; set; }

        /// <summary>
        /// Optional gate consulted before a mission is assigned to the selected captain. Returns true to allow
        /// the assignment, or false to defer it (the mission stays Pending and is retried). Used to enforce the
        /// "require a connected Harbor" policy: when set and it returns false, the mission waits instead of
        /// running in-process.
        /// </summary>
        public Func<Mission, Captain, Task<bool>>? CanAssignMissionAsync { get; set; }

        /// <summary>
        /// Chooses where a mission's dock is created before it is provisioned: on a Harbor that can serve the vessel, on
        /// the Admiral's host, or nowhere yet (the mission stays Pending). When null every dock is created on the
        /// Admiral's host.
        /// </summary>
        public Func<Mission, Captain, Vessel, Task<DockPlacement>>? ResolveDockPlacementAsync { get; set; }

        /// <summary>
        /// Where docks live. Operations on a Harbor-hosted dock (instruction files, git, the Definition-of-Done gate) run
        /// on its Harbor through this; without it every dock is treated as local.
        /// </summary>
        public DockHostResolver? DockHosts { get; set; }

        #endregion

        #region Private-Members

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EndpointBreakerState> _EndpointBreakers = new System.Collections.Concurrent.ConcurrentDictionary<string, EndpointBreakerState>(StringComparer.OrdinalIgnoreCase);
        private const int _EndpointBreakerThreshold = 3;
        private static readonly TimeSpan _EndpointBreakerOpenFor = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan _UnhealthyEndpointWindow = TimeSpan.FromMinutes(15);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _CompletionsInFlight = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _ReviewDecisionGate = new SemaphoreSlim(1, 1);

        private const int _MaxLaunchFailuresBeforeFail = 6;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MissionLaunchFailure> _LaunchFailures = new System.Collections.Concurrent.ConcurrentDictionary<string, MissionLaunchFailure>(StringComparer.Ordinal);

        private string _Header = "[MissionService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService? _Git;
        private IDockService _Docks;
        private ICaptainService _Captains;
        private IPromptTemplateService? _PromptTemplates;
        private IDefinitionOfDoneGate? _DefinitionOfDone;
        private readonly IDockFileSystem _LocalFiles = new LocalDockFileSystem();
        private readonly IHostCommandExecutor _LocalCommands = new LocalHostCommandExecutor();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _PlacementWaitReasons =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _RefusedHandoffWarnings =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
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
            "MUX.md",
            "AGENTS.override.md"
        };

        /// <summary>
        /// Tracks in-flight mission assignment operations by mission ID.
        /// Prevents duplicate provisioning/launch when multiple dispatch paths
        /// race on the same mission.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, byte> _InFlightAssignments =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

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
            /// Optional dependency reference emitted by the architect (legacy marker format: a "Depends on:" line).
            /// </summary>
            public string? DependsOnReference { get; set; } = null;

            /// <summary>
            /// 1-based index of an earlier parsed mission this one depends on (structured armada-plan format).
            /// </summary>
            public int? DependsOnIndex { get; set; } = null;

            /// <summary>
            /// Whether the mission waits until the other Worker missions in the voyage settle.
            /// </summary>
            public bool WaitForOtherMissions { get; set; } = false;
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
        /// <param name="definitionOfDone">Optional in-dock Definition-of-Done gate.</param>
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
        public async Task<MissionAssignmentBlocker?> GetAssignmentBlockerAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.Status != MissionStatusEnum.Pending) return null;

            MissionAssignmentBlocker blocker = new MissionAssignmentBlocker();

            Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.VesselMissing;
                blocker.Summary = String.IsNullOrEmpty(mission.VesselId)
                    ? "This mission has no vessel, so it cannot be assigned."
                    : "This mission's vessel no longer exists, so it cannot be assigned.";
                return blocker;
            }

            if (UsesSharedLocalAndWorkingDirectory(vessel))
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.VesselMisconfigured;
                blocker.Summary = "Vessel " + vessel.Name + " uses the same directory for its local repository and working directory, so a dock cannot be provisioned. Change the vessel's working directory.";
                return blocker;
            }

            if (_LaunchFailures.TryGetValue(mission.Id, out MissionLaunchFailure? launchFailure))
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.LaunchFailing;
                blocker.UntilUtc = launchFailure.NextAttemptUtc;
                blocker.Summary = "Starting the captain failed " + launchFailure.Count + (launchFailure.Count == 1 ? " time" : " times")
                    + ": " + launchFailure.Reason + " Armada tries again at " + launchFailure.NextAttemptUtc.ToString("HH:mm:ss") + " UTC.";
                return blocker;
            }

            if (!String.IsNullOrEmpty(mission.DependsOnMissionId))
            {
                Mission? dependency = await _Database.Missions.ReadAsync(mission.DependsOnMissionId, token).ConfigureAwait(false);
                blocker.DependsOnMissionId = mission.DependsOnMissionId;
                if (dependency == null)
                {
                    blocker.Reason = MissionAssignmentBlockerReasonEnum.DependencyNotFinished;
                    blocker.Summary = "Waiting for mission " + mission.DependsOnMissionId + ", which no longer exists.";
                    return blocker;
                }

                if (dependency.Status != MissionStatusEnum.Complete && dependency.Status != MissionStatusEnum.WorkProduced)
                {
                    blocker.Reason = MissionAssignmentBlockerReasonEnum.DependencyNotFinished;
                    blocker.Summary = "Waiting for mission '" + dependency.Title + "' (" + dependency.Status + ") to finish.";
                    return blocker;
                }

                if (dependency.Status == MissionStatusEnum.WorkProduced && !IsPipelineHandoffPrepared(mission, dependency))
                {
                    blocker.Reason = MissionAssignmentBlockerReasonEnum.DependencyHandoffPending;
                    blocker.Summary = "Mission '" + dependency.Title + "' finished; Armada is preparing the handoff to this stage.";
                    return blocker;
                }

                blocker.DependsOnMissionId = null;
            }

            IntegrationBarrierState barrier = await EvaluateIntegrationBarrierAsync(mission, token).ConfigureAwait(false);
            if (barrier.FailedMission != null)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers;
                blocker.SetBlockingMissions(new List<string> { barrier.FailedMission.Id });
                blocker.Summary = "Mission '" + barrier.FailedMission.Title + "' in another slice ended " + barrier.FailedMission.Status
                    + "; this integration will not run without it and is cancelled on the next dispatch pass. Restart that mission to continue.";
                return blocker;
            }
            if (barrier.Waiting)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers;
                blocker.Summary = barrier.WaitingSummary ?? "Waiting for the voyage's other implementation pipelines, including reviews and checks, to settle.";
                return blocker;
            }

            List<Mission> vesselMissions = await ActiveMissionsOnVesselAsync(vessel.Id, token).ConfigureAwait(false);
            List<Mission> activeMissions = vesselMissions
                .Where(m => m.Id != mission.Id && (m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress))
                .ToList();
            List<Mission> broadMissions = activeMissions.Where(m => IsBroadScope(m)).ToList();
            if (broadMissions.Count > 0)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.VesselBroadScopeMissionActive;
                blocker.SetBlockingMissions(broadMissions.Select(m => m.Id));
                blocker.Summary = "Broad-scope mission '" + broadMissions[0].Title + "' holds vessel " + vessel.Name + " until it finishes.";
                return blocker;
            }

            if (activeMissions.Count > 0 && IsBroadScope(mission))
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.BroadScopeWaitingForVessel;
                blocker.SetBlockingMissions(activeMissions.Select(m => m.Id));
                blocker.Summary = "This is a broad-scope mission; it waits until vessel " + vessel.Name + " has no active missions (" + activeMissions.Count + " running).";
                return blocker;
            }

            List<Mission> sameBranch = BranchHolders(mission, activeMissions);
            if (sameBranch.Count > 0)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.BranchInUse;
                blocker.SetBlockingMissions(sameBranch.Select(m => m.Id));
                blocker.Summary = "Branch " + mission.BranchName + " is in use by '" + sameBranch[0].Title + "'. This mission continues that branch and starts when it finishes.";
                return blocker;
            }

            if (activeMissions.Count > 0 && !vessel.AllowConcurrentMissions)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.VesselConcurrencyLimit;
                blocker.SetBlockingMissions(activeMissions.Select(m => m.Id));
                blocker.Summary = "Vessel " + vessel.Name + " runs one mission at a time and is busy with '" + activeMissions[0].Title + "'. Turn on Allow Concurrent Missions on the vessel to run more at once.";
                return blocker;
            }

            // Captain availability: the same pool FindAvailableCaptainAsync draws from (idle captains), checked
            // without resolving or persisting a preferred captain.
            List<Captain> allCaptains = (await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false))
                .Where(c => CaptainVisibleToMission(c, mission)).ToList();
            if (allCaptains.Count == 0)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.NoCaptains;
                blocker.Summary = "Waiting for a captain: there are no captains. Create one to run missions.";
                return blocker;
            }

            List<Captain> idleCaptains = allCaptains.Where(c => c.State == CaptainStateEnum.Idle).ToList();
            if (idleCaptains.Count > 0)
            {
                bool eligible = await IsAnyCaptainEligibleAsync(idleCaptains, mission, token).ConfigureAwait(false);
                if (eligible && _PlacementWaitReasons.TryGetValue(mission.Id, out string? placementReason))
                {
                    blocker.Reason = MissionAssignmentBlockerReasonEnum.PlacementWaiting;
                    blocker.Summary = "A captain is free, but the mission's dock cannot be placed yet: " + placementReason;
                    return blocker;
                }

                if (eligible)
                {
                    blocker.Reason = MissionAssignmentBlockerReasonEnum.AwaitingDispatch;
                    blocker.Summary = "A captain is free; the mission is assigned on the next dispatch cycle.";
                    return blocker;
                }

                // Authors of the reviewed work are named as a cause only when the exclusion is what removed them.
                HashSet<string> excludedAuthors = _Settings.ExcludeReviewAuthorsFromJudge
                    ? await ReviewAuthorCaptainIdsAsync(mission, token).ConfigureAwait(false)
                    : new HashSet<string>(StringComparer.Ordinal);
                bool authorsIdle = idleCaptains.Any(c => excludedAuthors.Contains(c.Id));
                blocker.Reason = MissionAssignmentBlockerReasonEnum.NoEligibleCaptain;
                blocker.Summary = "Waiting for a captain: no idle captain may take this mission"
                    + (!String.IsNullOrEmpty(mission.Persona) ? " (persona " + mission.Persona + ")" : "")
                    + (mission.Tier.HasValue ? " (tier " + mission.Tier.Value + " or higher)" : "")
                    + ". Check the persona's captain priorities and the captains' allowed personas and tiers."
                    + (authorsIdle ? " Idle captains that ran an earlier stage of the reviewed work are excluded because ExcludeReviewAuthorsFromJudge is on." : "");
                await DescribeCaptainsAsync(blocker, allCaptains, mission, token).ConfigureAwait(false);
                return blocker;
            }

            blocker.Reason = MissionAssignmentBlockerReasonEnum.NoIdleCaptain;
            await DescribeCaptainsAsync(blocker, allCaptains, mission, token).ConfigureAwait(false);
            List<DateTime> quarantineEnds = allCaptains
                .Where(c => c.State == CaptainStateEnum.Quarantined && c.QuarantineUntilUtc.HasValue)
                .Select(c => c.QuarantineUntilUtc!.Value)
                .ToList();
            if (allCaptains.All(c => c.State == CaptainStateEnum.Quarantined) && quarantineEnds.Count > 0)
            {
                blocker.UntilUtc = quarantineEnds.Min();
            }

            if (blocker.CaptainCount == 1 && blocker.Captains.Count == 1)
            {
                blocker.Summary = "Waiting for a captain: " + (blocker.Captains[0].CaptainName ?? blocker.Captains[0].CaptainId) + " is " + blocker.Captains[0].Detail + ".";
            }
            else
            {
                blocker.Summary = "Waiting for a captain: every captain is busy.";
            }

            return blocker;
        }

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

                if (_LaunchFailures.TryGetValue(mission.Id, out MissionLaunchFailure? backoff) && DateTime.UtcNow < backoff.NextAttemptUtc
                    && !await HasUntriedIdleCaptainAsync(mission, backoff, token).ConfigureAwait(false))
                {
                    _Logging.Debug(_Header + "mission " + mission.Id + " backing off after " + backoff.Count + " failed launch(es) until " + backoff.NextAttemptUtc.ToString("o"));
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

                        if (!mission.FailureKind.HasValue)
                        {
                            mission.FailureKind = MissionFailureKindEnum.DependencyFailed;
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
                    _Logging.Debug(_Header + "mission " + mission.Id + " depends on " + dependency.Id +
                        " which is WorkProduced, but handoff is not prepared yet -- deferring assignment");
                    return false;
                }
            }

            IntegrationBarrierState barrier = await EvaluateIntegrationBarrierAsync(mission, token).ConfigureAwait(false);
            if (barrier.FailedMission != null)
            {
                await CancelIntegrationForFailedSliceAsync(mission, barrier.FailedMission, token).ConfigureAwait(false);
                return false;
            }
            if (barrier.Waiting)
            {
                _Logging.Debug(_Header + "mission " + mission.Id + " is the voyage's final integration slice -- deferring assignment: " + barrier.WaitingSummary);
                return false;
            }

            // Check for vessel-level lock (broad-scope missions block new assignments)
            List<Mission> activeMissions = await ActiveMissionsOnVesselAsync(vessel.Id, token).ConfigureAwait(false);
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

            // Two missions continuing one branch (two slices that both build on an earlier slice) cannot share it: git
            // refuses a second worktree for a checked-out branch. The later one waits and then stacks on the first.
            List<Mission> branchHolders = BranchHolders(mission, activeMissions);
            if (branchHolders.Count > 0)
            {
                _Logging.Info(_Header + "mission " + mission.Id + " waits: branch " + mission.BranchName + " is in use by " + branchHolders[0].Id);
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
            Captain? captain = await FindAvailableCaptainAsync(mission, token).ConfigureAwait(false);
            if (captain == null)
            {
                _Logging.Warn(_Header + "no idle captains available for mission " + mission.Id +
                    (mission.Persona != null ? " (persona: " + mission.Persona + ")" : ""));
                return false;
            }

            // Policy gate: when the deployment requires a connected Harbor, defer assignment until one is
            // available for this mission rather than running the captain in-process. The mission stays Pending
            // and is retried on the next dispatch cycle.
            if (CanAssignMissionAsync != null)
            {
                bool allowed = await CanAssignMissionAsync(mission, captain).ConfigureAwait(false);
                if (!allowed)
                {
                    _Logging.Info(_Header + "deferring mission " + mission.Id + ": launch policy requires the requesting user's Harbor to be connected and eligible, and none is yet");
                    return false;
                }
            }

            // Choose where the dock lives before creating it: a Harbor that can serve the vessel, the Admiral's host, or
            // nowhere yet (the mission stays Pending and is retried on the next dispatch cycle).
            DockPlacement placement = DockPlacement.OnAdmiral("no Harbor placement is configured");
            if (ResolveDockPlacementAsync != null)
            {
                placement = await ResolveDockPlacementAsync(mission, captain, vessel).ConfigureAwait(false);
                if (placement.Wait)
                {
                    // Log a reason once per mission until it changes, not on every dispatch cycle.
                    string previousReason = _PlacementWaitReasons.TryGetValue(mission.Id, out string? known) ? known : String.Empty;
                    if (!String.Equals(previousReason, placement.Reason, StringComparison.Ordinal))
                    {
                        _PlacementWaitReasons[mission.Id] = placement.Reason;
                        _Logging.Warn(_Header + "mission " + mission.Id + " waits: " + placement.Reason);
                    }

                    return false;
                }
            }

            _PlacementWaitReasons.TryRemove(mission.Id, out string? _);

            // Missions with an existing branch continue work on that branch. This covers
            // downstream pipeline stages, review rework loops, and resumed missions.
            bool preserveExistingBranch = !String.IsNullOrEmpty(mission.BranchName);
            string branchName = preserveExistingBranch
                ? mission.BranchName!
                : BuildMissionBranchName(captain, mission);

            // Claim the captain before any slow work. The claim is atomic (only an Idle captain is taken), so a
            // concurrent dispatcher can never take it in the gap, and from here on the captain names this mission,
            // which keeps orphan recovery from reverting the assignment while its dock is being provisioned.
            bool claimed = await _Database.Captains.TryClaimAsync(captain.Id, mission.Id, null, token).ConfigureAwait(false);
            if (!claimed)
            {
                _Logging.Info(_Header + "captain " + captain.Id + " was claimed by another mission before " + mission.Id + " could take it; retrying next cycle");
                return false;
            }

            mission.BranchName = branchName;
            mission.CaptainId = captain.Id;
            mission.Status = MissionStatusEnum.Assigned;
            mission.LastUpdateUtc = DateTime.UtcNow;
            bool assignmentRecorded = await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { MissionStatusEnum.Pending }, token).ConfigureAwait(false);
            if (!assignmentRecorded)
            {
                // Someone else moved the mission (cancelled, or assigned by another caller) after it was read.
                _Logging.Info(_Header + "mission " + mission.Id + " left Pending before its assignment was recorded; releasing captain " + captain.Id);
                await _Database.Captains.TryReleaseFromMissionAsync(captain.Id, mission.Id, CancellationToken.None).ConfigureAwait(false);
                return false;
            }

            captain.State = CaptainStateEnum.Working;
            captain.CurrentMissionId = mission.Id;
            captain.LastHeartbeatUtc = DateTime.UtcNow;
            captain.LastUpdateUtc = DateTime.UtcNow;

            // Everything from here to launch runs inside one rollback boundary: any failure puts the mission back to
            // Pending, releases the captain only while it still holds this mission, and reclaims the dock.
            Dock? dock = null;
            try
            {
                try
                {
                    _Logging.Debug(_Header + "provisioning dock for mission " + mission.Id + " on vessel " + vessel.Id + " with captain " + captain.Id);
                    if (placement.HarborId != null)
                        dock = await _Docks.ProvisionOnHarborAsync(vessel, captain, branchName, mission.Id, placement.HarborId, token).ConfigureAwait(false);
                    else
                        dock = await _Docks.ProvisionAsync(vessel, captain, branchName, mission.Id, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "dock provisioning threw for mission " + mission.Id + " vessel " + vessel.Id + " captain " + captain.Id + ": "
                        + (ex is DockProvisioningException ? ex.Message : ex.ToString()));
                    await RollbackAssignmentAsync(mission, captain, null, preserveExistingBranch).ConfigureAwait(false);
                    return false;
                }

                if (dock == null)
                {
                    _Logging.Warn(_Header + "dock provisioning failed for captain " + captain.Id + " vessel " + vessel.Id + " mission " + mission.Id + " -- reverting to Pending");
                    await RollbackAssignmentAsync(mission, captain, null, preserveExistingBranch).ConfigureAwait(false);
                    return false;
                }

                // Track the dock on the mission and the captain, each only while they still describe this assignment.
                mission.DockId = dock.Id;
                mission.LastUpdateUtc = DateTime.UtcNow;
                bool dockRecorded = await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { MissionStatusEnum.Assigned }, token).ConfigureAwait(false);
                bool captainHasDock = dockRecorded && await _Database.Captains.TrySetDockAsync(captain.Id, mission.Id, dock.Id, token).ConfigureAwait(false);
                if (!captainHasDock)
                {
                    _Logging.Warn(_Header + "mission " + mission.Id + " or captain " + captain.Id + " changed while dock " + dock.Id + " was provisioned; abandoning this assignment");
                    await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                    return false;
                }

                captain.CurrentDockId = dock.Id;

                // Commit continuity: a later stage must start from exactly the commit the stage before it produced. A dock
                // that lacks it (the branch lived on another host and this one created it from the default branch) would
                // silently discard upstream work, so the stage is refused with the reason instead.
                string? continuityFailure = preserveExistingBranch ? await CheckStageCommitContinuityAsync(mission, dock, token).ConfigureAwait(false) : null;
                if (continuityFailure != null)
                {
                    _Logging.Warn(_Header + "mission " + mission.Id + " refused: " + continuityFailure);
                    await FailAssignmentAsync(mission, captain, dock, continuityFailure).ConfigureAwait(false);
                    return false;
                }

                // A final integration slice starts from every other slice's settled work, not only its own lineage.
                string? integrationFailure = await IntegrateSiblingSlicesAsync(mission, dock, token).ConfigureAwait(false);
                if (integrationFailure != null)
                {
                    _Logging.Warn(_Header + "mission " + mission.Id + " refused: " + integrationFailure);
                    await FailAssignmentAsync(mission, captain, dock, integrationFailure).ConfigureAwait(false);
                    return false;
                }

                // Create assignment signal
                Signal signal = new Signal(SignalTypeEnum.Assignment, mission.Title);
                signal.TenantId = mission.TenantId;
                signal.UserId = mission.UserId;
                signal.ToCaptainId = captain.Id;
                await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

                // Generate mission CLAUDE.md into worktree (on the dock's Harbor for a Harbor-hosted dock)
                DockHost? harborHost = HarborHostFor(dock);
                await GenerateClaudeMdOnHostAsync(harborHost, dock.WorktreePath!, mission, vessel, captain, token).ConfigureAwait(false);
                await EnsureMissionInstructionsPresentAsync(harborHost, dock.WorktreePath!, mission, captain, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not prepare mission " + mission.Id + " for captain " + captain.Id + ": " + ex.Message);
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                return false;
            }
            catch (OperationCanceledException)
            {
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                throw;
            }

            // Launch agent process via captain service
            if (_Captains.OnLaunchAgent == null)
            {
                _Logging.Warn(_Header + "no OnLaunchAgent handler configured -- cannot launch agent for captain " + captain.Id);
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                return false;
            }

            int processId;
            try
            {
                processId = await _Captains.OnLaunchAgent.Invoke(captain, mission, dock).ConfigureAwait(false);
            }
            catch (MissionLaunchConfigurationException configuration)
            {
                // A deterministic policy/configuration problem cannot be repaired by repeatedly launching.
                // Keep it visible as a terminal operational failure, without a provider call or rescue loop.
                _LaunchFailures.TryRemove(mission.Id, out MissionLaunchFailure? _);
                await FailAssignmentAsync(mission, captain, dock, configuration.Message).ConfigureAwait(false);

                // The same configuration fails every mission this captain is given; take it out of the pool for the
                // quarantine window instead of letting it be picked first again.
                await QuarantineMisconfiguredCaptainAsync(captain, configuration.Message).ConfigureAwait(false);
                return false;
            }
            catch (HarborDockNotFoundException dockMissing)
            {
                // The Harbor chosen for the mission does not have its dock, and the mission cannot run on the Admiral
                // host either. Returning it to Pending would provision a new dock and launch into the same missing path
                // on every dispatch cycle, so the mission fails with the actionable reason instead, and its dependents
                // and voyage settle like any other terminal failure.
                _Logging.Warn(_Header + "mission " + mission.Id + " failed at launch: " + dockMissing.Message);
                await FailAssignmentAsync(mission, captain, dock, dockMissing.Message).ConfigureAwait(false);
                return false;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "failed to launch agent for captain " + captain.Id + ": " + ex.ToString());

                // Back off instead of re-provisioning and re-launching on every dispatch cycle; after repeated
                // failures with the same cause (a CLI that is not installed where the mission lands) fail the
                // mission with the reason rather than loop forever.
                MissionLaunchFailure failure = RecordLaunchFailure(mission, captain, ex);
                if (failure.Count >= _MaxLaunchFailuresBeforeFail && IsRuntimeMissing(ex))
                {
                    _LaunchFailures.TryRemove(mission.Id, out MissionLaunchFailure? _);
                    await FailAssignmentAsync(mission, captain, dock,
                        "Captain " + captain.Name + " could not be started " + failure.Count + " times: " + failure.Reason
                        + " If this mission should run on your Harbor, connect it and turn on Require Harbor for launch (Server settings), then restart the mission.").ConfigureAwait(false);
                    return false;
                }

                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);

                try
                {
                    Signal errorSignal = new Signal(SignalTypeEnum.Error, "Failed to launch agent: " + ex.Message);
                    errorSignal.TenantId = mission.TenantId;
                    errorSignal.UserId = mission.UserId;
                    errorSignal.FromCaptainId = captain.Id;
                    await _Database.Signals.CreateAsync(errorSignal, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception signalEx)
                {
                    _Logging.Warn(_Header + "could not record launch failure signal for mission " + mission.Id + ": " + signalEx.Message);
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                throw;
            }

            // A fast in-process captain (an ApiEndpoint loop against a quick or failing endpoint) can finish before the
            // launch call returns, and its exit handler may already have moved the mission on (WorkProduced, Failed) and
            // released the captain. Both writes below are therefore compare-and-set: the captain's process handle is
            // recorded only while it still holds this mission, and the launch only while the mission is still Assigned.
            // A failure past this point must not roll back: the agent is running and its exit handler owns the outcome.
            try
            {
                if (await _Database.Captains.TrySetProcessAsync(captain.Id, mission.Id, processId, CancellationToken.None).ConfigureAwait(false))
                {
                    captain.ProcessId = processId;
                }

                mission.ProcessId = processId;
                mission.Status = MissionStatusEnum.InProgress;
                mission.StartedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                bool launchRecorded = await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { MissionStatusEnum.Assigned }, CancellationToken.None).ConfigureAwait(false);
                Mission? currentMission = launchRecorded ? null : await _Database.Missions.ReadAsync(mission.Id, CancellationToken.None).ConfigureAwait(false);
                if (currentMission != null)
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " already moved to " + currentMission.Status + " before its launch was recorded (agent process " + processId + " finished first); keeping that state");
                    mission.ProcessId = currentMission.ProcessId;
                    mission.Status = currentMission.Status;
                    mission.StartedUtc = currentMission.StartedUtc;
                    mission.LastUpdateUtc = currentMission.LastUpdateUtc;
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "agent process " + processId + " for mission " + mission.Id + " launched but its launch could not be recorded: " + ex.Message
                    + " (the process exit handler and health checks own the outcome)");
            }

            _LaunchFailures.TryRemove(mission.Id, out MissionLaunchFailure? _);
            _Logging.Info(_Header + "launched agent process " + processId + " for captain " + captain.Id);
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

            // Deduplication is not keyed by mission id here: a mission-id gate (held past the handler) also swallowed
            // the completion of a later attempt of the same mission. Each process exit is claimed once by its caller
            // (IMissionMethods.TryClaimProcessExitAsync), and the core's first write is a compare-and-set on the status
            // it read, so a duplicate for the same attempt loses that write and stops.
            _CompletionsInFlight.AddOrUpdate(missionId, 1, (_, count) => count + 1);
            try
            {
                await HandleCompletionCoreAsync(captain, missionId, token).ConfigureAwait(false);
            }
            finally
            {
                if (_CompletionsInFlight.AddOrUpdate(missionId, 0, (_, count) => count - 1) <= 0)
                    _CompletionsInFlight.TryRemove(new KeyValuePair<string, int>(missionId, 0));
            }
        }

        /// <summary>
        /// Cancel the definition-of-done command registered for a mission, when one is running.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        internal void CancelActiveDefinitionOfDone(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return;
            _DefinitionOfDone?.CancelMission(missionId);
        }

        /// <inheritdoc />
        public async Task HandleClaimedCompletionAsync(Captain captain, string missionId, DateTime expectedVersion, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(missionId)) return;

            // Deduplication is not keyed by mission id here: a mission-id gate (held past the handler) also swallowed
            // the completion of a later attempt of the same mission. Each process exit is claimed once by its caller
            // (IMissionMethods.TryClaimProcessExitAsync), and the core's first write is a compare-and-set on the status
            // it read, so a duplicate for the same attempt loses that write and stops.
            _CompletionsInFlight.AddOrUpdate(missionId, 1, (_, count) => count + 1);
            try
            {
                await HandleCompletionCoreAsync(captain, missionId, token, expectedVersion).ConfigureAwait(false);
            }
            finally
            {
                if (_CompletionsInFlight.AddOrUpdate(missionId, 0, (_, count) => count - 1) <= 0)
                    _CompletionsInFlight.TryRemove(new KeyValuePair<string, int>(missionId, 0));
            }
        }

        /// <inheritdoc />
        public Task<bool> HasStrictCaptainAssignmentAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return HasStrictCaptainOverrideAsync(mission, token);
        }

        /// <inheritdoc />
        public string? GetPlacementWaitReason(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return null;
            return _PlacementWaitReasons.TryGetValue(missionId, out string? reason) ? reason : null;
        }

        /// <inheritdoc />
        public async Task RecordEndpointOutcomeAsync(Captain captain, bool healthy, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(captain.ModelEndpointId)) return;
            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId!, token).ConfigureAwait(false);
            if (endpoint == null) return;

            string key = EndpointCapacityKey(endpoint);
            EndpointBreakerState state = _EndpointBreakers.GetOrAdd(key, _ => new EndpointBreakerState());
            lock (state)
            {
                if (healthy)
                {
                    if (state.OpenUntilUtc.HasValue) _Logging.Info(_Header + "endpoint " + endpoint.BaseUrl + " ran cleanly; circuit closed");
                    state.ConsecutiveFailures = 0;
                    state.OpenUntilUtc = null;
                    return;
                }

                state.ConsecutiveFailures++;
                if (state.ConsecutiveFailures >= _EndpointBreakerThreshold)
                {
                    state.OpenUntilUtc = DateTime.UtcNow.Add(_EndpointBreakerOpenFor);
                    _Logging.Warn(_Header + "endpoint " + endpoint.BaseUrl + " failed " + state.ConsecutiveFailures + " run(s) in a row; no new missions go to it until " + state.OpenUntilUtc.Value.ToString("o"));
                }
            }
        }

        /// <inheritdoc />
        public bool IsCompletionInFlight(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return false;
            return _CompletionsInFlight.TryGetValue(missionId, out int count) && count > 0;
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

        #endregion

        #region Private-Methods

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _BaseCommitCheckFailures = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        private async Task<bool> HasUntriedIdleCaptainAsync(Mission mission, MissionLaunchFailure failure, CancellationToken token)
        {
            // Backoff protects against relaunching the same broken captain; it should not hold a mission back when a
            // captain that has not failed it is idle and could start it now.
            List<Captain> idle = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            lock (failure.FailedCaptainIds)
                return idle.Any(c => CaptainVisibleToMission(c, mission) && !failure.FailedCaptainIds.Contains(c.Id));
        }

        private async Task QuarantineMisconfiguredCaptainAsync(Captain captain, string reason)
        {
            try
            {
                // Take the captain out of Idle atomically where the tenant-scoped reserve applies; a tenantless captain
                // (single-user setups and tests) is checked and written directly.
                if (!String.IsNullOrEmpty(captain.TenantId)
                    && !await _Database.Captains.TryReserveAsync(captain.TenantId!, captain.Id, CaptainStateEnum.Quarantined, CancellationToken.None).ConfigureAwait(false))
                {
                    _Logging.Info(_Header + "captain " + captain.Id + " was taken before it could be quarantined for its configuration; leaving it");
                    return;
                }

                Captain? current = await _Database.Captains.ReadAsync(captain.Id, CancellationToken.None).ConfigureAwait(false);
                if (current == null) return;
                if (String.IsNullOrEmpty(captain.TenantId))
                {
                    if (current.State != CaptainStateEnum.Idle || !String.IsNullOrEmpty(current.CurrentMissionId)) return;
                    current.State = CaptainStateEnum.Quarantined;
                }
                else if (current.State != CaptainStateEnum.Quarantined)
                {
                    return;
                }
                current.QuarantineUntilUtc = DateTime.UtcNow.AddMinutes(_Settings.CaptainQuarantineMinutes);
                current.QuarantineReason = "configuration: " + reason;
                current.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(current, CancellationToken.None).ConfigureAwait(false);
                _Logging.Warn(_Header + "quarantined captain " + captain.Id + " for " + _Settings.CaptainQuarantineMinutes + " minute(s): " + reason);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not quarantine misconfigured captain " + captain.Id + ": " + ex.Message);
            }
        }

        private async Task<List<Captain>> ApplyCapacityLimitsAsync(List<Captain> idleCaptains, Mission mission, CancellationToken token)
        {
            if (idleCaptains.Count == 0) return idleCaptains;

            // Per-runtime caps (AgentSettings.MaxConcurrent, 0 = unlimited) and per-endpoint caps
            // (InferenceOptions.MaxConcurrentMissions) count every Working captain, so several captains pointed at one
            // local model server share its budget instead of each starting a run against it.
            Dictionary<AgentRuntimeEnum, int> runtimeLimits = new Dictionary<AgentRuntimeEnum, int>();
            foreach (AgentSettings agent in _Settings.Agents ?? new List<AgentSettings>())
            {
                if (agent.MaxConcurrent > 0 && (!runtimeLimits.TryGetValue(agent.Runtime, out int existing) || agent.MaxConcurrent < existing))
                    runtimeLimits[agent.Runtime] = agent.MaxConcurrent;
            }

            bool anyEndpoint = idleCaptains.Any(c => !String.IsNullOrEmpty(c.ModelEndpointId));
            if (runtimeLimits.Count == 0 && !anyEndpoint) return idleCaptains;

            List<Captain> working = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);
            Dictionary<string, ModelEndpoint?> endpoints = new Dictionary<string, ModelEndpoint?>(StringComparer.Ordinal);
            async Task<ModelEndpoint?> EndpointOf(Captain captain)
            {
                if (String.IsNullOrEmpty(captain.ModelEndpointId)) return null;
                if (!endpoints.TryGetValue(captain.ModelEndpointId!, out ModelEndpoint? endpoint))
                {
                    endpoint = await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId!, token).ConfigureAwait(false);
                    endpoints[captain.ModelEndpointId!] = endpoint;
                }
                return endpoint;
            }

            Dictionary<string, int> workingByEndpoint = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (anyEndpoint)
            {
                foreach (Captain busy in working)
                {
                    ModelEndpoint? endpoint = await EndpointOf(busy).ConfigureAwait(false);
                    if (endpoint == null) continue;
                    string key = EndpointCapacityKey(endpoint);
                    workingByEndpoint[key] = (workingByEndpoint.TryGetValue(key, out int count) ? count : 0) + 1;
                }
            }

            List<Captain> admitted = new List<Captain>();
            foreach (Captain candidate in idleCaptains)
            {
                if (runtimeLimits.TryGetValue(candidate.Runtime, out int runtimeLimit) && working.Count(w => w.Runtime == candidate.Runtime) >= runtimeLimit)
                {
                    _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": " + candidate.Runtime + " is at its MaxConcurrent of " + runtimeLimit);
                    continue;
                }

                ModelEndpoint? endpoint = await EndpointOf(candidate).ConfigureAwait(false);
                if (endpoint != null)
                {
                    if (!endpoint.Enabled)
                    {
                        _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": its model endpoint " + endpoint.Id + " is disabled");
                        continue;
                    }

                    // A recent failed health probe means a launch would fail the same way. An old result says nothing about
                    // now (probes may be off), so only a check within the window counts.
                    if (endpoint.HealthStatus == EndpointHealthStatusEnum.Unhealthy && endpoint.LastHealthCheckUtc.HasValue
                        && DateTime.UtcNow - endpoint.LastHealthCheckUtc.Value <= _UnhealthyEndpointWindow)
                    {
                        _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": endpoint " + endpoint.BaseUrl + " failed its health check at "
                            + endpoint.LastHealthCheckUtc.Value.ToString("o") + (String.IsNullOrEmpty(endpoint.LastHealthError) ? String.Empty : " (" + endpoint.LastHealthError + ")"));
                        continue;
                    }

                    if (_EndpointBreakers.TryGetValue(EndpointCapacityKey(endpoint), out EndpointBreakerState? breaker))
                    {
                        bool open;
                        lock (breaker) open = breaker.OpenUntilUtc.HasValue && breaker.OpenUntilUtc.Value > DateTime.UtcNow;
                        if (open)
                        {
                            _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": endpoint " + endpoint.BaseUrl + " circuit is open after repeated failures");
                            continue;
                        }
                    }

                    int? endpointLimit = endpoint.InferenceOptions?.MaxConcurrentMissions;
                    if (endpointLimit.HasValue && (workingByEndpoint.TryGetValue(EndpointCapacityKey(endpoint), out int busyCount) ? busyCount : 0) >= endpointLimit.Value)
                    {
                        _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": endpoint " + endpoint.BaseUrl + " is at its limit of " + endpointLimit.Value + " concurrent mission(s)");
                        continue;
                    }
                }

                admitted.Add(candidate);
            }

            return admitted;
        }

        private static string EndpointCapacityKey(ModelEndpoint endpoint)
        {
            string url = (endpoint.BaseUrl ?? String.Empty).Trim().TrimEnd('/');
            return String.IsNullOrEmpty(url) ? "endpoint:" + endpoint.Id : url.ToLowerInvariant();
        }

        private async Task<string?> ReadDockHeadAsync(Dock dock, CancellationToken token)
        {
            if (String.IsNullOrEmpty(dock.WorktreePath)) return null;
            DockHost? harborHost = HarborHostFor(dock);
            IGitService? git = harborHost != null ? harborHost.Git : _Git;
            if (git == null) return null;
            try
            {
                return await git.GetHeadCommitHashAsync(dock.WorktreePath!, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not read HEAD of dock " + dock.Id + ": " + ex.Message);
                return null;
            }
        }

        private async Task<string?> CheckStageCommitContinuityAsync(Mission mission, Dock dock, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.DependsOnMissionId) || String.IsNullOrEmpty(dock.WorktreePath)) return null;
            Mission? upstream = await _Database.Missions.ReadAsync(mission.DependsOnMissionId!, token).ConfigureAwait(false);
            if (upstream == null || String.IsNullOrEmpty(upstream.CommitHash)) return null;
            if (!String.Equals(upstream.BranchName, mission.BranchName, StringComparison.Ordinal)) return null;

            DockHost? harborHost = HarborHostFor(dock);
            IGitService? git = harborHost != null ? harborHost.Git : _Git;
            if (git == null) return null;

            string? head = await ReadDockHeadAsync(dock, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(head)) return null;
            if (String.Equals(head, upstream.CommitHash, StringComparison.OrdinalIgnoreCase)) return null;

            bool? contained;
            try
            {
                contained = await git.IsAncestorAsync(dock.WorktreePath!, upstream.CommitHash!, head!, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not verify commit continuity for mission " + mission.Id + ": " + ex.Message);
                return null;
            }

            if (contained == true) return null;

            string where = harborHost != null ? "Harbor " + harborHost.HarborId : "the Admiral host";
            return "Commit continuity check failed: branch " + mission.BranchName + " on " + where + " starts at " + head
                + ", which does not contain commit " + upstream.CommitHash + " produced by the previous stage " + upstream.Id + ". "
                + "The previous stage ran on a different host and its commits are not in this repository, so this stage would have started without them. "
                + "Remediation: run the stage where the previous stage ran (connect that Harbor, or turn off Require Harbor for launch if it ran on the Admiral), "
                + "or push branch " + mission.BranchName + " to the vessel's remote, then restart this mission.";
        }

        private const string _OutputRepairStart = "<!-- armada:output-repair -->";
        private const string _OutputRepairEnd = "<!-- /armada:output-repair -->";

        private async Task<bool> TryScheduleOutputRepairAsync(Mission mission, string problem, string contract, CancellationToken token)
        {
            // A person decides review-gated stages; never re-run behind their back.
            if (mission.RequiresReview) return false;

            int max = _Settings.MaxOutputRepairAttempts;
            if (mission.OutputRepairAttempts >= max) return false;

            MissionStatusEnum observedStatus = mission.Status;
            int attempt = mission.OutputRepairAttempts + 1;
            string note = _OutputRepairStart + "\n## Output contract repair (attempt " + attempt + " of " + max + ")\n"
                + "Your previous response for this mission could not be used: " + problem.Trim() + "\n"
                + "Do the same job again from the current branch and respond in exactly this format. Nothing else about the task has changed.\n"
                + "Required format: " + contract.Trim() + "\n"
                + _OutputRepairEnd;

            mission.OutputRepairAttempts = attempt;
            mission.Description = ReplaceOutputRepairNote(mission.Description, note);
            mission.Status = MissionStatusEnum.Pending;
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.AgentOutput = null;
            mission.DiffSnapshot = null;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.TotalRuntimeMs = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            if (!await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { observedStatus }, token).ConfigureAwait(false))
            {
                _Logging.Info(_Header + "mission " + mission.Id + " left " + observedStatus + " before its output repair was recorded; not repairing");
                mission.OutputRepairAttempts = attempt - 1;
                return false;
            }

            _Logging.Warn(_Header + "mission " + mission.Id + " (" + (mission.Persona ?? "stage") + ") broke its output contract (" + problem.Trim()
                + "); running it again with the contract errors quoted (repair " + attempt + "/" + max + ")");
            return true;
        }

        private async Task FinishOutputRepairScheduledAsync(Mission mission, Captain captain, Dock? dock, CancellationToken token)
        {
            if (dock != null)
            {
                try { await _Docks.ReclaimAsync(dock.Id, token: CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "error reclaiming dock " + dock.Id + " before output repair of mission " + mission.Id + ": " + ex.Message); }
            }

            await ReleaseCaptainAfterCompletionAsync(captain, mission.Id).ConfigureAwait(false);
            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
        }

        private string AppendRepairHistory(string reason, Mission mission)
        {
            if (mission.OutputRepairAttempts <= 0)
                return _Settings.MaxOutputRepairAttempts <= 0 ? reason : reason + " (output repair is unavailable for review-gated stages or was not possible)";
            return reason + " (after " + mission.OutputRepairAttempts + " output-contract repair attempt(s); the stage kept breaking its output format, so check the captain's model and persona template)";
        }

        private static string ReplaceOutputRepairNote(string? description, string note)
        {
            string current = description ?? String.Empty;
            int start = current.IndexOf(_OutputRepairStart, StringComparison.Ordinal);
            if (start >= 0)
            {
                int end = current.IndexOf(_OutputRepairEnd, start, StringComparison.Ordinal);
                current = end >= 0
                    ? current.Substring(0, start) + current.Substring(end + _OutputRepairEnd.Length)
                    : current.Substring(0, start);
            }

            return current.TrimEnd() + "\n\n" + note;
        }

        private async Task ReleaseCaptainAfterCompletionAsync(Captain captain, string missionId)
        {
            // Compare-and-set: release only while the captain still holds this mission. Orphan recovery can finalize an
            // older mission using a captain record that has already moved on, and that newer assignment must survive.
            try
            {
                bool released = await _Database.Captains.TryReleaseFromMissionAsync(captain.Id, missionId, CancellationToken.None).ConfigureAwait(false);
                if (released)
                    _Logging.Info(_Header + "released captain " + captain.Id + " after mission " + missionId);
                else
                    _Logging.Debug(_Header + "skipping captain release for mission " + missionId + " because captain " + captain.Id + " no longer holds it");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not release captain " + captain.Id + " after mission " + missionId + ": " + ex.Message);
            }
        }

        private async Task RollbackAssignmentAsync(Mission mission, Captain captain, Dock? dock, bool preserveExistingBranch)
        {
            // Cleanup runs with no cancellation: a shutdown mid-assignment must not strand a claimed captain.
            try
            {
                mission.Status = MissionStatusEnum.Pending;
                mission.CaptainId = null;
                if (!preserveExistingBranch)
                    mission.BranchName = null;
                mission.DockId = null;
                mission.ProcessId = null;
                mission.StartedUtc = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                bool reverted = await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { MissionStatusEnum.Assigned }, CancellationToken.None).ConfigureAwait(false);
                if (!reverted)
                    _Logging.Info(_Header + "mission " + mission.Id + " already left Assigned; leaving its current state during rollback");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not revert mission " + mission.Id + " to Pending during rollback: " + ex.Message);
            }

            await ReleaseClaimedCaptainAsync(mission, captain).ConfigureAwait(false);
            await ReclaimAssignmentDockAsync(mission, dock).ConfigureAwait(false);
        }

        private async Task FailAssignmentAsync(Mission mission, Captain captain, Dock dock, string reason)
        {
            try
            {
                mission.Status = MissionStatusEnum.Failed;
                mission.FailureKind = MissionFailureKindEnum.Infra;
                mission.FailureReason = reason;
                mission.CaptainId = null;
                mission.DockId = null;
                mission.ProcessId = null;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                bool failed = await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { MissionStatusEnum.Assigned }, CancellationToken.None).ConfigureAwait(false);
                if (failed)
                    ArmadaMetrics.MissionsFailed.Add(1);
                else
                    _Logging.Info(_Header + "mission " + mission.Id + " already left Assigned; not marking it Failed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not mark mission " + mission.Id + " failed: " + ex.Message);
            }

            await ReleaseClaimedCaptainAsync(mission, captain).ConfigureAwait(false);
            await ReclaimAssignmentDockAsync(mission, dock).ConfigureAwait(false);

            try
            {
                Signal failedSignal = new Signal(SignalTypeEnum.Error, "Mission " + mission.Id + " failed: " + reason);
                failedSignal.TenantId = mission.TenantId;
                failedSignal.UserId = mission.UserId;
                failedSignal.FromCaptainId = captain.Id;
                await _Database.Signals.CreateAsync(failedSignal, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not record failure signal for mission " + mission.Id + ": " + ex.Message);
            }

            try
            {
                await CancelDependentPipelineStagesAsync(mission, CancellationToken.None).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not settle dependents or voyage after mission " + mission.Id + " failed: " + ex.Message);
            }
        }

        private async Task ReleaseClaimedCaptainAsync(Mission mission, Captain captain)
        {
            try
            {
                bool released = await _Database.Captains.TryReleaseFromMissionAsync(captain.Id, mission.Id, CancellationToken.None).ConfigureAwait(false);
                if (released)
                {
                    captain.State = CaptainStateEnum.Idle;
                    captain.CurrentMissionId = null;
                    captain.CurrentDockId = null;
                    captain.ProcessId = null;
                    captain.RecoveryAttempts = 0;
                    _Logging.Info(_Header + "released captain " + captain.Id + " from mission " + mission.Id);
                }
                else
                {
                    _Logging.Info(_Header + "captain " + captain.Id + " no longer holds mission " + mission.Id + "; leaving it as is");
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not release captain " + captain.Id + " from mission " + mission.Id + ": " + ex.Message);
            }
        }

        private async Task ReclaimAssignmentDockAsync(Mission mission, Dock? dock)
        {
            if (dock == null) return;
            try
            {
                await _Docks.ReclaimAsync(dock.Id, token: CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to reclaim dock " + dock.Id + " for mission " + mission.Id + ": " + ex.Message);
            }
        }

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

            // A Harbor-hosted dock's branch lives in the repository on that Harbor's host.
            DockHost? harborHost = HarborHostFor(dock);
            IGitService git = harborHost != null ? harborHost.Git : _Git;
            string? repositoryPath = harborHost != null ? dock!.RepositoryPath : vessel?.LocalPath;
            string? workingDirectory = harborHost != null ? (dock!.CheckoutPath ?? dock.RepositoryPath) : vessel?.WorkingDirectory;

            if (vessel == null || String.IsNullOrEmpty(repositoryPath))
            {
                _Logging.Warn(_Header + "unable to clean architect branch " + branchName +
                    " for mission " + mission.Id + " because vessel metadata is incomplete");
                return;
            }

            BranchCleanupPolicyEnum cleanupPolicy = vessel.BranchCleanupPolicy ?? _Settings.BranchCleanupPolicy;
            if (cleanupPolicy == BranchCleanupPolicyEnum.None)
            {
                _Logging.Debug(_Header + "branch cleanup policy is None - retaining architect branch " + branchName + " after handoff");
                return;
            }

            try
            {
                await git.DeleteLocalBranchAsync(repositoryPath, branchName, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "deleted architect branch " + branchName + " from bare repo after successful handoff");
            }
            catch (Exception branchEx)
            {
                _Logging.Warn(_Header + "failed to delete architect branch " + branchName + " from bare repo: " + branchEx.Message);
            }

            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
            {
                if (String.IsNullOrEmpty(workingDirectory))
                {
                    _Logging.Warn(_Header + "cannot delete remote architect branch " + branchName +
                        " because vessel working directory is not configured");
                    return;
                }

                try
                {
                    await git.DeleteRemoteBranchAsync(workingDirectory, branchName, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "deleted remote architect branch " + branchName + " after successful handoff");
                }
                catch (Exception remoteBranchEx)
                {
                    _Logging.Warn(_Header + "failed to delete remote architect branch " + branchName + ": " + remoteBranchEx.Message);
                }
            }
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
                DockHost? harborHost = HarborHostFor(dock);
                IGitService git = harborHost != null ? harborHost.Git : _Git;
                changedFiles = await git.GetChangedFilesSinceAsync(dock.WorktreePath, startCommit, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "scope validation failed for mission " + mission.Id +
                    " while reading changed files: " + ex.Message);
                return false;
            }

            List<string> outOfScopeFiles = FindOutOfScopeFiles(changedFiles, allowedFiles);
            if (outOfScopeFiles.Count < 1)
            {
                return false;
            }

            mission.Status = MissionStatusEnum.Failed;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            mission.FailureReason = "Mission modified files outside its scoped file list: " + String.Join(", ", outOfScopeFiles);
            mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "mission " + mission.Id + " failed scope validation: " + mission.FailureReason);
            return true;
        }

        internal static HashSet<string> ParseMissionScopedFiles(string description)
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
                    string normalizedPath = NormalizeMissionPath(pathMatch.Groups["path"].Value).TrimEnd('.');
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
                _Logging.Warn(_Header + "could not read dock start commit metadata for " + dockId + ": " + ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Return the changed files that fall outside a mission's scoped allow list. Empty paths (a transport artifact)
        /// and the runtime instruction files are never reported. An allow-list entry with a directory must match the
        /// repo-relative path exactly (case-insensitive); a bare file name matches a changed file with that file name
        /// in any directory.
        /// </summary>
        /// <param name="changedFiles">Repo-relative paths changed since the dock's start commit.</param>
        /// <param name="allowedFiles">The scoped allow list parsed from the mission description.</param>
        /// <returns>The normalized out-of-scope paths, without empty entries or duplicates.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
        internal static List<string> FindOutOfScopeFiles(IEnumerable<string> changedFiles, ICollection<string> allowedFiles)
        {
            if (changedFiles == null) throw new ArgumentNullException(nameof(changedFiles));
            if (allowedFiles == null) throw new ArgumentNullException(nameof(allowedFiles));

            HashSet<string> exactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> bareNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string allowed in allowedFiles)
            {
                string normalizedAllowed = NormalizeMissionPath(allowed);
                if (String.IsNullOrEmpty(normalizedAllowed)) continue;
                if (normalizedAllowed.IndexOf('/') < 0) bareNames.Add(normalizedAllowed);
                else exactPaths.Add(normalizedAllowed);
            }

            List<string> outOfScope = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string changedFile in changedFiles)
            {
                string normalizedPath = NormalizeMissionPath(changedFile);
                if (String.IsNullOrEmpty(normalizedPath)) continue;
                if (_IgnoredMissionArtifactFiles.Contains(normalizedPath)) continue;
                if (exactPaths.Contains(normalizedPath)) continue;

                int slash = normalizedPath.LastIndexOf('/');
                string fileName = slash >= 0 ? normalizedPath.Substring(slash + 1) : normalizedPath;
                if (fileName.Length > 0 && bareNames.Contains(fileName)) continue;

                if (seen.Add(normalizedPath)) outOfScope.Add(normalizedPath);
            }

            return outOfScope;
        }

        private static string NormalizeMissionPath(string path)
        {
            string normalized = (path ?? String.Empty).Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);
            return normalized;
        }

        private async Task EnsureMissionInstructionsPresentAsync(
            DockHost? harborHost,
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
            IDockFileSystem files = harborHost != null ? harborHost.Files : _LocalFiles;
            if (await files.ReadTextAsync(instructionsPath, token).ConfigureAwait(false) != null) return;

            string snapshotPath = Path.Combine(_Settings.LogDirectory, "instructions", mission.Id + "." + instructionsFileName);
            if (!File.Exists(snapshotPath))
            {
                _Logging.Warn(_Header + "mission instructions missing at " + instructionsPath +
                    " and no snapshot exists at " + snapshotPath);
                return;
            }

            await files.WriteTextAsync(instructionsPath, await File.ReadAllTextAsync(snapshotPath, token).ConfigureAwait(false), token).ConfigureAwait(false);
            _Logging.Warn(_Header + "restored missing mission instructions from snapshot to " + instructionsPath);
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
                if (JudgeVerdictParser.ParseLine(line).HasValue) continue;

                lines.Add(line);
            }

            return String.Join(" ", lines);
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
                _Logging.Warn(_Header + "could not project architect mission definitions into log for " + architectMission.Id + ": " + ex.ToString());
            }
        }

        private async Task<Mission> RequireReviewMissionAsync(string missionId, CancellationToken token)
        {
            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null) throw new KeyNotFoundException("Mission not found: " + missionId);
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

        private async Task ReclaimMissionDockAsync(string dockId, CancellationToken token, string? forceReason = null,
            [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            if (String.IsNullOrEmpty(dockId)) return;

            try
            {
                await _Docks.ReclaimAsync(dockId, token: token, forceReason: forceReason, caller: caller).ConfigureAwait(false);
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

        /// <summary>
        /// Determine whether a dependent pipeline mission has had upstream handoff context applied.
        /// The handoff path stamps the downstream mission with the dependency's branch name,
        /// so branch equality is used as the minimum readiness signal before launch.
        /// </summary>
        private static bool IsPipelineHandoffPrepared(Mission mission, Mission dependency)
        {
            if (mission == null || dependency == null) return false;
            if (String.IsNullOrEmpty(mission.DependsOnMissionId)) return true;

            if (PersonaCatalog.Matches(dependency.Persona, PersonaCatalog.Architect))
            {
                return !String.IsNullOrEmpty(mission.Description) &&
                    mission.Description.Contains(ArchitectHandoffMarker, StringComparison.Ordinal);
            }

            // If the dependency never had a branch, there is no stronger handoff signal
            // available here; fall back to allowing assignment.
            if (String.IsNullOrEmpty(dependency.BranchName)) return true;

            return String.Equals(mission.BranchName, dependency.BranchName, StringComparison.Ordinal);
        }

        /// <summary>
        /// The Harbor host of a Harbor-hosted dock, or null for a dock on this machine (which keeps using the local git
        /// service and disk exactly as before).
        /// </summary>
        private MissionLaunchFailure RecordLaunchFailure(Mission mission, Captain captain, Exception ex)
        {
            MissionLaunchFailure failure = _LaunchFailures.AddOrUpdate(mission.Id,
                _ => new MissionLaunchFailure { Count = 1 },
                (_, existing) => { existing.Count++; return existing; });
            lock (failure.FailedCaptainIds) failure.FailedCaptainIds.Add(captain.Id);
            HarborJobFailedException? harborFailure = FindInnerException<HarborJobFailedException>(ex);
            AgentRuntimeNotInstalledException? notInstalled = FindInnerException<AgentRuntimeNotInstalledException>(ex);
            string reason = notInstalled != null ? notInstalled.Message : harborFailure != null ? harborFailure.HarborMessage : ex.Message;
            failure.Reason = reason.Trim().TrimEnd('.') + ".";
            failure.LastUtc = DateTime.UtcNow;
            // 30s, 60s, 2m, 4m, then every 5 minutes.
            double seconds = Math.Min(300, 30 * Math.Pow(2, Math.Max(0, failure.Count - 1)));
            failure.NextAttemptUtc = DateTime.UtcNow.AddSeconds(seconds);
            _Logging.Warn(_Header + "mission " + mission.Id + ": launch " + failure.Count + " of captain " + captain.Id + " failed (" + failure.Reason + "); next attempt in " + seconds.ToString("0") + "s");
            return failure;
        }

        private static bool IsRuntimeMissing(Exception ex)
        {
            if (FindInnerException<AgentRuntimeNotInstalledException>(ex) != null) return true;
            HarborJobFailedException? harborFailure = FindInnerException<HarborJobFailedException>(ex);
            return harborFailure != null && harborFailure.HarborMessage.Contains("not installed", StringComparison.OrdinalIgnoreCase);
        }

        private static T? FindInnerException<T>(Exception? ex) where T : Exception
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
                if (current is T match) return match;
            return null;
        }

        private const int _MaxJudgeReworkRounds = 2;

        private static string StripPipelineHandoff(string? description)
        {
            // A stage's description is the task plus what earlier stages handed it; keep the task.
            string text = description ?? String.Empty;
            int cut = text.IndexOf("\n\n---\n## Prior Stage Output", StringComparison.Ordinal);
            return cut > 0 ? text.Substring(0, cut).TrimEnd() : text;
        }

        /// <summary>
        /// The persona a mission is routed as: its own, or Worker for a mission dispatched without one (a plain dispatch
        /// with no pipeline, create_mission, a fleet action), so persona captain priorities and voyage captain
        /// assignments apply to it like any Worker stage.
        /// </summary>
        private static string RoutingPersona(Mission mission)
        {
            return String.IsNullOrWhiteSpace(mission.Persona) ? PersonaCatalog.Worker : mission.Persona!.Trim();
        }

        #endregion
    }
}
