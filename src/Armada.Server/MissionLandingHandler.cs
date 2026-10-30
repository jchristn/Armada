namespace Armada.Server
{
    using System.Collections.Concurrent;
    using System.IO;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server.WebSocket;

    /// <summary>
    /// Handles mission landing orchestration: diff capture, merge/PR flows, voyage completion, and PR reconciliation.
    /// </summary>
    public class MissionLandingHandler
    {
        #region Public-Members

        /// <summary>
        /// Where docks live. A Harbor-hosted dock lands on its Harbor: diff capture, push, pull request, and LocalMerge into
        /// the user's checkout there all run through the Harbor. Without it every dock lands on this machine.
        /// </summary>
        public DockHostResolver? DockHosts { get; set; } = null;

        #endregion

        /// <summary>Optional executor override for local landing checks.</summary>
        public IHostCommandExecutor? LocalVerificationCommands { get; set; }

        /// <summary>
        /// Black box capture for landing failures, or null.
        /// </summary>
        public IBlackBoxCapture? BlackBox { get; set; } = null;

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

            _Logging.Debug(_Header + "capturing diff for mission " + mission.Id + " before worktree reclamation");
            IGitService git = GitFor(dock);

            // Capture diff and persist to database + file
            string baseBranch = "main";
            try
            {
                if (!String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? diffVessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                    if (diffVessel != null) baseBranch = diffVessel.DefaultBranch;
                }

                string diff = await git.DiffAsync(dock.WorktreePath, baseBranch).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(diff))
                {
                    // Persist to database so it survives worktree reclamation
                    mission.DiffSnapshot = diff;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Debug(_Header + "persisted diff snapshot to database for mission " + mission.Id + " (" + diff.Length + " chars)");

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
                string? commitHash = await git.GetHeadCommitHashAsync(dock.WorktreePath).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(commitHash))
                {
                    mission.CommitHash = commitHash;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Debug(_Header + "captured commit hash " + commitHash + " for mission " + mission.Id);
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

            _Logging.Debug(_Header + "handling landing for mission " + mission.Id);

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

            // Resolve landing mode: voyage > vessel > global > MergeAndPush
            LandingModeEnum resolvedLandingMode = voyage?.LandingMode ?? vessel?.LandingMode ?? _Settings.LandingMode ?? LandingModeEnum.MergeAndPush;

            // LocalMerge merges without pushing; MergeAndPush merges and then pushes the working directory
            bool effectivePr = resolvedLandingMode == LandingModeEnum.PullRequest;
            bool effectivePush = resolvedLandingMode == LandingModeEnum.MergeAndPush;
            bool effectiveMerge = effectivePr && (voyage?.AutoMergePullRequests ?? _Settings.AutoMergePullRequests);

            bool landingModeIsNone = resolvedLandingMode == LandingModeEnum.None;
            bool landingModeIsMergeQueue = resolvedLandingMode == LandingModeEnum.MergeQueue;

            // Resolve branch cleanup policy: vessel > global setting
            BranchCleanupPolicyEnum cleanupPolicy = vessel?.BranchCleanupPolicy ?? _Settings.BranchCleanupPolicy;

            // Where the mission branch lives and what LocalMerge lands into: for a Harbor-hosted dock, the repository and
            // the user's checkout on that Harbor's host (all git work runs there); otherwise the vessel's bare repository
            // and working directory on this machine.
            bool harborDock = IsHarborDock(dock);
            IGitService git = GitFor(dock);
            string? sourceRepoPath = harborDock ? dock.RepositoryPath : vessel?.LocalPath;
            string? targetWorkDir = harborDock ? dock.CheckoutPath : vessel?.WorkingDirectory;

            // Acquire per-vessel merge lock to prevent concurrent git operations on the same repo
            string vesselLockKey = mission.VesselId ?? dock.VesselId ?? "unknown";
            SemaphoreSlim vesselLock = _VesselMergeLocks.GetOrAdd(vesselLockKey, _ => new SemaphoreSlim(1, 1));

            _Logging.Debug(_Header + "acquiring merge lock for vessel " + vesselLockKey + " (mission " + mission.Id + ")");
            await vesselLock.WaitAsync().ConfigureAwait(false);

            DateTime landingVersion = mission.LastUpdateUtc;
            bool landingSucceeded = false;
            bool landingAttempted = false;
            string? landingFailureReason = null;
            // Only a merge conflict git reports is a LandingConflict (LandingFailureClassifier); everything else is Infra.
            MissionFailureKindEnum landingFailureKind = MissionFailureKindEnum.Infra;
            GitDirtyCheckoutException? dirtyCheckout = null;
            // Black box facts of the failing step, read while the merge lock is held (the target can move after it).
            BlackBoxStepEnum landingStep = BlackBoxStepEnum.Unknown;
            Exception? landingException = null;
            BlackBoxGitFacts landingFacts = new BlackBoxGitFacts { CheckoutPath = targetWorkDir };
            LandingTargetSynchronizationResult syncFacts = new LandingTargetSynchronizationResult();

            try
            {
                _Logging.Debug(_Header + "merge lock acquired for vessel " + vesselLockKey + " (mission " + mission.Id + ")");

                Mission? owner = await _Database.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                if (owner == null || owner.Status != MissionStatusEnum.WorkProduced || owner.StartedUtc != mission.StartedUtc || owner.ProcessId != mission.ProcessId)
                    return;
                landingVersion = owner.LastUpdateUtc;
                bool synchronizationFailed = false;

                // Every landing step below runs git in the dock. A dock whose directory vanished is recreated from its
                // branch on its own host; one that cannot be recreated fails as Infra naming the path and branch, instead
                // of a process-start error being reported as a merge conflict.
                if (!landingModeIsNone)
                {
                    string? dockProblem = await EnsureLandingDockAsync(mission, dock, landingFacts).ConfigureAwait(false);
                    if (dockProblem != null)
                    {
                        synchronizationFailed = true;
                        landingAttempted = true;
                        landingFailureKind = MissionFailureKindEnum.Infra;
                        landingFailureReason = dockProblem;
                        landingStep = BlackBoxStepEnum.DockMissing;
                    }
                }

                bool synchronizeTarget = false;
                if (!synchronizationFailed && !landingModeIsNone && vessel != null && (!String.IsNullOrEmpty(targetWorkDir) || effectivePr || landingModeIsMergeQueue))
                {
                    try
                    {
                        synchronizeTarget = await git.BranchExistsAsync(dock.WorktreePath, dock.BranchName).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        synchronizationFailed = true;
                        landingAttempted = true;
                        landingFailureKind = LandingFailureClassifier.Classify(ex);
                        landingFailureReason = "Current-target integration failed: " + ex.Message;
                        landingStep = BlackBoxStepEnum.TargetIntegration;
                        landingException = ex;
                    }
                }

                if (synchronizeTarget && vessel != null)
                {
                    try
                    {
                        LandingTargetSynchronizationResult synchronized = await LandingTargetSynchronization.SynchronizeAsync(git, dock.WorktreePath, vessel.DefaultBranch, effectivePush ? null : targetWorkDir, syncFacts).ConfigureAwait(false);
                        mission.CommitHash = await git.GetHeadCommitHashAsync(dock.WorktreePath).ConfigureAwait(false);
                        if (effectivePush && !String.IsNullOrEmpty(targetWorkDir))
                            await git.ValidatePublishedAncestryAsync(dock.WorktreePath!, targetWorkDir, vessel.DefaultBranch, mission.CommitHash!).ConfigureAwait(false);
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, landingVersion, new[] { MissionStatusEnum.WorkProduced }).ConfigureAwait(false)) return;
                        landingVersion = mission.LastUpdateUtc;
                        if (synchronized.Advanced)
                        {
                            await _Database.Events.CreateAsync(new ArmadaEvent("mission.landing_target_advanced",
                                "Integrated the current target; landing verifies the resulting commit before publishing.")
                            {
                                TenantId = mission.TenantId, UserId = mission.UserId, EntityType = "mission", EntityId = mission.Id,
                                MissionId = mission.Id, VesselId = mission.VesselId, VoyageId = mission.VoyageId,
                                Payload = System.Text.Json.JsonSerializer.Serialize(LandingTargetSynchronization.ToAdvancedPayload(synchronized, vessel.DefaultBranch))
                            }).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        synchronizationFailed = true;
                        landingAttempted = true;
                        landingFailureReason = "Current-target integration failed: " + ex.Message;
                        landingFailureKind = LandingFailureClassifier.Classify(ex);
                        landingStep = BlackBoxSiteFacts.LandingStepOf(ex, BlackBoxStepEnum.TargetIntegration);
                        landingException = ex;
                    }
                }

                if (!synchronizationFailed && !landingModeIsNone && !landingModeIsMergeQueue)
                {
                    try
                    {
                        WorkflowProfile? profile = vessel == null ? null : await new WorkflowProfileService(_Database, _Logging)
                            .ResolveForVesselAsync(new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = vessel.TenantId }, vessel).ConfigureAwait(false);
                        IHostCommandExecutor commands = harborDock ? DockHosts!.ForDock(dock).Commands
                            : LocalVerificationCommands ?? new LocalHostCommandExecutor();
                        Harbor? harbor = harborDock ? await _Database.Harbors.ReadAsync(dock.HarborId!).ConfigureAwait(false) : null;
                        bool windows = harborDock ? FleetActionShellCommandBuilder.IsWindowsPlatform(harbor?.OsPlatform) : OperatingSystem.IsWindows();
                        mission.CommitHash = await LandingVerification.VerifyAsync(_Database, git, commands, dock.WorktreePath!, windows,
                            LandingVerification.Commands(profile, vessel), _Settings.StageCheckTimeoutMinutes, mission).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        synchronizationFailed = true;
                        landingAttempted = true;
                        landingFailureKind = MissionFailureKindEnum.Infra;
                        landingFailureReason = "Landing verification refused publication: " + ex.Message;
                        landingStep = BlackBoxStepEnum.Verification;
                        landingException = ex;
                    }
                }

                Mission? publishOwner = await _Database.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                if (publishOwner == null || publishOwner.Status != MissionStatusEnum.WorkProduced || publishOwner.LastUpdateUtc != landingVersion)
                    return;

                if (synchronizationFailed) { /* Record LandingFailed through the common path below. */ }
                else if (effectivePr)
                {
                    // Push + PR flow
                    landingAttempted = true;
                    BlackBoxStepEnum prStep = BlackBoxStepEnum.Push;
                    try
                    {
                        await git.PushBranchAsync(dock.WorktreePath).ConfigureAwait(false);
                        _Logging.Debug(_Header + "pushed branch " + dock.BranchName);
                        prStep = BlackBoxStepEnum.PullRequest;

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

                        string prUrl = await git.CreatePullRequestAsync(
                            dock.WorktreePath,
                            mission.Title,
                            prBody).ConfigureAwait(false);

                        mission.PrUrl = prUrl;
                        _Logging.Info(_Header + "created PR: " + prUrl);

                        // PR created -- transition to PullRequestOpen (not Complete).
                        // Complete is reserved for when the PR is actually merged.
                        mission.Status = MissionStatusEnum.PullRequestOpen;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, landingVersion, new[] { MissionStatusEnum.WorkProduced }).ConfigureAwait(false)) return;
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
                            _WebSocketHub.BroadcastToTenant(mission.TenantId, "mission.pull_request_open", "Pull request opened: " + mission.Title, new
                            {
                                entityType = "mission",
                                entityId = mission.Id,
                                captainId = mission.CaptainId,
                                missionId = mission.Id,
                                vesselId = mission.VesselId,
                                voyageId = mission.VoyageId
                            });
                            _WebSocketHub.BroadcastMissionChange(mission, MissionStatusEnum.PullRequestOpen.ToString());
                        }

                        // PR path handles its own status -- skip the generic landing result block below
                        landingSucceeded = false;
                        landingAttempted = false;

                        // Auto-merge if enabled
                        if (effectiveMerge && !String.IsNullOrEmpty(prUrl))
                        {
                            try
                            {
                                await git.EnableAutoMergeAsync(dock.WorktreePath, prUrl).ConfigureAwait(false);
                                _Logging.Debug(_Header + "enabled auto-merge for PR: " + prUrl);

                                // Poll for merge completion, then transition to Complete
                                if (vessel != null && !String.IsNullOrEmpty(targetWorkDir) && !String.IsNullOrEmpty(sourceRepoPath) && !String.IsNullOrEmpty(dock.BranchName))
                                {
                                    _ = PollAndPullAfterMergeAsync(git, targetWorkDir, sourceRepoPath, dock.BranchName, prUrl, mission.Id, cleanupPolicy);
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
                        _Logging.Warn(_Header + "error pushing/creating PR for mission " + mission.Id + ": " + ex.ToString());
                        landingSucceeded = false;
                        landingFailureReason = "Error pushing/creating PR: " + ex.Message;
                        landingFailureKind = LandingFailureClassifier.Classify(ex);
                        landingStep = prStep;
                        landingException = ex;
                    }
                }
                else if (!landingModeIsNone
                    && !landingModeIsMergeQueue
                    && vessel != null && (effectivePush || !String.IsNullOrEmpty(targetWorkDir)) && !String.IsNullOrEmpty(sourceRepoPath))
                {
                    // Local merge runs for LocalMerge and MergeAndPush. An explicit None (manual landing)
                    // or MergeQueue must never merge into the user's working directory, even when one is configured.
                    // Check if the mission actually produced mergeable changes.
                    // Pipeline stages like Architect may complete without code changes (they output
                    // mission markers to stdout instead). Skip merge if no changes were made.
                    // Use the DiffSnapshot (captured before handoff) and the branch existence in
                    // the bare repo as indicators -- the worktree may already be gone at this point.
                    bool hasChanges = true;
                    bool branchExists = false;
                    if (!String.IsNullOrEmpty(dock.BranchName) && !String.IsNullOrEmpty(sourceRepoPath))
                    {
                        // Also check if the branch actually exists in the bare repo
                        try
                        {
                            branchExists = await git.BranchExistsAsync(sourceRepoPath, dock.BranchName).ConfigureAwait(false);
                        }
                        catch { }
                    }

                    if (String.IsNullOrEmpty(mission.DiffSnapshot) || mission.DiffSnapshot.Trim().Length == 0)
                    {
                        hasChanges = false;
                        _Logging.Debug(_Header + "mission " + mission.Id + " has no diff snapshot -- no code changes to merge");
                    }
                    else if (!branchExists)
                    {
                        hasChanges = false;
                        _Logging.Debug(_Header + "mission " + mission.Id + " branch " + dock.BranchName + " not in bare repo -- no code changes to merge");
                    }

                    if (!hasChanges)
                    {
                        // Missing diff/branch information is not proof that the work landed. In particular,
                        // a stale completion racing a resume can arrive before a diff is available.
                        // Keep the branch and the mission recoverable instead of reporting a successful merge.
                        mission.Status = MissionStatusEnum.WorkProduced;
                        mission.CompletedUtc = null;
                        mission.FailureKind = MissionFailureKindEnum.Infra;
                        mission.FailureReason = !branchExists
                            ? "Landing was not attempted: the source branch could not be confirmed. Work remains unlanded."
                            : "Landing was not attempted: the mission diff snapshot is unavailable. Work remains unlanded.";
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                        _Logging.Warn(_Header + "mission " + mission.Id + " remains WorkProduced: " + mission.FailureReason);
                        // Non-terminal: the mission stays WorkProduced with its branch kept.
                        await BlackBox.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.LocalMerge, context =>
                        {
                            context.Vessel = vessel;
                            context.Voyage = voyage;
                            context.Dock = dock;
                            context.GitFacts = new BlackBoxGitFacts { CheckoutPath = targetWorkDir, GitOperation = "rev-parse --verify " + dock.BranchName, WorktreeExisted = landingFacts.WorktreeExisted };
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                    // Local merge flow: fetch captain's branch from bare repo and merge into user's working directory
                    landingAttempted = true;
                    BlackBoxStepEnum mergeStep = effectivePush ? BlackBoxStepEnum.Push : BlackBoxStepEnum.FastForward;
                    try
                    {
                        if (effectivePush)
                        {
                            // Publish from the isolated mission dock. The developer checkout may be dirty or on another branch.
                            await git.PushVerifiedCommitAsync(dock.WorktreePath!, mission.CommitHash!, vessel.DefaultBranch).ConfigureAwait(false);
                            landingSucceeded = true;
                            if (!String.IsNullOrEmpty(targetWorkDir))
                                await SyncCheckoutAfterPrMergeAsync(git, targetWorkDir, vessel.DefaultBranch, mission).ConfigureAwait(false);
                        }
                        else
                        {
                            await git.FastForwardVerifiedCommitAsync(targetWorkDir!, sourceRepoPath, mission.CommitHash!, vessel.DefaultBranch).ConfigureAwait(false);
                            landingSucceeded = true;
                        }

                        // Only clean up the mission branch after confirmed success (merge + push).
                        // Preserve branch on failure for retry.
                        if (landingSucceeded)
                        {
                            await CleanupMissionBranchAsync(
                                git,
                                sourceRepoPath,
                                targetWorkDir ?? dock.WorktreePath!,
                                dock.BranchName,
                                cleanupPolicy,
                                "after successful landing",
                                dock.WorktreePath).ConfigureAwait(false);
                        }
                        else if (!landingSucceeded)
                        {
                            _Logging.Debug(_Header + "preserving branch " + dock.BranchName + " for retry (landing failed)");
                        }
                        else
                        {
                            _Logging.Debug(_Header + "branch cleanup policy is None -- retaining branch " + dock.BranchName + " for inspection");
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error merging locally for mission " + mission.Id + ": " + ex.ToString() + " -- branch " + dock.BranchName + " is still available in the bare repo");
                        landingSucceeded = false;
                        landingFailureReason = "Error " + (effectivePush ? "publishing verified commit" : "merging locally") + ": " + ex.Message;

                        // A dirty checkout or unpushed target commits are the operator's to resolve; a rescue mission cannot
                        // fix them, so record them as an environment failure rather than a landing conflict.
                        landingFailureKind = LandingFailureClassifier.Classify(ex);
                        landingStep = BlackBoxSiteFacts.LandingStepOf(ex, mergeStep);
                        landingException = ex;
                        if (ex is GitDirtyCheckoutException dirty)
                        {
                            dirtyCheckout = dirty;
                            landingFailureReason += _Settings.LandingDeferralDeadlineHours > 0
                                ? ". Resolve those changes; landing is retried automatically once the checkout is clean (for up to "
                                    + _Settings.LandingDeferralDeadlineHours + " hours), or retry landing yourself; the branch " + dock.BranchName + " is kept."
                                : ". Resolve those changes, then retry landing; the branch " + dock.BranchName + " is kept.";
                        }
                    }
                    } // end hasChanges else
                }
                else if (landingModeIsMergeQueue)
                {
                    // MergeQueue mode: auto-enqueue the branch into the merge queue.
                    // Processing (test-and-land) remains a separate trigger via process_merge_queue.
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
                        _Logging.Warn(_Header + "error auto-enqueuing merge entry for mission " + mission.Id + ": " + mqEx.Message + " -- branch " + dock.BranchName + " is still available for manual enqueue");
                    }
                    // Mission stays as WorkProduced; merge queue processing will land it
                }
                else if (harborDock && String.IsNullOrEmpty(targetWorkDir) && !landingModeIsNone)
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " work produced: branch " + dock.BranchName + " is in Harbor " + dock.HarborId
                        + "'s own clone " + dock.RepositoryPath + ", and that Harbor has no checkout of the vessel to land into (landing mode: " + resolvedLandingMode
                        + "); map the vessel to a checkout in Harbor > Settings > Repositories, or use PullRequest landing");
                }
                else
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " work produced -- branch " + dock.BranchName + " available in bare repo (landing mode: " + resolvedLandingMode + ")");
                    // No landing configured or LandingMode.None -- mission stays as WorkProduced
                }
            }
            finally
            {
                vesselLock.Release();
                _Logging.Debug(_Header + "merge lock released for vessel " + vesselLockKey + " (mission " + mission.Id + ")");
            }

            // The landing operation can outlive cancellation or another owner. Never overwrite its newer state.
            Mission? latest = await _Database.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
            if (latest == null || latest.Status != MissionStatusEnum.WorkProduced || latest.StartedUtc != mission.StartedUtc || latest.LastUpdateUtc != landingVersion)
                return;

            // Transition mission status based on landing result
            if (landingAttempted)
            {
                if (landingSucceeded)
                {
                    mission.Status = MissionStatusEnum.Complete;
                    mission.FailureKind = null;
                    mission.FailureReason = null;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, landingVersion, new[] { MissionStatusEnum.WorkProduced }).ConfigureAwait(false)) return;
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
                    mission.FailureKind = landingFailureKind;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, landingVersion, new[] { MissionStatusEnum.WorkProduced }).ConfigureAwait(false)) return;
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

                    if (dirtyCheckout != null)
                        await RecordLandingDeferralAsync(mission, dock, dirtyCheckout).ConfigureAwait(false);

                    landingFacts.BranchTip ??= syncFacts.Before ?? mission.CommitHash;
                    landingFacts.TargetTipAfter ??= syncFacts.RemoteTargetTip ?? syncFacts.LocalTargetTip;
                    await BlackBox.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, landingStep, context =>
                    {
                        context.Vessel = vessel;
                        context.Voyage = voyage;
                        context.Dock = dock;
                        context.GitFacts = landingFacts;
                        BlackBoxSiteFacts.ApplyException(context, landingException);
                        context.Note = "Landing mode " + resolvedLandingMode + (harborDock ? " on Harbor " + dock.HarborId : "") + ".";
                    }).ConfigureAwait(false);
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

                _WebSocketHub.BroadcastToTenant(mission.TenantId, eventType, eventMessage, new
                {
                    entityType = "mission",
                    entityId = mission.Id,
                    captainId = mission.CaptainId,
                    missionId = mission.Id,
                    vesselId = mission.VesselId,
                    voyageId = mission.VoyageId
                });

                // Broadcast specific mission change for dashboard toast notifications
                _WebSocketHub.BroadcastMissionChange(mission, mission.Status.ToString());
            }

            // NOTE: Dock reclaim is NOT done here. MissionService.HandleCompletionAsync
            // owns the full finalization sequence (reclaim dock, release captain, dispatch next)
            // to prevent duplicate reclaim calls from racing.
        }

        /// <summary>
        /// Broadcast a settled voyage to the dashboard using the status already stored on the voyage.
        /// </summary>
        public Task HandleVoyageCompleteAsync(Voyage voyage)
        {
            _Logging.Info(_Header + "voyage " + voyage.Id + " reached status " + voyage.Status + ", broadcasting to dashboard");

            if (_WebSocketHub != null)
            {
                _WebSocketHub.BroadcastVoyageChange(voyage);
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
            string? pullDir = vessel?.WorkingDirectory;
            IGitService git = _Git;

            // A mission whose dock was on a Harbor is checked, and pulled, on that Harbor (its checkout is there).
            Dock? missionDock = !String.IsNullOrEmpty(mission.DockId) ? await _Database.Docks.ReadAsync(mission.DockId).ConfigureAwait(false) : null;
            if (missionDock != null && IsHarborDock(missionDock))
            {
                if (DockHosts?.Harbors == null || !DockHosts.Harbors.IsConnected(missionDock.HarborId!)) return false;
                git = DockHosts.ForDock(missionDock).Git;
                workingDir = missionDock.CheckoutPath ?? missionDock.RepositoryPath;
                pullDir = missionDock.CheckoutPath;
            }

            if (String.IsNullOrEmpty(workingDir)) return false;

            try
            {
                bool merged = await git.IsPrMergedAsync(workingDir, mission.PrUrl).ConfigureAwait(false);
                if (merged)
                {
                    mission.Status = MissionStatusEnum.Complete;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "PR reconciler: mission " + mission.Id + " PR merged, status set to Complete");

                    // Bring the checkout up to date without disturbing the user's work there: fetch, and fast-forward
                    // the target branch only when it is checked out and clean.
                    if (!String.IsNullOrEmpty(pullDir))
                    {
                        await SyncCheckoutAfterPrMergeAsync(git, pullDir, vessel?.DefaultBranch ?? "main").ConfigureAwait(false);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "PR reconciler: error checking PR status for mission " + mission.Id + ": " + ex.ToString());
            }

            return false;
        }

        /// <summary>
        /// Poll for PR merge completion, pull latest, transition mission to Complete, and clean up branches.
        /// </summary>
        public Task PollAndPullAfterMergeAsync(string workingDirectory, string bareRepoPath, string branchName, string prUrl, string missionId, BranchCleanupPolicyEnum cleanupPolicy = BranchCleanupPolicyEnum.LocalOnly)
        {
            return PollAndPullAfterMergeAsync(_Git, workingDirectory, bareRepoPath, branchName, prUrl, missionId, cleanupPolicy);
        }

        #endregion

        #region Private-Methods

        private async Task PollAndPullAfterMergeAsync(IGitService git, string workingDirectory, string bareRepoPath, string branchName, string prUrl, string missionId, BranchCleanupPolicyEnum cleanupPolicy)
        {
            try
            {
                // Poll for up to 5 minutes (30 attempts, 10 seconds apart)
                // Uses the vessel working directory (not the mission dock) for gh CLI context,
                // since the dock worktree may be reclaimed before the PR merges.
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                    bool merged = await git.IsPrMergedAsync(workingDirectory, prUrl).ConfigureAwait(false);
                    if (merged)
                    {
                        _Logging.Debug(_Header + "PR " + prUrl + " merged, updating " + workingDirectory);
                        await SyncCheckoutAfterPrMergeAsync(git, workingDirectory, await MissionTargetBranchAsync(missionId).ConfigureAwait(false)).ConfigureAwait(false);

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
                                    _WebSocketHub.BroadcastToTenant(mission.TenantId, "mission.completed", "Mission completed (PR merged): " + mission.Title, new
                                    {
                                        entityType = "mission",
                                        entityId = mission.Id,
                                        captainId = mission.CaptainId,
                                        missionId = mission.Id,
                                        vesselId = mission.VesselId,
                                        voyageId = mission.VoyageId
                                    });
                                    _WebSocketHub.BroadcastMissionChange(mission, MissionStatusEnum.Complete.ToString());
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
                                await git.DeleteLocalBranchAsync(bareRepoPath, branchName).ConfigureAwait(false);
                                _Logging.Debug(_Header + "deleted branch " + branchName + " from bare repo after PR merge");
                            }
                            catch (Exception branchEx)
                            {
                                _Logging.Warn(_Header + "failed to delete branch " + branchName + " from bare repo: " + branchEx.Message);
                            }

                            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
                            {
                                try
                                {
                                    await git.DeleteRemoteBranchAsync(workingDirectory, branchName).ConfigureAwait(false);
                                    _Logging.Debug(_Header + "deleted remote branch " + branchName + " after PR merge");
                                }
                                catch (Exception remoteBranchEx)
                                {
                                    _Logging.Warn(_Header + "failed to delete remote branch " + branchName + ": " + remoteBranchEx.Message);
                                }
                            }
                        }
                        else
                        {
                            _Logging.Debug(_Header + "branch cleanup policy is None -- retaining branch " + branchName + " for inspection");
                        }

                        return;
                    }
                }

                _Logging.Info(_Header + "PR " + prUrl + " not merged within 5 minutes -- mission " + missionId + " stays PullRequestOpen until merge is confirmed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error polling/pulling after merge for mission " + missionId + ": " + ex.ToString());
            }
        }

        /// <summary>
        /// After a pull request merged: fetch in the checkout, and fast-forward the target branch only when it is the
        /// branch checked out there, the working tree is clean, and it has no commits of its own. Otherwise the checkout
        /// is left as it is (only its remote-tracking refs move).
        /// </summary>
        private async Task SyncCheckoutAfterPrMergeAsync(IGitService git, string checkoutPath, string targetBranch, Mission? landedMission = null)
        {
            // Best effort only: the work is already published, so a checkout that cannot follow never fails the landing.
            try
            {
                await git.FetchRemotesAsync(checkoutPath).ConfigureAwait(false);

                string? current = await git.GetCurrentBranchAsync(checkoutPath).ConfigureAwait(false);
                if (!String.Equals(current, targetBranch, StringComparison.Ordinal))
                {
                    _Logging.Debug(_Header + "fetched in " + checkoutPath + "; not fast-forwarding " + targetBranch + " because the checkout is on " + (current ?? "a detached HEAD"));
                    await RecordCheckoutSyncSkippedAsync(landedMission, checkoutPath, "the checkout is on " + (current ?? "a detached HEAD") + ", not " + targetBranch).ConfigureAwait(false);
                    return;
                }

                GitWorkingTreeStatus status = await git.GetWorkingTreeStatusAsync(checkoutPath).ConfigureAwait(false);
                if (!status.IsClean)
                {
                    _Logging.Debug(_Header + "fetched in " + checkoutPath + "; not fast-forwarding " + targetBranch + " because the working tree has changes");
                    await RecordCheckoutSyncSkippedAsync(landedMission, checkoutPath, "the working tree has changes").ConfigureAwait(false);
                    return;
                }

                bool? behindOnly = await git.IsAncestorAsync(checkoutPath, "HEAD", "refs/remotes/origin/" + targetBranch).ConfigureAwait(false);
                if (behindOnly != true)
                {
                    _Logging.Debug(_Header + "fetched in " + checkoutPath + "; " + targetBranch + " cannot be fast-forwarded to origin");
                    await RecordCheckoutSyncSkippedAsync(landedMission, checkoutPath, targetBranch + " has commits that are not on origin, so it cannot be fast-forwarded").ConfigureAwait(false);
                    return;
                }

                await git.PullAsync(checkoutPath).ConfigureAwait(false);
                _Logging.Debug(_Header + "fast-forwarded " + targetBranch + " in " + checkoutPath + " after PR merge");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not update " + checkoutPath + " after PR merge: " + ex.Message);
                await RecordCheckoutSyncSkippedAsync(landedMission, checkoutPath, ex.Message).ConfigureAwait(false);
            }
        }

        private async Task RecordCheckoutSyncSkippedAsync(Mission? mission, string checkoutPath, string why)
        {
            if (mission == null) return;
            _Logging.Warn(_Header + "mission " + mission.Id + " landed, but " + checkoutPath + " was not updated: " + why);
            try
            {
                await _Database.Events.CreateAsync(new ArmadaEvent("mission.checkout_sync_skipped",
                    "Landed; the developer checkout " + checkoutPath + " was left as it is because " + why + ".")
                {
                    TenantId = mission.TenantId, UserId = mission.UserId, EntityType = "mission", EntityId = mission.Id,
                    MissionId = mission.Id, VesselId = mission.VesselId, VoyageId = mission.VoyageId, CaptainId = mission.CaptainId
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error emitting mission.checkout_sync_skipped for " + mission.Id + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Make sure the dock's worktree exists before landing runs git in it. Returns null when landing can go on, or the
        /// Infra failure reason when the worktree is gone and cannot be recreated from the mission branch.
        /// </summary>
        private async Task<string?> EnsureLandingDockAsync(Mission mission, Dock dock, BlackBoxGitFacts facts)
        {
            bool? exists = await _Docks.WorktreeExistsAsync(dock).ConfigureAwait(false);
            if (exists == true) facts.WorktreeExisted = true;
            if (exists != false) return null;

            // The worktree was gone: whether it is recreated below or not, landing did not find it where it was left.
            facts.WorktreeExisted = false;

            string where = DockHostResolver.IsHarborDock(dock) ? " on Harbor " + dock.HarborId : "";
            _Logging.Warn(_Header + "dock " + dock.Id + " worktree " + dock.WorktreePath + where + " is missing at landing of mission " + mission.Id
                + "; recreating it from branch " + dock.BranchName);

            string? why = null;
            Dock? recorded = String.IsNullOrEmpty(dock.Id) ? null : await _Database.Docks.ReadAsync(dock.Id).ConfigureAwait(false);
            if (recorded == null)
            {
                why = "the dock is not recorded, so there is nothing to recreate";
            }
            else
            {
                DockRestoreResult restored = await _Docks.RestoreWorktreeAsync(dock.Id, "landing mission " + mission.Id, true).ConfigureAwait(false);
                if (restored.Restored)
                {
                    if (!String.IsNullOrEmpty(mission.CommitHash) && !String.IsNullOrEmpty(restored.HeadCommit)
                        && !String.Equals(mission.CommitHash, restored.HeadCommit, StringComparison.OrdinalIgnoreCase))
                    {
                        return "Dock missing: the worktree " + dock.WorktreePath + where + " for dock " + dock.Id + " was recreated from branch '"
                            + dock.BranchName + "', but the branch is at " + restored.HeadCommit + ", not the mission's commit " + mission.CommitHash
                            + ". Point the branch at the mission's commit, then retry landing.";
                    }
                    return null;
                }
                why = restored.Reason;
            }

            return "Dock missing: the worktree " + dock.WorktreePath + where + " for dock " + dock.Id + " no longer exists and could not be recreated from branch '"
                + dock.BranchName + "'" + (String.IsNullOrEmpty(why) ? "" : " (" + why + ")") + ". Restore the branch or the worktree, then retry landing.";
        }

        /// <summary>
        /// Record that a LocalMerge landing waits on a dirty checkout, so the landing-integrity sweep retries it once the
        /// checkout is clean. The first deferral of a run of retries sets the deadline.
        /// </summary>
        private async Task RecordLandingDeferralAsync(Mission mission, Dock dock, GitDirtyCheckoutException dirty)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                DateTime first = now;
                EnumerationResult<ArmadaEvent> previous = await _Database.Events.EnumerateAsync(new EnumerationQuery
                {
                    MissionId = mission.Id,
                    EventType = LandingIntegrityService.DeferredEventType,
                    PageSize = 1,
                    Order = EnumerationOrderEnum.CreatedDescending
                }).ConfigureAwait(false);
                LandingDeferral? prior = LandingIntegrityService.ReadDeferral(previous.Objects.FirstOrDefault());
                if (prior != null && prior.FirstDeferredUtc > now.AddHours(-_Settings.LandingDeferralDeadlineHours)) first = prior.FirstDeferredUtc;

                LandingDeferral deferral = new LandingDeferral
                {
                    CheckoutPath = dirty.CheckoutPath,
                    HarborId = DockHostResolver.IsHarborDock(dock) ? dock.HarborId : null,
                    BranchName = dock.BranchName,
                    ModifiedPaths = new List<string>(dirty.ModifiedPaths),
                    FirstDeferredUtc = first,
                    DeadlineUtc = first.AddHours(_Settings.LandingDeferralDeadlineHours)
                };
                ArmadaEvent evt = new ArmadaEvent(LandingIntegrityService.DeferredEventType,
                    "Landing waits for " + dirty.CheckoutPath + " to be clean; retried automatically until " + deferral.DeadlineUtc.ToString("u"))
                {
                    TenantId = mission.TenantId, UserId = mission.UserId, EntityType = "mission", EntityId = mission.Id,
                    MissionId = mission.Id, VesselId = mission.VesselId, VoyageId = mission.VoyageId, CaptainId = mission.CaptainId,
                    Payload = System.Text.Json.JsonSerializer.Serialize(deferral)
                };
                await _Database.Events.CreateAsync(evt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error recording landing deferral for " + mission.Id + ": " + ex.Message);
            }
        }

        private async Task<string> MissionTargetBranchAsync(string missionId)
        {
            try
            {
                Mission? mission = await _Database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                if (mission != null && !String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                    if (vessel != null && !String.IsNullOrEmpty(vessel.DefaultBranch)) return vessel.DefaultBranch;
                }
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not read the target branch for mission " + missionId + ": " + ex.Message);
            }

            return "main";
        }

        /// <summary>
        /// Whether a dock lives on a Harbor (and this Admiral can reach Harbors).
        /// </summary>
        private bool IsHarborDock(Dock dock)
        {
            return DockHosts != null && DockHostResolver.IsHarborDock(dock);
        }

        /// <summary>
        /// The git service for the host a dock lives on.
        /// </summary>
        private IGitService GitFor(Dock dock)
        {
            return IsHarborDock(dock) ? DockHosts!.ForDock(dock).Git : _Git;
        }

        private async Task CleanupMissionBranchAsync(
            IGitService git,
            string bareRepoPath,
            string workingDirectory,
            string branchName,
            BranchCleanupPolicyEnum cleanupPolicy,
            string cleanupReason,
            string? activeWorktreePath = null)
        {
            if (String.IsNullOrEmpty(branchName)) return;

            if (cleanupPolicy == BranchCleanupPolicyEnum.None)
            {
                _Logging.Debug(_Header + "branch cleanup policy is None - retaining branch " + branchName + " for inspection");
                return;
            }

            if (!String.IsNullOrEmpty(activeWorktreePath))
            {
                try
                {
                    await git.RemoveWorktreeAsync(activeWorktreePath).ConfigureAwait(false);
                    _Logging.Debug(_Header + "removed active worktree " + activeWorktreePath + " before deleting branch " + branchName);
                }
                catch (Exception worktreeEx)
                {
                    _Logging.Warn(_Header + "failed to remove active worktree " + activeWorktreePath + " before deleting branch " + branchName + ": " + worktreeEx.Message);
                }
            }

            try
            {
                await git.DeleteLocalBranchAsync(bareRepoPath, branchName).ConfigureAwait(false);
                _Logging.Debug(_Header + "deleted branch " + branchName + " from bare repo " + cleanupReason);
            }
            catch (Exception branchEx)
            {
                _Logging.Warn(_Header + "failed to delete branch " + branchName + " from bare repo: " + branchEx.Message);
            }

            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
            {
                try
                {
                    await git.DeleteRemoteBranchAsync(workingDirectory, branchName).ConfigureAwait(false);
                    _Logging.Debug(_Header + "deleted remote branch " + branchName + " " + cleanupReason);
                }
                catch (Exception remoteBranchEx)
                {
                    _Logging.Warn(_Header + "failed to delete remote branch " + branchName + ": " + remoteBranchEx.Message);
                }
            }
        }

        #endregion
    }
}
