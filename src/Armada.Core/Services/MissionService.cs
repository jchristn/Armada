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

            // Find an idle captain
            Captain? captain = await FindAvailableCaptainAsync(token).ConfigureAwait(false);
            if (captain == null)
            {
                _Logging.Warn(_Header + "no idle captains available for mission " + mission.Id);
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
                dock = await _Docks.ProvisionAsync(vessel, captain, branchName, mission.Id, token).ConfigureAwait(false);
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

            // Atomically claim the captain — only succeeds if captain is still Idle.
            // This prevents a race where two concurrent TryAssignAsync calls both find
            // the same idle captain and overwrite each other's mission assignment.
            bool claimed = await _Database.Captains.TryClaimAsync(captain.Id, mission.Id, dock.Id, token).ConfigureAwait(false);
            if (!claimed)
            {
                _Logging.Warn(_Header + "captain " + captain.Id + " was claimed by another mission before we could assign " + mission.Id + " — reverting to Pending");

                mission.Status = MissionStatusEnum.Pending;
                mission.CaptainId = null;
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

            Mission? mission = !String.IsNullOrEmpty(captain.TenantId)
                ? await _Database.Missions.ReadAsync(captain.TenantId, missionId, token).ConfigureAwait(false)
                : await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null) return;

            // Mark mission as work produced (agent finished, landing not yet attempted)
            mission.Status = MissionStatusEnum.WorkProduced;
            mission.ProcessId = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Info(_Header + "mission " + mission.Id + " work produced by captain " + captain.Id);

            // Emit mission.work_produced event for audit trail
            try
            {
                ArmadaEvent workProducedEvent = new ArmadaEvent("mission.work_produced", "Work produced: " + mission.Title);
                workProducedEvent.TenantId = mission.TenantId;
                workProducedEvent.UserId = mission.UserId;
                workProducedEvent.EntityType = "mission";
                workProducedEvent.EntityId = mission.Id;
                workProducedEvent.CaptainId = captain.Id;
                workProducedEvent.MissionId = mission.Id;
                workProducedEvent.VesselId = mission.VesselId;
                workProducedEvent.VoyageId = mission.VoyageId;
                await _Database.Events.CreateAsync(workProducedEvent, token).ConfigureAwait(false);
            }
            catch (Exception evtEx)
            {
                _Logging.Warn(_Header + "error emitting mission.work_produced event for " + mission.Id + ": " + evtEx.Message);
            }

            // Get dock for push/PR (prefer mission-level DockId, fall back to captain-level)
            Dock? dock = null;
            string? dockId = mission.DockId ?? captain.CurrentDockId;
            if (!String.IsNullOrEmpty(dockId))
            {
                dock = !String.IsNullOrEmpty(mission.TenantId)
                    ? await _Database.Docks.ReadAsync(mission.TenantId, dockId, token).ConfigureAwait(false)
                    : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
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

            // Invoke OnMissionComplete synchronously (Phase A: push branch, create PR, or enqueue).
            // Captain stays in Working state until the handoff completes, preventing the captain
            // from being reassigned while git operations are still in progress.
            if (dock != null && OnMissionComplete != null)
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
            if (!String.IsNullOrEmpty(completionDockId))
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

            // Log work produced signal
            Signal signal = new Signal(SignalTypeEnum.Completion, "Work produced: " + mission.Title);
            signal.FromCaptainId = captain.Id;
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

            // Release the captain to idle only AFTER the handoff and dock reclaim are done
            await _Captains.ReleaseAsync(captain, token).ConfigureAwait(false);

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
        public async Task GenerateClaudeMdAsync(string worktreePath, Mission mission, Vessel vessel, Captain? captain = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            string claudeMdPath = Path.Combine(worktreePath, "CLAUDE.md");

            string content = "";

            if (captain != null && !String.IsNullOrEmpty(captain.SystemInstructions))
            {
                content +=
                    "## Captain Instructions\n" +
                    captain.SystemInstructions + "\n" +
                    "\n";
            }

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

            if (vessel.EnableModelContext && !String.IsNullOrEmpty(vessel.ModelContext))
            {
                content +=
                    "## Model Context\n" +
                    "The following context was accumulated by AI agents during previous missions on this repository. " +
                    "Use this information to work more effectively.\n" +
                    "\n" +
                    vessel.ModelContext + "\n" +
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
                "- Commit and push your changes -- the Admiral will also push if needed\n" +
                "- If you encounter a blocking issue, commit what you have and exit\n" +
                "- Exit with code 0 on success\n" +
                "- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages\n" +
                "- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text\n" +
                "\n" +
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
                "landing, resulting in a LandingFailed status and wasted work.\n" +
                "\n" +
                "## Progress Signals (Optional)\n" +
                "You can report progress to the Admiral by printing these lines to stdout:\n" +
                "- `[ARMADA:PROGRESS] 50` -- report completion percentage (0-100)\n" +
                "- `[ARMADA:STATUS] Testing` -- transition mission to Testing status\n" +
                "- `[ARMADA:STATUS] Review` -- transition mission to Review status\n" +
                "- `[ARMADA:MESSAGE] your message here` -- send a progress message\n";

            if (vessel.EnableModelContext)
            {
                content +=
                    "\n" +
                    "## Model Context Updates\n" +
                    "\n" +
                    "Model context accumulation is enabled for this vessel. Before you finish your mission, " +
                    "review the existing model context above (if any) and consider whether you have discovered " +
                    "key information that would help future agents work on this repository more effectively. " +
                    "Examples include: architectural insights, testing patterns, build quirks, common pitfalls, " +
                    "important dependencies, or performance considerations.\n" +
                    "\n" +
                    "If you have useful additions, call `armada_update_vessel_context` with the `modelContext` " +
                    "parameter set to the COMPLETE updated model context (not just your additions -- include " +
                    "the existing content with your additions merged in). Keep the context concise, factual, " +
                    "and focused on information that is not obvious from reading the code.\n" +
                    "\n" +
                    "If you have nothing to add, skip this step.\n";
            }

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
            // Only idle captains are eligible for assignment
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            if (idleCaptains.Count > 0)
                return idleCaptains[0];

            return null;
        }

        #endregion
    }
}
