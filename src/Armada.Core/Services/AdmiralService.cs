namespace Armada.Core.Services
{
    using System.IO;
    using System.Linq;
    using SyslogLogging;
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

        #endregion

        #region Private-Members

        private string _Header = "[AdmiralService] ";
        private static readonly TimeSpan _AssignedOrphanRecoveryGracePeriod = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _WorkProducedReleaseGracePeriod = TimeSpan.FromMinutes(2);
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private ICaptainService _Captains;
        private IMissionService _Missions;
        private IVoyageService _Voyages;
        private IDockService _Docks;
        private IPlaybookService _Playbooks;
        private IEscalationService? _Escalation;
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
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (missionDescriptions == null || missionDescriptions.Count == 0)
                throw new ArgumentException("At least one mission is required", nameof(missionDescriptions));

            // Verify vessel exists
            Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (vessel == null) throw new InvalidOperationException("Vessel not found: " + vesselId);
            if (selectedPlaybooks != null && selectedPlaybooks.Count > 0 && !String.IsNullOrEmpty(vessel.TenantId))
            {
                await _Playbooks.ResolveSelectionsAsync(vessel.TenantId, selectedPlaybooks, token).ConfigureAwait(false);
            }

            // Create voyage
            Voyage voyage = new Voyage(title, description);
            voyage.TenantId = vessel.TenantId;
            voyage.UserId = vessel.UserId;
            voyage.Status = VoyageStatusEnum.Open;
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
            return await DispatchVoyageAsync(title, description, vesselId, missionDescriptions, pipelineId, null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Voyage> DispatchVoyageAsync(
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missionDescriptions,
            string? pipelineId,
            List<SelectedPlaybook>? selectedPlaybooks,
            CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (missionDescriptions == null || missionDescriptions.Count == 0)
                throw new ArgumentException("At least one mission is required", nameof(missionDescriptions));

            // Verify vessel exists
            Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (vessel == null) throw new InvalidOperationException("Vessel not found: " + vesselId);
            if (selectedPlaybooks != null && selectedPlaybooks.Count > 0 && !String.IsNullOrEmpty(vessel.TenantId))
            {
                await _Playbooks.ResolveSelectionsAsync(vessel.TenantId, selectedPlaybooks, token).ConfigureAwait(false);
            }

            // Resolve pipeline: explicit > vessel default > fleet default > WorkerOnly
            Pipeline? pipeline = await ResolvePipelineAsync(pipelineId, vessel, token).ConfigureAwait(false);

            // If pipeline is single-stage Worker (or null), use the standard dispatch path
            if (pipeline == null || (pipeline.Stages.Count == 1 && pipeline.Stages[0].PersonaName == "Worker"))
            {
                return await DispatchVoyageAsync(title, description, vesselId, missionDescriptions, selectedPlaybooks, token).ConfigureAwait(false);
            }

            // Multi-stage pipeline: create voyage, then for each mission create a chain of persona stages
            Voyage voyage = new Voyage(title, description);
            voyage.TenantId = vessel.TenantId;
            voyage.UserId = vessel.UserId;
            voyage.Status = VoyageStatusEnum.Open;
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
                    Mission mission = new Mission(
                        "[" + stage.PersonaName + "] " + baseTitle,
                        md.Description);
                    mission.TenantId = vessel.TenantId;
                    mission.UserId = vessel.UserId;
                    mission.VoyageId = voyage.Id;
                    mission.VesselId = vesselId;
                    mission.Persona = stage.PersonaName;
                    mission.DependsOnMissionId = previousMissionId;

                    // Only the first stage starts as Pending; dependent stages also start as Pending
                    // but won't be assigned until their dependency completes
                    mission = await _Database.Missions.CreateAsync(mission, token).ConfigureAwait(false);
                    await PersistMissionPlaybooksAsync(mission, voyage.SelectedPlaybooks, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "created pipeline mission " + mission.Id + ": " + mission.Title +
                        " (stage " + stage.Order + "/" + pipeline.Stages.Count + ", persona: " + stage.PersonaName +
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
            if (mission.SelectedPlaybooks != null && mission.SelectedPlaybooks.Count > 0 && !String.IsNullOrEmpty(mission.TenantId))
            {
                await _Playbooks.ResolveSelectionsAsync(mission.TenantId, mission.SelectedPlaybooks, token).ConfigureAwait(false);
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
            ArmadaStatus status = new ArmadaStatus();

            // Captain counts
            List<Captain> allCaptains = await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false);
            status.TotalCaptains = allCaptains.Count;
            status.IdleCaptains = allCaptains.Count(c => c.State == CaptainStateEnum.Idle);
            status.WorkingCaptains = allCaptains.Count(c => c.State == CaptainStateEnum.Working);
            status.StalledCaptains = allCaptains.Count(c => c.State == CaptainStateEnum.Stalled);

            // Mission counts by status
            List<Mission> allMissions = await _Database.Missions.EnumerateAsync(token).ConfigureAwait(false);
            foreach (MissionStatusEnum missionStatus in Enum.GetValues<MissionStatusEnum>())
            {
                int count = allMissions.Count(m => m.Status == missionStatus);
                if (count > 0) status.MissionsByStatus[missionStatus.ToString()] = count;
            }

            // Active voyages
            List<Voyage> activeVoyages = await _Database.Voyages.EnumerateByStatusAsync(VoyageStatusEnum.InProgress, token).ConfigureAwait(false);
            List<Voyage> openVoyages = await _Database.Voyages.EnumerateByStatusAsync(VoyageStatusEnum.Open, token).ConfigureAwait(false);
            status.ActiveVoyages = activeVoyages.Count + openVoyages.Count;

            foreach (Voyage voyage in activeVoyages.Concat(openVoyages))
            {
                VoyageProgress? progress = await _Voyages.GetProgressAsync(voyage.Id, token: token).ConfigureAwait(false);
                if (progress != null) status.Voyages.Add(progress);
            }

            // Recent signals
            status.RecentSignals = await _Database.Signals.EnumerateRecentAsync(10, token).ConfigureAwait(false);

            if (OnGetRemoteTunnelStatus != null)
            {
                status.RemoteTunnel = OnGetRemoteTunnelStatus();
            }

            return status;
        }

        /// <inheritdoc />
        public async Task RecallCaptainAsync(string captainId, CancellationToken token = default)
        {
            await _Captains.RecallAsync(captainId, token: token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RecallAllAsync(CancellationToken token = default)
        {
            _Logging.Warn(_Header + "RECALLING ALL CAPTAINS");

            List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);

            foreach (Captain captain in workingCaptains)
            {
                try
                {
                    await _Captains.RecallAsync(captain.Id, token: token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error recalling captain " + captain.Id + ": " + ex.Message);
                }
            }
        }

        /// <inheritdoc />
        public async Task HealthCheckAsync(CancellationToken token = default)
        {
            List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);

            if (workingCaptains.Count > 0)
            {
                _Logging.Info(_Header + "starting parallel health checks for " + workingCaptains.Count + " working captain(s)");

                List<Task> healthCheckTasks = workingCaptains.Select(captain =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            await HealthCheckCaptainAsync(captain, token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _Logging.Warn(_Header + "error processing health check for captain " + captain.Id + ": " + ex.Message);
                        }
                    }, token)).ToList();

                await Task.WhenAll(healthCheckTasks).ConfigureAwait(false);

                _Logging.Info(_Header + "completed parallel health checks for " + workingCaptains.Count + " working captain(s)");
            }

            // Safety net: detect orphaned InProgress missions whose captain has moved on.
            // This catches any mission that was left InProgress due to a captain being
            // reassigned before the health check could detect the old process exit.
            await RecoverOrphanedMissionsAsync(token).ConfigureAwait(false);

            // Check for completed voyages
            List<Voyage> completedVoyages = await _Voyages.CheckCompletionsAsync(token).ConfigureAwait(false);
            if (OnVoyageComplete != null)
            {
                foreach (Voyage completedVoyage in completedVoyages)
                {
                    try { await OnVoyageComplete.Invoke(completedVoyage).ConfigureAwait(false); }
                    catch (Exception ex) { _Logging.Warn(_Header + "error in OnVoyageComplete callback: " + ex.Message); }
                }
            }

            // Reconcile PullRequestOpen missions — check if their PRs have been merged
            await ReconcilePullRequestMissionsAsync(token).ConfigureAwait(false);

            // Reclaim docks stuck in Provisioned state with no active captain
            await ReclaimOrphanedDocksAsync(token).ConfigureAwait(false);

            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);

            // Captain pool management: auto-spawn if below minimum idle count
            if (_Settings.MinIdleCaptains > 0)
            {
                await MaintainCaptainPoolAsync(token).ConfigureAwait(false);
            }

            // Evaluate escalation rules
            if (_Escalation != null)
            {
                await _Escalation.EvaluateAsync(token).ConfigureAwait(false);
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
                bool processAlive = false;

                if (captain.ProcessId != null)
                {
                    try
                    {
                        System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(captain.ProcessId.Value);
                        processAlive = !process.HasExited;
                    }
                    catch (ArgumentException)
                    {
                        // Process no longer exists
                    }
                }

                if (processAlive) continue;

                // Reset active missions back to Pending for re-dispatch
                List<Mission> captainMissions = await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false);
                List<Mission> activeMissions = captainMissions.Where(m =>
                    m.Status == MissionStatusEnum.InProgress ||
                    m.Status == MissionStatusEnum.Assigned).ToList();

                foreach (Mission mission in activeMissions)
                {
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
        public async Task HandleProcessExitAsync(int processId, int? exitCode, string captainId, string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

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

            // If the mission is already in a terminal state, nothing to do — the health check
            // or another handler already processed this completion/failure.
            if (mission.Status == MissionStatusEnum.Complete ||
                mission.Status == MissionStatusEnum.Failed ||
                mission.Status == MissionStatusEnum.Cancelled ||
                mission.Status == MissionStatusEnum.WorkProduced ||
                mission.Status == MissionStatusEnum.LandingFailed ||
                mission.Status == MissionStatusEnum.PullRequestOpen)
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

            await EmitEventAsync("captain.process_exited",
                "Agent process " + processId + " exited with code " + (exitCode?.ToString() ?? "unknown") + " for captain " + captain.Name,
                entityType: "captain", entityId: captain.Id,
                captainId: captain.Id, missionId: missionId,
                vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);

            if (exitCode == 0)
            {
                // Clean exit — delegate to the normal completion flow
                _Logging.Info(_Header + "agent process " + processId + " exited cleanly for mission " + missionId + " — handling completion");
                await _Missions.HandleCompletionAsync(captain, missionId, token).ConfigureAwait(false);
            }
            else
            {
                // Non-zero or unknown exit code — fail the mission deterministically.
                // Process exits should not bounce between recovery paths; preserve the
                // captured runtime error, halt the voyage, and only stall the captain
                // when the failure indicates the runtime itself is unavailable.
                _Logging.Warn(_Header + "agent process " + processId + " exited with code " + (exitCode?.ToString() ?? "unknown") + " for mission " + missionId);
                string failureReason = await BuildProcessExitFailureReasonAsync(missionId, exitCode, token).ConfigureAwait(false);
                await HandleTerminalProcessExitFailureAsync(captain, mission, missionId, failureReason, token).ConfigureAwait(false);
            }

            // Try to dispatch any pending missions now that capacity may have freed up
            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Resolve which pipeline to use for a dispatch.
        /// Resolution order: explicit pipelineId > vessel default > fleet default > null (WorkerOnly).
        /// </summary>
        private async Task<Pipeline?> ResolvePipelineAsync(string? pipelineId, Vessel vessel, CancellationToken token)
        {
            // Explicit pipeline ID takes priority
            if (!String.IsNullOrEmpty(pipelineId))
            {
                Pipeline? explicit_ = await _Database.Pipelines.ReadAsync(pipelineId, token).ConfigureAwait(false);
                if (explicit_ != null) return explicit_;

                // Try by name if not found by ID
                explicit_ = await _Database.Pipelines.ReadByNameAsync(pipelineId, token).ConfigureAwait(false);
                if (explicit_ != null) return explicit_;
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
            // Look up the captain's single active mission
            Mission? mission = null;
            if (!String.IsNullOrEmpty(captain.CurrentMissionId))
            {
                mission = await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
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
                (mission.Status == MissionStatusEnum.Complete ||
                 mission.Status == MissionStatusEnum.Failed ||
                 mission.Status == MissionStatusEnum.Cancelled ||
                 mission.Status == MissionStatusEnum.WorkProduced ||
                 mission.Status == MissionStatusEnum.LandingFailed ||
                 mission.Status == MissionStatusEnum.PullRequestOpen))
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
            if (processId == null) return;

            bool isAlive = false;
            int exitCode = -1;
            try
            {
                System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId.Value);
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
                // Process no longer exists in process table.
                // Check if the process exit callback already fired — if so, the async
                // completion handler is in progress and we should not race it with recovery.
                if (OnIsProcessExitHandled != null && OnIsProcessExitHandled(processId.Value))
                {
                    _Logging.Debug(_Header + "captain " + captain.Id + " process " + processId +
                        " no longer exists but exit callback already fired — skipping health check to avoid race");
                    return;
                }

                // A missing PID could mean the agent crashed, was killed externally, or the OS
                // recycled the PID. Only a confirmed clean exit should be treated as success.
                isAlive = false;
                exitCode = -1;
            }

            string missionId = captain.CurrentMissionId ?? "unknown";

            if (!isAlive)
            {
                if (exitCode == 0)
                {
                    // Clean exit = mission complete
                    _Logging.Info(_Header + "captain " + captain.Id + " process " + processId + " completed successfully for mission " + missionId);

                    // Emit captain.completed event
                    await EmitEventAsync("captain.completed", "Captain " + captain.Id + " process " + processId + " exited successfully",
                        entityType: "captain", entityId: captain.Id,
                        captainId: captain.Id, missionId: missionId, token: token).ConfigureAwait(false);

                    await _Missions.HandleCompletionAsync(captain, missionId, token).ConfigureAwait(false);
                }
                else
                {
                    _Logging.Warn(_Header + "captain " + captain.Id + " process " + processId + " exited with code " + exitCode + " for mission " + missionId);

                    // Emit captain.completed event (process exited, even if non-zero)
                    await EmitEventAsync("captain.completed", "Captain " + captain.Id + " process " + processId + " exited with code " + exitCode,
                        entityType: "captain", entityId: captain.Id,
                        captainId: captain.Id, missionId: missionId, token: token).ConfigureAwait(false);

                    string failureReason = await BuildProcessExitFailureReasonAsync(missionId, exitCode, token).ConfigureAwait(false);
                    await HandleTerminalProcessExitFailureAsync(captain, mission, missionId, failureReason, token).ConfigureAwait(false);
                }
            }
            else
            {
                // Process is alive — do NOT update heartbeat here.
                // Heartbeat should only be updated when the agent produces actual output
                // (handled by HandleAgentOutput in ArmadaServer). Updating it here from the
                // health check would mask stalled agents that are technically running but
                // producing no output.

                // Check for stall (no output for too long)
                if (captain.LastHeartbeatUtc.HasValue)
                {
                    TimeSpan elapsed = DateTime.UtcNow - captain.LastHeartbeatUtc.Value;
                    if (elapsed.TotalMinutes > _Settings.StallThresholdMinutes)
                    {
                        _Logging.Warn(_Header + "captain " + captain.Id + " appears stalled (" + elapsed.TotalMinutes.ToString("F1") + " min since last heartbeat)");

                        // Attempt auto-recovery if under the limit
                        if (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts)
                        {
                            // Kill the stalled process first
                            if (_Captains.OnStopAgent != null)
                            {
                                try { await _Captains.OnStopAgent.Invoke(captain).ConfigureAwait(false); }
                                catch { }
                            }

                            await _Captains.TryRecoverAsync(captain, token).ConfigureAwait(false);
                        }
                        else
                        {
                            // Mark the active mission as Failed
                            if (mission != null &&
                                mission.Status != MissionStatusEnum.Complete &&
                                mission.Status != MissionStatusEnum.WorkProduced &&
                                mission.Status != MissionStatusEnum.LandingFailed &&
                                mission.Status != MissionStatusEnum.PullRequestOpen)
                            {
                                mission.Status = MissionStatusEnum.Failed;
                                mission.FailureReason = "Captain stalled, recovery exhausted";
                                mission.ProcessId = null;
                                mission.CompletedUtc = DateTime.UtcNow;
                                mission.LastUpdateUtc = DateTime.UtcNow;
                                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                                _Logging.Warn(_Header + "mission " + mission.Id + " marked failed (captain stalled, recovery exhausted)");

                                // Emit mission.failed event
                                await EmitEventAsync("mission.failed", "Mission failed: " + mission.Title + " (captain stalled, recovery exhausted)",
                                    entityType: "mission", entityId: mission.Id,
                                    captainId: captain.Id, missionId: mission.Id, token: token).ConfigureAwait(false);
                            }

                            // Kill the stalled process
                            if (_Captains.OnStopAgent != null)
                            {
                                try { await _Captains.OnStopAgent.Invoke(captain).ConfigureAwait(false); }
                                catch { }
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
        /// Emit an event to the event log database.
        /// </summary>
        private async Task EmitEventAsync(string eventType, string message,
            string? entityType = null, string? entityId = null,
            string? captainId = null, string? missionId = null,
            string? vesselId = null, string? voyageId = null,
            CancellationToken token = default)
        {
            try
            {
                ArmadaEvent evt = new ArmadaEvent(eventType, message);
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
                _Logging.Warn(_Header + "error emitting event " + eventType + ": " + ex.Message);
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
                        // Check if a Pending mission is preserving this dock for re-dispatch
                        List<Mission> pendingMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false);
                        bool preservedForMission = pendingMissions.Any(m => m.DockId == dock.Id);
                        if (preservedForMission)
                        {
                            _Logging.Info(_Header + "skipping reclaim of dock " + dock.Id + " -- preserved for a pending mission re-dispatch");
                            continue;
                        }

                        _Logging.Info(_Header + "reclaiming orphaned dock " + dock.Id + " (created " + dock.CreatedUtc + ", no active captain)");
                        try
                        {
                            await _Docks.ReclaimAsync(dock.Id, token: token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _Logging.Warn(_Header + "error reclaiming orphaned dock " + dock.Id + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error in orphaned dock reclamation: " + ex.Message);
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
                        _Logging.Warn(_Header + "error reconciling PR for mission " + mission.Id + ": " + ex.Message);
                    }
                }

                if (checked_count > 0)
                    _Logging.Info(_Header + "reconciled " + checked_count + " PullRequestOpen mission(s)");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error in PR reconciliation: " + ex.Message);
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
                if (String.IsNullOrEmpty(mission.CaptainId)) continue;

                Captain? captain = await _Database.Captains.ReadAsync(mission.CaptainId, token).ConfigureAwait(false);
                if (captain == null) continue;

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
                            continue;
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
                        mission.Status = MissionStatusEnum.Pending;
                        mission.CaptainId = null;
                        mission.DockId = null;
                        mission.ProcessId = null;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                        continue;
                    }

                    // Check if the mission's process exited (work may already be done)
                    bool processAlive = false;
                    if (mission.ProcessId.HasValue)
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

                        // Complete the mission — the agent finished its work but the status was never updated
                        _Logging.Info(_Header + "completing orphaned mission " + mission.Id + " (agent process exited, captain moved on)");
                        await _Missions.HandleCompletionAsync(captain, mission.Id, token).ConfigureAwait(false);

                        await EmitEventAsync("mission.orphan_recovered",
                            "Orphaned mission recovered: " + mission.Title + " (captain " + captain.Id + " had moved on)",
                            entityType: "mission", entityId: mission.Id,
                            captainId: captain.Id, missionId: mission.Id,
                            vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);
                    }
                }
            }
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
                _Logging.Info(_Header + "retrying dispatch for pending missions that previously could not be assigned");
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

                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel == null) continue;

                bool assigned = await _Missions.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                if (!assigned)
                {
                    _Logging.Warn(_Header + "could not assign pending mission " + mission.Id + " - will retry on next health check cycle");
                    anyFailed = true;
                }
            }

            _RetryDispatchNeeded = anyFailed;
        }

        private async Task<bool> HasAvailableCapacityAsync(CancellationToken token)
        {
            // Only idle captains have capacity for new assignments
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            return idleCaptains.Count > 0;
        }

        /// <summary>
        /// Reclaim the dock associated with a captain and/or mission.
        /// Clears CurrentDockId on the captain and DockId on the mission after reclaim.
        /// Handles gracefully if the dock or worktree is already gone.
        /// </summary>
        private async Task ReclaimDockAsync(Captain captain, Mission? mission, CancellationToken token)
        {
            string? dockId = captain.CurrentDockId;
            if (String.IsNullOrEmpty(dockId) && mission != null)
            {
                dockId = mission.DockId;
            }

            if (String.IsNullOrEmpty(dockId)) return;

            try
            {
                await _Docks.ReclaimAsync(dockId, token: token).ConfigureAwait(false);
                _Logging.Info(_Header + "reclaimed dock " + dockId + " for captain " + captain.Id);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error reclaiming dock " + dockId + " for captain " + captain.Id + ": " + ex.Message);
            }

            // Clear dock references
            if (!String.IsNullOrEmpty(captain.CurrentDockId))
            {
                captain.CurrentDockId = null;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
            }

            if (mission != null && !String.IsNullOrEmpty(mission.DockId))
            {
                mission.DockId = null;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            }
        }

        private async Task<string> BuildProcessExitFailureReasonAsync(string missionId, int? exitCode, CancellationToken token)
        {
            string logPath = Path.Combine(_Settings.LogDirectory, "missions", missionId + ".log");
            if (File.Exists(logPath))
            {
                try
                {
                    string[] lines = await File.ReadAllLinesAsync(logPath, token).ConfigureAwait(false);
                    for (int i = lines.Length - 1; i >= 0 && i >= lines.Length - 40; i--)
                    {
                        string line = lines[i].Trim();
                        if (String.IsNullOrEmpty(line)) continue;
                        if (line.Contains("API Error:", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("overloaded_error", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("[stderr]", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("exception", StringComparison.OrdinalIgnoreCase))
                        {
                            return NormalizeProcessExitFailureReason(line);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "could not inspect mission log for failure reason on " + missionId + ": " + ex.Message);
                }
            }

            return "Agent process exited with code " + (exitCode?.ToString() ?? "unknown");
        }

        private async Task HandleTerminalProcessExitFailureAsync(
            Captain captain,
            Mission? mission,
            string missionId,
            string failureReason,
            CancellationToken token)
        {
            if (mission != null)
            {
                mission.Status = MissionStatusEnum.Failed;
                mission.FailureReason = failureReason;
                mission.ProcessId = null;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
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
            await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);

            if (IsCaptainUnavailableFailureReason(failureReason))
            {
                await _Database.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled, token).ConfigureAwait(false);
                _Logging.Warn(_Header + "captain " + captain.Id + " marked Stalled after non-retryable runtime failure on mission " + missionId);
            }
            else
            {
                _Logging.Info(_Header + "captain " + captain.Id + " released to Idle after mission failure " + missionId);
            }

            if (_Escalation != null)
            {
                await _Escalation.FireAsync(EscalationTriggerEnum.RecoveryExhausted, captain.Id,
                    "Captain " + captain.Id + " failed mission " + missionId + " (" + failureReason + ")", token).ConfigureAwait(false);
            }

            string signalMessage = "Mission " + missionId + " failed: " + failureReason;
            if (IsCaptainUnavailableFailureReason(failureReason))
            {
                signalMessage += " (captain stalled)";
            }

            Signal signal = new Signal(SignalTypeEnum.Error, signalMessage);
            signal.FromCaptainId = captain.Id;
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);
        }

        private static bool IsCaptainUnavailableFailureReason(string failureReason)
        {
            if (String.IsNullOrWhiteSpace(failureReason))
                return false;

            string normalized = NormalizeProcessExitFailureReason(failureReason);

            if (normalized.Contains("hit your limit", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("invalid api key", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("authentication failed", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("login required", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool mentionsModel = normalized.Contains("model", StringComparison.OrdinalIgnoreCase);
            bool modelUnavailable =
                normalized.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("unavailable", StringComparison.OrdinalIgnoreCase);

            return mentionsModel && modelUnavailable;
        }

        private static string NormalizeProcessExitFailureReason(string failureReason)
        {
            if (String.IsNullOrWhiteSpace(failureReason))
                return String.Empty;

            string normalized = failureReason.Trim();
            if (normalized.StartsWith("[stderr]", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("[stderr]".Length).Trim();
            }

            return normalized;
        }

        private async Task HaltVoyageAsync(string voyageId, string failedMissionId, string failureReason, CancellationToken token)
        {
            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return;
            if (voyage.Status == VoyageStatusEnum.Cancelled || voyage.Status == VoyageStatusEnum.Complete) return;

            voyage.Status = VoyageStatusEnum.Cancelled;
            voyage.CompletedUtc = DateTime.UtcNow;
            voyage.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            foreach (Mission otherMission in voyageMissions)
            {
                if (otherMission.Id == failedMissionId) continue;

                bool isTerminal =
                    otherMission.Status == MissionStatusEnum.Complete ||
                    otherMission.Status == MissionStatusEnum.Failed ||
                    otherMission.Status == MissionStatusEnum.Cancelled ||
                    otherMission.Status == MissionStatusEnum.LandingFailed ||
                    otherMission.Status == MissionStatusEnum.PullRequestOpen ||
                    otherMission.Status == MissionStatusEnum.WorkProduced;

                if (isTerminal) continue;

                otherMission.Status = MissionStatusEnum.Cancelled;
                otherMission.FailureReason = "Voyage halted after mission " + failedMissionId + " failed: " + failureReason;
                otherMission.ProcessId = null;
                otherMission.CompletedUtc = DateTime.UtcNow;
                otherMission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(otherMission, token).ConfigureAwait(false);
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

            _Logging.Info(_Header + "captain pool: " + idleCount + " idle, need " + needed + " more to reach minimum of " + _Settings.MinIdleCaptains);

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
