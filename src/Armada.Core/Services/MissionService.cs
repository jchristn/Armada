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
        private IPromptTemplateService? _PromptTemplates;

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
        public MissionService(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            IDockService docks,
            ICaptainService captains,
            IPromptTemplateService? promptTemplates = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
            _Captains = captains ?? throw new ArgumentNullException(nameof(captains));
            _PromptTemplates = promptTemplates;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> TryAssignAsync(Mission mission, Vessel vessel, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            // Check pipeline dependency -- skip if the mission depends on another that hasn't completed
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

            // Find an idle captain, preferring those matching the mission's persona
            Captain? captain = await FindAvailableCaptainAsync(mission.Persona, token).ConfigureAwait(false);
            if (captain == null)
            {
                _Logging.Warn(_Header + "no idle captains available for mission " + mission.Id +
                    (mission.Persona != null ? " (persona: " + mission.Persona + ")" : ""));
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

            // Get dock for diff capture (prefer mission-level DockId, fall back to captain-level)
            Dock? dock = null;
            string? dockId = mission.DockId ?? captain.CurrentDockId;
            if (!String.IsNullOrEmpty(dockId))
            {
                dock = !String.IsNullOrEmpty(mission.TenantId)
                    ? await _Database.Docks.ReadAsync(mission.TenantId, dockId, token).ConfigureAwait(false)
                    : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
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

            // Pipeline handoff: if missions in the same voyage depend on this one, prepare them
            await TryHandoffToNextStageAsync(mission, token).ConfigureAwait(false);

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

            // Build placeholder context for template rendering
            Dictionary<string, string> templateParams = new Dictionary<string, string>
            {
                ["MissionId"] = mission.Id,
                ["MissionTitle"] = mission.Title,
                ["MissionDescription"] = mission.Description ?? "No additional description provided.",
                ["MissionPersona"] = mission.Persona ?? "Worker",
                ["VoyageId"] = mission.VoyageId ?? "",
                ["VesselId"] = vessel.Id,
                ["VesselName"] = vessel.Name,
                ["DefaultBranch"] = vessel.DefaultBranch,
                ["BranchName"] = mission.BranchName ?? "unknown",
                ["FleetId"] = vessel.FleetId ?? "",
                ["ProjectContext"] = vessel.ProjectContext ?? "",
                ["StyleGuide"] = vessel.StyleGuide ?? "",
                ["ModelContext"] = vessel.ModelContext ?? "",
                ["CaptainId"] = captain?.Id ?? "",
                ["CaptainName"] = captain?.Name ?? "",
                ["CaptainInstructions"] = captain?.SystemInstructions ?? "",
                ["Timestamp"] = DateTime.UtcNow.ToString("o")
            };

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

            // Mission preamble and metadata -- resolve persona prompt first, then inject into metadata template
            string personaPrompt = await ResolvePersonaPromptAsync(mission.Persona, templateParams, token).ConfigureAwait(false);
            templateParams["PersonaPrompt"] = personaPrompt;
            content += await ResolveSectionAsync("mission.metadata", templateParams, token).ConfigureAwait(false);
            content += "\n";

            // Rules, context conservation, merge conflicts, progress signals -- from templates or hardcoded fallback
            content += await ResolveSectionAsync("mission.rules", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.context_conservation", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.merge_conflict_avoidance", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.progress_signals", templateParams, token).ConfigureAwait(false);

            // Model context updates
            if (vessel.EnableModelContext)
            {
                content += "\n";
                content += await ResolveSectionAsync("mission.model_context_updates", templateParams, token).ConfigureAwait(false);
            }

            // If there's an existing CLAUDE.md, preserve it and prepend our instructions
            if (File.Exists(claudeMdPath))
            {
                string existing = await File.ReadAllTextAsync(claudeMdPath).ConfigureAwait(false);
                templateParams["ExistingClaudeMd"] = existing;
                content += await ResolveSectionAsync("mission.existing_instructions_wrapper", templateParams, token).ConfigureAwait(false);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(claudeMdPath)!);
            await File.WriteAllTextAsync(claudeMdPath, content).ConfigureAwait(false);

            // Ensure CLAUDE.md is gitignored so agents don't commit it.
            // It's mission-specific and causes merge conflicts during landing.
            string gitignorePath = Path.Combine(worktreePath, ".gitignore");
            try
            {
                string gitignoreContent = File.Exists(gitignorePath)
                    ? await File.ReadAllTextAsync(gitignorePath).ConfigureAwait(false)
                    : "";
                if (!gitignoreContent.Contains("CLAUDE.md"))
                {
                    string entry = (gitignoreContent.Length > 0 && !gitignoreContent.EndsWith("\n") ? "\n" : "") + "CLAUDE.md\n";
                    await File.AppendAllTextAsync(gitignorePath, entry).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not update .gitignore for CLAUDE.md: " + ex.Message);
            }

            _Logging.Info(_Header + "generated mission CLAUDE.md at " + claudeMdPath);
        }

        /// <summary>
        /// Resolve a persona prompt template by persona name. Falls back to default worker preamble.
        /// </summary>
        private async Task<string> ResolvePersonaPromptAsync(string? persona, Dictionary<string, string> templateParams, CancellationToken token)
        {
            string templateName = "persona.worker";
            if (!String.IsNullOrEmpty(persona))
            {
                templateName = "persona." + persona.ToLowerInvariant();
            }

            if (_PromptTemplates != null)
            {
                string rendered = await _PromptTemplates.RenderAsync(templateName, templateParams, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(rendered))
                    return rendered;
            }

            // Fallback for backward compatibility
            return "You are an Armada captain executing a mission. Follow these instructions carefully.";
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
                        "- `[ARMADA:MESSAGE] your message here` -- send a progress message\n";

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
                        "If you have useful additions, call `armada_update_vessel_context` with the `modelContext` " +
                        "parameter set to the COMPLETE updated model context (not just your additions -- include " +
                        "the existing content with your additions merged in). Be thorough -- this context is a " +
                        "goldmine for future agents. Focus on information that is not obvious from reading the code, " +
                        "and organize it clearly with sections or headings.\n" +
                        "\n" +
                        "If you have nothing to add, skip this step.\n";

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

        /// <summary>
        /// After a mission produces work, check if any missions in the same voyage depend on it
        /// and prepare them for assignment (inject prior stage context into description).
        /// </summary>
        private async Task TryHandoffToNextStageAsync(Mission completedMission, CancellationToken token)
        {
            if (String.IsNullOrEmpty(completedMission.VoyageId)) return;

            // Find missions that depend on this completed mission
            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(completedMission.VoyageId, token).ConfigureAwait(false);
            List<Mission> dependentMissions = voyageMissions.Where(m =>
                m.DependsOnMissionId == completedMission.Id &&
                m.Status == MissionStatusEnum.Pending).ToList();

            if (dependentMissions.Count == 0) return;

            // Special handling for Architect stage: parse output into new missions
            if (String.Equals(completedMission.Persona, "Architect", StringComparison.OrdinalIgnoreCase))
            {
                List<ParsedArchitectMission> parsed = ParseArchitectOutput(completedMission);
                if (parsed.Count > 0)
                {
                    _Logging.Info(_Header + "architect produced " + parsed.Count + " mission definitions");

                    foreach (Mission nextMission in dependentMissions)
                    {
                        if (String.Equals(nextMission.Persona, "Worker", StringComparison.OrdinalIgnoreCase))
                        {
                            // Update the first parsed mission into this existing Worker mission slot
                            ParsedArchitectMission first = parsed[0];
                            nextMission.Title = first.Title + " [Worker]";
                            nextMission.Description = first.Description;
                            nextMission.BranchName = completedMission.BranchName;
                            nextMission.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Missions.UpdateAsync(nextMission, token).ConfigureAwait(false);

                            // Find what depends on this worker mission (Judge, TestEngineer stages)
                            List<Mission> postWorkerStages = voyageMissions.Where(m =>
                                m.DependsOnMissionId == nextMission.Id).ToList();

                            // Create additional worker missions for remaining parsed items
                            for (int i = 1; i < parsed.Count; i++)
                            {
                                Mission additionalWorker = new Mission(parsed[i].Title + " [Worker]", parsed[i].Description);
                                additionalWorker.TenantId = completedMission.TenantId;
                                additionalWorker.UserId = completedMission.UserId;
                                additionalWorker.VoyageId = completedMission.VoyageId;
                                additionalWorker.VesselId = completedMission.VesselId;
                                additionalWorker.Persona = "Worker";
                                additionalWorker.DependsOnMissionId = completedMission.Id;
                                additionalWorker = await _Database.Missions.CreateAsync(additionalWorker, token).ConfigureAwait(false);
                                _Logging.Info(_Header + "architect created additional worker mission " + additionalWorker.Id + ": " + parsed[i].Title);

                                // Clone the post-worker stages (Judge, TestEngineer) for each additional worker
                                foreach (Mission postWorkerStage in postWorkerStages)
                                {
                                    Mission clonedStage = new Mission(
                                        parsed[i].Title + " [" + postWorkerStage.Persona + "]",
                                        postWorkerStage.Description);
                                    clonedStage.TenantId = completedMission.TenantId;
                                    clonedStage.UserId = completedMission.UserId;
                                    clonedStage.VoyageId = completedMission.VoyageId;
                                    clonedStage.VesselId = completedMission.VesselId;
                                    clonedStage.Persona = postWorkerStage.Persona;
                                    clonedStage.DependsOnMissionId = additionalWorker.Id;
                                    clonedStage = await _Database.Missions.CreateAsync(clonedStage, token).ConfigureAwait(false);
                                    _Logging.Info(_Header + "architect created chained stage " + clonedStage.Id +
                                        " (" + clonedStage.Persona + ") depending on " + additionalWorker.Id);
                                }
                            }

                            // Try to assign the first worker mission
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

                    return; // Architect special handling complete, skip normal handoff
                }
                // If no [ARMADA:MISSION] markers found, fall through to normal handoff
            }

            foreach (Mission nextMission in dependentMissions)
            {
                // Build persona-specific preamble for the next stage
                string personaPreamble = "";
                switch (nextMission.Persona)
                {
                    case "Worker":
                        personaPreamble = "## Your Role: Worker (Implement)\n\n" +
                            "You are implementing code changes based on the Architect's plan. " +
                            "Review the prior stage output below and implement the described changes.\n\n";
                        break;
                    case "TestEngineer":
                        personaPreamble = "## Your Role: TestEngineer (Write Tests)\n\n" +
                            "You are writing tests for code changes made by the Worker. " +
                            "Review the diff below and write unit tests, integration tests, or test harness updates " +
                            "that cover the changes. Follow existing test patterns in the repository.\n\n";
                        break;
                    case "Judge":
                        personaPreamble = "## Your Role: Judge (Review)\n\n" +
                            "You are reviewing the completed work for correctness, completeness, scope compliance, " +
                            "and style. Examine the diff below against the original mission description. " +
                            "Produce a clear verdict: PASS, FAIL (with reasons), or NEEDS_REVISION (with feedback).\n\n";
                        break;
                }

                // Inject context from the completed stage into the next stage's description
                string handoffContext = "\n\n---\n" +
                    "## Prior Stage Output\n" +
                    "The previous pipeline stage (" + (completedMission.Persona ?? "Worker") + ") " +
                    "completed mission \"" + completedMission.Title + "\" (" + completedMission.Id + ").\n" +
                    "Branch: " + (completedMission.BranchName ?? "unknown") + "\n";

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

                // Try to assign the next stage (dependency check in TryAssignAsync will now pass)
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

        /// <summary>
        /// Parse structured mission definitions from an architect's output.
        /// Looks for [ARMADA:MISSION] markers in the mission diff snapshot or description.
        /// </summary>
        private List<ParsedArchitectMission> ParseArchitectOutput(Mission architectMission)
        {
            List<ParsedArchitectMission> results = new List<ParsedArchitectMission>();

            // Look in diff snapshot first, then description
            string? source = null;
            if (!String.IsNullOrEmpty(architectMission.DiffSnapshot) &&
                architectMission.DiffSnapshot.Contains("[ARMADA:MISSION]"))
            {
                source = architectMission.DiffSnapshot;
            }
            else if (!String.IsNullOrEmpty(architectMission.Description) &&
                     architectMission.Description.Contains("[ARMADA:MISSION]"))
            {
                source = architectMission.Description;
            }

            if (source == null) return results;

            // Split on [ARMADA:MISSION] markers that appear at the start of a line with no indentation.
            // Indented markers (e.g. in template examples) are ignored.
            string[] segments = System.Text.RegularExpressions.Regex.Split(source, @"(?m)^\[ARMADA:MISSION\][ \t]*");

            // First segment is everything before the first marker -- skip it
            for (int i = 1; i < segments.Length; i++)
            {
                string segment = segments[i].Trim();
                if (String.IsNullOrEmpty(segment)) continue;

                // First line is the title, rest is description
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

                if (!String.IsNullOrEmpty(title))
                {
                    ParsedArchitectMission parsed = new ParsedArchitectMission();
                    parsed.Title = title;
                    parsed.Description = description;
                    results.Add(parsed);
                }
            }

            return results;
        }

        private async Task<Captain?> FindAvailableCaptainAsync(string? persona, CancellationToken token)
        {
            // Only idle captains are eligible for assignment
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            if (idleCaptains.Count == 0)
                return null;

            // If no persona requirement, return any idle captain
            if (String.IsNullOrEmpty(persona))
                return idleCaptains[0];

            // Filter by AllowedPersonas (null = any persona is allowed)
            List<Captain> eligible = new List<Captain>();
            foreach (Captain captain in idleCaptains)
            {
                if (String.IsNullOrEmpty(captain.AllowedPersonas))
                {
                    // No restriction -- captain can fill any persona
                    eligible.Add(captain);
                }
                else
                {
                    // Check if the persona is in the allowed list
                    // AllowedPersonas is a JSON array string, e.g. '["Worker","Judge"]'
                    if (captain.AllowedPersonas.Contains("\"" + persona + "\"", StringComparison.OrdinalIgnoreCase))
                    {
                        eligible.Add(captain);
                    }
                }
            }

            if (eligible.Count == 0)
            {
                // Fall back to any idle captain if none match persona constraints
                // This ensures work still gets done even if persona config is incomplete
                return idleCaptains[0];
            }

            // Prefer captains whose PreferredPersona matches
            foreach (Captain captain in eligible)
            {
                if (!String.IsNullOrEmpty(captain.PreferredPersona) &&
                    String.Equals(captain.PreferredPersona, persona, StringComparison.OrdinalIgnoreCase))
                {
                    return captain;
                }
            }

            // No preferred match -- return first eligible
            return eligible[0];
        }

        #endregion
    }
}
