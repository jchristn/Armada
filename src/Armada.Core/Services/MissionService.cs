namespace Armada.Core.Services
{
    using SyslogLogging;
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

        #endregion

        #region Private-Members

        private string _Header = "[MissionService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IDockService _Docks;
        private ICaptainService _Captains;

        /// <summary>
        /// Tracks in-flight mission complete handler operations by mission ID.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, Task> _InFlightCompletions = new System.Collections.Concurrent.ConcurrentDictionary<string, Task>();

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
        public MissionService(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            IDockService docks,
            ICaptainService captains)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
            _Captains = captains ?? throw new ArgumentNullException(nameof(captains));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> TryAssignAsync(Mission mission, Vessel vessel, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

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
            int concurrentCount = activeMissions.Count(m =>
                m.Status == MissionStatusEnum.InProgress ||
                m.Status == MissionStatusEnum.Assigned);

            if (IsBroadScope(mission) && concurrentCount > 0)
            {
                _Logging.Warn(_Header + "broad-scope mission " + mission.Id + " deferred — vessel " + vessel.Id + " has " + concurrentCount + " active mission(s)");
                return false;
            }

            // Warn about concurrent missions on same vessel
            if (concurrentCount > 0)
            {
                _Logging.Warn(_Header + "vessel " + vessel.Id + " already has " + concurrentCount + " active mission(s) — potential for conflicts");
            }

            // Find a captain with available capacity (idle or working with room for more)
            Captain? captain = await FindAvailableCaptainAsync(token).ConfigureAwait(false);
            if (captain == null)
            {
                _Logging.Warn(_Header + "no captains with available capacity for mission " + mission.Id);
                return false;
            }

            // Generate branch name
            string branchName = Constants.BranchPrefix + captain.Name.ToLowerInvariant() + "/" + mission.Id;
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
                dock = await _Docks.ProvisionAsync(vessel, captain, branchName, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "dock provisioning threw for mission " + mission.Id + " vessel " + vessel.Id + " captain " + captain.Id + ": " + ex.Message);

                // Revert mission to Pending
                mission.Status = MissionStatusEnum.Pending;
                mission.CaptainId = null;
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
                mission.BranchName = null;
                mission.DockId = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                return false;
            }

            // Track dock on the mission for per-mission dock tracking
            mission.DockId = dock.Id;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            // Update captain
            captain.State = CaptainStateEnum.Working;
            captain.CurrentMissionId = mission.Id;
            captain.CurrentDockId = dock.Id;
            captain.LastHeartbeatUtc = DateTime.UtcNow;
            captain.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);

            // Create assignment signal
            Signal signal = new Signal(SignalTypeEnum.Assignment, mission.Title);
            signal.ToCaptainId = captain.Id;
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

            // Generate mission CLAUDE.md into worktree
            await GenerateClaudeMdAsync(dock.WorktreePath!, mission, vessel, token).ConfigureAwait(false);

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
                    mission.BranchName = null;
                    mission.DockId = null;
                    mission.ProcessId = null;
                    mission.StartedUtc = null;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                    Signal errorSignal = new Signal(SignalTypeEnum.Error, "Failed to launch agent: " + ex.Message);
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
                mission.BranchName = null;
                mission.DockId = null;
                mission.ProcessId = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                return false;
            }

            _Logging.Info(_Header + "assigned mission " + mission.Id + " to captain " + captain.Id + " at " + dock.WorktreePath);
            return true;
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

            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null) return;

            // Mark mission complete
            mission.Status = MissionStatusEnum.Complete;
            mission.ProcessId = null;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Info(_Header + "mission " + mission.Id + " completed by captain " + captain.Id);

            // Emit mission.completed event for audit trail
            try
            {
                ArmadaEvent completedEvent = new ArmadaEvent("mission.completed", "Mission completed: " + mission.Title);
                completedEvent.EntityType = "mission";
                completedEvent.EntityId = mission.Id;
                completedEvent.CaptainId = captain.Id;
                completedEvent.MissionId = mission.Id;
                completedEvent.VesselId = mission.VesselId;
                completedEvent.VoyageId = mission.VoyageId;
                await _Database.Events.CreateAsync(completedEvent, token).ConfigureAwait(false);
            }
            catch (Exception evtEx)
            {
                _Logging.Warn(_Header + "error emitting mission.completed event for " + mission.Id + ": " + evtEx.Message);
            }

            // Get dock for push/PR (prefer mission-level DockId, fall back to captain-level)
            Dock? dock = null;
            string? dockId = mission.DockId ?? captain.CurrentDockId;
            if (!String.IsNullOrEmpty(dockId))
            {
                dock = await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            }

            // Capture diff synchronously before releasing the captain, to ensure the worktree is still available
            if (dock != null && OnCaptureDiff != null)
            {
                try
                {
                    await OnCaptureDiff.Invoke(mission, dock).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error capturing diff for mission " + mission.Id + ": " + ex.Message);
                }
            }

            // Invoke OnMissionComplete for push/PR handling (fire-and-forget to avoid blocking health check loop)
            if (dock != null && OnMissionComplete != null)
            {
                Func<Mission, Dock, Task> handler = OnMissionComplete;
                string completionMissionId = mission.Id;
                _Logging.Info(_Header + "queuing background merge/push for mission " + completionMissionId);

                Task completionTask = Task.Run(async () =>
                {
                    try
                    {
                        await handler.Invoke(mission, dock).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error in mission complete handler for " + completionMissionId + ": " + ex.Message);
                    }
                    finally
                    {
                        _InFlightCompletions.TryRemove(completionMissionId, out _);
                    }
                });

                _InFlightCompletions.TryAdd(completionMissionId, completionTask);
            }

            // Log completion signal
            Signal signal = new Signal(SignalTypeEnum.Completion, "Mission completed: " + mission.Title);
            signal.FromCaptainId = captain.Id;
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

            // Check if captain has remaining active missions
            List<Mission> activeMissions = await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false);
            int remainingActive = activeMissions.Count(m =>
                m.Status == MissionStatusEnum.InProgress ||
                m.Status == MissionStatusEnum.Assigned);

            if (remainingActive == 0)
            {
                // No more active missions — release the captain to idle
                await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);
            }
            else
            {
                // Captain still has active missions — update CurrentMissionId/CurrentDockId to another active one
                Mission? nextActive = activeMissions.FirstOrDefault(m =>
                    m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Assigned);
                if (nextActive != null)
                {
                    captain.CurrentMissionId = nextActive.Id;
                    captain.CurrentDockId = nextActive.DockId;
                    captain.ProcessId = nextActive.ProcessId;
                    captain.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                }

                _Logging.Info(_Header + "captain " + captain.Id + " still has " + remainingActive + " active mission(s)");
            }

            // Try to pick up next pending mission
            List<Mission> pendingMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false);
            if (pendingMissions.Any())
            {
                Mission nextMission = pendingMissions.OrderBy(m => m.Priority).ThenBy(m => m.CreatedUtc).First();
                if (!String.IsNullOrEmpty(nextMission.VesselId))
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(nextMission.VesselId, token).ConfigureAwait(false);
                    if (vessel != null)
                    {
                        await TryAssignAsync(nextMission, vessel, token).ConfigureAwait(false);
                    }
                }
            }
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
        public async Task GenerateClaudeMdAsync(string worktreePath, Mission mission, Vessel vessel, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            string claudeMdPath = Path.Combine(worktreePath, "CLAUDE.md");

            string content = "";

            if (!String.IsNullOrEmpty(vessel.ProjectContext))
            {
                content +=
                    "## Project Context\n" +
                    vessel.ProjectContext + "\n" +
                    "\n";
            }

            if (!String.IsNullOrEmpty(vessel.StyleGuide))
            {
                content +=
                    "## Code Style\n" +
                    vessel.StyleGuide + "\n" +
                    "\n";
            }

            content +=
                "# Mission Instructions\n" +
                "\n" +
                "You are an Armada captain executing a mission. Follow these instructions carefully.\n" +
                "\n" +
                "## Mission\n" +
                "- **Title:** " + mission.Title + "\n" +
                "- **ID:** " + mission.Id + "\n" +
                (mission.VoyageId != null ? "- **Voyage:** " + mission.VoyageId + "\n" : "") +
                "\n" +
                "## Description\n" +
                (mission.Description ?? "No additional description provided.") + "\n" +
                "\n" +
                "## Repository\n" +
                "- **Name:** " + vessel.Name + "\n" +
                "- **Branch:** " + (mission.BranchName ?? "unknown") + "\n" +
                "- **Default Branch:** " + vessel.DefaultBranch + "\n" +
                "\n" +
                "## Rules\n" +
                "- Work only within this worktree directory\n" +
                "- Commit all changes to the current branch\n" +
                "- Commit and push your changes — the Admiral will also push if needed\n" +
                "- If you encounter a blocking issue, commit what you have and exit\n" +
                "- Exit with code 0 on success\n" +
                "\n" +
                "## Progress Signals (Optional)\n" +
                "You can report progress to the Admiral by printing these lines to stdout:\n" +
                "- `[ARMADA:PROGRESS] 50` — report completion percentage (0-100)\n" +
                "- `[ARMADA:STATUS] Testing` — transition mission to Testing status\n" +
                "- `[ARMADA:STATUS] Review` — transition mission to Review status\n" +
                "- `[ARMADA:MESSAGE] your message here` — send a progress message\n";

            // If there's an existing CLAUDE.md, preserve it and prepend our instructions
            if (File.Exists(claudeMdPath))
            {
                string existing = await File.ReadAllTextAsync(claudeMdPath).ConfigureAwait(false);
                content += "\n## Existing Project Instructions\n\n" + existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(claudeMdPath)!);
            await File.WriteAllTextAsync(claudeMdPath, content).ConfigureAwait(false);

            _Logging.Info(_Header + "generated mission CLAUDE.md at " + claudeMdPath);
        }

        #endregion

        #region Private-Methods

        private async Task<Captain?> FindAvailableCaptainAsync(CancellationToken token)
        {
            // First check idle captains (always have capacity)
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            if (idleCaptains.Count > 0)
                return idleCaptains[0];

            // Then check working captains that have capacity (MaxParallelism > 1)
            List<Captain> workingCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);
            foreach (Captain captain in workingCaptains)
            {
                if (captain.MaxParallelism <= 1) continue;

                List<Mission> captainMissions = await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false);
                int activeCount = captainMissions.Count(m =>
                    m.Status == MissionStatusEnum.InProgress ||
                    m.Status == MissionStatusEnum.Assigned);

                if (activeCount < captain.MaxParallelism)
                {
                    _Logging.Info(_Header + "captain " + captain.Id + " has capacity (" + activeCount + "/" + captain.MaxParallelism + ")");
                    return captain;
                }
            }

            return null;
        }

        #endregion
    }
}
