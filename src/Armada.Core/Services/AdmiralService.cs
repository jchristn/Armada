namespace Armada.Core.Services
{
    using System.IO;
    using System.Linq;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Top-level orchestration service for coordinating missions and captains.
    /// Delegates domain logic to CaptainService, MissionService, VoyageService, and DockService.
    /// </summary>
    public class AdmiralService : IAdmiralService
    {
        #region Public-Members

        /// <inheritdoc />
        public Func<Captain, Mission, Dock, Task<int>>? OnLaunchAgent
        {
            get => _Captains.OnLaunchAgent;
            set => _Captains.OnLaunchAgent = value;
        }

        /// <inheritdoc />
        public Func<Captain, Task>? OnStopAgent
        {
            get => _Captains.OnStopAgent;
            set => _Captains.OnStopAgent = value;
        }

        /// <inheritdoc />
        public Func<Mission, Dock, Task>? OnCaptureDiff
        {
            get => _Missions.OnCaptureDiff;
            set => _Missions.OnCaptureDiff = value;
        }

        /// <inheritdoc />
        public Func<Mission, Dock, Task>? OnMissionComplete
        {
            get => _Missions.OnMissionComplete;
            set => _Missions.OnMissionComplete = value;
        }

        /// <inheritdoc />
        public Func<Voyage, Task>? OnVoyageComplete { get; set; }

        /// <inheritdoc />
        public Func<Mission, Task<bool>>? OnReconcilePullRequest { get; set; }

        /// <inheritdoc />
        public Func<int, bool>? OnIsProcessExitHandled { get; set; }

        /// <summary>
        /// Optional callback for retrieving outbound remote-tunnel status.
        /// This is kept as a concrete AdmiralService hook so server-only infrastructure
        /// can surface tunnel state without expanding the public orchestration interface yet.
        /// </summary>
        public Func<RemoteTunnelStatus>? OnGetRemoteTunnelStatus { get; set; }

        /// <summary>
        /// Optional health-check hook that completes WorkProduced manual-landing (Landing Mode None) missions whose branch
        /// was merged into the target by hand. Returns the number reconciled. Concrete hook, like
        /// <see cref="OnGetRemoteTunnelStatus"/>, so the orchestration interface does not change.
        /// </summary>
        public Func<CancellationToken, Task<int>>? OnReconcileManualLandings { get; set; }

        /// <summary>
        /// Optional health-check hook that keeps unlanded work landable (LandingIntegrityService): it recreates missing
        /// dock worktrees of unlanded missions from their branches and retries landings deferred on a dirty checkout.
        /// Returns the number of repairs and retries. Concrete hook, so the orchestration interface does not change.
        /// </summary>
        public Func<CancellationToken, Task<int>>? OnLandingIntegritySweep { get; set; }

        /// <summary>
        /// Optional health-check hook that completes LandingFailed missions whose work is proven to be on the vessel's
        /// remote default branch (see <see cref="LandingReconciler"/>). Returns the number reconciled. Concrete hook, like
        /// <see cref="OnReconcileManualLandings"/>, so the orchestration interface does not change.
        /// </summary>
        public Func<CancellationToken, Task<int>>? OnReconcileLandingFailures { get; set; }

        /// <summary>
        /// Optional hook reporting whether an agent process was launched by this admiral and is still running under
        /// its runtime's exit callback. Such a process is owned by that callback: the health check and the orphan
        /// sweep treat it as alive instead of probing the operating system, which cannot see in-process captains
        /// (an ApiEndpoint loop has a synthetic process id) or Harbor-hosted ones. Concrete hook, like
        /// <see cref="OnGetRemoteTunnelStatus"/>, so the orchestration interface does not change.
        /// </summary>
        public Func<int, bool>? OnIsProcessTracked { get; set; }

        /// <summary>Oldest outstanding runtime tool call for a process, or null. Does not refresh output heartbeats.</summary>
        public Func<int, DateTime?>? OnGetToolWaitStartedUtc { get; set; }

        /// <summary>Whether a process is owned by a Harbor and can outlive this Admiral.</summary>
        public Func<int, bool>? OnIsRemoteProcessTracked { get; set; }

        /// <summary>Bounded startup wait for a restored Harbor process to report its status.</summary>
        public Func<int, bool>? OnIsProcessAwaitingReconnect { get; set; }

        /// <summary>
        /// Time source. In-process waits and windows (the wait for a fast-exiting captain's launch to be recorded, the
        /// crash-loop window) are measured on its monotonic clock, never its wall clock, so a wall-clock jump (the host
        /// sleeping and waking, an NTP step) cannot end a wait early or empty a window. Defaults to
        /// <see cref="TimeProvider.System"/>; tests substitute a provider whose wall clock jumps.
        /// </summary>
        internal TimeProvider Time
        {
            get => _Time;
            set => _Time = value ?? throw new ArgumentNullException(nameof(Time));
        }

        #endregion

        #region Private-Members

        private static readonly TimeSpan _StalledCaptainRetryAfter = TimeSpan.FromMinutes(15);

        // A Harbor laptop sleeps and an Admiral is redeployed; an interrupted mission is re-dispatched at least this
        // many times (more when MaxNoOpRedispatchAttempts is higher) before an interruption fails it.
        private const int _MinInterruptedRedispatches = 3;

        private int InterruptedRedispatchLimit => Math.Max(_Settings.MaxNoOpRedispatchAttempts, _MinInterruptedRedispatches);

        private string _Header = "[AdmiralService] ";
        private static readonly TimeSpan _AssignedOrphanRecoveryGracePeriod = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _WorkProducedReleaseGracePeriod = TimeSpan.FromMinutes(2);
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private ISystemResourceProbe _ResourceProbe = new SystemResourceProbe();
        private long _MemoryPressureDeferrals = 0;
        // Process ids whose exit handler is running, with a count for overlapping callbacks; the health check skips them.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _ExitsInProgress =
            new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
        // Missions with a resume in place in flight, so a second path reporting the same stop does not launch again.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _ResumingMissions =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        // When each mission's current run of transient failures began, for TransientOutageGraceMinutes.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _TransientOutageStarts =
            new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<long>> _CaptainCrashTimes =
            new System.Collections.Concurrent.ConcurrentDictionary<string, List<long>>();
        private TimeProvider _Time = TimeProvider.System;
        private ICaptainService _Captains;
        private IMissionService _Missions;
        private IVoyageService _Voyages;
        private IDockService _Docks;
        private IPlaybookService _Playbooks;
        private IEscalationService? _Escalation;
        private readonly StageOutcomeService _StageOutcomes;
        private bool _RetryDispatchNeeded = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="captains">Captain service.</param>
        /// <param name="missions">Mission service.</param>
        /// <param name="voyages">Voyage service.</param>
        /// <param name="docks">Dock service.</param>
        /// <param name="escalation">Optional escalation service.</param>
        public AdmiralService(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            ICaptainService captains,
            IMissionService missions,
            IVoyageService voyages,
            IDockService docks,
            IEscalationService? escalation = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Captains = captains ?? throw new ArgumentNullException(nameof(captains));
            _Missions = missions ?? throw new ArgumentNullException(nameof(missions));
            _Voyages = voyages ?? throw new ArgumentNullException(nameof(voyages));
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
            _Playbooks = new PlaybookService(_Database, _Logging);
            _Escalation = escalation;
            // DockHosts is assigned after construction, so the provenance probe resolves the host on each probe.
            _StageOutcomes = new StageOutcomeService(_Database, _Logging, new StageProvenanceProbe(_Logging, ProvenanceCommandsFor), () => (_Missions as MissionService)?.DockHosts);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            CancellationToken token = default)
        {
            return await DispatchVoyageAsync(title, description, vesselId, missionDescriptions, (List<SelectedPlaybook>?)null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            List<SelectedPlaybook>? selectedPlaybooks,
            CancellationToken token = default)
        {
            return await DispatchStandardVoyageAsync(title, description, vesselId, missionDescriptions, selectedPlaybooks, null, null, null, null, false, token).ConfigureAwait(false);
        }

        private async Task<Voyage> DispatchStandardVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            List<SelectedPlaybook>? selectedPlaybooks,
            PipelineStage? singleStagePolicy,
            string? captainOverridesJson,
            LandingModeEnum? landingMode,
            RunApprovalOptions? approvalOptions,
            bool isEvaluation,
            CancellationToken token)
        {
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (missionDescriptions == null || missionDescriptions.Count == 0)
                throw new ArgumentException("At least one mission is required", nameof(missionDescriptions));
            RequireDispatchLimits(missionDescriptions, selectedPlaybooks);

            // Verify vessel exists
            Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (vessel == null) throw new KeyNotFoundException("Vessel not found: " + vesselId);
            if (selectedPlaybooks != null && selectedPlaybooks.Count > 0 && !String.IsNullOrEmpty(vessel.TenantId))
            {
                await _Playbooks.ResolveSelectionsAsync(vessel.TenantId, selectedPlaybooks, token).ConfigureAwait(false);
            }

            // Create voyage. Persist the per-persona captain overrides up front so the inline mission
            // assignment below resolves the preferred captain.
            Voyage voyage = new Voyage(title, description);
            voyage.TenantId = vessel.TenantId;
            voyage.UserId = vessel.UserId;
            voyage.Status = VoyageStatusEnum.Open;
            voyage.CaptainOverridesJson = captainOverridesJson;
            voyage.ApprovalOptions = approvalOptions?.Snapshot();
            voyage.LandingMode = landingMode;
            voyage.IsEvaluation = isEvaluation;
            voyage = await _Database.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
            voyage.SelectedPlaybooks = ClonePlaybookSelections(selectedPlaybooks);
            if (voyage.SelectedPlaybooks.Count > 0)
            {
                await _Database.Playbooks.SetVoyageSelectionsAsync(voyage.Id, voyage.SelectedPlaybooks, token).ConfigureAwait(false);
            }
            _Logging.Info(_Header + "created voyage " + voyage.Id + ": " + title);

            // Create missions
            foreach (MissionDescription md in missionDescriptions)
            {
                Mission mission = new Mission(md.Title, md.Description);
                mission.TenantId = vessel.TenantId;
                mission.UserId = vessel.UserId;
                mission.VoyageId = voyage.Id;
                mission.VesselId = vesselId;
                mission.IsEvaluation = voyage.IsEvaluation;
                mission.Tier = md.Tier;
                mission.RequestedCaptainId = md.RequestedCaptainId;
                if (singleStagePolicy != null)
                {
                    mission.Persona = singleStagePolicy.PersonaName;
                    mission.RequiresReview = singleStagePolicy.RequiresReview;
                    mission.ReviewDenyAction = singleStagePolicy.ReviewDenyAction;
                }

                if (!String.IsNullOrWhiteSpace(md.Persona))
                    mission.Persona = md.Persona!.Trim();
                mission = await _Database.Missions.CreateAsync(mission, token).ConfigureAwait(false);
                await PersistMissionPlaybooksAsync(mission, voyage.SelectedPlaybooks, token).ConfigureAwait(false);
                _Logging.Info(_Header + "created mission " + mission.Id + ": " + md.Title);

                // Try to auto-assign
                await _Missions.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
            }

            // Update voyage status - only transition to InProgress if at least one mission was assigned
            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false);
            bool anyAssigned = voyageMissions.Any(m =>
                m.Status == MissionStatusEnum.Assigned ||
                m.Status == MissionStatusEnum.InProgress);

            if (anyAssigned)
            {
                voyage.Status = VoyageStatusEnum.InProgress;
            }

            voyage.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);

            return voyage;
        }

        /// <inheritdoc />
        public async Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            string? pipelineId,
            CancellationToken token = default)
        {
            return await DispatchVoyageAsync(title, description, vesselId, missionDescriptions, pipelineId, null, null, null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Voyage> DispatchVoyageAsync(
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
            bool isEvaluation = false)
        {
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (missionDescriptions == null || missionDescriptions.Count == 0)
                throw new ArgumentException("At least one mission is required", nameof(missionDescriptions));
            RequireDispatchLimits(missionDescriptions, selectedPlaybooks);

            // Verify vessel exists
            Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (vessel == null) throw new KeyNotFoundException("Vessel not found: " + vesselId);
            if (selectedPlaybooks != null && selectedPlaybooks.Count > 0 && !String.IsNullOrEmpty(vessel.TenantId))
            {
                await _Playbooks.ResolveSelectionsAsync(vessel.TenantId, selectedPlaybooks, token).ConfigureAwait(false);
            }

            // Resolve pipeline: explicit > vessel default > fleet default > WorkerOnly. When no pipeline is named and
            // every mission carries its own persona (a Mission fleet action's Persona), the missions run as single
            // stages with those personas instead of the vessel or fleet default pipeline.
            bool personaDispatch = String.IsNullOrWhiteSpace(pipelineId)
                && missionDescriptions.All(md => !String.IsNullOrWhiteSpace(md.Persona));
            Pipeline? pipeline = personaDispatch ? null : await ResolvePipelineAsync(pipelineId, vessel, token).ConfigureAwait(false);

            // If pipeline is single-stage Worker (or null), use the standard dispatch path
            if (pipeline == null)
            {
                return await DispatchStandardVoyageAsync(title, description, vesselId, missionDescriptions, selectedPlaybooks, null, captainOverridesJson, landingMode, approvalOptions, isEvaluation, token).ConfigureAwait(false);
            }

            if (pipeline.Stages.Count == 1 && PersonaCatalog.Matches(pipeline.Stages[0].PersonaName, PersonaCatalog.Worker))
            {
                return await DispatchStandardVoyageAsync(title, description, vesselId, missionDescriptions, selectedPlaybooks, pipeline.Stages[0], captainOverridesJson, landingMode, approvalOptions, isEvaluation, token).ConfigureAwait(false);
            }

            // Multi-stage pipeline: create voyage, then for each mission create a chain of persona stages.
            // Persist the per-persona captain overrides on the voyage BEFORE creating missions so the first
            // stage's inline assignment (below) resolves the preferred captain.
            Voyage voyage = new Voyage(title, description);
            voyage.TenantId = vessel.TenantId;
            voyage.UserId = vessel.UserId;
            voyage.Status = VoyageStatusEnum.Open;
            voyage.CaptainOverridesJson = captainOverridesJson;
            voyage.ApprovalOptions = approvalOptions?.Snapshot();
            voyage.LandingMode = landingMode;
            voyage.IsEvaluation = isEvaluation;
            voyage = await _Database.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
            voyage.SelectedPlaybooks = ClonePlaybookSelections(selectedPlaybooks);
            if (voyage.SelectedPlaybooks.Count > 0)
            {
                await _Database.Playbooks.SetVoyageSelectionsAsync(voyage.Id, voyage.SelectedPlaybooks, token).ConfigureAwait(false);
            }
            _Logging.Info(_Header + "created pipeline voyage " + voyage.Id + ": " + title + " (pipeline: " + pipeline.Name + ")");

            foreach (MissionDescription md in missionDescriptions)
            {
                string? previousMissionId = null;

                // Use a short base title (first 60 chars of the mission title)
                string baseTitle = md.Title.Length > 60 ? md.Title.Substring(0, 60).TrimEnd() + "..." : md.Title;

                foreach (PipelineStage stage in pipeline.Stages.OrderBy(s => s.Order))
                {
                    string stagePersonaName = PersonaCatalog.NormalizeName(stage.PersonaName);
                    if (String.IsNullOrEmpty(stagePersonaName)) stagePersonaName = stage.PersonaName;

                    Mission mission = new Mission(
                        "[" + stagePersonaName + "] " + baseTitle,
                        md.Description);
                    mission.TenantId = vessel.TenantId;
                    mission.UserId = vessel.UserId;
                    mission.VoyageId = voyage.Id;
                    mission.VesselId = vesselId;
                    mission.IsEvaluation = voyage.IsEvaluation;
                    mission.Persona = stagePersonaName;
                    mission.DependsOnMissionId = previousMissionId;
                    mission.RequiresReview = stage.RequiresReview;
                    mission.ReviewDenyAction = stage.ReviewDenyAction;

                    // A mission's tier floor applies to every stage; a requested captain applies to the first stage
                    // only (later stages are routed by their persona).
                    mission.Tier = md.Tier;
                    if (previousMissionId == null) mission.RequestedCaptainId = md.RequestedCaptainId;

                    // Only the first stage starts as Pending; dependent stages also start as Pending
                    // but won't be assigned until their dependency completes
                    mission = await _Database.Missions.CreateAsync(mission, token).ConfigureAwait(false);
                    await PersistMissionPlaybooksAsync(mission, voyage.SelectedPlaybooks, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "created pipeline mission " + mission.Id + ": " + mission.Title +
                        " (stage " + stage.Order + "/" + pipeline.Stages.Count + ", persona: " + stagePersonaName +
                        (previousMissionId != null ? ", depends on: " + previousMissionId : "") + ")");

                    // Try to auto-assign only if no dependency (first stage)
                    if (previousMissionId == null)
                    {
                        await _Missions.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                    }

                    previousMissionId = mission.Id;
                }
            }

            // Update voyage status
            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false);
            bool anyAssigned = voyageMissions.Any(m =>
                m.Status == MissionStatusEnum.Assigned ||
                m.Status == MissionStatusEnum.InProgress);

            if (anyAssigned)
            {
                voyage.Status = VoyageStatusEnum.InProgress;
            }

            voyage.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);

            return voyage;
        }

        /// <inheritdoc />
        public async Task<Mission> DispatchMissionAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            CollectionBounds.Validate(mission);
            if (mission.SelectedPlaybooks != null && mission.SelectedPlaybooks.Count > 0 && !String.IsNullOrEmpty(mission.TenantId))
            {
                await _Playbooks.ResolveSelectionsAsync(mission.TenantId, mission.SelectedPlaybooks, token).ConfigureAwait(false);
            }

            // A mission added to an evaluation voyage is evaluation work too.
            if (!mission.IsEvaluation && !String.IsNullOrEmpty(mission.VoyageId))
            {
                Voyage? owner = await _Database.Voyages.ReadAsync(mission.VoyageId!, token).ConfigureAwait(false);
                if (owner != null && owner.IsEvaluation) mission.IsEvaluation = true;
            }

            mission = await _Database.Missions.CreateAsync(mission, token).ConfigureAwait(false);
            await PersistMissionPlaybooksAsync(mission, mission.SelectedPlaybooks, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created mission " + mission.Id + ": " + mission.Title);

            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel != null)
                {
                    bool assigned = await _Missions.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);

                    // Re-read mission from database to get updated status
                    Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (refreshed != null)
                        mission = refreshed;

                    if (!assigned || mission.Status == MissionStatusEnum.Pending)
                    {
                        _Logging.Warn(_Header + "mission " + mission.Id + " created but could not be assigned to any captain");
                        _RetryDispatchNeeded = true;
                    }
                }
                else
                {
                    _Logging.Warn(_Header + "mission " + mission.Id + " created but vessel " + mission.VesselId + " not found for assignment");
                }
            }

            return mission;
        }

        private static void RequireDispatchLimits(List<MissionDescription> missionDescriptions, List<SelectedPlaybook>? selectedPlaybooks)
        {
            CollectionLimits.RequireRequestItems(missionDescriptions, "Missions");
            CollectionLimits.RequireConfigItems(selectedPlaybooks, "SelectedPlaybooks");
        }

        private List<SelectedPlaybook> ClonePlaybookSelections(List<SelectedPlaybook>? selections)
        {
            if (selections == null || selections.Count == 0) return new List<SelectedPlaybook>();

            return selections.Select(s => new SelectedPlaybook
            {
                PlaybookId = s.PlaybookId,
                DeliveryMode = s.DeliveryMode
            }).ToList();
        }

        private async Task PersistMissionPlaybooksAsync(Mission mission, List<SelectedPlaybook>? selections, CancellationToken token)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            mission.SelectedPlaybooks = ClonePlaybookSelections(selections);
            if (mission.SelectedPlaybooks.Count == 0 || String.IsNullOrEmpty(mission.TenantId))
            {
                mission.PlaybookSnapshots = new List<MissionPlaybookSnapshot>();
                return;
            }

            List<MissionPlaybookSnapshot> snapshots = await _Playbooks.CreateSnapshotsAsync(
                mission.TenantId,
                mission.SelectedPlaybooks,
                token).ConfigureAwait(false);
            mission.PlaybookSnapshots = snapshots;
            await _Database.Playbooks.SetMissionSnapshotsAsync(mission.Id, snapshots, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ArmadaStatus> GetStatusAsync(CancellationToken token = default)
        {
            return await BuildStatusAsync(null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ArmadaStatus> GetStatusAsync(AuthContext caller, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (caller.IsAdmin) return await BuildStatusAsync(null, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(caller.TenantId))
            {
                // No tenant means no tenant data; only the server-wide fields are reported.
                ArmadaStatus empty = new ArmadaStatus();
                empty.MemoryPressureDeferrals = System.Threading.Interlocked.Read(ref _MemoryPressureDeferrals);
                if (OnGetRemoteTunnelStatus != null) empty.RemoteTunnel = OnGetRemoteTunnelStatus();
                return empty;
            }

            return await BuildStatusAsync(caller.TenantId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Mission> CancelMissionAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            await MissionArchive.RecordAsync(_Database.Events, _Database.StageOutcomes, mission, "Cancel", token).ConfigureAwait(false);

            Captain? captain = String.IsNullOrEmpty(mission.CaptainId)
                ? null
                : await _Database.Captains.ReadAsync(mission.CaptainId, token).ConfigureAwait(false);
            bool ownsCaptain = captain != null && captain.CurrentMissionId == mission.Id;

            // Stop the process before releasing anything. The stop handler resolves the owning Harbor from the
            // captain's current mission, so it must run while the captain still points at this mission.
            if (ownsCaptain && captain!.ProcessId.HasValue && _Captains.OnStopAgent != null)
            {
                try
                {
                    await _Captains.OnStopAgent.Invoke(captain).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "cancel of mission " + mission.Id + " could not stop the agent for captain " + captain.Id + ": " + ex.Message);
                    throw new InvalidOperationException(
                        "The mission was not cancelled because its agent process (" + captain.Name + ") did not confirm it stopped. "
                        + "Reconnect the owning Harbor and retry the cancel. Detail: " + ex.Message, ex);
                }
            }

            if (ownsCaptain)
            {
                List<Mission> otherActive = (await _Database.Missions.EnumerateByCaptainAsync(captain!.Id, token).ConfigureAwait(false))
                    .Where(m => m.Id != mission.Id && (m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Assigned)).ToList();
                if (otherActive.Count == 0)
                {
                    captain.State = CaptainStateEnum.Idle;
                    captain.CurrentMissionId = null;
                    captain.CurrentDockId = null;
                    captain.ProcessId = null;
                    captain.RecoveryAttempts = 0;
                    captain.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                }
            }

            mission.Status = MissionStatusEnum.Cancelled;
            mission.ProcessId = null;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            return await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task RecallCaptainAsync(string captainId, CancellationToken token = default)
        {
            return RecallCaptainAsync(captainId, null, token);
        }

        /// <inheritdoc />
        public async Task RecallCaptainAsync(string captainId, OperatorStopContext? stop, CancellationToken token = default)
        {
            await _Captains.RecallAsync(captainId, null, stop, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task RecallAllAsync(CancellationToken token = default)
        {
            return RecallAllAsync(null, token);
        }

        /// <inheritdoc />
        public async Task RecallAllAsync(OperatorStopContext? stop, CancellationToken token = default)
        {
            _Logging.Warn(_Header + "RECALLING ALL CAPTAINS");

            List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);

            List<Exception> failures = new List<Exception>();
            foreach (Captain captain in workingCaptains)
            {
                try
                {
                    await _Captains.RecallAsync(captain.Id, null, stop, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error recalling captain " + captain.Id + ": " + ex.ToString());
                    failures.Add(ex);
                }
            }
            if (failures.Count > 0)
                throw new InvalidOperationException("Some captain recalls did not complete or confirm exit. Check the affected assignments and their owning hosts before retrying.", new AggregateException(failures));
        }

        /// <inheritdoc />
        public async Task StopAllAgentProcessesAsync(CancellationToken token = default)
        {
            if (_Captains.OnStopAgent == null) return;

            List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);
            if (workingCaptains.Count == 0) return;

            _Logging.Warn(_Header + "shutdown: stopping " + workingCaptains.Count + " agent process(es) so none are orphaned");
            foreach (Captain captain in workingCaptains)
            {
                if (!captain.ProcessId.HasValue) continue;
                if (OnIsRemoteProcessTracked?.Invoke(captain.ProcessId.Value) == true)
                {
                    _Logging.Info(_Header + "shutdown: leaving Harbor agent running for captain " + captain.Id);
                    continue;
                }
                try
                {
                    await _Captains.OnStopAgent.Invoke(captain).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error stopping agent for captain " + captain.Id + " on shutdown: " + ex.ToString());
                }
            }
        }

        /// <inheritdoc />
        public async Task HealthCheckAsync(CancellationToken token = default)
        {
            // Each phase is isolated: one that throws is logged and the rest of the tick still runs, so a single bad
            // record cannot stop merge reconciliation, recovery, or dispatch for everything else. Shutdown
            // cancellation still propagates.
            await RunHealthPhaseAsync("lift expired quarantines", () => LiftExpiredQuarantinesAsync(token), token).ConfigureAwait(false);
            await RunHealthPhaseAsync("release recovered stalled captains", () => ReleaseRecoveredStalledCaptainsAsync(token), token).ConfigureAwait(false);
            await RunHealthPhaseAsync("relaunch captains whose Harbor reconnected", async () =>
            {
                int relaunched = await _Captains.RetryRelaunchesAwaitingHarborAsync(token).ConfigureAwait(false);
                if (relaunched > 0) _Logging.Info(_Header + "relaunched " + relaunched + " captain(s) whose Harbor reconnected");
            }, token).ConfigureAwait(false);

            await RunHealthPhaseAsync("captain health checks", async () =>
            {
                List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);
                if (workingCaptains.Count == 0) return;

                _Logging.Debug(_Header + "starting parallel health checks for " + workingCaptains.Count + " working captain(s)");

                List<Task> healthCheckTasks = workingCaptains.Select(captain =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            await HealthCheckCaptainAsync(captain, token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _Logging.Warn(_Header + "error processing health check for captain " + captain.Id + ": " + ex.ToString());
                        }
                    }, token)).ToList();

                await Task.WhenAll(healthCheckTasks).ConfigureAwait(false);

                _Logging.Debug(_Header + "completed parallel health checks for " + workingCaptains.Count + " working captain(s)");
            }, token).ConfigureAwait(false);

            // Safety net: detect orphaned InProgress missions whose captain has moved on.
            // This catches any mission that was left InProgress due to a captain being
            // reassigned before the health check could detect the old process exit.
            await RunHealthPhaseAsync("orphaned mission recovery", () => RecoverOrphanedMissionsAsync(token), token).ConfigureAwait(false);

            // A stage that failed or was cancelled by a path that did not cascade (stopping its captain) must not leave the
            // stages after it Pending forever.
            await RunHealthPhaseAsync("stages behind failed dependencies", async () =>
            {
                int settled = await _Missions.SettleStagesBehindFailedDependenciesAsync(token).ConfigureAwait(false);
                if (settled > 0) _Logging.Info(_Header + "settled the waiting stages of " + settled + " failed or cancelled mission(s)");
            }, token).ConfigureAwait(false);

            // Re-drive pipeline handoffs that dangled: WorkProduced missions whose downstream
            // Pending stage was never prepared and would otherwise never be dispatched.
            await RunHealthPhaseAsync("dangling handoff recovery", async () =>
            {
                int redriven = await _Missions.RecoverDanglingHandoffsAsync(token).ConfigureAwait(false);
                if (redriven > 0)
                {
                    ArmadaMetrics.HandoffsRedriven.Add(redriven);
                    _Logging.Info(_Header + "re-drove " + redriven + " dangling pipeline handoff(s)");
                }
            }, token).ConfigureAwait(false);

            await RunHealthPhaseAsync("missing merge handoff recovery", () => FlagMissingMergeHandoffsAsync(DateTime.UtcNow, token), token).ConfigureAwait(false);

            // Check for completed voyages
            await RunHealthPhaseAsync("voyage completion", async () =>
            {
                List<Voyage> completedVoyages = await _Voyages.CheckCompletionsAsync(_Missions.IsCompletionInFlight, token).ConfigureAwait(false);
                if (OnVoyageComplete == null) return;
                foreach (Voyage completedVoyage in completedVoyages)
                {
                    try { await OnVoyageComplete.Invoke(completedVoyage).ConfigureAwait(false); }
                    catch (Exception ex) { _Logging.Warn(_Header + "error in OnVoyageComplete callback: " + ex.ToString()); }
                }
            }, token).ConfigureAwait(false);

            // Reconcile PullRequestOpen missions — check if their PRs have been merged
            await RunHealthPhaseAsync("pull request reconciliation", () => ReconcilePullRequestMissionsAsync(token), token).ConfigureAwait(false);

            // Reconcile manual-landing (Landing Mode None) missions whose branch was merged by hand.
            if (OnReconcileManualLandings != null)
            {
                await RunHealthPhaseAsync("manual landing reconciliation", async () =>
                {
                    int reconciledManual = await OnReconcileManualLandings.Invoke(token).ConfigureAwait(false);
                    if (reconciledManual > 0) _Logging.Info(_Header + "completed " + reconciledManual + " manually merged mission(s)");
                }, token).ConfigureAwait(false);
            }

            if (OnLandingIntegritySweep != null)
            {
                await RunHealthPhaseAsync("landing integrity", async () =>
                {
                    int repaired = await OnLandingIntegritySweep.Invoke(token).ConfigureAwait(false);
                    if (repaired > 0) _Logging.Info(_Header + "landing integrity repaired or retried " + repaired + " mission dock(s) or landing(s)");
                }, token).ConfigureAwait(false);
            }

            // Complete stale LandingFailed missions whose work reached the remote default branch by another path.
            if (OnReconcileLandingFailures != null)
                await RunHealthPhaseAsync("landing failure reconciliation", () => OnReconcileLandingFailures.Invoke(token), token).ConfigureAwait(false);

            // Release captains held by missions whose review has gone overdue (dock preserved).
            await RunHealthPhaseAsync("overdue review recovery", () => RecoverOverdueReviewsAsync(token), token).ConfigureAwait(false);

            // Reclaim docks stuck in Provisioned state with no active captain
            await RunHealthPhaseAsync("orphaned dock reclamation", () => ReclaimOrphanedDocksAsync(token), token).ConfigureAwait(false);

            await RunHealthPhaseAsync("pending mission dispatch", () => DispatchPendingMissionsAsync(token), token).ConfigureAwait(false);

            // Captain pool management: auto-spawn if below minimum idle count
            if (_Settings.MinIdleCaptains > 0)
            {
                await RunHealthPhaseAsync("captain pool maintenance", () => MaintainCaptainPoolAsync(token), token).ConfigureAwait(false);
            }

            // Evaluate escalation rules
            if (_Escalation != null)
            {
                await RunHealthPhaseAsync("escalation rules", () => _Escalation.EvaluateAsync(token), token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task CleanupStaleCaptainsAsync(CancellationToken token = default)
        {
            List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);
            if (workingCaptains.Count == 0) return;

            int resetCount = 0;

            foreach (Captain captain in workingCaptains)
            {
                // Verify liveness with PID identity: a process whose start time is later than the
                // mission launch is a recycled PID, not our agent, so the captain is treated as
                // stale rather than left stuck in Working forever.
                Mission? currentMission = null;
                if (!String.IsNullOrEmpty(captain.CurrentMissionId))
                {
                    currentMission = await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
                }

                if (currentMission?.Status == MissionStatusEnum.Verifying) continue; // Replayed by HealthCheckCaptainAsync after a restart.

                bool processAlive = captain.ProcessId.HasValue &&
                    (OnIsProcessTracked?.Invoke(captain.ProcessId.Value) == true ||
                     OnIsProcessExitHandled?.Invoke(captain.ProcessId.Value) == true ||
                     ProcessSupervisor.IsTrackedProcessAlive(captain.ProcessId.Value, currentMission?.StartedUtc));

                if (processAlive) continue;

                // Dead local agents (or a Harbor job with no recoverable ownership record) represent an actual
                // interruption. Resume in the existing dock instead of quietly provisioning a fresh generation.
                if (currentMission != null && (currentMission.Status == MissionStatusEnum.InProgress || currentMission.Status == MissionStatusEnum.Assigned))
                {
                    if (currentMission.RedispatchAttempts < InterruptedRedispatchLimit)
                        await HandleInterruptedProcessExitAsync(captain, currentMission, currentMission.Id, -1, token).ConfigureAwait(false);
                    else
                        await HandleTerminalProcessExitFailureAsync(captain, currentMission, currentMission.Id, "Restart recovery limit reached after Admiral interruption", RuntimeFailureKindEnum.Transient, token, MissionFailureKindEnum.Infra).ConfigureAwait(false);
                    resetCount++;
                    continue;
                }

                // Reset active missions back to Pending for re-dispatch
                List<Mission> captainMissions = await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false);
                List<Mission> activeMissions = captainMissions.Where(m =>
                    m.Status == MissionStatusEnum.InProgress ||
                    m.Status == MissionStatusEnum.Assigned).ToList();

                foreach (Mission mission in activeMissions)
                {
                    mission.RedispatchAttempts++;
                    mission.Status = MissionStatusEnum.Pending;
                    mission.CaptainId = null;
                    mission.ProcessId = null;
                    mission.StartedUtc = null;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    // Preserve DockId and BranchName so the next captain can continue
                    // from partial work in the existing worktree

                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                    if (!String.IsNullOrEmpty(mission.DockId))
                    {
                        _Logging.Info(_Header + "preserved dock " + mission.DockId + " for mission " + mission.Id + " (branch: " + (mission.BranchName ?? "none") + ")");

                        await EmitEventAsync("mission.dock_preserved", "Dock preserved for re-dispatch: " + mission.Id,
                            entityType: "mission", entityId: mission.Id,
                            missionId: mission.Id, token: token).ConfigureAwait(false);
                    }
                    else
                    {
                        _Logging.Info(_Header + "reset stale mission " + mission.Id + " to Pending (no dock to preserve)");
                    }
                }

                // Reset captain to Idle
                await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                resetCount++;
                _Logging.Info(_Header + "reset stale captain " + captain.Id + " to Idle on startup (process " + (captain.ProcessId?.ToString() ?? "null") + " not running)");
            }

            if (resetCount > 0)
            {
                _Logging.Info(_Header + "startup cleanup: reset " + resetCount + " stale captain(s) to Idle");

                // Dispatch any pending missions that were freed up
                await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        /// <inheritdoc />
        public Task<AutoLandDecision?> EvaluateAutoLandAsync(string missionId, CancellationToken token = default)
        {
            return _Missions.EvaluateAutoLandAsync(missionId, token);
        }

        /// <inheritdoc />
        public async Task<DispatchValidationResult> ValidateDispatchAsync(
            string? objectiveId,
            string? pipelineId,
            string? pipelineName,
            string? vesselId,
            int missionCount,
            bool allowBareVoyage,
            CancellationToken token = default)
        {
            // One dispatch request carries at most MaxRequestItems missions (REST, MCP, and every other caller).
            CollectionLimits.RequireCount(missionCount, "Missions", CollectionLimits.MaxRequestItems);

            // Objective link must exist when supplied.
            if (!String.IsNullOrWhiteSpace(objectiveId))
            {
                Objective? objective = await _Database.Objectives.ReadAsync(objectiveId!, token).ConfigureAwait(false);
                if (objective == null)
                    return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.ObjectiveNotFound, "Objective not found: " + objectiveId);
            }

            // Resolve a pipeline by explicit id (an id that is really a name is accepted too), else by name. A named
            // pipeline that does not exist is an error; the vessel and fleet defaults apply only when none was named.
            string? resolvedPipelineId = String.IsNullOrWhiteSpace(pipelineId) ? null : pipelineId!.Trim();
            if (resolvedPipelineId != null)
            {
                Pipeline? explicitPipeline = await _Database.Pipelines.ReadAsync(resolvedPipelineId, token).ConfigureAwait(false)
                    ?? await _Database.Pipelines.ReadByNameAsync(resolvedPipelineId, token).ConfigureAwait(false);
                if (explicitPipeline == null)
                    return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.PipelineNotFound, "Pipeline not found: " + resolvedPipelineId);
                resolvedPipelineId = explicitPipeline.Id;
            }
            else if (!String.IsNullOrWhiteSpace(pipelineName))
            {
                Pipeline? namedPipeline = await _Database.Pipelines.ReadByNameAsync(pipelineName!, token).ConfigureAwait(false);
                if (namedPipeline == null)
                    return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.PipelineNotFound, "Pipeline not found: " + pipelineName);
                resolvedPipelineId = namedPipeline.Id;
            }

            // A missing vessel or zero missions is a bare voyage when allowed, otherwise an error.
            bool isBareVoyage = String.IsNullOrWhiteSpace(vesselId) || missionCount == 0;
            if (isBareVoyage && !allowBareVoyage)
            {
                if (String.IsNullOrWhiteSpace(vesselId))
                    return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.MissingVessel, "A vessel id is required to dispatch a voyage.");
                return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.NoMissions, "At least one mission is required to dispatch a voyage.");
            }

            return DispatchValidationResult.Valid(resolvedPipelineId, isBareVoyage);
        }

        /// <summary>
        /// Handle an agent process exit: reconcile the captain and mission state for the exited process.
        /// </summary>
        /// <param name="processId">Operating-system process id that exited.</param>
        /// <param name="exitCode">Process exit code, or null when unavailable.</param>
        /// <param name="captainId">Captain identifier associated with the process.</param>
        /// <param name="missionId">Mission identifier associated with the process.</param>
        /// <param name="token">Cancellation token.</param>
        public Task HandleProcessExitAsync(int processId, int? exitCode, string captainId, string missionId, CancellationToken token = default)
        {
            return HandleProcessExitAsync(processId, RuntimeFailureClassifier.Decide(exitCode, null), captainId, missionId, token);
        }

        /// <summary>
        /// Handle an agent process exit with its typed exit outcome: reconcile the captain and mission state for the
        /// exited process. Quarantine, stall, and crash-loop decisions read <see cref="RuntimeExitInfo.FailureKind"/>,
        /// decided once from the exit code and the structured provider error; output text is never searched.
        /// </summary>
        /// <param name="processId">Operating-system process id that exited.</param>
        /// <param name="exitInfo">Typed exit outcome.</param>
        /// <param name="captainId">Captain identifier associated with the process.</param>
        /// <param name="missionId">Mission identifier associated with the process.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task HandleProcessExitAsync(int processId, RuntimeExitInfo exitInfo, string captainId, string missionId, CancellationToken token = default)
        {
            if (exitInfo == null) throw new ArgumentNullException(nameof(exitInfo));
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            // While this runs (it can verify the stage for many minutes) the health check leaves the process alone.
            _ExitsInProgress.AddOrUpdate(processId, 1, (_, count) => count + 1);
            StageAttemptCapture capture = new StageAttemptCapture { MissionId = missionId, ExitCode = exitInfo.ExitCode, RuntimeFailureKind = exitInfo.FailureKind };
            try
            {
                await HandleProcessExitCoreAsync(processId, exitInfo, captainId, missionId, capture, token).ConfigureAwait(false);
            }
            finally
            {
                if (_ExitsInProgress.AddOrUpdate(processId, 0, (_, count) => count - 1) <= 0) _ExitsInProgress.TryRemove(processId, out _);
                // One outcome per claimed exit, recorded after Armada has acted on it so the disposition is known.
                if (capture.Claimed && !token.IsCancellationRequested)
                    await _StageOutcomes.RecordAttemptAsync(capture, token).ConfigureAwait(false);
            }
        }

        private async Task HandleProcessExitCoreAsync(int processId, RuntimeExitInfo exitInfo, string captainId, string missionId, StageAttemptCapture capture, CancellationToken token)
        {
            int? exitCode = exitInfo.ExitCode;

            // System-scoped: process exit handler receives only IDs with no tenant context;
            // tenant is unknown until the entity is read.
            Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null)
            {
                _Logging.Warn(_Header + "captain " + captainId + " not found during process exit handling");
                return;
            }

            // Use captain's tenant to scope the mission read when available.
            // Fall back to unscoped read if tenant-scoped read fails (tenant mismatch).
            Mission? mission = null;
            if (!String.IsNullOrEmpty(captain.TenantId))
            {
                mission = await _Database.Missions.ReadAsync(captain.TenantId, missionId, token).ConfigureAwait(false);
            }
            if (mission == null)
            {
                mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            }
            if (mission == null)
            {
                _Logging.Warn(_Header + "mission " + missionId + " not found during process exit handling");
                return;
            }

            // A fast in-process captain (an ApiEndpoint loop against a quick or failing endpoint) can exit before
            // the launch path records the mission as InProgress. Assigned cannot move to WorkProduced or Failed, and
            // the launch would then overwrite the outcome with InProgress, leaving the mission stuck with no process.
            // Wait briefly for the launch to be recorded before handling the exit.
            if (mission.Status == MissionStatusEnum.Assigned)
            {
                // Measured on the monotonic clock: a wall-clock deadline expired at once when the host slept and woke,
                // and the exit was then handled against a mission the launch path was about to mark InProgress.
                long waitStarted = _Time.GetTimestamp();
                TimeSpan waitFor = TimeSpan.FromSeconds(30);
                while (mission != null && mission.Status == MissionStatusEnum.Assigned && _Time.GetElapsedTime(waitStarted) < waitFor)
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                    mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
                }

                if (mission == null)
                {
                    _Logging.Warn(_Header + "mission " + missionId + " disappeared while waiting for its launch to be recorded");
                    return;
                }

                if (mission.Status == MissionStatusEnum.Assigned)
                    _Logging.Warn(_Header + "mission " + missionId + " is still Assigned 30s after agent process " + processId + " exited; handling the exit anyway");

                Captain? refreshedCaptain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
                if (refreshedCaptain != null) captain = refreshedCaptain;
            }

            // If the mission is already in a terminal state, nothing to do — the health check
            // or another handler already processed this completion/failure.
            if (MissionStateMachine.IsTerminalOrPostWork(mission.Status))
            {
                _Logging.Debug(_Header + "mission " + missionId + " already in terminal/post-work state " + mission.Status + " — skipping process exit handling");
                return;
            }

            // If the captain is no longer working on this mission, skip
            if (captain.CurrentMissionId != missionId)
            {
                _Logging.Debug(_Header + "captain " + captainId + " is no longer assigned to mission " + missionId + " — skipping process exit handling");
                return;
            }

            // Exactly one handler owns a process exit. The claim is a conditional write on the mission's recorded
            // process, so a concurrent health check (or an exit from an earlier attempt) cannot also act on it.
            if (!await _Database.Missions.TryClaimProcessExitAsync(missionId, processId, token).ConfigureAwait(false))
            {
                _Logging.Debug(_Header + "exit of agent process " + processId + " for mission " + missionId + " is already handled or belongs to an earlier attempt -- skipping");
                return;
            }

            capture.Claimed = true;
            capture.Captain = captain;
            capture.StatusBefore = mission.Status;
            capture.OutputRepairAttemptsBefore = mission.OutputRepairAttempts;
            capture.RedispatchAttemptsBefore = mission.RedispatchAttempts;
            capture.StartedUtc = mission.StartedUtc;
            capture.DockId = mission.DockId;
            capture.EndedUtc = DateTime.UtcNow;
            mission.ProcessId = null;
            Mission? exitSnapshot = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (exitSnapshot == null || exitSnapshot.ProcessId.HasValue || exitSnapshot.StartedUtc != mission.StartedUtc
                || MissionStateMachine.IsTerminalOrPostWork(exitSnapshot.Status)) return;
            mission = exitSnapshot;

            await EmitEventAsync("captain.process_exited",
                "Agent process " + processId + " exited with code " + (exitCode?.ToString() ?? "unknown") + " for captain " + captain.Name,
                entityType: "captain", entityId: captain.Id,
                captainId: captain.Id, missionId: missionId,
                vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);

            // Feed the shared endpoint circuit breaker: a clean run closes it, an endpoint-level failure (the model
            // server unreachable, overloaded or missing the model) counts toward opening it. Task failures and crashes
            // say nothing about the endpoint.
            try
            {
                if (exitCode == 0)
                    await _Missions.RecordEndpointOutcomeAsync(captain, true, token).ConfigureAwait(false);
                else if (exitInfo.FailureKind == RuntimeFailureKindEnum.Transient || exitInfo.FailureKind == RuntimeFailureKindEnum.ModelUnavailable)
                    await _Missions.RecordEndpointOutcomeAsync(captain, false, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "could not record the endpoint outcome for captain " + captain.Id + ": " + ex.Message);
            }

            if (exitCode == 0)
            {
                // A local model that says it is done is checked first: when the vessel's build or unit tests fail in
                // the dock, the captain is resumed with the output (or the stage escalated to the voyage's planner)
                // instead of handing broken code to the next stage.
                if (mission != null)
                {
                    Mission? claimed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (claimed == null || claimed.ProcessId.HasValue || claimed.StartedUtc != mission.StartedUtc
                        || (claimed.Status != MissionStatusEnum.InProgress && claimed.Status != MissionStatusEnum.Testing)) return;
                    DateTime claimVersion = claimed.LastUpdateUtc;
                    claimed.Status = MissionStatusEnum.Verifying;
                    claimed.LastUpdateUtc = DateTime.UtcNow;
                    if (!await _Database.Missions.TryUpdateIfUnchangedAsync(claimed, claimVersion, new[] { MissionStatusEnum.InProgress, MissionStatusEnum.Testing }, token).ConfigureAwait(false)) return;
                    mission = claimed;
                    DateTime verificationVersion = mission.LastUpdateUtc;
                    await EmitEventAsync("mission.stage_check_started", "Stage check queued or running for " + mission.Id,
                        entityType: "mission", entityId: mission.Id, captainId: captain.Id, missionId: mission.Id,
                        vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);
                    StageCheckResult check = await _Missions.CheckStageWorkAsync(mission, captain, token).ConfigureAwait(false);
                    await RecordStageCheckAsync(mission, captain, check, token).ConfigureAwait(false);
                    capture.StageCheck = check;
                    Mission? afterCheck = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (afterCheck == null || afterCheck.Status != MissionStatusEnum.Verifying || afterCheck.ProcessId.HasValue
                        || afterCheck.LastUpdateUtc != verificationVersion || afterCheck.StartedUtc != mission.StartedUtc)
                    {
                        _Logging.Info(_Header + "ignoring a late stage check result for " + mission.Id);
                        await EmitEventAsync("mission.stage_check_stale", "Ignoring a late stage check result for " + mission.Id,
                            entityType: "mission", entityId: mission.Id, missionId: mission.Id, token: token).ConfigureAwait(false);
                        return;
                    }
                    afterCheck.Status = MissionStatusEnum.InProgress;
                    afterCheck.LastUpdateUtc = DateTime.UtcNow;
                    if (!await _Database.Missions.TryUpdateIfUnchangedAsync(afterCheck, verificationVersion, new[] { MissionStatusEnum.Verifying }, token).ConfigureAwait(false))
                    {
                        _Logging.Info(_Header + "stage check result lost its exit claim for " + mission.Id);
                        await EmitEventAsync("mission.stage_check_stale", "Ignoring a stage check result whose exit claim changed for " + mission.Id,
                            entityType: "mission", entityId: mission.Id, missionId: mission.Id, token: token).ConfigureAwait(false);
                        return;
                    }
                    mission = afterCheck;
                    string checkFailure = check.FailureMessage ?? "The stage check failed (" + check.Status + "/" + check.Reason + "). Rerun the configured checks.";
                    if (check.IsBlocking)
                    {
                        if (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts && await TryResumeInPlaceAsync(captain, mission, checkFailure, token).ConfigureAwait(false))
                            return;
                        if (await TryEscalateToPlannerAsync(captain, mission, checkFailure, capture, token).ConfigureAwait(false))
                        {
                            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
                            return;
                        }

                        await HandleTerminalProcessExitFailureAsync(captain, mission, missionId, checkFailure, RuntimeFailureKindEnum.TaskFailure, token).ConfigureAwait(false);
                        return;
                    }
                }

                // Clean exit — delegate to the normal completion flow
                _TransientOutageStarts.TryRemove(missionId, out DateTime _);
                _Logging.Info(_Header + "agent process " + processId + " exited cleanly for mission " + missionId + " — handling completion");
                await _Missions.HandleClaimedCompletionAsync(captain, missionId, mission!.LastUpdateUtc, token).ConfigureAwait(false);
            }
            else
            {
                // Non-zero or unknown exit code — fail the mission deterministically.
                // Process exits should not bounce between recovery paths; preserve the
                // captured runtime error, halt the voyage, and only stall the captain
                // when the failure indicates the runtime itself is unavailable.
                _Logging.Warn(_Header + "agent process " + processId + " exited with code " + (exitCode?.ToString() ?? "unknown") + " for mission " + missionId);

                // A negative exit code is an interruption (the runtime maps OperationCanceledException from a
                // stop/shutdown to -1, and a vanished PID reports -1), not a genuine agent failure — real
                // agent/model/config failures exit with a positive code. Re-dispatch an interrupted mission
                // (bounded by MaxNoOpRedispatchAttempts) instead of terminally failing the voyage, so a
                // server/harbor restart or reconnect does not nuke an otherwise-fine pipeline.
                if (exitCode.HasValue && exitCode.Value < 0
                    && mission != null
                    && mission.Status != MissionStatusEnum.Cancelled
                    && mission.RedispatchAttempts < InterruptedRedispatchLimit)
                {
                    await HandleInterruptedProcessExitAsync(captain, mission, missionId, exitCode.Value, token).ConfigureAwait(false);
                    await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
                    return;
                }

                string failureReason = exitInfo.Describe();

                // A model endpoint hiccup (Transient) or a run that ran out of iterations or budget (TaskFailure) is not
                // the end of the mission: relaunch the captain in the same dock, where its edits so far still are, with
                // a note about the earlier attempt, up to MaxRecoveryAttempts times.
                // Transient failures past MaxRecoveryAttempts keep resuming while the outage is younger than
                // TransientOutageGraceMinutes, so a proxy or model-server restart does not fail work in progress.
                bool withinOutageGrace = exitInfo.FailureKind == RuntimeFailureKindEnum.Transient && mission != null && WithinTransientOutageGrace(mission.Id);
                if (exitInfo.FailureKind != RuntimeFailureKindEnum.Transient) _TransientOutageStarts.TryRemove(missionId, out DateTime _);
                if ((exitInfo.FailureKind == RuntimeFailureKindEnum.Transient || exitInfo.FailureKind == RuntimeFailureKindEnum.TaskFailure)
                    && mission != null
                    && (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts || withinOutageGrace)
                    && await TryResumeInPlaceAsync(captain, mission, failureReason, token,
                        exitInfo.FailureKind == RuntimeFailureKindEnum.Transient ? TransientResumeDelay(captain.RecoveryAttempts) : TimeSpan.Zero).ConfigureAwait(false))
                {
                    return;
                }

                // The resumes are used up and the captain still cannot finish (often a local model): hand one last
                // attempt, on the same branch with the work so far, to the frontier captain that planned the voyage.
                if ((exitInfo.FailureKind == RuntimeFailureKindEnum.Transient || exitInfo.FailureKind == RuntimeFailureKindEnum.TaskFailure)
                    && mission != null
                    && await TryEscalateToPlannerAsync(captain, mission, failureReason, capture, token).ConfigureAwait(false))
                {
                    await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
                    return;
                }

                // Provider usage-limit / auth failures will just fail again immediately, and a crash loop (N non-clean
                // failures inside the window) means the captain keeps dying on work: quarantine it out of the dispatch
                // pool. The decision is made before the captain is released, so it goes straight from Working to
                // Quarantined and is never Idle in between.
                RuntimeFailureKindEnum failureKind = exitInfo.FailureKind;
                bool quarantine = failureKind == RuntimeFailureKindEnum.UsageLimit || failureKind == RuntimeFailureKindEnum.AuthFailure
                    || (exitCode >= 0 && failureKind == RuntimeFailureKindEnum.Crash && RecordCrashAndCheckLoop(captainId));
                await HandleTerminalProcessExitFailureAsync(captain, mission, missionId, failureReason, exitInfo.FailureKind, token,
                    exitCode < 0 ? MissionFailureKindEnum.Infra : null, quarantine, exitInfo.ProviderError).ConfigureAwait(false);
            }

            // Try to dispatch any pending missions now that capacity may have freed up
            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Return quarantined captains whose window has passed to the Idle pool so they can pick up work.
        /// </summary>
        private async Task ReleaseRecoveredStalledCaptainsAsync(CancellationToken token)
        {
            // A captain is marked Stalled after a provider condition (usage limit, auth failure, model unavailable).
            // Those usually clear on their own; without this a Stalled captain never takes work again, and every
            // persona routed only to it (with Wait) stays Pending indefinitely.
            List<Captain> stalled = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Stalled, token).ConfigureAwait(false);
            foreach (Captain captain in stalled)
            {
                if (!String.IsNullOrEmpty(captain.CurrentMissionId)) continue;
                if (DateTime.UtcNow - captain.LastUpdateUtc < _StalledCaptainRetryAfter) continue;

                await _Database.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Idle, token).ConfigureAwait(false);
                _Logging.Info(_Header + "returned stalled captain " + captain.Id + " to Idle after " + _StalledCaptainRetryAfter.TotalMinutes.ToString("0") + " minutes; it is tried again with the next mission");
                await EmitEventAsync("captain.stall_released", "Captain " + captain.Name + " returned to Idle after being stalled for " + _StalledCaptainRetryAfter.TotalMinutes.ToString("0") + " minutes",
                    entityType: "captain", entityId: captain.Id, captainId: captain.Id, token: token).ConfigureAwait(false);
            }
        }

        private async Task LiftExpiredQuarantinesAsync(CancellationToken token)
        {
            List<Captain> quarantined = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Quarantined, token).ConfigureAwait(false);
            foreach (Captain captain in quarantined)
            {
                if (!captain.QuarantineUntilUtc.HasValue || captain.QuarantineUntilUtc.Value > DateTime.UtcNow) continue;

                captain.State = CaptainStateEnum.Idle;
                captain.QuarantineUntilUtc = null;
                captain.QuarantineReason = null;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                _Logging.Info(_Header + "lifted quarantine for captain " + captain.Id);
                await EmitEventAsync("captain.quarantine_lifted", "Captain " + captain.Name + " quarantine lifted",
                    entityType: "captain", entityId: captain.Id, captainId: captain.Id, token: token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move a captain into the Quarantined state for the configured window so tier selection stops
        /// handing it work until its provider condition clears. Re-reads the captain to override any
        /// release-to-Idle that just happened during failure handling.
        /// </summary>
        /// <summary>
        /// Record a captain crash and report whether it has now crashed at least the configured threshold of
        /// times within the configured window (a crash loop). Clears the record once the loop is detected so
        /// the counter restarts after quarantine. Tracking is in-memory: a restart resets the counter, which
        /// is the intended behavior for a fresh process.
        /// </summary>
        internal bool RecordCrashAndCheckLoop(string captainId)
        {
            int threshold = _Settings.CaptainCrashLoopThreshold;
            if (threshold <= 0) return false;

            // Crash times are monotonic timestamps: on the wall clock a sleep/wake jump between two crashes pushed the
            // earlier one out of the window, and a backward step kept old crashes in it.
            long now = _Time.GetTimestamp();
            TimeSpan window = TimeSpan.FromMinutes(Math.Max(1, _Settings.CaptainCrashLoopWindowMinutes));

            List<long> times = _CaptainCrashTimes.GetOrAdd(captainId, _ => new List<long>());
            lock (times)
            {
                times.RemoveAll(t => _Time.GetElapsedTime(t, now) > window);
                times.Add(now);
                if (times.Count >= threshold)
                {
                    times.Clear();
                    return true;
                }
                return false;
            }
        }

        private async Task QuarantineCaptainAsync(string captainId, RuntimeFailureKindEnum kind, CancellationToken token, RuntimeProviderError? providerError = null)
        {
            Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null) return;

            string reason = kind switch
            {
                RuntimeFailureKindEnum.AuthFailure => "provider auth failure",
                RuntimeFailureKindEnum.Crash => "crash loop",
                _ => "provider usage limit"
            };

            // Prefer the provider's stated reset time (from the structured provider error: an explicit reset time
            // or Retry-After) over the configured backoff, but never trust an out-of-range value into the window.
            DateTime nowUtc = DateTime.UtcNow;
            DateTime until = nowUtc.AddMinutes(_Settings.CaptainQuarantineMinutes);
            if (ProviderResetParser.TryGetResetUtc(providerError, nowUtc, out DateTime? resetUtc) && resetUtc.HasValue)
            {
                until = resetUtc.Value;
                reason += " (provider reset time)";
            }

            captain.State = CaptainStateEnum.Quarantined;
            captain.QuarantineReason = reason;
            captain.QuarantineUntilUtc = until;
            captain.CurrentMissionId = null;
            captain.CurrentDockId = null;
            captain.ProcessId = null;
            captain.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);

            _Logging.Warn(_Header + "quarantined captain " + captain.Id + " (" + reason + ") until " + captain.QuarantineUntilUtc.Value.ToString("o"));
            await EmitEventAsync("captain.quarantined",
                "Captain " + captain.Name + " quarantined: " + reason,
                entityType: "captain", entityId: captain.Id, captainId: captain.Id, token: token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Build the status summary across every tenant (null) or for one tenant.
        /// </summary>
        private async Task<ArmadaStatus> BuildStatusAsync(string? tenantId, CancellationToken token)
        {
            ArmadaStatus status = new ArmadaStatus();
            bool scoped = !String.IsNullOrEmpty(tenantId);

            // Captain counts
            List<Captain> allCaptains = scoped
                ? await _Database.Captains.EnumerateAsync(tenantId!, token).ConfigureAwait(false)
                : await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false);
            status.TotalCaptains = allCaptains.Count;
            status.IdleCaptains = allCaptains.Count(c => c.State == CaptainStateEnum.Idle);
            status.WorkingCaptains = allCaptains.Count(c => c.State == CaptainStateEnum.Working);
            status.StalledCaptains = allCaptains.Count(c => c.State == CaptainStateEnum.Stalled);

            // Mission counts by status
            Dictionary<MissionStatusEnum, int> missionCounts = scoped
                ? await _Database.Missions.CountByStatusAsync(tenantId!, token).ConfigureAwait(false)
                : await _Database.Missions.CountByStatusAsync(token).ConfigureAwait(false);
            foreach (KeyValuePair<MissionStatusEnum, int> kvp in missionCounts)
            {
                if (kvp.Value > 0) status.MissionsByStatus[kvp.Key.ToString()] = kvp.Value;
            }

            // Active voyage counts, counted in the database (a one-row page carries the exact total). The voyages
            // themselves are read one page at a time through IVoyageService.EnumerateProgressAsync.
            status.InProgressVoyages = await CountVoyagesAsync(tenantId, VoyageStatusEnum.InProgress, token).ConfigureAwait(false);
            status.OpenVoyages = await CountVoyagesAsync(tenantId, VoyageStatusEnum.Open, token).ConfigureAwait(false);
            status.ActiveVoyages = status.InProgressVoyages + status.OpenVoyages;
            status.MemoryPressureDeferrals = System.Threading.Interlocked.Read(ref _MemoryPressureDeferrals);

            // Recent signals
            status.RecentSignals = scoped
                ? await _Database.Signals.EnumerateRecentAsync(tenantId!, ArmadaStatus.RecentSignalCount, token).ConfigureAwait(false)
                : await _Database.Signals.EnumerateRecentAsync(ArmadaStatus.RecentSignalCount, token).ConfigureAwait(false);

            if (OnGetRemoteTunnelStatus != null)
            {
                status.RemoteTunnel = OnGetRemoteTunnelStatus();
            }

            return status;
        }

        private async Task<int> CountVoyagesAsync(string? tenantId, VoyageStatusEnum voyageStatus, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery { PageNumber = 1, PageSize = CollectionLimits.MinPageSize, Status = voyageStatus.ToString() };
            EnumerationResult<Voyage> page = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Voyages.EnumerateAsync(tenantId!, query, token).ConfigureAwait(false)
                : await _Database.Voyages.EnumerateAsync(query, token).ConfigureAwait(false);
            return (int)Math.Min(int.MaxValue, page.TotalRecords);
        }

        /// <summary>
        /// Resolve which pipeline to use for a dispatch.
        /// Resolution order: explicit pipelineId > vessel default > fleet default > null (WorkerOnly).
        /// An explicit pipeline that does not resolve throws <see cref="PipelineNotFoundException"/> instead of
        /// silently falling back to a default.
        /// </summary>
        private async Task<Pipeline?> ResolvePipelineAsync(string? pipelineId, Vessel vessel, CancellationToken token)
        {
            // Explicit pipeline ID takes priority
            if (!String.IsNullOrWhiteSpace(pipelineId))
            {
                string reference = pipelineId!.Trim();
                Pipeline? explicit_ = await _Database.Pipelines.ReadAsync(reference, token).ConfigureAwait(false);
                if (explicit_ != null) return explicit_;

                // Try by name if not found by ID
                explicit_ = await _Database.Pipelines.ReadByNameAsync(reference, token).ConfigureAwait(false);
                if (explicit_ != null) return explicit_;

                throw new PipelineNotFoundException(reference);
            }

            // Vessel default
            if (!String.IsNullOrEmpty(vessel.DefaultPipelineId))
            {
                Pipeline? vesselPipeline = await _Database.Pipelines.ReadAsync(vessel.DefaultPipelineId, token).ConfigureAwait(false);
                if (vesselPipeline != null) return vesselPipeline;

                // Pipeline no longer exists -- clear the stale reference
                _Logging.Warn(_Header + "vessel " + vessel.Id + " references missing pipeline " + vessel.DefaultPipelineId + " -- clearing");
                vessel.DefaultPipelineId = null;
                await _Database.Vessels.UpdateAsync(vessel, token).ConfigureAwait(false);
            }

            // Fleet default
            if (!String.IsNullOrEmpty(vessel.FleetId))
            {
                Fleet? fleet = await _Database.Fleets.ReadAsync(vessel.FleetId, token).ConfigureAwait(false);
                if (fleet != null && !String.IsNullOrEmpty(fleet.DefaultPipelineId))
                {
                    Pipeline? fleetPipeline = await _Database.Pipelines.ReadAsync(fleet.DefaultPipelineId, token).ConfigureAwait(false);
                    if (fleetPipeline != null) return fleetPipeline;

                    // Pipeline no longer exists -- clear the stale reference
                    _Logging.Warn(_Header + "fleet " + fleet.Id + " references missing pipeline " + fleet.DefaultPipelineId + " -- clearing");
                    fleet.DefaultPipelineId = null;
                    await _Database.Fleets.UpdateAsync(fleet, token).ConfigureAwait(false);
                }
            }

            return null;
        }

        /// <summary>
        /// Process health check for a single captain. Isolated so exceptions in one captain
        /// do not prevent processing of other captains.
        /// </summary>
        private async Task HealthCheckCaptainAsync(Captain captain, CancellationToken token)
        {
            // Exit handling owns stage checks and resume, including the period with no replacement PID.
            // Check before releasing a post-work captain, not only before probing its old process.
            if ((!String.IsNullOrEmpty(captain.CurrentMissionId) && _ResumingMissions.ContainsKey(captain.CurrentMissionId))
                || (captain.ProcessId.HasValue && _ExitsInProgress.ContainsKey(captain.ProcessId.Value))) return;

            // Look up the captain's single active mission
            Mission? mission = null;
            if (!String.IsNullOrEmpty(captain.CurrentMissionId))
            {
                mission = await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
            }

            if (mission?.Status == MissionStatusEnum.Verifying)
            {
                // The exit handler owns queued and running verification. A server restart loses that owner;
                // restore its process claim so the ordinary health path can replay verification safely.
                if (captain.ProcessId.HasValue && _ExitsInProgress.ContainsKey(captain.ProcessId.Value)) return;
                if (!captain.ProcessId.HasValue) return;
                DateTime version = mission.LastUpdateUtc;
                mission.Status = MissionStatusEnum.InProgress;
                mission.ProcessId = captain.ProcessId;
                mission.LastUpdateUtc = DateTime.UtcNow;
                if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, version, new[] { MissionStatusEnum.Verifying }, token).ConfigureAwait(false)) return;
            }

            if (mission != null && mission.Status == MissionStatusEnum.WorkProduced)
            {
                TimeSpan workProducedAge = DateTime.UtcNow - mission.LastUpdateUtc;
                if (workProducedAge < _WorkProducedReleaseGracePeriod)
                {
                    _Logging.Debug(_Header + "captain " + captain.Id + " mission " + captain.CurrentMissionId +
                        " is freshly WorkProduced (" + workProducedAge.TotalSeconds.ToString("F1") +
                        "s) - deferring captain release while handoff/landing completes");
                    return;
                }
            }

            // Check for terminal mission state (e.g. server restart between completion and release)
            if (mission != null &&
                MissionStateMachine.IsTerminalOrPostWork(mission.Status))
            {
                _Logging.Warn(_Header + "captain " + captain.Id + " has terminal/post-work mission " + captain.CurrentMissionId + " (status: " + mission.Status + ") - releasing to Idle");
                await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                return;
            }

            bool isActive = mission != null &&
                (mission.Status == MissionStatusEnum.InProgress ||
                 mission.Status == MissionStatusEnum.Assigned ||
                 mission.Status == MissionStatusEnum.Review ||
                 mission.Status == MissionStatusEnum.Testing);

            if (!isActive && captain.ProcessId == null)
            {
                // Orphaned captain - Working state but no active mission and no process.
                _Logging.Warn(_Header + "captain " + captain.Id + " is Working but has no active mission or process - releasing to Idle");
                await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                return;
            }

            // Determine the process ID to check
            int? processId = captain.ProcessId ?? mission?.ProcessId;
            if (processId.HasValue && OnIsProcessAwaitingReconnect?.Invoke(processId.Value) == true) return;

            // A stall-recovery relaunch that was refused (requireHarborForLaunch with the user's Harbor offline) leaves
            // the captain Working on its active mission with no process and a spent recovery attempt. Treat it as a
            // live but silent process so the stall check below retries recovery (the Harbor may have reconnected) or
            // fails the mission once the attempts run out.
            bool awaitingRecoveryRelaunch = processId == null && isActive && captain.RecoveryAttempts > 0;
            if (processId == null && !awaitingRecoveryRelaunch) return;

            bool isAlive = false;
            int exitCode = -1;

            if (awaitingRecoveryRelaunch)
            {
                isAlive = true;
            }
            // A process this admiral launched and still tracks is owned by its runtime's exit callback, which reports the
            // real outcome. Probing the OS here would misread an in-process ApiEndpoint loop (synthetic process id) or a
            // Harbor-hosted process as vanished and race the callback with a second, conflicting exit decision.
            else if (OnIsProcessTracked != null && OnIsProcessTracked(processId!.Value))
            {
                isAlive = true;
            }
            else if (_ExitsInProgress.ContainsKey(processId!.Value))
            {
                // This admiral's exit handler for the process is still running (a stage check can take many minutes):
                // it owns the outcome for as long as it runs, beyond the time-limited handled-exit record below.
                _Logging.Debug(_Header + "captain " + captain.Id + " process " + processId + " exit is still being handled -- skipping");
                return;
            }
            else if (OnIsProcessExitHandled != null && OnIsProcessExitHandled(processId!.Value))
            {
                // The exit callback already fired for this process: its async handler owns the outcome, so the
                // health check must not race it with a second, conflicting decision.
                _Logging.Debug(_Header + "captain " + captain.Id + " process " + processId +
                    " exit callback already fired -- skipping health check to avoid racing its handler");
                return;
            }
            else
            {
                try
                {
                    System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId!.Value);
                    if (process.HasExited)
                    {
                        isAlive = false;
                        try { exitCode = process.ExitCode; }
                        catch { }
                    }
                    else
                    {
                        isAlive = true;
                    }
                }
                catch (ArgumentException)
                {
                    // A missing PID could mean the agent crashed, was killed externally, or the OS
                    // recycled the PID. Only a confirmed clean exit should be treated as success.
                    isAlive = false;
                    exitCode = -1;
                }
            }

            string missionId = captain.CurrentMissionId ?? "unknown";

            if (!isAlive && exitCode != 0 && mission != null && mission.ProcessId.HasValue)
            {
                // Claim the exit so the callback (or a second health check) cannot also handle it.
                if (!await _Database.Missions.TryClaimProcessExitAsync(mission.Id, processId!.Value, token).ConfigureAwait(false))
                {
                    _Logging.Debug(_Header + "captain " + captain.Id + " process " + processId + " exit for mission " + mission.Id + " is already handled or belongs to an earlier attempt -- skipping");
                    return;
                }

                mission.ProcessId = null;
            }

            if (!isAlive)
            {
                if (exitCode == 0)
                {
                    // A clean exit still needs the same stage check as a runtime callback.
                    _Logging.Info(_Header + "captain " + captain.Id + " process " + processId + " completed successfully for mission " + missionId);

                    // Emit captain.completed event
                    await EmitEventAsync("captain.completed", "Captain " + captain.Id + " process " + processId + " exited successfully",
                        entityType: "captain", entityId: captain.Id,
                        captainId: captain.Id, missionId: missionId, token: token).ConfigureAwait(false);

                    await HandleProcessExitAsync(processId!.Value, 0, captain.Id, missionId, token).ConfigureAwait(false);
                }
                else
                {
                    _Logging.Warn(_Header + "captain " + captain.Id + " process " + processId + " exited with code " + exitCode + " for mission " + missionId);

                    // Emit captain.completed event (process exited, even if non-zero)
                    await EmitEventAsync("captain.completed", "Captain " + captain.Id + " process " + processId + " exited with code " + exitCode,
                        entityType: "captain", entityId: captain.Id,
                        captainId: captain.Id, missionId: missionId, token: token).ConfigureAwait(false);

                    // A negative exit code signals an interruption rather than a genuine agent failure:
                    // the runtime maps OperationCanceledException (a stop/shutdown) to -1, and the health
                    // monitor uses -1 for a vanished PID. Real agent/model/config failures exit with a
                    // positive code. Re-dispatch an interrupted mission (bounded by MaxNoOpRedispatchAttempts)
                    // instead of terminally failing the voyage, so a server/harbor restart or reconnect does
                    // not nuke an otherwise-fine pipeline. Genuine, repeatable interruptions still fail once
                    // the redispatch budget is exhausted.
                    if (exitCode < 0
                        && mission != null
                        && mission.Status != MissionStatusEnum.Cancelled
                        && mission.RedispatchAttempts < InterruptedRedispatchLimit)
                    {
                        await HandleInterruptedProcessExitAsync(captain, mission, missionId, exitCode, token).ConfigureAwait(false);
                        return;
                    }

                    // The health monitor only knows the exit code (no structured provider error reached it).
                    RuntimeExitInfo healthExitInfo = RuntimeFailureClassifier.Decide(exitCode, null);
                    await HandleTerminalProcessExitFailureAsync(captain, mission, missionId, healthExitInfo.Describe(), healthExitInfo.FailureKind, token, exitCode < 0 ? MissionFailureKindEnum.Infra : null).ConfigureAwait(false);
                }
            }
            else
            {
                // Process is alive — do NOT update heartbeat here.
                // Heartbeat should only be updated when the agent produces actual output
                // (handled by HandleAgentOutput in ArmadaServer). Updating it here from the
                // health check would mask stalled agents that are technically running but
                // producing no output.

                // Hard runaway backstop: force-fail a mission that has run past the configured
                // max-runtime ceiling, independent of stall detection (a mission emitting steady
                // output would never trip the stall path). 0 disables the cap.
                if (_Settings.MaxMissionRuntimeMinutes > 0 &&
                    mission != null &&
                    mission.StartedUtc.HasValue &&
                    (DateTime.UtcNow - mission.StartedUtc.Value).TotalMinutes > _Settings.MaxMissionRuntimeMinutes)
                {
                    _Logging.Warn(_Header + "captain " + captain.Id + " mission " + missionId +
                        " exceeded max runtime of " + _Settings.MaxMissionRuntimeMinutes + " min - force-failing runaway");

                    if (_Captains.OnStopAgent != null)
                    {
                        try { await _Captains.OnStopAgent.Invoke(captain).ConfigureAwait(false); }
                        catch (Exception)
                        {
                            await RecordUnconfirmedStopAsync(captain, mission, token).ConfigureAwait(false);
                            return;
                        }
                    }

                    if (!MissionStateMachine.IsTerminalOrPostWork(mission.Status))
                    {
                        mission.Status = MissionStatusEnum.Failed;
                        mission.FailureReason = "Mission exceeded max runtime of " + _Settings.MaxMissionRuntimeMinutes + " minutes";
                        mission.FailureKind = MissionFailureKindEnum.MaxRuntimeExceeded;
                        mission.ProcessId = null;
                        mission.CompletedUtc = DateTime.UtcNow;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                        ArmadaMetrics.MissionRuntimeExceeded.Add(1);
                        ArmadaMetrics.MissionsFailed.Add(1);

                        await EmitEventAsync("mission.failed", "Mission failed: " + mission.Title + " (exceeded max runtime)",
                            entityType: "mission", entityId: mission.Id,
                            captainId: captain.Id, missionId: mission.Id, token: token).ConfigureAwait(false);
                    }

                    await ReclaimDockAsync(captain, mission, token).ConfigureAwait(false);
                    await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                    return;
                }

                // Check for stall (no output for too long)
                DateTime? outputHeartbeat = captain.LastHeartbeatUtc ?? mission?.StartedUtc;
                if (outputHeartbeat.HasValue)
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    TimeSpan elapsed = nowUtc - outputHeartbeat.Value;
                    DateTime? toolStarted = processId.HasValue ? OnGetToolWaitStartedUtc?.Invoke(processId.Value) : null;
                    if (RuntimeToolWaitTracker.IsStalled(nowUtc, outputHeartbeat, toolStarted,
                        _Settings.StallThresholdMinutes, _Settings.ToolWaitHardLimitMinutes))
                    {
                        _Logging.Warn(_Header + "captain " + captain.Id + " appears stalled (" + elapsed.TotalMinutes.ToString("F1") + " min since last heartbeat)");
                        ArmadaMetrics.CaptainStalls.Add(1);
                        string stallReason = "No activity for " + elapsed.TotalMinutes.ToString("F1")
                            + " min (configured stall threshold: " + _Settings.StallThresholdMinutes + " min).";
                        string stallRemediation = "Remediation: inspect provider/tool progress and pending background work; correct blocked configuration or job lifetime before restarting.";
                        await EmitEventAsync("captain.stalled", stallReason + " "
                            + (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts
                                ? "Automatic recovery " + (captain.RecoveryAttempts + 1) + " of " + _Settings.MaxRecoveryAttempts + " will be attempted. "
                                : "Automatic stall recovery is exhausted. ") + stallRemediation,
                            entityType: "captain", entityId: captain.Id, captainId: captain.Id,
                            missionId: mission?.Id, vesselId: mission?.VesselId, voyageId: mission?.VoyageId,
                            token: token, tenantId: mission?.TenantId ?? captain.TenantId,
                            userId: mission?.UserId ?? captain.UserId).ConfigureAwait(false);

                        // Attempt auto-recovery if under the limit
                        if (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts)
                        {
                            ArmadaMetrics.CaptainRecoveries.Add(1);
                            // Kill the stalled process first
                            if (captain.ProcessId.HasValue && _Captains.OnStopAgent == null)
                            {
                                await RecordUnconfirmedStopAsync(captain, mission, token).ConfigureAwait(false);
                                return;
                            }
                            try
                            {
                                if (captain.ProcessId.HasValue && _Captains.OnStopAgent != null)
                                    await _Captains.OnStopAgent.Invoke(captain).ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                await RecordUnconfirmedStopAsync(captain, mission, token).ConfigureAwait(false);
                                return;
                            }

                            await _Captains.TryRecoverAsync(captain, token).ConfigureAwait(false);
                        }
                        else
                        {
                            if (_Captains.OnStopAgent != null)
                            {
                                try { await _Captains.OnStopAgent.Invoke(captain).ConfigureAwait(false); }
                                catch (Exception)
                                {
                                    await RecordUnconfirmedStopAsync(captain, mission, token).ConfigureAwait(false);
                                    return;
                                }
                            }

                            // Mark the active mission as Failed
                            if (mission != null &&
                                !MissionStateMachine.IsTerminalOrPostWork(mission.Status))
                            {
                                mission.Status = MissionStatusEnum.Failed;
                                mission.FailureReason = "Captain stalled, recovery exhausted. " + stallReason + " " + stallRemediation;
                                mission.FailureKind = MissionFailureKindEnum.StallRecoveryExhausted;
                                mission.ProcessId = null;
                                mission.CompletedUtc = DateTime.UtcNow;
                                mission.LastUpdateUtc = DateTime.UtcNow;
                                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                                ArmadaMetrics.MissionsFailed.Add(1);
                                _Logging.Warn(_Header + "mission " + mission.Id + " marked failed (captain stalled, recovery exhausted)");

                                // Emit mission.failed event
                                await EmitEventAsync("mission.failed", "Mission failed: " + mission.Title + " (captain stalled, recovery exhausted)",
                                    entityType: "mission", entityId: mission.Id,
                                    captainId: captain.Id, missionId: mission.Id, token: token).ConfigureAwait(false);
                            }

                            // Reclaim the dock worktree so it doesn't leak
                            await ReclaimDockAsync(captain, mission, token).ConfigureAwait(false);

                            // Release the captain to Idle instead of Stalled so it can pick up new work.
                            // The mission is already marked Failed -- no need to also block the captain.
                            await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                            _Logging.Info(_Header + "captain " + captain.Id + " released to Idle after stall recovery exhaustion");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Retain active ownership and surface remediation when stop acknowledgement is unavailable.
        /// </summary>
        private async Task RecordUnconfirmedStopAsync(Captain captain, Mission? mission, CancellationToken token)
        {
            const string reason = "Recovery deferred: the previous job has not confirmed its exit. No replacement was launched. Remediation: reconnect the owning Harbor and confirm the job has stopped before retrying.";
            _Logging.Warn(_Header + reason + " Captain: " + captain.Id);
            if (mission != null)
            {
                // The owning host has not confirmed the job stopped: an environment problem, not the work.
                mission.FailureKind = MissionFailureKindEnum.Infra;
                mission.FailureReason = reason;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            }
            await EmitEventAsync("captain.stop_unconfirmed", reason, entityType: "captain", entityId: captain.Id,
                captainId: captain.Id, missionId: mission?.Id, voyageId: mission?.VoyageId,
                token: token, tenantId: mission?.TenantId ?? captain.TenantId, userId: mission?.UserId ?? captain.UserId).ConfigureAwait(false);
        }

        /// <summary>Emit an event to the event log database.</summary>
        private async Task RecordStageCheckAsync(Mission mission, Captain captain, StageCheckResult check, CancellationToken token)
        {
            try
            {
                await _Database.Events.CreateAsync(StageCheckEvents.Build(mission, captain, check), token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record the stage check for mission " + mission.Id + ": " + ex.Message);
            }
        }

        private async Task EmitEventAsync(string eventType, string message,
            string? entityType = null, string? entityId = null,
            string? captainId = null, string? missionId = null,
            string? vesselId = null, string? voyageId = null,
            CancellationToken token = default, string? tenantId = null, string? userId = null)
        {
            try
            {
                ArmadaEvent evt = new ArmadaEvent(eventType, message);
                evt.TenantId = tenantId;
                evt.UserId = userId;
                evt.EntityType = entityType;
                evt.EntityId = entityId;
                evt.CaptainId = captainId;
                evt.MissionId = missionId;
                evt.VesselId = vesselId;
                evt.VoyageId = voyageId;
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error emitting event " + eventType + ": " + ex.ToString());
            }
        }

        /// <summary>
        /// Reclaim docks that have been active for too long without an associated working captain.
        /// This catches docks leaked by failed launches or agent crashes.
        /// </summary>
        private async Task ReclaimOrphanedDocksAsync(CancellationToken token)
        {
            try
            {
                List<Dock> allDocks = await _Database.Docks.EnumerateAsync(token).ConfigureAwait(false);
                DateTime threshold = DateTime.UtcNow.AddMinutes(-5);

                foreach (Dock dock in allDocks)
                {
                    if (!dock.Active) continue;

                    // Skip recently created docks (may still be in the provisioning process)
                    if (dock.CreatedUtc > threshold) continue;

                    // Check if any captain is actively using this dock
                    bool inUse = false;
                    if (!String.IsNullOrEmpty(dock.CaptainId))
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(dock.CaptainId, token).ConfigureAwait(false);
                        if (captain != null && captain.State == CaptainStateEnum.Working && captain.CurrentDockId == dock.Id)
                        {
                            inUse = true;
                        }
                    }

                    if (!inUse)
                    {
                        // Preserve the dock if a Pending mission is holding it for re-dispatch, or an
                        // in-review mission is retaining its worktree for the reviewer (the held
                        // captain may have been released by the review-timeout watchdog).
                        List<Mission> pendingMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false);
                        List<Mission> reviewMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Review, token).ConfigureAwait(false);
                        List<Mission> failedMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Failed, token).ConfigureAwait(false);
                        // Unlanded work keeps its worktree so retry_landing can land it.
                        List<Mission> landingFailedMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.LandingFailed, token).ConfigureAwait(false);
                        List<Mission> workProducedMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.WorkProduced, token).ConfigureAwait(false);
                        bool preservedForMission = pendingMissions.Any(m => m.DockId == dock.Id) ||
                                                   reviewMissions.Any(m => m.DockId == dock.Id) ||
                                                   landingFailedMissions.Any(m => m.DockId == dock.Id) ||
                                                   workProducedMissions.Any(m => m.DockId == dock.Id) ||
                                                   failedMissions.Any(m => m.DockId == dock.Id && m.FailureKind == MissionFailureKindEnum.StallRecoveryExhausted);
                        if (preservedForMission)
                        {
                            _Logging.Debug(_Header + "skipping reclaim of dock " + dock.Id + " -- preserved for a pending re-dispatch or an in-review mission");
                            continue;
                        }

                        _Logging.Info(_Header + "reclaiming orphaned dock " + dock.Id + " (created " + dock.CreatedUtc + ", no active captain)");
                        try
                        {
                            await _Docks.ReclaimAsync(dock.Id, token: token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _Logging.Warn(_Header + "error reclaiming orphaned dock " + dock.Id + ": " + ex.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error in orphaned dock reclamation: " + ex.ToString());
            }
        }

        /// <summary>
        /// Release the captain held by a mission whose review has awaited action past the configured
        /// timeout. The mission stays in Review and its dock is preserved for the reviewer; only the
        /// scarce captain is freed so a forgotten review cannot pin capacity forever. Fires once per
        /// overdue mission (the deadline is cleared after handling).
        /// </summary>
        private async Task RecoverOverdueReviewsAsync(CancellationToken token)
        {
            try
            {
                List<Mission> reviews = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Review, token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;

                foreach (Mission mission in reviews)
                {
                    if (!mission.ReviewDeadlineUtc.HasValue || mission.ReviewDeadlineUtc.Value > now) continue;

                    Voyage? approvalVoyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
                    RunApprovalOptions? options = approvalVoyage?.ApprovalOptions;
                    if (options == null && _Settings.ReviewTimeoutMinutes <= 0) continue;
                    if (options != null && mission.RequiresReview)
                    {
                        // Re-read through the review service so a human decision that already won is preserved.
                        try
                        {
                            string comment = "Approval timer expired; applied run default: " + options.TimeoutDecision + ".";
                            if (options.TimeoutDecision == ApprovalTimeoutDecisionEnum.Approve)
                                await _Missions.ApproveReviewAsync(mission.Id, null, comment, token: token).ConfigureAwait(false);
                            else
                                await _Missions.DenyReviewAsync(mission.Id, null, comment, ReviewDenyActionEnum.FailPipeline, token).ConfigureAwait(false);
                        }
                        catch (InvalidOperationException) { /* A concurrent reviewer already resolved it. */ }
                        continue;
                    }

                    _Logging.Warn(_Header + "review overdue for mission " + mission.Id +
                        " -- releasing held captain (mission and dock preserved for the reviewer)");

                    if (!String.IsNullOrEmpty(mission.CaptainId))
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(mission.CaptainId, token).ConfigureAwait(false);
                        if (captain != null && captain.State == CaptainStateEnum.Working && captain.CurrentMissionId == mission.Id)
                        {
                            await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                        }
                    }

                    // Clear the deadline so the watchdog fires once; the mission remains in Review.
                    mission.ReviewDeadlineUtc = null;
                    mission.LastUpdateUtc = now;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    ArmadaMetrics.ReviewsOverdue.Add(1);

                    await EmitEventAsync("mission.review_overdue",
                        "Review overdue: " + mission.Title + " -- held captain released, awaiting reviewer",
                        entityType: "mission", entityId: mission.Id,
                        captainId: mission.CaptainId, missionId: mission.Id,
                        vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error recovering overdue reviews: " + ex.ToString());
            }
        }

        // A restart or an exception after WorkProduced was persisted can lose the landing callback.
        // Flag rather than retry: the dock may already be reclaimed or the branch may need review.
        private async Task FlagMissingMergeHandoffsAsync(DateTime utcNow, CancellationToken token)
        {
            List<Mission> produced = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.WorkProduced, token).ConfigureAwait(false);
            foreach (Mission mission in produced)
            {
                if (utcNow - mission.LastUpdateUtc < TimeSpan.FromMinutes(5)
                    || _Missions.IsCompletionInFlight(mission.Id)
                    || MissionModeContract.IsReadOnly(mission.Mode) || mission.RequiresReview) continue;
                Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                Voyage? voyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
                if ((voyage?.LandingMode ?? vessel?.LandingMode ?? _Settings.LandingMode ?? LandingModeEnum.MergeAndPush) != LandingModeEnum.MergeQueue) continue;
                if (voyage != null && (await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false))
                    .Any(m => m.DependsOnMissionId == mission.Id)) continue;
                EnumerationResult<MergeEntry> entries = await _Database.MergeEntries.EnumerateAsync(new EnumerationQuery { MissionId = mission.Id, PageSize = 1 }, token).ConfigureAwait(false);
                if (entries.Objects.Count > 0) continue;
                EnumerationResult<ArmadaEvent> checks = await _Database.Events.EnumerateAsync(new EnumerationQuery
                {
                    MissionId = mission.Id, EventType = StageCheckEvents.EventType, PageSize = 1,
                    Order = EnumerationOrderEnum.CreatedDescending
                }, token).ConfigureAwait(false);
                StageCheckResult? check = StageCheckEvents.Read(checks.Objects.FirstOrDefault());
                if (check == null || check.IsBlocking) continue;
                DateTime version = mission.LastUpdateUtc;
                mission.Status = MissionStatusEnum.LandingFailed;
                // A lost landing callback is an Armada handoff failure, not a merge conflict an autonomous rescue can fix.
                mission.FailureKind = MissionFailureKindEnum.Infra;
                mission.FailureReason = "Stage check was nonblocking, but no merge queue entry was recorded within five minutes. Inspect the preserved branch and enqueue landing manually.";
                mission.LastUpdateUtc = DateTime.UtcNow;
                if (await _Database.Missions.TryUpdateIfUnchangedAsync(mission, version, new[] { MissionStatusEnum.WorkProduced }, token).ConfigureAwait(false))
                    await EmitEventAsync("mission.landing_handoff_missing", mission.FailureReason,
                        entityType: "mission", entityId: mission.Id, missionId: mission.Id, vesselId: mission.VesselId,
                        voyageId: mission.VoyageId, tenantId: mission.TenantId, userId: mission.UserId, token: token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reconcile missions in PullRequestOpen status by checking if their PRs have been merged.
        /// This replaces the fire-and-forget 5-minute poller with a persistent, restart-safe check.
        /// Rate-limited to at most 10 missions per health check cycle.
        /// </summary>
        private async Task ReconcilePullRequestMissionsAsync(CancellationToken token)
        {
            if (OnReconcilePullRequest == null) return;

            try
            {
                List<Mission> prMissions = await _Database.Missions.EnumerateByStatusAsync(
                    MissionStatusEnum.PullRequestOpen, token).ConfigureAwait(false);

                if (prMissions.Count == 0) return;

                // Rate-limit: check at most 10 per cycle to avoid hammering the git host API
                int checked_count = 0;
                foreach (Mission mission in prMissions)
                {
                    if (checked_count >= 10) break;

                    try
                    {
                        await OnReconcilePullRequest.Invoke(mission).ConfigureAwait(false);
                        checked_count++;
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error reconciling PR for mission " + mission.Id + ": " + ex.ToString());
                    }
                }

                if (checked_count > 0)
                    _Logging.Debug(_Header + "reconciled " + checked_count + " PullRequestOpen mission(s)");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error in PR reconciliation: " + ex.ToString());
            }
        }

        /// <summary>
        /// Detect and recover orphaned missions — missions stuck in InProgress, Assigned,
        /// Review, or Testing whose captain has moved on to a different mission. This handles
        /// the edge case where a captain was reassigned before the health check could detect
        /// the old process exit.
        /// </summary>
        private async Task RecoverOrphanedMissionsAsync(CancellationToken token)
        {
            List<Mission> inProgressMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress, token).ConfigureAwait(false);
            List<Mission> assignedMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Assigned, token).ConfigureAwait(false);
            List<Mission> reviewMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Review, token).ConfigureAwait(false);
            List<Mission> testingMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Testing, token).ConfigureAwait(false);
            List<Mission> activeMissions = inProgressMissions.Concat(assignedMissions).Concat(reviewMissions).Concat(testingMissions).ToList();

            foreach (Mission mission in activeMissions)
            {
                try
                {
                    await RecoverOrphanedMissionAsync(mission, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error checking mission " + mission.Id + " for orphan recovery: " + ex.ToString());
                }
            }
        }

        private async Task RecoverOrphanedMissionAsync(Mission mission, CancellationToken token)
        {
            {
                // A claimed exit clears ProcessId before checking the stage or relaunching. It is not evidence
                // of a completed agent. In particular, do not race the gap before a resume attaches its new PID.
                if (_ResumingMissions.ContainsKey(mission.Id)) return;
                Mission? current = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                if (current == null || current.Status != mission.Status || current.ProcessId != mission.ProcessId) return;
                if (mission.ProcessId.HasValue && _ExitsInProgress.ContainsKey(mission.ProcessId.Value)) return;
                if (!mission.ProcessId.HasValue && mission.StartedUtc.HasValue) return;

                if (mission.Status == MissionStatusEnum.Review &&
                    mission.RequiresReview &&
                    mission.ReviewRequestedUtc.HasValue &&
                    !mission.ProcessId.HasValue)
                {
                    return;
                }

                if (String.IsNullOrEmpty(mission.CaptainId)) return;

                Captain? captain = await _Database.Captains.ReadAsync(mission.CaptainId, token).ConfigureAwait(false);
                if (captain == null) return;

                // If the captain's current mission is different, this mission is orphaned
                if (captain.CurrentMissionId != mission.Id)
                {
                    bool assignmentLaunchStillPending =
                        mission.Status == MissionStatusEnum.Assigned &&
                        !mission.ProcessId.HasValue &&
                        !mission.StartedUtc.HasValue;

                    if (assignmentLaunchStillPending)
                    {
                        TimeSpan assignmentAge = DateTime.UtcNow - mission.LastUpdateUtc;
                        if (assignmentAge < _AssignedOrphanRecoveryGracePeriod)
                        {
                            _Logging.Debug(_Header + "skipping orphan recovery for freshly assigned mission " + mission.Id +
                                " (" + assignmentAge.TotalSeconds.ToString("F1") + "s since assignment)");
                            return;
                        }
                    }

                    _Logging.Warn(_Header + "orphaned mission detected: " + mission.Id + " is " + mission.Status +
                        " but captain " + captain.Id + " is working on " + (captain.CurrentMissionId ?? "nothing") +
                        " — checking if work was completed");

                    // Only recover missions that actually launched an agent process.
                    // Assigned-but-never-started missions can be legitimately re-routed while still pending.
                    if (!mission.ProcessId.HasValue && !mission.StartedUtc.HasValue)
                    {
                        _Logging.Warn(_Header + "orphaned mission " + mission.Id +
                            " never started an agent process -- reverting to Pending instead of completing");
                        MissionStatusEnum observedStatus = mission.Status;
                        mission.Status = MissionStatusEnum.Pending;
                        mission.CaptainId = null;
                        mission.DockId = null;
                        mission.ProcessId = null;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { observedStatus }, token).ConfigureAwait(false);
                        return;
                    }

                    // Check if the mission's process exited (work may already be done)
                    bool processAlive = false;
                    if (mission.ProcessId.HasValue && OnIsProcessTracked != null && OnIsProcessTracked(mission.ProcessId.Value))
                    {
                        // Still running under its runtime's exit callback, which owns its outcome.
                        processAlive = true;
                    }
                    else if (mission.ProcessId.HasValue)
                    {
                        try
                        {
                            System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(mission.ProcessId.Value);
                            processAlive = !process.HasExited;
                        }
                        catch (ArgumentException)
                        {
                            // Process no longer exists
                        }
                    }

                    if (!processAlive)
                    {
                        // Process exited — check if there are commits on the branch (work was done)
                        Dock? dock = null;
                        if (!String.IsNullOrEmpty(mission.DockId))
                        {
                            dock = await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
                        }

                        if (mission.ProcessId.HasValue &&
                            !await _Database.Missions.TryClaimProcessExitAsync(mission.Id, mission.ProcessId.Value, token).ConfigureAwait(false))
                        {
                            _Logging.Debug(_Header + "orphaned mission " + mission.Id + " exit is already handled -- skipping");
                            return;
                        }

                        // A pi launch whose exit was never handled may have left its generated .pi/mcp.json in the dock
                        // (it is git-excluded and holds no token, but it must not linger), and its generated
                        // AGENTS.override.md, which pi would load ahead of the repository's AGENTS.md and the next mission
                        // generation would read back as the dock's own instructions.
                        if (captain.Runtime == AgentRuntimeEnum.Pi && dock != null && !String.IsNullOrEmpty(dock.WorktreePath))
                        {
                            if (PiProjectMcpConfig.RemoveGenerated(dock.WorktreePath))
                                _Logging.Info(_Header + "removed the generated .pi/mcp.json left in orphaned mission " + mission.Id + "'s dock");
                            if (RemoveGeneratedInstructionsFile(dock.WorktreePath, MissionPromptBuilder.GetInstructionsFileName(AgentRuntimeEnum.Pi.ToString())))
                                _Logging.Info(_Header + "removed the generated mission instructions left in orphaned mission " + mission.Id + "'s dock");
                        }

                        // A vanished process has no confirmed exit result or stage check. Preserve its work for
                        // operator recovery, rather than treating it as a clean exit and starting landing.
                        Mission? recovered = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                        if (recovered == null || recovered.Status != mission.Status || recovered.ProcessId.HasValue) return;
                        recovered.Status = MissionStatusEnum.WorkProduced;
                        recovered.FailureKind = MissionFailureKindEnum.Infra;
                        recovered.FailureReason = "Orphaned agent exit was not verified; stage checks and landing remain outstanding.";
                        recovered.CompletedUtc = null;
                        recovered.LastUpdateUtc = DateTime.UtcNow;
                        if (!await _Database.Missions.TryUpdateIfStatusAsync(recovered, new MissionStatusEnum[] { mission.Status }, token).ConfigureAwait(false)) return;

                        await EmitEventAsync("mission.orphan_recovered",
                            "Orphaned mission held as WorkProduced with unverified work: " + mission.Title + " (captain " + captain.Id + " had moved on)",
                            entityType: "mission", entityId: mission.Id,
                            captainId: captain.Id, missionId: mission.Id,
                            vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);
                    }
                }
            }
        }

        private static bool RemoveGeneratedInstructionsFile(string worktreePath, string fileName)
        {
            // Only a file Armada generated (it carries the mission instructions heading); never the repository's own.
            try
            {
                string path = System.IO.Path.Combine(worktreePath, fileName);
                if (!System.IO.File.Exists(path)) return false;
                if (!System.IO.File.ReadAllText(path).Contains("# Mission Instructions", StringComparison.Ordinal)) return false;
                System.IO.File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task RunHealthPhaseAsync(string phase, Func<Task> action, CancellationToken token)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "health check phase '" + phase + "' failed; continuing with the remaining phases: " + ex.ToString());
            }
        }

        private async Task<string> DescribePendingReasonAsync(Mission mission, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(mission.DependsOnMissionId))
            {
                Mission? dependency = await _Database.Missions.ReadAsync(mission.DependsOnMissionId, token).ConfigureAwait(false);
                if (dependency == null)
                    return "its upstream mission " + mission.DependsOnMissionId + " was not found";
                if (dependency.Status != MissionStatusEnum.Complete && dependency.Status != MissionStatusEnum.WorkProduced)
                    return "waiting on upstream mission " + dependency.Id + " which is " + dependency.Status
                        + (dependency.Status == MissionStatusEnum.Review ? " (awaiting your review/approval)" : "");
                return "upstream mission " + dependency.Id + " is " + dependency.Status + " but the branch/context handoff is not yet ready";
            }

            string? placementReason = _Missions.GetPlacementWaitReason(mission.Id);
            if (placementReason != null)
                return "its dock cannot be placed yet: " + placementReason;

            if (!String.IsNullOrEmpty(mission.RequestedCaptainId))
                return "no idle captain matches the requested captain " + mission.RequestedCaptainId;

            return "assignment preconditions are not yet met (captain routing, vessel path configuration, or a transient in-flight assignment)";
        }

        private async Task DispatchPendingMissionsAsync(CancellationToken token)
        {
            List<Mission> pendingMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false);
            if (pendingMissions.Count == 0)
            {
                _RetryDispatchNeeded = false;
                return;
            }

            if (_RetryDispatchNeeded)
            {
                _Logging.Debug(_Header + "retrying dispatch for pending missions that previously could not be assigned");
            }

            // Check for any idle captains with available capacity
            bool hasCapacity = await HasAvailableCapacityAsync(token).ConfigureAwait(false);
            if (!hasCapacity) return;

            bool anyFailed = false;

            foreach (Mission mission in pendingMissions)
            {
                hasCapacity = await HasAvailableCapacityAsync(token).ConfigureAwait(false);
                if (!hasCapacity) break;

                if (string.IsNullOrEmpty(mission.VesselId)) continue;

                // One mission that throws (a bad record, a provider error) is logged and retried next cycle; it must
                // not keep every mission behind it from being dispatched.
                try
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                    if (vessel == null) continue;

                    bool assigned = await _Missions.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                    if (!assigned)
                    {
                        string reason = await DescribePendingReasonAsync(mission, token).ConfigureAwait(false);
                        _Logging.Info(_Header + "pending mission " + mission.Id + " not yet assigned: " + reason + " - will retry on the next health check cycle");
                        anyFailed = true;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error dispatching pending mission " + mission.Id + ": " + ex.ToString() + " - will retry on the next health check cycle");
                    anyFailed = true;
                }
            }

            _RetryDispatchNeeded = anyFailed;
        }

        private async Task<bool> HasAvailableCapacityAsync(CancellationToken token)
        {
            // Global concurrency ceiling: refuse new work once the configured number of missions
            // is already running, as backpressure against unbounded captain execution.
            if (_Settings.MaxConcurrentMissions > 0)
            {
                List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);
                if (workingCaptains.Count >= _Settings.MaxConcurrentMissions) return false;
            }

            // Resource-pressure admission: defer rather than launch a captain onto a memory-starved host,
            // where it would be OOM-killed mid-mission and burn a redispatch. Disabled when the floor is 0,
            // and fails open when host memory cannot be measured.
            if (_Settings.MinAvailableMemoryBytesForLaunch > 0)
            {
                long available = _ResourceProbe.GetAvailablePhysicalBytes();
                long total = _ResourceProbe.GetTotalPhysicalBytes();
                AdmissionDecision admission = ResourceAdmission.Evaluate(available, total, _Settings.MinAvailableMemoryBytesForLaunch);
                if (!admission.Admit)
                {
                    System.Threading.Interlocked.Increment(ref _MemoryPressureDeferrals);
                    _Logging.Info(_Header + "capacity gate " + (admission.DeferReason ?? "deferred for memory pressure"));
                    return false;
                }
            }

            // Only idle captains have capacity for new assignments
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            return idleCaptains.Count > 0;
        }

        /// <summary>
        /// Reclaim the dock associated with a captain and/or mission.
        /// Clears CurrentDockId on the captain and DockId on the mission after reclaim.
        /// Handles gracefully if the dock or worktree is already gone.
        /// </summary>
        private async Task ReclaimDockAsync(Captain captain, Mission? mission, CancellationToken token,
            [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            string? dockId = captain.CurrentDockId;
            if (String.IsNullOrEmpty(dockId) && mission != null)
            {
                dockId = mission.DockId;
            }

            if (String.IsNullOrEmpty(dockId)) return;
            // A stalled captain may have valuable edits even after an earlier commit. Retain the entire dock,
            // including untracked files, rather than relying on the best-effort leftover-work commit path.
            if (mission?.FailureKind == MissionFailureKindEnum.StallRecoveryExhausted)
            {
                _Logging.Warn(_Header + "retaining dock " + dockId + " for stalled mission " + mission.Id + "; recover work before deleting the dock");
                return;
            }

            bool reclaimed = true;
            try
            {
                reclaimed = await _Docks.ReclaimAsync(dockId, token: token, caller: caller).ConfigureAwait(false);
                if (reclaimed) _Logging.Info(_Header + "reclaimed dock " + dockId + " for captain " + captain.Id);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error reclaiming dock " + dockId + " for captain " + captain.Id + ": " + ex.ToString());
            }

            // Clear dock references
            if (!String.IsNullOrEmpty(captain.CurrentDockId))
            {
                captain.CurrentDockId = null;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
            }

            // A refused reclaim means the dock still holds unlanded work (its lease); the mission keeps pointing at it.
            if (reclaimed && mission != null && !String.IsNullOrEmpty(mission.DockId))
            {
                // Conditional on the status this caller holds: a stale copy must never move the mission back.
                mission.DockId = null;
                if (!await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { mission.Status }, token).ConfigureAwait(false))
                    _Logging.Debug(_Header + "mission " + mission.Id + " changed status while its dock was reclaimed -- not overwriting it");
            }
        }

        /// <summary>
        /// Handle an interrupted (cancelled/vanished) captain process by re-dispatching its mission instead of
        /// terminally failing it. Used for negative exit codes (a stop/shutdown cancellation or a missing PID),
        /// which are not genuine agent failures. The dock and captain are reclaimed, the mission is reset to
        /// Pending for a fresh assignment, and the voyage is left running. Bounded by the caller against
        /// <see cref="ArmadaSettings.MaxNoOpRedispatchAttempts"/>.
        /// </summary>
        private bool WithinTransientOutageGrace(string missionId)
        {
            int graceMinutes = _Settings.TransientOutageGraceMinutes;
            if (graceMinutes <= 0) return false;
            DateTime started = _TransientOutageStarts.GetOrAdd(missionId, _ => DateTime.UtcNow);
            if (DateTime.UtcNow - started < TimeSpan.FromMinutes(graceMinutes)) return true;
            _TransientOutageStarts.TryRemove(missionId, out DateTime _);
            return false;
        }

        private TimeSpan TransientResumeDelay(int recoveryAttempts)
        {
            int baseSeconds = _Settings.TransientResumeDelaySeconds;
            if (baseSeconds <= 0) return TimeSpan.Zero;
            double seconds = Math.Min(60, baseSeconds * Math.Pow(2, Math.Max(0, recoveryAttempts)));
            return TimeSpan.FromSeconds(seconds * (0.75 + Random.Shared.NextDouble() * 0.5));
        }

        private async Task<bool> TryResumeInPlaceAsync(Captain captain, Mission mission, string failureReason, CancellationToken token, TimeSpan delay = default)
        {
            // One resume per mission at a time: two paths (a stage-check result and an exit observed by the health check)
            // reaching here for the same stop must not launch two agents into one dock.
            if (!_ResumingMissions.TryAdd(mission.Id, 0))
            {
                _Logging.Info(_Header + "mission " + mission.Id + " is already being resumed; not resuming again (" + failureReason + ")");
                return true;
            }

            try
            {
                return await ResumeInPlaceAsync(captain, mission, failureReason, token, delay).ConfigureAwait(false);
            }
            finally
            {
                _ResumingMissions.TryRemove(mission.Id, out _);
            }
        }

        private async Task<bool> ResumeInPlaceAsync(Captain captain, Mission mission, string failureReason, CancellationToken token, TimeSpan delay)
        {
            if (delay > TimeSpan.Zero)
            {
                // Back off before hitting the endpoint again. The captain keeps holding the mission meanwhile (its old
                // process is recorded as handled, so health checks do not relaunch it), and the resume stands down if the
                // mission was cancelled, stopped or reassigned during the wait.
                _Logging.Info(_Header + "mission " + mission.Id + " resumes in " + delay.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s after a transient failure");
                await Task.Delay(delay, token).ConfigureAwait(false);

                Mission? current = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                Captain? holder = await _Database.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false);
                if (current == null || holder == null
                    || (current.Status != MissionStatusEnum.InProgress && current.Status != MissionStatusEnum.Assigned)
                    || !String.Equals(holder.CurrentMissionId, mission.Id, StringComparison.Ordinal))
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " moved on while waiting to resume; not relaunching");
                    return true;
                }

                mission = current;
                captain = holder;
            }

            // A resume that finished just before this one already attached a new agent (a process other than the one this
            // call is replacing): the stop this call reports is handled, so launching again would put a second agent into
            // the dock.
            Mission? latest = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            if (latest == null || MissionStateMachine.IsTerminalOrPostWork(latest.Status)) return true;
            if (latest.ProcessId.HasValue && latest.ProcessId != mission.ProcessId)
            {
                _Logging.Info(_Header + "mission " + mission.Id + " already has agent process " + latest.ProcessId.Value + "; not resuming again (" + failureReason + ")");
                return true;
            }

            Dock? dock = String.IsNullOrEmpty(mission.DockId) ? null : await _Database.Docks.ReadAsync(mission.DockId!, token).ConfigureAwait(false);
            if (dock == null || String.IsNullOrEmpty(dock.WorktreePath)) return false;

            int attempt = captain.RecoveryAttempts + 1;
            string note = "\n\n## Earlier attempt " + attempt + " stopped\n" + failureReason.Trim()
                + "\nYour work so far is still in the working tree. Run git status and git diff to see it, keep what is right, and continue the mission from there. Do not start over."
                + " Every line in git diff was written by you during this mission: you may change or remove it, even where the instructions say not to change existing lines (that means lines that were there before the mission started)."
                + " Write files with plain content only, never with the line numbers that read_file shows.";
            // An outage ridden out over many resumes repeats one failure; record it once instead of once per attempt.
            if (!(mission.Description ?? String.Empty).Contains(failureReason.Trim(), StringComparison.Ordinal))
                mission.Description = (mission.Description ?? String.Empty) + note;
            DateTime resumeVersion = mission.LastUpdateUtc;
            mission.ProcessId = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, resumeVersion, new[] { MissionStatusEnum.InProgress, MissionStatusEnum.Assigned, MissionStatusEnum.Testing }, token).ConfigureAwait(false)) return true;

            try
            {
                await _Missions.RegenerateInstructionsAsync(mission, captain, dock, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is TimeoutException)
            {
                _Logging.Warn(_Header + "could not rewrite the instructions for mission " + mission.Id + " before resuming: " + ex.Message);
            }

            _Logging.Warn(_Header + "mission " + mission.Id + " stopped (" + failureReason + "); resuming captain " + captain.Id + " in the same dock (attempt " + attempt + "/" + _Settings.MaxRecoveryAttempts + ")");
            await EmitEventAsync("mission.resumed", "Mission resumed in place after: " + failureReason,
                entityType: "mission", entityId: mission.Id, captainId: captain.Id, missionId: mission.Id,
                vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);

            await _Captains.TryRecoverAsync(captain, token).ConfigureAwait(false);
            Mission? after = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            return after != null && after.Status != MissionStatusEnum.Failed;
        }

        private const string _EscalatedHeading = "## Escalated to ";

        private async Task<bool> TryEscalateToPlannerAsync(Captain captain, Mission mission, string failureReason, StageAttemptCapture capture, CancellationToken token)
        {
            // Planning and review stages are not escalated; only stages that do the work.
            if (PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge) || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect)
                || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.ProductManager) || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Recorder))
                return false;
            if (String.IsNullOrEmpty(mission.VoyageId) || (mission.Description ?? String.Empty).Contains(_EscalatedHeading, StringComparison.Ordinal)) return false;
            // A strict assignment fixes the captain on purpose (tests that control model identity), so it fails rather
            // than finishing on a different model.
            if (await _Missions.HasStrictCaptainAssignmentAsync(mission, token).ConfigureAwait(false)) return false;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId!, token).ConfigureAwait(false);
            Captain? plannerCaptain = await CaptainEscalationSelector.SelectAsync(_Database, mission, captain.Id, voyageMissions, token).ConfigureAwait(false);
            if (plannerCaptain == null) return false;

            // Keep the work so far on the branch before the dock goes.
            Dock? dock = String.IsNullOrEmpty(mission.DockId) ? null : await _Database.Docks.ReadAsync(mission.DockId!, token).ConfigureAwait(false);
            if (dock != null)
            {
                try
                {
                    await _Missions.CommitDockWorkAsync(mission, captain, dock, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is TimeoutException)
                {
                    _Logging.Warn(_Header + "could not commit mission " + mission.Id + "'s work before escalating: " + ex.Message);
                }

                // The escalation record compares this commit with the finished branch (kept or replaced).
                capture.EscalatedCommit = await TryReadDockHeadAsync(dock, token).ConfigureAwait(false);
            }

            MissionStatusEnum observedStatus = mission.Status;
            DateTime escalationVersion = mission.LastUpdateUtc;

            mission.Description = (mission.Description ?? String.Empty) + "\n\n" + _EscalatedHeading + plannerCaptain.Name + "\n"
                + captain.Name + " could not finish this mission (" + failureReason.Trim() + "). Its work so far is committed on this branch and may not build yet. "
                + "Review it with git log and git diff against the base branch, fix what is wrong, and finish the mission.";
            mission.RequestedCaptainId = plannerCaptain.Id;
            mission.Status = MissionStatusEnum.Pending;
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, escalationVersion, new MissionStatusEnum[] { observedStatus }, token).ConfigureAwait(false))
                return true;

            await ReclaimDockAsync(captain, mission, token).ConfigureAwait(false);
            await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);

            capture.EscalatedToCaptainId = plannerCaptain.Id;
            _Logging.Warn(_Header + "mission " + mission.Id + ": " + captain.Name + " could not finish after its resumes (" + failureReason + "); escalating to " + plannerCaptain.Name);
            await EmitEventAsync("mission.escalated", "Mission escalated to " + plannerCaptain.Name + " after " + captain.Name + " could not finish: " + mission.Title,
                entityType: "mission", entityId: mission.Id, captainId: plannerCaptain.Id, missionId: mission.Id,
                vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);
            return true;
        }

        private IHostCommandExecutor? ProvenanceCommandsFor(string? harborId)
        {
            DockHostResolver? hosts = (_Missions as MissionService)?.DockHosts;
            if (String.IsNullOrEmpty(harborId)) return hosts != null ? hosts.Local.Commands : new LocalHostCommandExecutor();
            if (hosts?.Harbors == null) return null;
            return hosts.ForHarbor(harborId!).Commands;
        }

        private async Task<string?> TryReadDockHeadAsync(Dock dock, CancellationToken token)
        {
            if (String.IsNullOrEmpty(dock.WorktreePath)) return null;
            DockHostResolver? hosts = (_Missions as MissionService)?.DockHosts;
            if (hosts == null) return null;
            try
            {
                return await hosts.ForDock(dock).Git.GetHeadCommitHashAsync(dock.WorktreePath!, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "could not read the head of dock " + dock.Id + ": " + ex.Message);
                return null;
            }
        }

        private async Task HandleInterruptedProcessExitAsync(
            Captain captain,
            Mission mission,
            string missionId,
            int exitCode,
            CancellationToken token)
        {
            Dock? interruptedDock = String.IsNullOrEmpty(mission.DockId) ? null : await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
            if (interruptedDock != null && interruptedDock.Active && !String.IsNullOrEmpty(interruptedDock.WorktreePath))
            {
                // Bounded: CaptainService.TryRecoverAsync counts the relaunch in the persisted RedispatchAttempts, and
                // every caller checks InterruptedRedispatchLimit before entering this path.
                await TryResumeInPlaceAsync(captain, mission, "The agent process ended without reporting an exit (exit " + exitCode + "), for example after a server or Harbor restart", token).ConfigureAwait(false);
                return;
            }

            // Reclaim the dock and release the captain before resetting the mission (ReclaimDockAsync keys off
            // the captain's/mission's current dock, so do it before clearing those references).
            MissionStatusEnum observedStatus = mission.Status;
            await ReclaimDockAsync(captain, mission, token).ConfigureAwait(false);
            await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);

            mission.RedispatchAttempts++;
            mission.Status = MissionStatusEnum.Pending;
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.TotalRuntimeMs = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            if (!await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { observedStatus }, token).ConfigureAwait(false))
            {
                _Logging.Warn(_Header + "mission " + missionId + " left " + observedStatus + " while its interrupted exit was handled -- not re-dispatching");
                return;
            }

            _Logging.Warn(_Header + "mission " + missionId + " interrupted (process exit code " + exitCode +
                ", treated as a stop/restart cancellation rather than a failure); re-dispatching (attempt " +
                mission.RedispatchAttempts + "/" + InterruptedRedispatchLimit + ") and leaving the voyage running");

            await EmitEventAsync("mission.redispatched",
                "Mission re-dispatched after interruption (exit code " + exitCode + "): " + mission.Title,
                entityType: "mission", entityId: mission.Id,
                captainId: captain.Id, missionId: mission.Id,
                vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);
        }

        private async Task HandleTerminalProcessExitFailureAsync(
            Captain captain,
            Mission? mission,
            string missionId,
            string failureReason,
            RuntimeFailureKindEnum runtimeFailureKind,
            CancellationToken token,
            MissionFailureKindEnum? missionFailureKind = null,
            bool quarantineCaptain = false,
            RuntimeProviderError? providerError = null)
        {
            bool missionFailed = false;
            if (mission != null)
            {
                MissionStatusEnum observedStatus = mission.Status;
                DateTime observedVersion = mission.LastUpdateUtc;
                mission.Status = MissionStatusEnum.Failed;
                mission.FailureReason = failureReason;
                // A transient endpoint failure that outlasted its resumes and the outage grace is the environment, not the
                // captain: Infra, which is left to a person instead of an autonomous rescue against the same endpoint.
                mission.FailureKind = missionFailureKind ?? (runtimeFailureKind == RuntimeFailureKindEnum.TaskFailure ? MissionFailureKindEnum.InvalidOutput
                    : runtimeFailureKind == RuntimeFailureKindEnum.Transient ? MissionFailureKindEnum.Infra : MissionFailureKindEnum.Crash);
                mission.ProcessId = null;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                missionFailed = await _Database.Missions.TryUpdateIfUnchangedAsync(mission, observedVersion, new MissionStatusEnum[] { observedStatus }, token).ConfigureAwait(false);
                if (!missionFailed)
                {
                    _Logging.Warn(_Header + "mission " + missionId + " left " + observedStatus + " before its failed process exit was recorded -- keeping the newer state");
                    await EmitEventAsync("mission.exit_handler_stale", "Ignored a stale failure result for " + missionId,
                        entityType: "mission", entityId: missionId, missionId: missionId, token: token).ConfigureAwait(false);
                    return;
                }
            }

            if (mission != null && missionFailed)
            {
                _Logging.Warn(_Header + "mission " + missionId + " marked failed after process exit");

                await EmitEventAsync("mission.failed", "Mission failed: " + mission.Title + " (" + failureReason + ")",
                    entityType: "mission", entityId: mission.Id,
                    captainId: captain.Id, missionId: mission.Id,
                    vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);

                if (!String.IsNullOrEmpty(mission.VoyageId))
                {
                    await HaltVoyageAsync(mission.VoyageId, mission.Id, failureReason, token).ConfigureAwait(false);
                }
            }

            await ReclaimDockAsync(captain, mission, token).ConfigureAwait(false);

            // Decide the captain's next state first and write it once. Releasing to Idle and then marking it Stalled or
            // Quarantined left a gap in which dispatch could hand the broken captain another mission.
            bool captainUnavailable = RuntimeFailureClassifier.IsCaptainUnavailable(runtimeFailureKind);
            if (quarantineCaptain)
            {
                await QuarantineCaptainAsync(captain.Id, runtimeFailureKind, token, providerError).ConfigureAwait(false);
            }
            else if (captainUnavailable)
            {
                captain.State = CaptainStateEnum.Stalled;
                captain.CurrentMissionId = null;
                captain.CurrentDockId = null;
                captain.ProcessId = null;
                captain.RecoveryAttempts = 0;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                _Logging.Warn(_Header + "captain " + captain.Id + " marked Stalled after non-retryable runtime failure on mission " + missionId);
            }
            else
            {
                await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                _Logging.Info(_Header + "captain " + captain.Id + " released to Idle after mission failure " + missionId);
            }

            if (_Escalation != null)
            {
                await _Escalation.FireAsync(EscalationTriggerEnum.RecoveryExhausted, captain.Id,
                    "Captain " + captain.Id + " failed mission " + missionId + " (" + failureReason + ")", token).ConfigureAwait(false);
            }

            string signalMessage = "Mission " + missionId + " failed: " + failureReason;
            if (captainUnavailable)
            {
                signalMessage += " (captain stalled)";
            }

            Signal signal = new Signal(SignalTypeEnum.Error, signalMessage);
            signal.FromCaptainId = captain.Id;
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);
        }

        private async Task HaltVoyageAsync(string voyageId, string failedMissionId, string failureReason, CancellationToken token)
        {
            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return;
            if (voyage.Status == VoyageStatusEnum.Cancelled || voyage.Status == VoyageStatusEnum.Complete) return;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);

            // A pipeline voyage (stages chained by dependency, Architect fan-out) halts only the failed stage's own
            // chain: sibling chains keep running, and the voyage settles as Failed once nothing is left active. A
            // voyage of independent missions halts as a whole, as before.
            bool pipelineVoyage = voyageMissions.Any(m => !String.IsNullOrEmpty(m.DependsOnMissionId));
            HashSet<string>? chain = null;
            if (pipelineVoyage)
            {
                chain = new HashSet<string>(StringComparer.Ordinal) { failedMissionId };
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (Mission candidate in voyageMissions)
                    {
                        if (chain.Contains(candidate.Id) || String.IsNullOrEmpty(candidate.DependsOnMissionId) || !chain.Contains(candidate.DependsOnMissionId!)) continue;
                        chain.Add(candidate.Id);
                        grew = true;
                    }
                }
            }
            else
            {
                voyage.Status = VoyageStatusEnum.Cancelled;
                voyage.CompletedUtc = DateTime.UtcNow;
                voyage.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);
            }

            foreach (Mission otherMission in voyageMissions)
            {
                if (otherMission.Id == failedMissionId) continue;
                if (chain != null && !chain.Contains(otherMission.Id)) continue;

                // Work waiting for a person's review is kept for that person to decide.
                if (otherMission.Status == MissionStatusEnum.Review) continue;

                bool isTerminal =
                    otherMission.Status == MissionStatusEnum.Complete ||
                    otherMission.Status == MissionStatusEnum.Failed ||
                    otherMission.Status == MissionStatusEnum.Cancelled ||
                    otherMission.Status == MissionStatusEnum.LandingFailed ||
                    otherMission.Status == MissionStatusEnum.PullRequestOpen ||
                    otherMission.Status == MissionStatusEnum.WorkProduced;

                if (isTerminal) continue;

                bool running = (otherMission.Status == MissionStatusEnum.InProgress || otherMission.Status == MissionStatusEnum.Assigned)
                    && !String.IsNullOrEmpty(otherMission.CaptainId);
                if (otherMission.Status == MissionStatusEnum.Blocked) continue;
                // A pipeline stage still waiting on the failed one is held as Blocked when that is configured.
                bool block = pipelineVoyage && _Settings.BlockDependentsOfFailedStages && otherMission.Status == MissionStatusEnum.Pending;
                otherMission.Status = block ? MissionStatusEnum.Blocked : MissionStatusEnum.Cancelled;
                otherMission.FailureReason = block
                    ? "Blocked by failed dependency " + failedMissionId + " (" + failureReason + "). Restart that mission to continue, or cancel the voyage."
                    : "Voyage halted after mission " + failedMissionId + " failed: " + failureReason;
                otherMission.FailureKind = MissionFailureKindEnum.DependencyFailed;
                otherMission.ProcessId = null;
                otherMission.CompletedUtc = block ? null : DateTime.UtcNow;
                otherMission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(otherMission, token).ConfigureAwait(false);

                // A cancelled mission whose captain is still running must actually stop: recall its captain through the
                // owner-aware stop path (which leaves the Cancelled status alone) instead of letting it run on unseen.
                if (running)
                {
                    try
                    {
                        Captain? holder = await _Database.Captains.ReadAsync(otherMission.CaptainId!, token).ConfigureAwait(false);
                        if (holder != null && String.Equals(holder.CurrentMissionId, otherMission.Id, StringComparison.Ordinal))
                            await _Captains.RecallAsync(holder.Id, holder.TenantId, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                    {
                        _Logging.Warn(_Header + "could not stop captain " + otherMission.CaptainId + " of halted mission " + otherMission.Id + ": " + ex.Message);
                    }
                }
            }

            if (pipelineVoyage)
            {
                List<Mission> after = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
                bool anyActive = after.Any(m => m.Status == MissionStatusEnum.Pending || m.Status == MissionStatusEnum.Blocked || m.Status == MissionStatusEnum.Assigned
                    || m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Verifying || m.Status == MissionStatusEnum.WorkProduced || m.Status == MissionStatusEnum.Review
                    || m.Status == MissionStatusEnum.PullRequestOpen || m.Status == MissionStatusEnum.Testing);
                if (!anyActive)
                {
                    voyage.Status = after.Any(m => m.Status == MissionStatusEnum.Complete) ? VoyageStatusEnum.Complete : VoyageStatusEnum.Failed;
                    if (after.Any(m => m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.LandingFailed)) voyage.Status = VoyageStatusEnum.Failed;
                    voyage.CompletedUtc = DateTime.UtcNow;
                    voyage.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);
                }

                await EmitEventAsync("voyage.stage_failed", "Pipeline stage " + failedMissionId + " failed; its following stages were cancelled",
                    entityType: "voyage", entityId: voyage.Id, missionId: failedMissionId, voyageId: voyage.Id, token: token).ConfigureAwait(false);
                return;
            }

            await EmitEventAsync("voyage.cancelled", "Voyage halted after mission " + failedMissionId + " failed",
                entityType: "voyage", entityId: voyage.Id, missionId: failedMissionId, voyageId: voyage.Id, token: token).ConfigureAwait(false);
        }

        private async Task MaintainCaptainPoolAsync(CancellationToken token)
        {
            List<Captain> allCaptains = await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false);
            int idleCount = allCaptains.Count(c => c.State == CaptainStateEnum.Idle);
            int totalCount = allCaptains.Count;

            int needed = _Settings.MinIdleCaptains - idleCount;
            if (needed <= 0) return;

            // Respect max captains cap
            if (_Settings.MaxCaptains > 0)
            {
                int headroom = _Settings.MaxCaptains - totalCount;
                if (headroom <= 0) return;
                needed = Math.Min(needed, headroom);
            }

            _Logging.Debug(_Header + "captain pool: " + idleCount + " idle, need " + needed + " more to reach minimum of " + _Settings.MinIdleCaptains);

            for (int i = 0; i < needed; i++)
            {
                // Name captains sequentially based on total count
                string name = "captain-" + (totalCount + i + 1);
                Captain captain = new Captain(name);
                captain.State = CaptainStateEnum.Idle;
                await _Database.Captains.CreateAsync(captain, token).ConfigureAwait(false);
                _Logging.Info(_Header + "auto-spawned captain " + captain.Id + " (" + name + ")");
            }
        }

        #endregion
    }
}
