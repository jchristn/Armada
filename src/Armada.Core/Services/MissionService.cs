namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Service for mission lifecycle management. A facade over the mission services (admission, launch, completion,
    /// handoff, review, repair, and the rest); it owns the shared state and builds them.
    /// </summary>
    public class MissionService : IMissionService
    {
        #region Public-Members

        /// <summary>
        /// Runs the format-only repair call through a captain's runtime in a scratch directory. Null turns the format turn
        /// off, so a contract violation goes straight to the full rerun.
        /// </summary>
        public IModelProbeExecutor? FormatRepairExecutor { get; set; } = null;

        /// <summary>Stable Admiral credential-host identity. Required for local usage enforcement; the server supplies its protected usage identity.</summary>
        public string? UsageSelectionLocalHostId { get; set; }

        /// <summary>Database reads the current dispatch pass has made, or -1 outside a pass.</summary>
        internal int DispatchPassReads => _Pass.Value?.Reads ?? -1;

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
        /// Invoked after a captain-selection decision commits (admission, repair or a new material waiting state).
        /// Best effort: exceptions are logged and never affect admission.
        /// </summary>
        public Action<CaptainSelectionChange>? OnCaptainSelectionChanged { get; set; }

        /// <summary>
        /// Chooses where a mission's dock is created before it is provisioned: on a Harbor that can serve the vessel, on
        /// the Admiral's host, or nowhere yet (the mission stays Pending). When null every dock is created on the
        /// Admiral's host.
        /// </summary>
        public Func<Mission, Captain, Vessel, Task<DockPlacement>>? ResolveDockPlacementAsync { get; set; }

        /// <summary>Optional placement preserving the verified usage credential host; existing branch and permission rules remain authoritative.</summary>
        public Func<Mission, Captain, Vessel, string, Task<DockPlacement>>? ResolveUsageDockPlacementAsync { get; set; }

        /// <summary>
        /// Where docks live. Operations on a Harbor-hosted dock (instruction files, git, the Definition-of-Done gate) run
        /// on its Harbor through this; without it every dock is treated as local.
        /// </summary>
        public DockHostResolver? DockHosts { get; set; }

        /// <summary>
        /// Mission status transitions (RS-3.5). The server shares one instance across services; a standalone service gets
        /// its own, which re-evaluates voyages through this service.
        /// </summary>
        public MissionTransitions Transitions { get; set; }

        /// <summary>
        /// Black box capture called at mission failure sites, or null when the black box is not wired (no capture).
        /// </summary>
        public IBlackBoxCapture? BlackBox { get; set; }

        /// <summary>
        /// Wakes the Admiral dispatcher (RS-4.5). Raised when a mission completes, is reviewed, or frees its captain. It must
        /// return at once: completion never waits for dispatch. Null when no dispatcher is wired (the heartbeat still runs).
        /// </summary>
        public Action? DispatchRequested { get; set; }

        /// <summary>
        /// Cancelled when the Admiral shuts down. Background dock provisioning and launches run under it instead of
        /// CancellationToken.None, so shutdown does not leave a launch running against a disposed database.
        /// </summary>
        public CancellationToken ShutdownToken { get; set; } = CancellationToken.None;

        /// <summary>Called after a <c>mission.starving</c> event is recorded, for live delivery.</summary>
        public Action<ArmadaEvent>? OnConcurrencyEvent
        {
            get => _WaitTracker.EventRecorded;
            set => _WaitTracker.EventRecorded = value;
        }

        /// <summary>Called when a Pending mission starts waiting on a different gate, for live delivery.</summary>
        public Action<MissionWaitGateChange>? OnWaitingGateChanged
        {
            get => _WaitTracker.GateChanged;
            set => _WaitTracker.GateChanged = value;
        }

        /// <summary>
        /// How long a stage check waits for a dock's disconnected Harbor to come back before giving up.
        /// </summary>
        public TimeSpan HarborReconnectWait { get; set; } = TimeSpan.FromMinutes(3);

        #endregion

        #region Private-Members

        private readonly MissionServiceContext _Context = new MissionServiceContext();

        /// <summary>
        /// The mission services and the state they share. Internal so tests can reach a service or replace the local
        /// command executor.
        /// </summary>
        internal MissionServiceContext Context => _Context;

        private readonly UsageSelectionBlockerStore _UsageSelection = new UsageSelectionBlockerStore();

        /// <summary>Event type recorded when a mission enters a materially new captain-selection waiting state.</summary>
        public const string SelectionWaitingEventType = "mission.selection_waiting";

        internal readonly AsyncLocal<DispatchPassCache?> _Pass = new AsyncLocal<DispatchPassCache?>();

        private readonly EndpointCircuitBreakers _EndpointBreakers;

        private readonly CompletionRunTracker _CompletionRuns = new CompletionRunTracker();

        private readonly MissionLaunchBackoff _LaunchBackoff;

        private string _Header = "[MissionService] ";

        private LoggingModule _Logging;

        private DatabaseDriver _Database;

        private ArmadaSettings _Settings;

        private IGitService? _Git;

        private IDockService _Docks;

        private ICaptainService _Captains;

        private IPromptTemplateService? _PromptTemplates;

        private IDefinitionOfDoneGate? _DefinitionOfDone;

        private readonly MissionAdmissionResultStore _AdmissionResults = new MissionAdmissionResultStore();

        // Lock order is vessel, then capacity. Release both before provisioning/launching:
        // completion callbacks may dispatch more work synchronously.
        private readonly VesselAdmissionLockRegistry _VesselAdmissionLocks = new VesselAdmissionLockRegistry();

        private readonly SemaphoreSlim _CapacityLock = new SemaphoreSlim(1, 1);

        private readonly MissionWaitTracker _WaitTracker;

        private readonly BackgroundLaunchSet _BackgroundLaunches = new BackgroundLaunchSet();

        /// <summary>
        /// Missions whose leftover work Armada committed during this attempt, read once by the completion handler.
        /// </summary>
        private readonly MissionFlagSet _LeftoverCommitted = new MissionFlagSet();

        /// <summary>
        /// Note placed in the description when the persona template carries the prior stage's evidence.
        /// </summary>
        internal const string HandoffInRoleNote = "The prior stage's handoff artifact is rendered in the role instructions above (prior stage evidence and diff).";

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
            _WaitTracker = new MissionWaitTracker(_Database, _Settings, _Logging);
            Transitions = new MissionTransitions(_Database, _Logging);
            Transitions.VoyageChanged = (voyageId, token) => _Context.MissionHandoffService.ReevaluateVoyageAsync(voyageId, token);
            RecoveryStateStore recoveryState = new RecoveryStateStore(_Database, _Logging);
            _EndpointBreakers = new EndpointCircuitBreakers(recoveryState, _Logging, _Header, delay => _Context.MissionWaitCoordinator.ScheduleDispatchAfter(delay));
            _LaunchBackoff = new MissionLaunchBackoff(recoveryState);
            BuildServices();
        }

        private void BuildServices()
        {
            _Context.AdmissionResults = _AdmissionResults;
            _Context.BackgroundLaunches = _BackgroundLaunches;
            _Context.CapacityLock = _CapacityLock;
            _Context.Captains = _Captains;
            _Context.CompletionRuns = _CompletionRuns;
            _Context.Database = _Database;
            _Context.DefinitionOfDone = _DefinitionOfDone;
            _Context.Docks = _Docks;
            _Context.EndpointBreakers = _EndpointBreakers;
            _Context.Git = _Git;
            _Context.Header = _Header;
            _Context.LaunchBackoff = _LaunchBackoff;
            _Context.LeftoverCommitted = _LeftoverCommitted;
            _Context.LocalCommands = new LocalHostCommandExecutor();
            _Context.LocalFiles = new LocalDockFileSystem();
            _Context.Logging = _Logging;
            _Context.Pass = _Pass;
            _Context.PromptTemplates = _PromptTemplates;
            _Context.Settings = _Settings;
            _Context.UsageSelection = _UsageSelection;
            _Context.VesselAdmissionLocks = _VesselAdmissionLocks;
            _Context.WaitTracker = _WaitTracker;
            _Context.ArchitectFanOutService = new ArchitectFanOutService(this, _Context);
            _Context.BranchDurabilityService = new BranchDurabilityService(this, _Context);
            _Context.IntegrationBarrierService = new IntegrationBarrierService(this, _Context);
            _Context.MissionAdmissionPipeline = new MissionAdmissionPipeline(this, _Context);
            _Context.MissionCaptainRouter = new MissionCaptainRouter(this, _Context);
            _Context.MissionDispatchPass = new MissionDispatchPass(this, _Context);
            _Context.MissionEscalationService = new MissionEscalationService(this, _Context);
            _Context.MissionFailureCapture = new MissionFailureCapture(this, _Context);
            _Context.MissionHandoffService = new MissionHandoffService(this, _Context);
            _Context.MissionHierarchyService = new MissionHierarchyService(this, _Context);
            _Context.MissionInstructionWriter = new MissionInstructionWriter(this, _Context);
            _Context.MissionLauncher = new MissionLauncher(this, _Context);
            _Context.MissionOutcomeTelemetry = new MissionOutcomeTelemetry(this, _Context);
            _Context.MissionOwnershipService = new MissionOwnershipService(this, _Context);
            _Context.MissionReviewService = new MissionReviewService(this, _Context);
            _Context.MissionReworkService = new MissionReworkService(this, _Context);
            _Context.MissionScopeGuard = new MissionScopeGuard(this, _Context);
            _Context.MissionUsageSelector = new MissionUsageSelector(this, _Context);
            _Context.MissionWaitCoordinator = new MissionWaitCoordinator(this, _Context);
            _Context.OutputRepairService = new OutputRepairService(this, _Context);
            _Context.StageCheckRunner = new StageCheckRunner(this, _Context);
            _Context.StageCompletionHandler = new StageCompletionHandler(this, _Context);
            _Context.WorkerEnvelopeService = new WorkerEnvelopeService(this, _Context);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Paths owned by active logical missions in the voyage that can run alongside <paramref name="mission"/>, as
        /// prompt lines; an overlap with the mission's own paths is called out.
        /// </summary>
        internal static string ConcurrentSiblingPaths(Mission mission, List<Mission> voyage)
            => MissionOwnershipService.ConcurrentSiblingPaths(mission, voyage);

        /// <summary>One line on the parent's finished result, or empty when the mission has no parent.</summary>
        internal static string ParentSummary(Mission mission, List<Mission> voyage)
            => MissionOwnershipService.ParentSummary(mission, voyage);

        /// <inheritdoc />
        public void ReconsiderCaptainSelection(Mission mission)
            => _Context.MissionUsageSelector.ReconsiderCaptainSelection(mission);

        /// <summary>Read-only current-state selection preview using shared readiness, eligibility and routing rules.</summary>
        /// <param name="mission">An already-authorized mission; its tenant/user scope bounds candidate evidence.</param>
        /// <param name="pageNumber">One-based comparison page.</param>
        /// <param name="pageSize">Comparison rows, 1..1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Authorized candidate explanations without account keys or writes.</returns>
        public Task<CaptainSelectionPreview> PreviewCaptainSelectionAsync(Mission mission, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => _Context.MissionUsageSelector.PreviewCaptainSelectionAsync(mission, pageNumber, pageSize, token);

        /// <inheritdoc />
        public Task<AutoLandDecision?> EvaluateAutoLandAsync(string missionId, CancellationToken token = default)
            => _Context.StageCompletionHandler.EvaluateAutoLandAsync(missionId, token);

        /// <inheritdoc />
        public IDisposable BeginDispatchPass()
            => _Context.MissionDispatchPass.BeginDispatchPass();

        /// <inheritdoc />
        public Task<DispatchCapacity> ReadDispatchCapacityAsync(CancellationToken token = default)
            => _Context.MissionDispatchPass.ReadDispatchCapacityAsync(token);

        /// <inheritdoc />
        public Task<bool> TryAssignAsync(Mission mission, Vessel vessel, CancellationToken token = default)
            => _Context.MissionLauncher.TryAssignAsync(mission, vessel, token);

        /// <inheritdoc />
        public Task<bool> TryAssignAsync(Mission mission, Vessel vessel, bool launchInBackground, CancellationToken token = default)
            => _Context.MissionLauncher.TryAssignAsync(mission, vessel, launchInBackground, token);

        /// <inheritdoc />
        public Task HandleClaimedCompletionAsync(Captain captain, string missionId, DateTime expectedVersion, CancellationToken token = default)
            => _Context.StageCompletionHandler.HandleClaimedCompletionAsync(captain, missionId, expectedVersion, token);

        /// <inheritdoc />
        public Task<bool> HasStrictCaptainAssignmentAsync(Mission mission, CancellationToken token = default)
            => _Context.MissionLauncher.HasStrictCaptainAssignmentAsync(mission, token);

        /// <inheritdoc />
        public Task<bool> IsEndpointBreakerOpenAsync(Captain captain, CancellationToken token = default)
            => _Context.MissionLauncher.IsEndpointBreakerOpenAsync(captain, token);

        /// <inheritdoc />
        public Task RecordEndpointOutcomeAsync(Captain captain, bool healthy, CancellationToken token = default)
            => _Context.MissionLauncher.RecordEndpointOutcomeAsync(captain, healthy, token);

        /// <summary>
        /// Load the endpoint breakers and launch backoff a previous run left (RELIABILITY_SIMPLICITY_PLAN.md RS-3.7). Call
        /// once at startup, before dispatch.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task LoadRecoveryStateAsync(CancellationToken token = default)
            => _Context.MissionLauncher.LoadRecoveryStateAsync(token);

        /// <inheritdoc />
        public bool IsBroadScope(Mission mission)
            => _Context.MissionScopeGuard.IsBroadScope(mission);

        internal static HashSet<string> ParseMissionScopedFiles(string description)
            => MissionScopeGuard.ParseMissionScopedFiles(description);

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
            => MissionScopeGuard.FindOutOfScopeFiles(changedFiles, allowedFiles);

        /// <inheritdoc />
        public Task<EscalationChoice?> ChooseEscalationAsync(Mission mission, string? failedCaptainId, CancellationToken token = default)
            => _Context.MissionEscalationService.ChooseEscalationAsync(mission, failedCaptainId, token);

        /// <inheritdoc />
        public Task<bool> ApplyEscalationAsync(Mission mission, Captain failedCaptain, EscalationChoice choice, EscalationMemory memory, CancellationToken token = default)
            => _Context.MissionEscalationService.ApplyEscalationAsync(mission, failedCaptain, choice, memory, token);

        internal Task<List<string>> RestoreDescendantsCancelledForParentAsync(Mission parent, CancellationToken token)
            => _Context.MissionHierarchyService.RestoreDescendantsCancelledForParentAsync(parent, token);

        /// <inheritdoc />
        public Task<int> CloseInterruptedWaitsAsync(CancellationToken token = default)
            => _Context.MissionWaitCoordinator.CloseInterruptedWaitsAsync(token);

        /// <inheritdoc />
        public Task ReconcileWaitsAsync(IEnumerable<string> pendingMissionIds, DateTime snapshotUtc, CancellationToken token = default)
            => _Context.MissionWaitCoordinator.ReconcileWaitsAsync(pendingMissionIds, snapshotUtc, token);

        /// <summary>
        /// Wait for background dock provisioning and launches started by <see cref="TryAssignAsync(Mission, Vessel, bool, CancellationToken)"/>
        /// to finish, including their failure handling.
        /// </summary>
        /// <param name="timeout">Longest time to wait.</param>
        /// <returns>True when none remain in flight.</returns>
        public Task<bool> WaitForBackgroundLaunchesAsync(TimeSpan timeout)
            => _Context.MissionWaitCoordinator.WaitForBackgroundLaunchesAsync(timeout);

        /// <inheritdoc />
        public MissionAdmissionResult? GetAdmissionResult(string missionId)
            => _Context.MissionWaitCoordinator.GetAdmissionResult(missionId);

        /// <inheritdoc />
        public Task<Mission> ApproveReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, bool conditional = false, CancellationToken token = default)
            => _Context.MissionReviewService.ApproveReviewAsync(missionId, reviewedByUserId, comment, conditional, token);

        /// <inheritdoc />
        public Task<Mission> RestartAsync(Mission mission, string? title = null, string? description = null, CancellationToken token = default)
            => _Context.MissionReviewService.RestartAsync(mission, title, description, token);

        /// <inheritdoc />
        public Task<Mission> DenyReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, ReviewDenyActionEnum? actionOverride = null, CancellationToken token = default)
            => _Context.MissionReviewService.DenyReviewAsync(missionId, reviewedByUserId, comment, actionOverride, token);

        /// <inheritdoc />
        public Task RegenerateInstructionsAsync(Mission mission, Captain captain, Dock dock, CancellationToken token = default)
            => _Context.MissionInstructionWriter.RegenerateInstructionsAsync(mission, captain, dock, token);

        /// <inheritdoc />
        public Task GenerateClaudeMdAsync(string worktreePath, Mission mission, Vessel vessel, Captain? captain = null, CancellationToken token = default)
            => _Context.MissionInstructionWriter.GenerateClaudeMdAsync(worktreePath, mission, vessel, captain, token);

        /// <summary>
        /// Deliver the prior stage's handoff once (RS-6.11). The persona template's declared slots decide where it goes:
        /// a template that declares <c>{PreviousStageOutput}</c> (or <c>{Diff}</c>) receives that part, and the
        /// description's prior-stage placeholder receives the rest, or a note that the evidence is in the role
        /// instructions. A rework description gets its findings and the rest of the handoff. When neither the template nor
        /// the description has a slot for the evidence (an operator-edited template and an older description), the
        /// handoff is returned for the composer to append after the mission section; otherwise null.
        /// </summary>
        internal static string? DeliverHandoff(Dictionary<string, string> templateParams, HandoffRendering? handoff, string? personaTemplateContent)
            => MissionInstructionWriter.DeliverHandoff(templateParams, handoff, personaTemplateContent);

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
            => MissionInstructionWriter.BuildPapercutsSection();

        /// <inheritdoc />
        public Task<string?> VerifyStageWorkAsync(Mission mission, Captain captain, CancellationToken token = default)
            => _Context.StageCheckRunner.VerifyStageWorkAsync(mission, captain, token);

        /// <inheritdoc />
        public Task<StageCheckResult> CheckStageWorkAsync(Mission mission, Captain captain, CancellationToken token = default)
            => _Context.StageCheckRunner.CheckStageWorkAsync(mission, captain, token);

        /// <inheritdoc />
        public Task CommitDockWorkAsync(Mission mission, Captain captain, Dock dock, CancellationToken token = default)
            => _Context.StageCheckRunner.CommitDockWorkAsync(mission, captain, dock, token);

        /// <inheritdoc />
        public Task<int> SettleStagesBehindFailedDependenciesAsync(CancellationToken token = default)
            => _Context.MissionHandoffService.SettleStagesBehindFailedDependenciesAsync(token);

        /// <summary>
        /// Recover pipeline handoffs that dangled: a mission reached WorkProduced but its Pending downstream stage was never
        /// prepared (branch and context not copied), so it could never be dispatched. Re-drives the handoff for any such
        /// mission. An Architect is reconciled against its own plan instead (an interrupted fan-out is finished without
        /// creating anything twice). Returns the number re-driven.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count of missions whose handoff was re-driven.</returns>
        public Task<int> RecoverDanglingHandoffsAsync(CancellationToken token = default)
            => _Context.MissionHandoffService.RecoverDanglingHandoffsAsync(token);

        /// <summary>
        /// Settle a voyage whose missions have all finished; does nothing while any is still running or completing, or
        /// when the voyage is already settled.
        /// </summary>
        /// <param name="voyageId">Voyage id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task ReevaluateVoyageAsync(string voyageId, CancellationToken token = default)
            => _Context.MissionHandoffService.ReevaluateVoyageAsync(voyageId, token);

        /// <inheritdoc />
        public Task<bool> TryAdmitVerificationResumeAsync(Mission mission, Captain captain, CancellationToken token = default)
            => _Context.MissionAdmissionPipeline.TryAdmitVerificationResumeAsync(mission, captain, token);

        /// <summary>
        /// The Worker eligibility envelope that applies to a mission: its own envelope tightened onto the vessel default,
        /// or null when neither sets anything. Stored values are used as they are; an invalid stored value is ignored.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="vessel">The mission's vessel, or null when it could not be read.</param>
        /// <returns>The effective envelope, or null.</returns>
        /// <exception cref="ArgumentNullException">The mission is null.</exception>
        public static WorkerEnvelope? EffectiveWorkerEnvelope(Mission mission, Vessel? vessel)
            => WorkerEnvelopeService.EffectiveWorkerEnvelope(mission, vessel);

        /// <summary>
        /// Append the Worker envelope section to an Architect or Judge persona prompt whose template has no
        /// <c>{WorkerEnvelope}</c> placeholder (an operator-customized template from before Phase 6.1) while an envelope
        /// applies, and warn once per template. The stored template is not changed.
        /// </summary>
        /// <param name="personaPrompt">Rendered persona prompt.</param>
        /// <param name="mission">Mission.</param>
        /// <param name="templateName">Name of the persona template that was rendered, for the warning.</param>
        /// <param name="templateParams">Template parameters (the rendered <c>WorkerEnvelope</c> value).</param>
        /// <returns>The persona prompt, with the envelope section when it was missing.</returns>
        /// <exception cref="ArgumentNullException">The mission or parameters are null.</exception>
        public string EnsureWorkerEnvelopeSection(string personaPrompt, Mission mission, string templateName, Dictionary<string, string> templateParams)
            => _Context.WorkerEnvelopeService.EnsureWorkerEnvelopeSection(personaPrompt, mission, templateName, templateParams);

        /// <summary>
        /// Why an idle captain may not take a mission, or null when it may. Uses the same rules as the assignment
        /// blocker's eligibility check, so the two cannot disagree.
        /// </summary>
        /// <param name="captain">Captain, assumed idle.</param>
        /// <param name="mission">Mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A short English reason, or null when the captain is eligible.</returns>
        /// <exception cref="ArgumentNullException">The captain or mission is null.</exception>
        internal Task<string?> CaptainIneligibilityReasonAsync(Captain captain, Mission mission, CancellationToken token = default)
            => _Context.MissionCaptainRouter.CaptainIneligibilityReasonAsync(captain, mission, token);

        /// <summary>
        /// Add what each of the mission's tenant's captains is doing to a blocker (the first
        /// <see cref="MissionAssignmentBlocker.MaxListed"/> by name; the full count goes in CaptainCount). Idle captains
        /// also say whether they may take the mission, for example "idle (not assigned: persona not allowed)".
        /// </summary>
        /// <param name="blocker">Blocker to add captain statuses to.</param>
        /// <param name="captains">Captains to describe.</param>
        /// <param name="mission">Mission the blocker explains.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">The blocker, captains or mission is null.</exception>
        internal Task DescribeCaptainsAsync(MissionAssignmentBlocker blocker, List<Captain> captains, Mission mission, CancellationToken token = default)
            => _Context.MissionCaptainRouter.DescribeCaptainsAsync(blocker, captains, mission, token);

        /// <summary>
        /// Deserialize a voyage's captain-override JSON into a list. Returns an empty list for null, empty, or
        /// malformed input rather than throwing.
        /// </summary>
        /// <param name="json">Serialized override array, or null.</param>
        /// <returns>The parsed overrides, or an empty list.</returns>
        public static List<CaptainAssignmentOverride> DeserializeCaptainOverrides(string? json)
            => MissionCaptainRouter.DeserializeCaptainOverrides(json);

        /// <summary>
        /// Serialize a list of captain overrides for voyage persistence. Returns null when the list is null or
        /// empty so the column stays null rather than storing "[]".
        /// </summary>
        /// <param name="overrides">The overrides to serialize, or null.</param>
        /// <returns>The serialized JSON array, or null.</returns>
        public static string? SerializeCaptainOverrides(List<CaptainAssignmentOverride>? overrides)
            => MissionCaptainRouter.SerializeCaptainOverrides(overrides);

        /// <inheritdoc />
        public Task<MissionAssignmentBlocker?> GetAssignmentBlockerAsync(Mission mission, CancellationToken token = default)
            => _Context.MissionAdmissionPipeline.GetAssignmentBlockerCoreAsync(mission, token);

        /// <inheritdoc />
        public async Task HandleCompletionAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(captain.Assignment?.Id)) return;

            await HandleCompletionAsync(captain, captain.Assignment!.Id!, token).ConfigureAwait(false);
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
            _CompletionRuns.Enter(missionId);
            try
            {
                await _Context.StageCompletionHandler.HandleCompletionCoreAsync(captain, missionId, token).ConfigureAwait(false);
            }
            finally
            {
                _CompletionRuns.Exit(missionId);
            }
        }

        /// <inheritdoc />
        public bool IsCompletionInFlight(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return false;
            return _CompletionRuns.IsInFlight(missionId);
        }

        /// <inheritdoc />
        public void StopCompletionCommands(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return;
            _CompletionRuns.StopCommands(missionId);
        }

        #endregion
    }
}
