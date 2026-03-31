namespace Armada.Server
{
    using System.Collections.Concurrent;
    using System.IO;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server.WebSocket;

    /// <summary>
    /// Handles mission landing orchestration: diff capture, merge/PR flows, voyage completion, and PR reconciliation.
    /// </summary>
    public class MissionLandingHandler
    {
        #region Private-Members

        private string _Header = "[MissionLanding] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService _Git;
        private IMergeQueueService _MergeQueue;
        private IMessageTemplateService _TemplateService;
        private IPromptTemplateService? _PromptTemplateService;
        private IDockService _Docks;
        private ArmadaWebSocketHub? _WebSocketHub;

        /// <summary>
        /// Per-vessel semaphores to prevent concurrent merge operations on the same repository.
        /// </summary>
        private ConcurrentDictionary<string, SemaphoreSlim> _VesselMergeLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="git">Git service.</param>
        /// <param name="mergeQueue">Merge queue service.</param>
        /// <param name="templateService">Message template service.</param>
        /// <param name="promptTemplateService">Prompt template service (optional).</param>
        /// <param name="docks">Dock service.</param>
        /// <param name="webSocketHub">WebSocket hub (nullable).</param>
        public MissionLandingHandler(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            IGitService git,
            IMergeQueueService mergeQueue,
            IMessageTemplateService templateService,
            IPromptTemplateService? promptTemplateService,
            IDockService docks,
            ArmadaWebSocketHub? webSocketHub)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
            _MergeQueue = mergeQueue ?? throw new ArgumentNullException(nameof(mergeQueue));
            _TemplateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _PromptTemplateService = promptTemplateService;
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
            _WebSocketHub = webSocketHub;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set or update the WebSocket hub reference (created after this handler).
        /// </summary>
        public void SetWebSocketHub(ArmadaWebSocketHub? hub)
        {
            _WebSocketHub = hub;
        }

        /// <summary>
        /// Capture diff and commit hash for a mission before worktree reclamation.
        /// </summary>
        public async Task HandleCaptureDiffAsync(Mission mission, Dock dock)
        {
            if (String.IsNullOrEmpty(dock.WorktreePath) || String.IsNullOrEmpty(dock.BranchName))
                return;

            _Logging.Info(_Header + "capturing diff for mission " + mission.Id + " before worktree reclamation");

            // Capture diff and persist to database + file
            string baseBranch = "main";
            try
            {
                if (!String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? diffVessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                    if (diffVessel != null) baseBranch = diffVessel.DefaultBranch;
                }

                string diff = await _Git.DiffAsync(dock.WorktreePath, baseBranch).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(diff))
                {
                    // Persist to database so it survives worktree reclamation
                    mission.DiffSnapshot = diff;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "persisted diff snapshot to database for mission " + mission.Id + " (" + diff.Length + " chars)");

                    // Also save to file for backwards compatibility
                    string diffDir = Path.Combine(_Settings.LogDirectory, "diffs");
                    Directory.CreateDirectory(diffDir);
                    string diffPath = Path.Combine(diffDir, mission.Id + ".diff");
                    await File.WriteAllTextAsync(diffPath, diff).ConfigureAwait(false);
                }
            }
            catch (Exception diffEx)
            {
                _Logging.Debug(_Header + "could not capture diff for mission " + mission.Id + ": " + diffEx.Message);
            }

            // Capture the HEAD commit hash before any merge/reclaim
            try
            {
                string? commitHash = await _Git.GetHeadCommitHashAsync(dock.WorktreePath).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(commitHash))
                {
                    mission.CommitHash = commitHash;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "captured commit hash " + commitHash + " for mission " + mission.Id);
                }
            }
            catch (Exception commitEx)
            {
                _Logging.Debug(_Header + "could not capture commit hash for mission " + mission.Id + ": " + commitEx.Message);
            }
        }

        /// <summary>
        /// Handle the full mission landing orchestration: resolve landing mode, merge/PR/enqueue, transition status, broadcast, and reclaim dock.
        /// </summary>
        public async Task HandleMissionCompleteAsync(Mission mission, Dock dock)
        {
            if (String.IsNullOrEmpty(dock.WorktreePath) || String.IsNullOrEmpty(dock.BranchName))
                return;

            _Logging.Info(_Header + "handling landing for mission " + mission.Id);

            // Look up the vessel and voyage for settings resolution
            Vessel? vessel = null;
            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                vessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
            }

            Voyage? voyage = null;
            if (!String.IsNullOrEmpty(mission.VoyageId))
            {
                voyage = await _Database.Voyages.ReadAsync(mission.VoyageId).ConfigureAwait(false);
            }

            // Resolve landing mode: voyage > vessel > global > derive from legacy booleans
            LandingModeEnum? resolvedLandingMode = voyage?.LandingMode ?? vessel?.LandingMode ?? _Settings.LandingMode;

            // Resolve effective settings from landing mode or legacy booleans
            bool effectivePush;
            bool effectivePr;
            bool effectiveMerge;

            if (resolvedLandingMode.HasValue)
            {
                // Explicit landing mode takes precedence over boolean flags
                effectivePr = resolvedLandingMode.Value == LandingModeEnum.PullRequest;
                effectivePush = effectivePr || resolvedLandingMode.Value == LandingModeEnum.LocalMerge;
                effectiveMerge = effectivePr && (voyage?.AutoMergePullRequests ?? _Settings.AutoMergePullRequests);
            }
            else
            {
                // Legacy boolean resolution: per-voyage override > global setting
                effectivePush = voyage?.AutoPush ?? _Settings.AutoPush;
                effectivePr = voyage?.AutoCreatePullRequests ?? _Settings.AutoCreatePullRequests;
                effectiveMerge = voyage?.AutoMergePullRequests ?? _Settings.AutoMergePullRequests;
            }

            bool landingModeIsNone = resolvedLandingMode == LandingModeEnum.None;
            bool landingModeIsMergeQueue = resolvedLandingMode == LandingModeEnum.MergeQueue;

            // Resolve branch cleanup policy: vessel > global setting
            BranchCleanupPolicyEnum cleanupPolicy = vessel?.BranchCleanupPolicy ?? _Settings.BranchCleanupPolicy;

            // Acquire per-vessel merge lock to prevent concurrent git operations on the same repo
            string vesselLockKey = mission.VesselId ?? dock.VesselId ?? "unknown";
            SemaphoreSlim vesselLock = _VesselMergeLocks.GetOrAdd(vesselLockKey, _ => new SemaphoreSlim(1, 1));

            _Logging.Info(_Header + "acquiring merge lock for vessel " + vesselLockKey + " (mission " + mission.Id + ")");
            await vesselLock.WaitAsync().ConfigureAwait(false);

            bool landingSucceeded = false;
            bool landingAttempted = false;
            string? landingFailureReason = null;

            try
            {
                _Logging.Info(_Header + "merge lock acquired for vessel " + vesselLockKey + " (mission " + mission.Id + ")");

                if (effectivePr)
                {
                    // Push + PR flow
                    landingAttempted = true;
                    try
                    {
                        await _Git.PushBranchAsync(dock.WorktreePath).ConfigureAwait(false);
                        _Logging.Info(_Header + "pushed branch " + dock.BranchName);

                        string prBody;
                        if (_PromptTemplateService != null)
                        {
                            Dictionary<string, string> prTemplateParams = new Dictionary<string, string>
                            {
                                ["MissionTitle"] = mission.Title,
                                ["MissionDescription"] = mission.Description ?? ""
                            };
                            string rendered = await _PromptTemplateService.RenderAsync("landing.pr_body", prTemplateParams).ConfigureAwait(false);
                            prBody = !String.IsNullOrEmpty(rendered) ? rendered : "## Mission\n**" + mission.Title + "**\n\n" + (mission.Description ?? "");
                        }
                        else
                        {
                            prBody = "## Mission\n**" + mission.Title + "**\n\n" + (mission.Description ?? "");
                        }

                        // Append PR metadata template
                        Dictionary<string, string> prContext = _TemplateService.BuildContext(mission, null, vessel, voyage, dock);
                        prBody = _TemplateService.RenderPrDescription(_Settings.MessageTemplates, prBody, prContext);

                        string prUrl = await _Git.CreatePullRequestAsync(
                            dock.WorktreePath,
                            mission.Title,
                            prBody).ConfigureAwait(false);

                        mission.PrUrl = prUrl;
                        await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                        _Logging.Info(_Header + "created PR: " + prUrl);

                        // PR created — transition to PullRequestOpen (not Complete).
                        // Complete is reserved for when the PR is actually merged.
                        mission.Status = MissionStatusEnum.PullRequestOpen;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                        _Logging.Info(_Header + "mission " + mission.Id + " PR created, status set to PullRequestOpen");

                        // Emit mission.pull_request_open event
                        try
                        {
                            ArmadaEvent prEvent = new ArmadaEvent("mission.pull_request_open", "Pull request opened: " + mission.Title);
                            prEvent.EntityType = "mission";
                            prEvent.EntityId = mission.Id;
                            prEvent.CaptainId = mission.CaptainId;
                            prEvent.MissionId = mission.Id;
                            prEvent.VesselId = mission.VesselId;
                            prEvent.VoyageId = mission.VoyageId;
                            await _Database.Events.CreateAsync(prEvent).ConfigureAwait(false);
                        }
                        catch (Exception evtEx)
                        {
                            _Logging.Warn(_Header + "error emitting mission.pull_request_open event for " + mission.Id + ": " + evtEx.Message);
                        }

                        // Broadcast PullRequestOpen via WebSocket
                        if (_WebSocketHub != null)
                        {
                            _WebSocketHub.BroadcastEvent("mission.pull_request_open", "Pull request opened: " + mission.Title, new
                            {
                                entityType = "mission",
                                entityId = mission.Id,
                                captainId = mission.CaptainId,
                                missionId = mission.Id,
                                vesselId = mission.VesselId,
                                voyageId = mission.VoyageId
                            });
                            _WebSocketHub.BroadcastMissionChange(mission.Id, MissionStatusEnum.PullRequestOpen.ToString(), mission.Title);
                        }

                        // PR path handles its own status — skip the generic landing result block below
                        landingSucceeded = false;
                        landingAttempted = false;

                        // Auto-merge if enabled
                        if (effectiveMerge && !String.IsNullOrEmpty(prUrl))
                        {
                            try
                            {
                                await _Git.EnableAutoMergeAsync(dock.WorktreePath, prUrl).ConfigureAwait(false);
                                _Logging.Info(_Header + "enabled auto-merge for PR: " + prUrl);

                                // Poll for merge completion, then transition to Complete
                                if (vessel != null && !String.IsNullOrEmpty(vessel.WorkingDirectory) && !String.IsNullOrEmpty(vessel.LocalPath) && !String.IsNullOrEmpty(dock.BranchName))
                                {
                                    _ = PollAndPullAfterMergeAsync(vessel.WorkingDirectory, vessel.LocalPath, dock.BranchName, prUrl, mission.Id, cleanupPolicy);
                                }
                            }
                            catch (Exception mergeEx)
                            {
                                _Logging.Warn(_Header + "failed to enable auto-merge for " + prUrl + ": " + mergeEx.Message);
                                // PR was created, so mission stays PullRequestOpen even if auto-merge enablement fails
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error pushing/creating PR for mission " + mission.Id + ": " + ex.Message);
                        landingSucceeded = false;
                        landingFailureReason = "Error pushing/creating PR: " + ex.Message;
                    }
                }
                else if (vessel != null && !String.IsNullOrEmpty(vessel.WorkingDirectory) && !String.IsNullOrEmpty(vessel.LocalPath))
                {
                    // Check if the mission actually produced mergeable changes.
                    // Pipeline stages like Architect may complete without code changes (they output
                    // mission markers to stdout instead). Skip merge if no changes were made.
                    // Use the DiffSnapshot (captured before handoff) and the branch existence in
                    // the bare repo as indicators -- the worktree may already be gone at this point.
                    bool hasChanges = true;
                    if (String.IsNullOrEmpty(mission.DiffSnapshot) || mission.DiffSnapshot.Trim().Length == 0)
                    {
                        hasChanges = false;
                        _Logging.Info(_Header + "mission " + mission.Id + " has no diff snapshot -- no code changes to merge");
                    }
                    else if (!String.IsNullOrEmpty(dock.BranchName) && !String.IsNullOrEmpty(vessel.LocalPath))
                    {
                        // Also check if the branch actually exists in the bare repo
                        try
                        {
                            bool branchExists = await _Git.BranchExistsAsync(vessel.LocalPath, dock.BranchName).ConfigureAwait(false);
                            if (!branchExists)
                            {
                                hasChanges = false;
                                _Logging.Info(_Header + "mission " + mission.Id + " branch " + dock.BranchName + " not in bare repo -- no code changes to merge");
                            }
                        }
                        catch { }
                    }

                    if (!hasChanges)
                    {
                        // No code changes -- skip merge and mark as complete
                        landingAttempted = true;
                        landingSucceeded = true;
                    }
                    else
                    {
                    // Local merge flow: fetch captain's branch from bare repo and merge into user's working directory
                    landingAttempted = true;
                    try
                    {
                        // Render merge commit message from template
                        string? mergeMessage = null;
                        Dictionary<string, string> mergeContext = _TemplateService.BuildContext(mission, null, vessel, voyage, dock);
                        mergeMessage = _TemplateService.RenderMergeCommitMessage(_Settings.MessageTemplates, mergeContext);

                        await _Git.MergeBranchLocalAsync(vessel.WorkingDirectory, vessel.LocalPath, dock.BranchName, vessel.DefaultBranch, mergeMessage).ConfigureAwait(false);
                        _Logging.Info(_Header + "merged branch " + dock.BranchName + " into " + vessel.WorkingDirectory);

                        landingSucceeded = true;

                        // Push the merged changes to the remote BEFORE branch cleanup,
                        // so the branch is preserved for retry if push fails.
                        if (effectivePush)
                        {
                            try
                            {
                                await _Git.PushBranchAsync(vessel.WorkingDirectory).ConfigureAwait(false);
                                _Logging.Info(_Header + "pushed merged changes from " + vessel.WorkingDirectory);
                            }
                            catch (Exception pushEx)
                            {
                                _Logging.Warn(_Header + "local merge succeeded but push failed for mission " + mission.Id + ": " + pushEx.Message);
                                landingSucceeded = false;
                                landingFailureReason = "Local merge succeeded but push failed: " + pushEx.Message;
                            }
                        }

                        // Only clean up the mission branch after confirmed success (merge + push).
                        // Preserve branch on failure for retry.
                        if (landingSucceeded && cleanupPolicy != BranchCleanupPolicyEnum.None)
                        {
                            try
                            {
                                await _Git.DeleteLocalBranchAsync(vessel.LocalPath, dock.BranchName).ConfigureAwait(false);
                                _Logging.Info(_Header + "deleted branch " + dock.BranchName + " from bare repo after successful landing");
                            }
                            catch (Exception branchEx)
                            {
                                _Logging.Warn(_Header + "failed to delete branch " + dock.BranchName + " from bare repo: " + branchEx.Message);
                            }

                            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
                            {
                                try
                                {
                                    await _Git.DeleteRemoteBranchAsync(vessel.WorkingDirectory, dock.BranchName).ConfigureAwait(false);
                                    _Logging.Info(_Header + "deleted remote branch " + dock.BranchName + " after successful landing");
                                }
                                catch (Exception remoteBranchEx)
                                {
                                    _Logging.Warn(_Header + "failed to delete remote branch " + dock.BranchName + ": " + remoteBranchEx.Message);
                                }
                            }
                        }
                        else if (!landingSucceeded)
                        {
                            _Logging.Info(_Header + "preserving branch " + dock.BranchName + " for retry (landing failed)");
                        }
                        else
                        {
                            _Logging.Info(_Header + "branch cleanup policy is None — retaining branch " + dock.BranchName + " for inspection");
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error merging locally for mission " + mission.Id + ": " + ex.Message + " -- branch " + dock.BranchName + " is still available in the bare repo");
                        landingSucceeded = false;
                        landingFailureReason = "Error merging locally: " + ex.Message;
                    }
                    } // end hasChanges else
                }
                else if (landingModeIsMergeQueue)
                {
                    // MergeQueue mode: auto-enqueue the branch into the merge queue.
                    // Processing (test-and-land) remains a separate trigger via armada_process_merge_queue.
                    try
                    {
                        string targetBranch = vessel?.DefaultBranch ?? "main";
                        MergeEntry entry = new MergeEntry(dock.BranchName, targetBranch);
                        entry.MissionId = mission.Id;
                        entry.VesselId = mission.VesselId;
                        entry = await _MergeQueue.EnqueueAsync(entry).ConfigureAwait(false);
                        _Logging.Info(_Header + "mission " + mission.Id + " auto-enqueued as merge entry " + entry.Id + " (branch " + dock.BranchName + " -> " + targetBranch + ")");

                        // Emit merge_queue.enqueued event
                        try
                        {
                            ArmadaEvent mqEvent = new ArmadaEvent("merge_queue.enqueued", "Mission " + mission.Id + " auto-enqueued for merge queue: " + dock.BranchName + " -> " + targetBranch);
                            mqEvent.EntityType = "merge_entry";
                            mqEvent.EntityId = entry.Id;
                            mqEvent.MissionId = mission.Id;
                            mqEvent.VesselId = mission.VesselId;
                            mqEvent.VoyageId = mission.VoyageId;
                            mqEvent.CaptainId = mission.CaptainId;
                            await _Database.Events.CreateAsync(mqEvent).ConfigureAwait(false);
                        }
                        catch (Exception evtEx)
                        {
                            _Logging.Warn(_Header + "error emitting merge_queue.enqueued event for " + mission.Id + ": " + evtEx.Message);
                        }
                    }
                    catch (Exception mqEx)
                    {
                        _Logging.Warn(_Header + "error auto-enqueuing merge entry for mission " + mission.Id + ": " + mqEx.Message + " — branch " + dock.BranchName + " is still available for manual enqueue");
                    }
                    // Mission stays as WorkProduced; merge queue processing will land it
                }
                else
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " work produced — branch " + dock.BranchName + " available in bare repo (landing mode: " + (resolvedLandingMode?.ToString() ?? "not configured") + ")");
                    // No landing configured or LandingMode.None — mission stays as WorkProduced
                }
            }
            finally
            {
                vesselLock.Release();
                _Logging.Info(_Header + "merge lock released for vessel " + vesselLockKey + " (mission " + mission.Id + ")");
            }

            // Transition mission status based on landing result
            if (landingAttempted)
            {
                if (landingSucceeded)
                {
                    mission.Status = MissionStatusEnum.Complete;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "mission " + mission.Id + " landed successfully, status set to Complete");

                    // Emit mission.completed event
                    try
                    {
                        ArmadaEvent completedEvent = new ArmadaEvent("mission.completed", "Mission completed: " + mission.Title);
                        completedEvent.EntityType = "mission";
                        completedEvent.EntityId = mission.Id;
                        completedEvent.CaptainId = mission.CaptainId;
                        completedEvent.MissionId = mission.Id;
                        completedEvent.VesselId = mission.VesselId;
                        completedEvent.VoyageId = mission.VoyageId;
                        await _Database.Events.CreateAsync(completedEvent).ConfigureAwait(false);
                    }
                    catch (Exception evtEx)
                    {
                        _Logging.Warn(_Header + "error emitting mission.completed event for " + mission.Id + ": " + evtEx.Message);
                    }
                }
                else
                {
                    mission.Status = MissionStatusEnum.LandingFailed;
                    mission.FailureReason = landingFailureReason;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Warn(_Header + "mission " + mission.Id + " landing failed, status set to LandingFailed");

                    // Emit mission.landing_failed event
                    try
                    {
                        ArmadaEvent failedEvent = new ArmadaEvent("mission.landing_failed", "Landing failed: " + mission.Title);
                        failedEvent.EntityType = "mission";
                        failedEvent.EntityId = mission.Id;
                        failedEvent.CaptainId = mission.CaptainId;
                        failedEvent.MissionId = mission.Id;
                        failedEvent.VesselId = mission.VesselId;
                        failedEvent.VoyageId = mission.VoyageId;
                        await _Database.Events.CreateAsync(failedEvent).ConfigureAwait(false);
                    }
                    catch (Exception evtEx)
                    {
                        _Logging.Warn(_Header + "error emitting mission.landing_failed event for " + mission.Id + ": " + evtEx.Message);
                    }
                }
            }

            // Broadcast via WebSocket for real-time UI updates.
            // Derive event type from mission.Status (not from boolean flags) so the broadcast
            // accurately reflects the current state. This prevents the PR path from emitting
            // a misleading "mission.work_produced" broadcast when the mission is already PullRequestOpen.
            if (_WebSocketHub != null)
            {
                string eventType;
                string eventMessage;

                switch (mission.Status)
                {
                    case MissionStatusEnum.Complete:
                        eventType = "mission.completed";
                        eventMessage = "Mission completed: " + mission.Title;
                        break;
                    case MissionStatusEnum.LandingFailed:
                        eventType = "mission.landing_failed";
                        eventMessage = "Landing failed: " + mission.Title;
                        break;
                    case MissionStatusEnum.PullRequestOpen:
                        eventType = "mission.pull_request_open";
                        eventMessage = "Pull request open: " + mission.Title;
                        break;
                    default:
                        eventType = "mission.work_produced";
                        eventMessage = "Work produced: " + mission.Title;
                        break;
                }

                _WebSocketHub.BroadcastEvent(eventType, eventMessage, new
                {
                    entityType = "mission",
                    entityId = mission.Id,
                    captainId = mission.CaptainId,
                    missionId = mission.Id,
                    vesselId = mission.VesselId,
                    voyageId = mission.VoyageId
                });

                // Broadcast specific mission change for dashboard toast notifications
                _WebSocketHub.BroadcastMissionChange(mission.Id, mission.Status.ToString(), mission.Title);
            }

            // NOTE: Dock reclaim is NOT done here. MissionService.HandleCompletionAsync
            // owns the full finalization sequence (reclaim dock, release captain, dispatch next)
            // to prevent duplicate reclaim calls from racing.
        }

        /// <summary>
        /// Handle voyage completion by broadcasting to the dashboard.
        /// </summary>
        public Task HandleVoyageCompleteAsync(Voyage voyage)
        {
            _Logging.Info(_Header + "voyage " + voyage.Id + " completed, broadcasting to dashboard");

            if (_WebSocketHub != null)
            {
                _WebSocketHub.BroadcastVoyageChange(voyage.Id, VoyageStatusEnum.Complete.ToString(), voyage.Title);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Persistent PR reconciler: checks if a PullRequestOpen mission's PR has been merged.
        /// Called from the health-check loop via delegate. Returns true if the mission was reconciled.
        /// </summary>
        public async Task<bool> HandleReconcilePullRequestAsync(Mission mission)
        {
            if (mission == null || String.IsNullOrEmpty(mission.PrUrl)) return false;
            if (mission.Status != MissionStatusEnum.PullRequestOpen) return false;

            // Find the vessel to get working directory context for gh CLI
            Vessel? vessel = null;
            if (!String.IsNullOrEmpty(mission.VesselId))
                vessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);

            string? workingDir = vessel?.WorkingDirectory ?? vessel?.LocalPath;
            if (String.IsNullOrEmpty(workingDir)) return false;

            try
            {
                bool merged = await _Git.IsPrMergedAsync(workingDir, mission.PrUrl).ConfigureAwait(false);
                if (merged)
                {
                    mission.Status = MissionStatusEnum.Complete;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "PR reconciler: mission " + mission.Id + " PR merged, status set to Complete");

                    // Pull latest into working directory if available
                    if (!String.IsNullOrEmpty(vessel?.WorkingDirectory))
                    {
                        try
                        {
                            await _Git.PullAsync(vessel.WorkingDirectory).ConfigureAwait(false);
                        }
                        catch (Exception pullEx)
                        {
                            _Logging.Warn(_Header + "PR reconciler: pull failed for " + vessel.WorkingDirectory + ": " + pullEx.Message);
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "PR reconciler: error checking PR status for mission " + mission.Id + ": " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Poll for PR merge completion, pull latest, transition mission to Complete, and clean up branches.
        /// </summary>
        public async Task PollAndPullAfterMergeAsync(string workingDirectory, string bareRepoPath, string branchName, string prUrl, string missionId, BranchCleanupPolicyEnum cleanupPolicy = BranchCleanupPolicyEnum.LocalOnly)
        {
            try
            {
                // Poll for up to 5 minutes (30 attempts, 10 seconds apart)
                // Uses the vessel working directory (not the mission dock) for gh CLI context,
                // since the dock worktree may be reclaimed before the PR merges.
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                    bool merged = await _Git.IsPrMergedAsync(workingDirectory, prUrl).ConfigureAwait(false);
                    if (merged)
                    {
                        _Logging.Info(_Header + "PR " + prUrl + " merged, pulling into " + workingDirectory);
                        await _Git.PullAsync(workingDirectory).ConfigureAwait(false);
                        _Logging.Info(_Header + "pulled latest into " + workingDirectory + " after PR merge");

                        // Transition mission from PullRequestOpen to Complete
                        try
                        {
                            Mission? mission = await _Database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                            if (mission != null && mission.Status == MissionStatusEnum.PullRequestOpen)
                            {
                                mission.Status = MissionStatusEnum.Complete;
                                mission.CompletedUtc = DateTime.UtcNow;
                                mission.LastUpdateUtc = DateTime.UtcNow;
                                await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                                _Logging.Info(_Header + "mission " + missionId + " PR merged, status set to Complete");

                                // Emit mission.completed event
                                try
                                {
                                    ArmadaEvent completedEvent = new ArmadaEvent("mission.completed", "Mission completed (PR merged): " + mission.Title);
                                    completedEvent.EntityType = "mission";
                                    completedEvent.EntityId = mission.Id;
                                    completedEvent.CaptainId = mission.CaptainId;
                                    completedEvent.MissionId = mission.Id;
                                    completedEvent.VesselId = mission.VesselId;
                                    completedEvent.VoyageId = mission.VoyageId;
                                    await _Database.Events.CreateAsync(completedEvent).ConfigureAwait(false);
                                }
                                catch (Exception evtEx)
                                {
                                    _Logging.Warn(_Header + "error emitting mission.completed event for " + missionId + ": " + evtEx.Message);
                                }

                                // Broadcast via WebSocket
                                if (_WebSocketHub != null)
                                {
                                    _WebSocketHub.BroadcastEvent("mission.completed", "Mission completed (PR merged): " + mission.Title, new
                                    {
                                        entityType = "mission",
                                        entityId = mission.Id,
                                        captainId = mission.CaptainId,
                                        missionId = mission.Id,
                                        vesselId = mission.VesselId,
                                        voyageId = mission.VoyageId
                                    });
                                    _WebSocketHub.BroadcastMissionChange(mission.Id, MissionStatusEnum.Complete.ToString(), mission.Title);
                                }
                            }
                        }
                        catch (Exception statusEx)
                        {
                            _Logging.Warn(_Header + "error transitioning mission " + missionId + " to Complete after PR merge: " + statusEx.Message);
                        }

                        // Clean up the mission branch based on cleanup policy
                        if (cleanupPolicy != BranchCleanupPolicyEnum.None)
                        {
                            try
                            {
                                await _Git.DeleteLocalBranchAsync(bareRepoPath, branchName).ConfigureAwait(false);
                                _Logging.Info(_Header + "deleted branch " + branchName + " from bare repo after PR merge");
                            }
                            catch (Exception branchEx)
                            {
                                _Logging.Warn(_Header + "failed to delete branch " + branchName + " from bare repo: " + branchEx.Message);
                            }

                            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
                            {
                                try
                                {
                                    await _Git.DeleteRemoteBranchAsync(workingDirectory, branchName).ConfigureAwait(false);
                                    _Logging.Info(_Header + "deleted remote branch " + branchName + " after PR merge");
                                }
                                catch (Exception remoteBranchEx)
                                {
                                    _Logging.Warn(_Header + "failed to delete remote branch " + branchName + ": " + remoteBranchEx.Message);
                                }
                            }
                        }
                        else
                        {
                            _Logging.Info(_Header + "branch cleanup policy is None — retaining branch " + branchName + " for inspection");
                        }

                        return;
                    }
                }

                _Logging.Info(_Header + "PR " + prUrl + " not merged within 5 minutes — mission " + missionId + " stays PullRequestOpen until merge is confirmed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error polling/pulling after merge for mission " + missionId + ": " + ex.Message);
            }
        }

        #endregion
    }
}
