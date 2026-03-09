namespace Armada.Core.Services
{
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
        public Func<Mission, Dock, Task>? OnMissionComplete
        {
            get => _Missions.OnMissionComplete;
            set => _Missions.OnMissionComplete = value;
        }

        #endregion

        #region Private-Members

        private string _Header = "[AdmiralService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private ICaptainService _Captains;
        private IMissionService _Missions;
        private IVoyageService _Voyages;
        private IEscalationService? _Escalation;

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
        /// <param name="escalation">Optional escalation service.</param>
        public AdmiralService(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            ICaptainService captains,
            IMissionService missions,
            IVoyageService voyages,
            IEscalationService? escalation = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Captains = captains ?? throw new ArgumentNullException(nameof(captains));
            _Missions = missions ?? throw new ArgumentNullException(nameof(missions));
            _Voyages = voyages ?? throw new ArgumentNullException(nameof(voyages));
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
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (missionDescriptions == null || missionDescriptions.Count == 0)
                throw new ArgumentException("At least one mission is required", nameof(missionDescriptions));

            // Verify vessel exists
            Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (vessel == null) throw new InvalidOperationException("Vessel not found: " + vesselId);

            // Create voyage
            Voyage voyage = new Voyage(title, description);
            voyage.Status = VoyageStatusEnum.Open;
            voyage = await _Database.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created voyage " + voyage.Id + ": " + title);

            // Create missions
            foreach (MissionDescription md in missionDescriptions)
            {
                Mission mission = new Mission(md.Title, md.Description);
                mission.VoyageId = voyage.Id;
                mission.VesselId = vesselId;
                mission = await _Database.Missions.CreateAsync(mission, token).ConfigureAwait(false);
                _Logging.Info(_Header + "created mission " + mission.Id + ": " + md.Title);

                // Try to auto-assign
                await _Missions.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
            }

            // Update voyage status — only transition to InProgress if at least one mission was assigned
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

            mission = await _Database.Missions.CreateAsync(mission, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created mission " + mission.Id + ": " + mission.Title);

            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel != null)
                {
                    await _Missions.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                }
            }

            return mission;
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
                VoyageProgress? progress = await _Voyages.GetProgressAsync(voyage.Id, token).ConfigureAwait(false);
                if (progress != null) status.Voyages.Add(progress);
            }

            // Recent signals
            status.RecentSignals = await _Database.Signals.EnumerateRecentAsync(10, token).ConfigureAwait(false);

            return status;
        }

        /// <inheritdoc />
        public async Task RecallCaptainAsync(string captainId, CancellationToken token = default)
        {
            await _Captains.RecallAsync(captainId, token).ConfigureAwait(false);
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
                    await _Captains.RecallAsync(captain.Id, token).ConfigureAwait(false);
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

            foreach (Captain captain in workingCaptains)
            {
                try
                {
                    await HealthCheckCaptainAsync(captain, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error processing health check for captain " + captain.Id + ": " + ex.Message);
                }
            }

            // Check for completed voyages
            await _Voyages.CheckCompletionsAsync(token).ConfigureAwait(false);

            // Dispatch pending missions to idle captains
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

        #endregion

        #region Private-Methods

        /// <summary>
        /// Process health check for a single captain. Isolated so exceptions in one captain
        /// do not prevent processing of other captains.
        /// </summary>
        private async Task HealthCheckCaptainAsync(Captain captain, CancellationToken token)
        {
            // Get all active missions for this captain (supports parallelism)
            List<Mission> captainMissions = await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false);

            // Fallback: include captain.CurrentMissionId if not already found via captain_id query
            if (!String.IsNullOrEmpty(captain.CurrentMissionId) && !captainMissions.Any(m => m.Id == captain.CurrentMissionId))
            {
                Mission? currentMission = await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
                if (currentMission != null) captainMissions.Add(currentMission);
            }

            List<Mission> activeMissions = captainMissions.Where(m =>
                m.Status == MissionStatusEnum.InProgress ||
                m.Status == MissionStatusEnum.Assigned).ToList();

            if (activeMissions.Count == 0 && captain.ProcessId == null)
            {
                // Orphaned captain — Working state but no missions and no process.
                // Release back to Idle so it can accept new work.
                _Logging.Warn(_Header + "captain " + captain.Id + " is Working but has no active missions or process — releasing to Idle");
                await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                return;
            }

            // If captain has a ProcessId but no active missions with processes,
            // use the captain-level process check (legacy single-mission behavior)
            if (activeMissions.Count == 0 && captain.ProcessId != null)
            {
                // First check if the captain's current mission is already in a terminal state.
                // This handles the case where a mission completed but the captain wasn't released
                // (e.g. server restart between mission completion and captain release).
                if (!String.IsNullOrEmpty(captain.CurrentMissionId))
                {
                    Mission? currentMission = await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
                    if (currentMission != null &&
                        (currentMission.Status == MissionStatusEnum.Complete ||
                         currentMission.Status == MissionStatusEnum.Failed ||
                         currentMission.Status == MissionStatusEnum.Cancelled))
                    {
                        _Logging.Warn(_Header + "captain " + captain.Id + " has stale ProcessId with terminal mission " + captain.CurrentMissionId + " (status: " + currentMission.Status + ") — releasing to Idle");
                        await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
                        return;
                    }
                }

                // Synthesize a check using captain.CurrentMissionId
                Mission syntheticMission = new Mission("legacy-check");
                syntheticMission.ProcessId = captain.ProcessId;
                if (!String.IsNullOrEmpty(captain.CurrentMissionId))
                    syntheticMission.Id = captain.CurrentMissionId;
                activeMissions.Add(syntheticMission);
            }

            // Check each active mission's process
            foreach (Mission mission in activeMissions)
            {
                int? missionProcessId = mission.ProcessId ?? (activeMissions.Count == 1 ? captain.ProcessId : null);
                if (missionProcessId == null) continue;

                bool isAlive = false;
                int exitCode = -1;
                try
                {
                    System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(missionProcessId.Value);
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
                    // Process no longer exists in process table — treat as clean exit
                    isAlive = false;
                    exitCode = 0;
                }

                if (!isAlive)
                {
                    if (exitCode == 0)
                    {
                        // Clean exit = mission complete
                        _Logging.Info(_Header + "captain " + captain.Id + " process " + missionProcessId + " completed successfully for mission " + mission.Id);

                        // Emit captain.completed event
                        await EmitEventAsync("captain.completed", "Captain " + captain.Id + " process " + missionProcessId + " exited successfully",
                            entityType: "captain", entityId: captain.Id,
                            captainId: captain.Id, missionId: mission.Id, token: token).ConfigureAwait(false);

                        await _Missions.HandleCompletionAsync(captain, mission.Id, token).ConfigureAwait(false);
                    }
                    else
                    {
                        _Logging.Warn(_Header + "captain " + captain.Id + " process " + missionProcessId + " exited with code " + exitCode + " for mission " + mission.Id);

                        // Emit captain.completed event (process exited, even if non-zero)
                        await EmitEventAsync("captain.completed", "Captain " + captain.Id + " process " + missionProcessId + " exited with code " + exitCode,
                            entityType: "captain", entityId: captain.Id,
                            captainId: captain.Id, missionId: mission.Id, token: token).ConfigureAwait(false);

                        // Attempt auto-recovery if under the limit
                        if (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts)
                        {
                            await _Captains.TryRecoverAsync(captain, token).ConfigureAwait(false);
                        }
                        else
                        {
                            // Mark this specific mission as Failed
                            mission.Status = MissionStatusEnum.Failed;
                            mission.ProcessId = null;
                            mission.CompletedUtc = DateTime.UtcNow;
                            mission.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                            _Logging.Warn(_Header + "mission " + mission.Id + " marked failed (captain recovery exhausted)");

                            // Emit mission.failed event
                            await EmitEventAsync("mission.failed", "Mission failed: " + mission.Title + " (captain recovery exhausted, exit code " + exitCode + ")",
                                entityType: "mission", entityId: mission.Id,
                                captainId: captain.Id, missionId: mission.Id, token: token).ConfigureAwait(false);

                            // Check if captain has any remaining active missions
                            List<Mission> remaining = (await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false))
                                .Where(m => m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Assigned).ToList();

                            if (remaining.Count == 0)
                            {
                                await _Database.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled, token).ConfigureAwait(false);
                            }

                            if (_Escalation != null)
                                await _Escalation.FireAsync(EscalationTriggerEnum.RecoveryExhausted, captain.Id, "Captain " + captain.Id + " recovery exhausted for mission " + mission.Id + " (exit code " + exitCode + ")", token).ConfigureAwait(false);

                            Signal signal = new Signal(SignalTypeEnum.Error, "Captain process exited with code " + exitCode + " for mission " + mission.Id + " (recovery exhausted)");
                            signal.FromCaptainId = captain.Id;
                            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);
                        }
                    }
                }
            }

            // For missions that are still alive, update heartbeat
            bool anyAlive = false;
            foreach (Mission mission in activeMissions)
            {
                int? missionProcessId = mission.ProcessId ?? (activeMissions.Count == 1 ? captain.ProcessId : null);
                if (missionProcessId == null) continue;

                try
                {
                    System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(missionProcessId.Value);
                    if (!process.HasExited) anyAlive = true;
                }
                catch (ArgumentException) { }
            }

            if (anyAlive)
            {
                await _Database.Captains.UpdateHeartbeatAsync(captain.Id, token).ConfigureAwait(false);

                // Check for stall (no heartbeat update for too long)
                if (captain.LastHeartbeatUtc.HasValue)
                {
                    TimeSpan elapsed = DateTime.UtcNow - captain.LastHeartbeatUtc.Value;
                    if (elapsed.TotalMinutes > _Settings.StallThresholdMinutes)
                    {
                        _Logging.Warn(_Header + "captain " + captain.Id + " appears stalled (" + elapsed.TotalMinutes.ToString("F1") + " min since last heartbeat)");

                        // Attempt auto-recovery if under the limit
                        if (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts && activeMissions.Count > 0)
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
                            await _Database.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled, token).ConfigureAwait(false);

                            // Mark all active missions as Failed
                            foreach (Mission stalledMission in activeMissions)
                            {
                                if (stalledMission.Status != MissionStatusEnum.Complete)
                                {
                                    stalledMission.Status = MissionStatusEnum.Failed;
                                    stalledMission.ProcessId = null;
                                    stalledMission.CompletedUtc = DateTime.UtcNow;
                                    stalledMission.LastUpdateUtc = DateTime.UtcNow;
                                    await _Database.Missions.UpdateAsync(stalledMission, token).ConfigureAwait(false);
                                    _Logging.Warn(_Header + "mission " + stalledMission.Id + " marked failed (captain stalled, recovery exhausted)");

                                    // Emit mission.failed event
                                    await EmitEventAsync("mission.failed", "Mission failed: " + stalledMission.Title + " (captain stalled, recovery exhausted)",
                                        entityType: "mission", entityId: stalledMission.Id,
                                        captainId: captain.Id, missionId: stalledMission.Id, token: token).ConfigureAwait(false);
                                }
                            }
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

        private async Task DispatchPendingMissionsAsync(CancellationToken token)
        {
            // Check for any captains with available capacity (idle or working with room)
            bool hasCapacity = await HasAvailableCapacityAsync(token).ConfigureAwait(false);
            if (!hasCapacity) return;

            List<Mission> pendingMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false);
            if (pendingMissions.Count == 0) return;

            foreach (Mission mission in pendingMissions)
            {
                hasCapacity = await HasAvailableCapacityAsync(token).ConfigureAwait(false);
                if (!hasCapacity) break;

                if (string.IsNullOrEmpty(mission.VesselId)) continue;

                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel == null) continue;

                await _Missions.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
            }
        }

        private async Task<bool> HasAvailableCapacityAsync(CancellationToken token)
        {
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            if (idleCaptains.Count > 0) return true;

            // Check working captains with MaxParallelism > 1
            List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);
            foreach (Captain captain in workingCaptains)
            {
                if (captain.MaxParallelism <= 1) continue;

                List<Mission> captainMissions = await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false);
                int activeCount = captainMissions.Count(m =>
                    m.Status == MissionStatusEnum.InProgress ||
                    m.Status == MissionStatusEnum.Assigned);

                if (activeCount < captain.MaxParallelism) return true;
            }

            return false;
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
