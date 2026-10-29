namespace Armada.Core.Services
{
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for dock (worktree) lifecycle management.
    /// </summary>
    public class DockService : IDockService
    {
        #region Public-Members

        /// <summary>
        /// Where docks live. Required to create or work on Harbor-hosted docks; without it every dock is on the Admiral's
        /// host.
        /// </summary>
        public DockHostResolver? Hosts { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _RepoProvisionLocks =
            new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DockProvisionFailure> _ProvisionFailures =
            new System.Collections.Concurrent.ConcurrentDictionary<string, DockProvisionFailure>(StringComparer.Ordinal);

        private string _Header = "[DockService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService _Git;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="git">Git service.</param>
        public DockService(LoggingModule logging, DatabaseDriver database, ArmadaSettings settings, IGitService git)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Dock?> ProvisionAsync(Vessel vessel, Captain captain, string branchName, string? missionId = null, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            string repoPath = vessel.LocalPath ?? Path.Combine(_Settings.ReposDirectory, vessel.Name + ".git");

            // Every provisioning attempt owns a distinct path, including redispatches of the same mission.
            // A delayed finalizer for an old dock must never remove a replacement attempt's worktree.
            Dock dock = new Dock(vessel.Id);
            string dockDirName = (!String.IsNullOrEmpty(missionId) ? missionId : captain.Name) + "_" + dock.Id;
            string worktreePath = Path.Combine(_Settings.DocksDirectory, vessel.Name, dockDirName);
            string normalizedRepoPath = Path.GetFullPath(repoPath);
            SemaphoreSlim repoLock = _RepoProvisionLocks.GetOrAdd(normalizedRepoPath, _ => new SemaphoreSlim(1, 1));
            bool repoLockAcquired = false;
            BlackBoxStepEnum step = BlackBoxStepEnum.DockProvision;
            if (!String.IsNullOrEmpty(missionId)) _ProvisionFailures.TryRemove(missionId, out DockProvisionFailure? _);

            try
            {
                await repoLock.WaitAsync(token).ConfigureAwait(false);
                repoLockAcquired = true;

                // Ensure bare clone exists. If the directory exists but isn't a valid repo
                // (e.g. leftover from a failed clone/seed), remove it and re-clone.
                if (Directory.Exists(repoPath) && !await _Git.IsRepositoryAsync(repoPath, token).ConfigureAwait(false))
                {
                    if (!ManagedPaths.IsStrictlyUnder(repoPath, _Settings.ReposDirectory))
                        throw new InvalidOperationException("Vessel " + vessel.Name + " LocalPath " + repoPath + " exists but is not a git repository, and it is outside the managed repos directory, so Armada will not remove it.");
                    _Logging.Warn(_Header + "removing corrupt/incomplete repo directory: " + repoPath);
                    await ForceRemoveDirectoryAsync(repoPath, token).ConfigureAwait(false);
                }

                if (!await _Git.IsRepositoryAsync(repoPath, token).ConfigureAwait(false))
                {
                    if (String.IsNullOrEmpty(vessel.RepoUrl))
                        throw new InvalidOperationException("Vessel " + vessel.Name + " has no remote URL configured");
                    await _Git.CloneBareAsync(vessel.RepoUrl, repoPath, token).ConfigureAwait(false);
                    vessel.LocalPath = repoPath;
                    await _Database.Vessels.UpdateAsync(vessel, token).ConfigureAwait(false);
                }

                // Ensure the configured default branch exists locally. Missing configured branches
                // in non-empty repos should be created from the remote/default history rather than
                // being treated as an empty repository.
                bool hasDefaultBranch = await _Git.EnsureLocalBranchAsync(repoPath, vessel.DefaultBranch, token).ConfigureAwait(false);
                if (!hasDefaultBranch)
                {
                    _Logging.Info(_Header + "bare repo for " + vessel.Name + " has no usable branch history for " + vessel.DefaultBranch + " -- seeding initial commit");
                    await SeedEmptyRepoAsync(vessel, repoPath, token).ConfigureAwait(false);
                }
                else if (String.IsNullOrEmpty(vessel.LocalPath))
                {
                    _Logging.Info(_Header + "bare repo exists but vessel LocalPath is empty for " + vessel.Name + ", updating to " + repoPath);
                    vessel.LocalPath = repoPath;
                    await _Database.Vessels.UpdateAsync(vessel, token).ConfigureAwait(false);
                }

                // Fetch latest from remote to ensure worktrees branch from current main
                if (!String.IsNullOrEmpty(vessel.RepoUrl))
                {
                    try
                    {
                        await _Git.FetchAsync(repoPath, token).ConfigureAwait(false);
                    }
                    catch (Exception fetchEx)
                    {
                        _Logging.Warn(_Header + "fetch failed for " + vessel.Name + ", continuing with local state: " + fetchEx.Message);
                    }
                }

                // Prune stale worktree registrations (handles "missing but registered" entries)
                try
                {
                    await _Git.PruneWorktreesAsync(repoPath, token).ConfigureAwait(false);
                }
                catch { }

                // Clean up stale worktree directories under this vessel's dock directory.
                // Only removes directories that are NOT associated with any active dock record.
                string vesselDockDir = Path.Combine(_Settings.DocksDirectory, vessel.Name);
                if (Directory.Exists(vesselDockDir))
                {
                    // Query active docks for this vessel to avoid deleting in-use worktrees
                    List<Dock> vesselDocks = await _Database.Docks.EnumerateByVesselAsync(vessel.Id, token).ConfigureAwait(false);
                    HashSet<string> activeDockPaths = new HashSet<string>(
                        vesselDocks
                            .Where(d => d.Active && !String.IsNullOrEmpty(d.WorktreePath))
                            .Select(d => d.WorktreePath!),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (string existingDir in Directory.GetDirectories(vesselDockDir))
                    {
                        string dirName = Path.GetFileName(existingDir);

                        // Skip the current captain's directory -- handled below
                        if (dirName == captain.Name) continue;

                        // Skip directories belonging to active docks
                        if (activeDockPaths.Contains(existingDir))
                        {
                            _Logging.Info(_Header + "skipping cleanup of " + existingDir + ": still in use by an active dock");
                            continue;
                        }

                        // Only clean up directories that look like git worktrees or repos
                        // and are not associated with any active dock.
                        string dotGitPath = Path.Combine(existingDir, ".git");
                        if (File.Exists(dotGitPath) || Directory.Exists(dotGitPath))
                        {
                            // Only attempt git worktree remove if the path is actually registered
                            bool isRegistered = await _Git.IsWorktreeRegisteredAsync(repoPath, existingDir, token).ConfigureAwait(false);
                            if (isRegistered)
                            {
                                _Logging.Debug(_Header + "cleaning up stale worktree from previous captain: " + existingDir);
                                try
                                {
                                    await _Git.RemoveWorktreeAsync(existingDir, token).ConfigureAwait(false);
                                }
                                catch { }
                            }
                            else
                            {
                                _Logging.Debug(_Header + "removing unregistered worktree directory: " + existingDir);
                            }

                            if (Directory.Exists(existingDir))
                            {
                                await ForceRemoveDirectoryAsync(existingDir, token).ConfigureAwait(false);
                            }

                            // Re-prune after removing stale worktrees
                            try
                            {
                                await _Git.PruneWorktreesAsync(repoPath, token).ConfigureAwait(false);
                            }
                            catch { }
                        }
                    }
                }

                // Clean up stale worktree directory if it exists from a previous run
                if (Directory.Exists(worktreePath))
                {
                    bool isRegistered = await _Git.IsWorktreeRegisteredAsync(repoPath, worktreePath, token).ConfigureAwait(false);
                    if (isRegistered)
                    {
                        _Logging.Debug(_Header + "removing stale dock directory: " + worktreePath);
                        try
                        {
                            await _Git.RemoveWorktreeAsync(worktreePath, token).ConfigureAwait(false);
                        }
                        catch (Exception rmEx)
                        {
                            _Logging.Warn(_Header + "git worktree remove failed for " + worktreePath + ": " + rmEx.Message);
                        }
                    }
                    else
                    {
                        _Logging.Debug(_Header + "removing unregistered dock directory: " + worktreePath);
                    }

                    await ForceRemoveDirectoryAsync(worktreePath, token).ConfigureAwait(false);
                }

                // Create worktree. If the branch already exists, GitService will attach to it
                // instead of recreating it so downstream pipeline stages and retries preserve work.
                step = BlackBoxStepEnum.WorktreeCreate;
                await _Git.CreateWorktreeAsync(repoPath, worktreePath, branchName, vessel.DefaultBranch, token).ConfigureAwait(false);

                step = BlackBoxStepEnum.StartCommitRecord;
                string? headCommit = await _Git.GetHeadCommitHashAsync(worktreePath, token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(headCommit))
                {
                    throw new InvalidOperationException("Provisioned worktree " + worktreePath +
                        " for branch " + branchName + " without a valid HEAD commit");
                }

                // Create dock record
                dock.TenantId = vessel.TenantId;
                dock.UserId = vessel.UserId;
                dock.CaptainId = captain.Id;
                dock.WorktreePath = worktreePath;
                dock.BranchName = branchName;
                // The start commit is durable on the dock (scope checks and the black box read it after a restart).
                DockStartCommit.Write(dock, headCommit, vessel.DefaultBranch);
                dock = await _Database.Docks.CreateAsync(dock, token).ConfigureAwait(false);

                try
                {
                    await PersistDockStartCommitAsync(dock.Id, headCommit, token).ConfigureAwait(false);
                }
                catch (Exception metadataEx)
                {
                    _Logging.Warn(_Header + "unable to persist dock start commit for " + dock.Id + ": " + metadataEx.Message);
                }

                ArmadaMetrics.DocksProvisioned.Add(1);
                _Logging.Info(_Header + "provisioned dock " + dock.Id + " at " + worktreePath);
                return dock;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "provisioning failed for vessel " + vessel.Id + " captain " + captain.Id + " repo " + (vessel.RepoUrl ?? "unknown") + ": " + ex.ToString());
                if (!String.IsNullOrEmpty(missionId))
                {
                    _ProvisionFailures[missionId] = new DockProvisionFailure
                    {
                        MissionId = missionId,
                        Step = step,
                        Exception = ex,
                        WorktreePath = worktreePath,
                        WorktreeExisted = Directory.Exists(worktreePath)
                    };
                }

                // Clean up partial state -- remove worktree directory if it was partially created
                if (Directory.Exists(worktreePath))
                {
                    await ForceRemoveDirectoryAsync(worktreePath, CancellationToken.None).ConfigureAwait(false);
                }

                return null;
            }
            finally
            {
                if (repoLockAcquired)
                    repoLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<Dock> ProvisionOnHarborAsync(Vessel vessel, Captain captain, string branchName, string missionId, string harborId, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));

            HarborConnectionManager? harbors = Hosts?.Harbors;
            if (harbors == null)
                throw new DockProvisioningException(vessel.Id, branchName, "Harbor delegation is not available on this Admiral, so no dock can be created on Harbor " + harborId + ".");

            Dock dock = new Dock(vessel.Id);
            string harbor = harbors.Describe(harborId);
            HarborDockClient client = new HarborDockClient(harbors);
            Armada.Core.Harbor.HarborDockResult? result;
            try
            {
                result = await client.ProvisionAsync(harborId, vessel, branchName, missionId + "_" + dock.Id, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                throw new DockProvisioningException(vessel.Id, branchName, "Harbor " + harbor + " could not create a dock for vessel " + vessel.Name + ": " + ex.Message);
            }

            if (result == null)
                throw new DockProvisioningException(vessel.Id, branchName, "Harbor " + harbor + " did not finish creating a dock for vessel " + vessel.Name
                    + " within " + (client.ProvisionTimeoutMs / 1000) + " seconds.");
            if (!result.Success || String.IsNullOrWhiteSpace(result.WorktreePath) || String.IsNullOrWhiteSpace(result.RepositoryPath))
                throw new DockProvisioningException(vessel.Id, branchName, "Harbor " + harbor + " could not create a dock for vessel " + vessel.Name + ": "
                    + (result.Message ?? "it did not say why") + ".");

            dock.TenantId = vessel.TenantId;
            dock.UserId = vessel.UserId;
            dock.CaptainId = captain.Id;
            dock.HarborId = harborId;
            dock.WorktreePath = result.WorktreePath;
            dock.RepositoryPath = result.RepositoryPath;
            dock.CheckoutPath = result.CheckoutPath;
            dock.BranchName = branchName;
            if (!String.IsNullOrEmpty(result.HeadCommit)) DockStartCommit.Write(dock, result.HeadCommit, vessel.DefaultBranch);
            dock = await _Database.Docks.CreateAsync(dock, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(result.HeadCommit))
            {
                try
                {
                    await PersistDockStartCommitAsync(dock.Id, result.HeadCommit, token).ConfigureAwait(false);
                }
                catch (Exception metadataEx)
                {
                    _Logging.Warn(_Header + "unable to persist dock start commit for " + dock.Id + ": " + metadataEx.Message);
                }
            }

            ArmadaMetrics.DocksProvisioned.Add(1);
            _Logging.Info(_Header + "provisioned dock " + dock.Id + " on Harbor " + harbor + " at " + dock.WorktreePath
                + " (" + result.Source + " repository " + dock.RepositoryPath + ")");
            return dock;
        }

        /// <inheritdoc />
        public async Task<bool> ReclaimAsync(string dockId, string? tenantId = null, CancellationToken token = default, string? forceReason = null, [CallerMemberName] string caller = "")
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Docks.ReadAsync(tenantId, dockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) return true;

            // Idempotency guard: if the dock is already inactive, it was already reclaimed.
            // This prevents duplicate worktree removal when both MissionService (background finalizer)
            // and ArmadaServer (HandleMissionCompleteAsync) both call ReclaimAsync for the same dock.
            if (!dock.Active)
            {
                _Logging.Debug(_Header + "dock " + dockId + " already reclaimed (Active=false) -- skipping");
                return true;
            }

            // One lease check for every reclaim path: work that has not landed (or is waiting on review, a pull request,
            // a landing retry or the merge queue) keeps its worktree unless the caller says explicitly why it may go.
            DockLeaseState lease = await ReadLeaseAsync(dock, token).ConfigureAwait(false);
            string reclaimCaller = String.IsNullOrWhiteSpace(caller) ? "unknown" : caller;
            if (lease.IsHeld && String.IsNullOrWhiteSpace(forceReason))
            {
                _Logging.Warn(_Header + "refusing to reclaim dock " + dock.Id + " for " + reclaimCaller + ": " + lease.Describe()
                    + "; its worktree " + dock.WorktreePath + " is kept");
                await EmitReclaimEventAsync("dock.reclaim_refused", "Kept dock " + dock.Id + " (" + lease.Describe() + "); reclaim requested by " + reclaimCaller,
                    dock, lease, reclaimCaller, null, true, token).ConfigureAwait(false);
                return false;
            }

            if (await HasOtherActivePathOwnerAsync(dock, token).ConfigureAwait(false))
            {
                _Logging.Warn(_Header + "retiring stale dock " + dock.Id + " without removing the worktree owned by another active dock");
            }
            else if (DockHostResolver.IsHarborDock(dock))
            {
                await RemoveHarborDockAsync(dock, token).ConfigureAwait(false);
            }
            else if (!String.IsNullOrEmpty(dock.WorktreePath))
            {
                try
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(dock.VesselId, token).ConfigureAwait(false);
                    bool isRegistered = !String.IsNullOrEmpty(vessel?.LocalPath) &&
                        await _Git.IsWorktreeRegisteredAsync(vessel.LocalPath, dock.WorktreePath, token).ConfigureAwait(false);

                    if (isRegistered)
                    {
                        await _Git.RemoveWorktreeAsync(dock.WorktreePath, token).ConfigureAwait(false);
                        _Logging.Info(_Header + "reclaimed dock " + dockId + " at " + dock.WorktreePath);
                    }
                    else
                    {
                        _Logging.Debug(_Header + "dock " + dockId + " worktree " + dock.WorktreePath +
                            " is not registered -- removing directory directly");
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error removing worktree for dock " + dockId + ": " + ex.ToString());
                }

                // Ensure the directory is actually removed -- on Windows, file handles
                // from the just-exited agent process can linger and block deletion.
                await ForceRemoveDirectoryAsync(dock.WorktreePath, token).ConfigureAwait(false);
            }

            TryDeleteDockStartCommitFile(dock.Id);

            // Update the dock record so DataExpiryService can purge it
            dock.Active = false;
            dock.CaptainId = null;
            dock.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Docks.UpdateAsync(dock, token).ConfigureAwait(false);
            ArmadaMetrics.DocksReclaimed.Add(1);

            MissionSummary? primary = lease.PrimaryMission;
            _Logging.Info(_Header + "dock " + dock.Id + " reclaimed by " + reclaimCaller
                + (primary != null ? " (mission " + primary.Id + " " + primary.Status + ")" : "")
                + (String.IsNullOrWhiteSpace(forceReason) ? "" : "; forced: " + forceReason));
            await EmitReclaimEventAsync("dock.reclaimed", "Reclaimed dock " + dock.Id + " for " + reclaimCaller
                + (primary != null ? " (mission " + primary.Id + " " + primary.Status + ")" : ""),
                dock, lease, reclaimCaller, forceReason, false, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public DockProvisionFailure? TakeProvisionFailure(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return null;
            return _ProvisionFailures.TryRemove(missionId, out DockProvisionFailure? failure) ? failure : null;
        }

        /// <inheritdoc />
        public async Task<bool?> WorktreeExistsAsync(Dock dock, CancellationToken token = default)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            if (String.IsNullOrEmpty(dock.WorktreePath)) return false;
            if (!DockHostResolver.IsHarborDock(dock)) return Directory.Exists(dock.WorktreePath);
            if (Hosts == null) return null;
            try
            {
                return await Hosts.ForDock(dock).Files.DirectoryExistsAsync(dock.WorktreePath, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An unreachable Harbor is not proof the dock is gone.
                _Logging.Warn(_Header + "unable to check dock " + dock.Id + " on Harbor " + dock.HarborId + ": " + ex.Message);
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<DockRestoreResult> RestoreWorktreeAsync(string dockId, string trigger, bool reactivate = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) return DockRestoreResult.Failed(dockId, "dock " + dockId + " is not recorded");
            if (!dock.Active && !reactivate) return DockRestoreResult.Failed(dockId, "dock " + dockId + " was reclaimed");
            if (String.IsNullOrEmpty(dock.WorktreePath) || String.IsNullOrEmpty(dock.BranchName))
                return DockRestoreResult.Failed(dockId, "dock " + dockId + " has no recorded worktree path or branch");

            DockRestoreResult result = new DockRestoreResult
            {
                DockId = dock.Id,
                HarborId = dock.HarborId,
                WorktreePath = dock.WorktreePath,
                BranchName = dock.BranchName,
                Trigger = trigger
            };

            bool harborDock = DockHostResolver.IsHarborDock(dock);
            IGitService git;
            string? repository;
            string baseBranch = "main";
            Vessel? vessel = await _Database.Vessels.ReadAsync(dock.VesselId, token).ConfigureAwait(false);
            if (vessel != null && !String.IsNullOrEmpty(vessel.DefaultBranch)) baseBranch = vessel.DefaultBranch;
            if (harborDock)
            {
                if (Hosts == null)
                {
                    result.Reason = "dock " + dock.Id + " is on Harbor " + dock.HarborId + ", but Harbor delegation is not available";
                    return result;
                }
                git = Hosts.ForDock(dock).Git;
                repository = dock.RepositoryPath;
            }
            else
            {
                git = _Git;
                repository = !String.IsNullOrEmpty(dock.RepositoryPath) ? dock.RepositoryPath : vessel?.LocalPath;
            }

            result.RepositoryPath = repository;
            if (String.IsNullOrEmpty(repository))
            {
                result.Reason = "the repository for dock " + dock.Id + " is unknown";
                return result;
            }

            try
            {
                bool? exists = await WorktreeExistsAsync(dock, token).ConfigureAwait(false);
                if (exists == true)
                {
                    result.Restored = true;
                    result.HeadCommit = await git.GetHeadCommitHashAsync(dock.WorktreePath, token).ConfigureAwait(false);
                    return result;
                }

                // Never let worktree creation invent a fresh branch from the base: the branch is the work.
                if (!await git.BranchExistsAsync(repository, dock.BranchName, token).ConfigureAwait(false))
                {
                    result.Reason = "branch '" + dock.BranchName + "' is not in " + repository;
                    return result;
                }

                // The vanished directory is usually still registered as a worktree; clear that before adding it again.
                try { await git.PruneWorktreesAsync(repository, token).ConfigureAwait(false); }
                catch (Exception pruneEx) when (pruneEx is not OperationCanceledException)
                {
                    _Logging.Debug(_Header + "could not prune worktrees in " + repository + " before restoring dock " + dock.Id + ": " + pruneEx.Message);
                }

                await git.CreateWorktreeAsync(repository, dock.WorktreePath, dock.BranchName, baseBranch, token).ConfigureAwait(false);
                result.HeadCommit = await git.GetHeadCommitHashAsync(dock.WorktreePath, token).ConfigureAwait(false);
                result.Restored = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "could not restore dock " + dock.Id + " at " + dock.WorktreePath + ": " + ex.Message);
                result.Reason = ex.Message;
                return result;
            }

            if (!dock.Active)
            {
                dock.Active = true;
                dock.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Docks.UpdateAsync(dock, token).ConfigureAwait(false);
            }

            DockLeaseState lease = await ReadLeaseAsync(dock, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "restored missing worktree of dock " + dock.Id + " at " + dock.WorktreePath + " from branch " + dock.BranchName + " (" + trigger + ")");
            try
            {
                ArmadaEvent evt = new ArmadaEvent("dock.repaired", "Recreated the missing worktree of dock " + dock.Id + " from branch " + dock.BranchName + " (" + trigger + ")");
                evt.EntityType = "dock";
                evt.EntityId = dock.Id;
                evt.TenantId = dock.TenantId;
                evt.UserId = dock.UserId;
                evt.VesselId = dock.VesselId;
                evt.MissionId = lease.PrimaryMission?.Id;
                evt.Payload = JsonSerializer.Serialize(result);
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "could not record dock.repaired for " + dock.Id + ": " + ex.Message);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task RepairAsync(string dockId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Docks.ReadAsync(tenantId, dockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) throw new KeyNotFoundException("Dock not found: " + dockId);

            if (!String.IsNullOrEmpty(dock.WorktreePath))
            {
                IGitService git = DockHostResolver.IsHarborDock(dock) && Hosts != null ? Hosts.ForDock(dock).Git : _Git;
                await git.RepairWorktreeAsync(dock.WorktreePath, token).ConfigureAwait(false);
                _Logging.Info(_Header + "repaired dock " + dockId);
            }
        }

        /// <inheritdoc />
        public async Task UnstickAsync(string dockId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Docks.ReadAsync(tenantId, dockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) throw new KeyNotFoundException("Dock not found: " + dockId);

            // Free any captain still holding this dock so it stops counting as "in use".
            if (!String.IsNullOrEmpty(dock.CaptainId))
            {
                Captain? captain = await _Database.Captains.ReadAsync(dock.CaptainId, token).ConfigureAwait(false);
                if (captain != null && captain.CurrentDockId == dock.Id)
                {
                    _Logging.Warn(_Header + "unstick: releasing captain " + captain.Id + " holding dock " + dockId);
                    captain.State = CaptainStateEnum.Idle;
                    captain.CurrentMissionId = null;
                    captain.CurrentDockId = null;
                    captain.ProcessId = null;
                    captain.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                }
            }

            await ReclaimAsync(dockId, tenantId, token, "operator unstick").ConfigureAwait(false);
            _Logging.Info(_Header + "unstuck dock " + dockId);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string dockId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Docks.ReadAsync(tenantId, dockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) throw new KeyNotFoundException("Dock not found: " + dockId);

            // Block deletion if an active mission is using this dock
            if (dock.Active && !String.IsNullOrEmpty(dock.CaptainId))
            {
                _Logging.Warn(_Header + "cannot delete dock " + dockId + " -- it is active with captain " + dock.CaptainId);
                return false;
            }

            await CleanupWorktreeAsync(dock, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(tenantId))
                await _Database.Docks.DeleteAsync(tenantId, dockId, token).ConfigureAwait(false);
            else
                await _Database.Docks.DeleteAsync(dockId, token).ConfigureAwait(false);

            _Logging.Info(_Header + "deleted dock " + dockId);
            return true;
        }

        /// <inheritdoc />
        public async Task PurgeAsync(string dockId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Docks.ReadAsync(tenantId, dockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) throw new KeyNotFoundException("Dock not found: " + dockId);

            await CleanupWorktreeAsync(dock, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(tenantId))
                await _Database.Docks.DeleteAsync(tenantId, dockId, token).ConfigureAwait(false);
            else
                await _Database.Docks.DeleteAsync(dockId, token).ConfigureAwait(false);

            _Logging.Info(_Header + "purged dock " + dockId + " (force)");
        }

        #endregion

        #region Private-Methods

        private async Task<DockLeaseState> ReadLeaseAsync(Dock dock, CancellationToken token)
        {
            DockLeaseState lease = new DockLeaseState();
            if (String.IsNullOrEmpty(dock.VesselId)) return lease;

            List<MissionSummary> summaries = await _Database.Missions.EnumerateSummariesByVesselAsync(dock.VesselId, token).ConfigureAwait(false);
            foreach (MissionSummary summary in summaries)
            {
                if (String.Equals(summary.DockId, dock.Id, StringComparison.Ordinal)) lease.Missions.Add(summary);
            }
            lease.Missions.Sort((a, b) => b.LastUpdateUtc.CompareTo(a.LastUpdateUtc));
            lease.HoldingMission = lease.Missions.FirstOrDefault(m => DockLeaseState.IsLeaseStatus(m.Status));
            if (lease.HoldingMission != null || lease.Missions.Count == 0) return lease;

            HashSet<string> missionIds = new HashSet<string>(lease.Missions.Select(m => m.Id), StringComparer.Ordinal);
            foreach (MergeStatusEnum status in new[] { MergeStatusEnum.Queued, MergeStatusEnum.Testing })
            {
                List<MergeEntry> entries = await _Database.MergeEntries.EnumerateByStatusAsync(status, token).ConfigureAwait(false);
                MergeEntry? holding = entries.FirstOrDefault(e => !String.IsNullOrEmpty(e.MissionId) && missionIds.Contains(e.MissionId));
                if (holding != null)
                {
                    lease.HoldingEntry = holding;
                    break;
                }
            }
            return lease;
        }

        private async Task EmitReclaimEventAsync(string eventType, string message, Dock dock, DockLeaseState lease, string caller, string? forceReason, bool refused, CancellationToken token)
        {
            MissionSummary? primary = lease.PrimaryMission;
            DockReclaimRecord record = new DockReclaimRecord
            {
                DockId = dock.Id,
                VesselId = dock.VesselId,
                HarborId = dock.HarborId,
                WorktreePath = dock.WorktreePath,
                BranchName = dock.BranchName,
                MissionId = primary?.Id,
                MissionStatus = primary?.Status.ToString(),
                Caller = caller,
                ForceReason = String.IsNullOrWhiteSpace(forceReason) ? null : forceReason,
                LeaseHolder = lease.IsHeld ? lease.Describe() : null,
                Refused = refused
            };

            try
            {
                ArmadaEvent evt = new ArmadaEvent(eventType, message);
                evt.EntityType = "dock";
                evt.EntityId = dock.Id;
                evt.TenantId = dock.TenantId;
                evt.UserId = dock.UserId;
                evt.VesselId = dock.VesselId;
                evt.CaptainId = dock.CaptainId;
                evt.MissionId = primary?.Id;
                evt.VoyageId = primary?.VoyageId;
                evt.Payload = JsonSerializer.Serialize(record);
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "could not record " + eventType + " for dock " + dock.Id + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Whether a legacy path has been reassigned to a different active dock.
        /// </summary>
        private async Task<bool> HasOtherActivePathOwnerAsync(Dock dock, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(dock.WorktreePath)) return false;
            foreach (Dock other in await _Database.Docks.EnumerateAsync(token).ConfigureAwait(false))
            {
                if (other.Id != dock.Id && other.Active && other.HarborId == dock.HarborId
                    && String.Equals(other.WorktreePath?.TrimEnd('/', '\\'), dock.WorktreePath.TrimEnd('/', '\\'), StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private async Task CleanupWorktreeAsync(Dock dock, CancellationToken token)
        {
            if (await HasOtherActivePathOwnerAsync(dock, token).ConfigureAwait(false))
            {
                _Logging.Warn(_Header + "skipping physical cleanup of stale dock " + dock.Id + " because another active dock owns its worktree");
                TryDeleteDockStartCommitFile(dock.Id);
                return;
            }
            if (DockHostResolver.IsHarborDock(dock))
            {
                await RemoveHarborDockAsync(dock, token).ConfigureAwait(false);
            }
            else if (!String.IsNullOrEmpty(dock.WorktreePath))
            {
                try
                {
                    await _Git.RemoveWorktreeAsync(dock.WorktreePath, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "removed worktree for dock " + dock.Id + " at " + dock.WorktreePath);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error removing worktree for dock " + dock.Id + ": " + ex.ToString());
                }

                await ForceRemoveDirectoryAsync(dock.WorktreePath, token).ConfigureAwait(false);
            }

            TryDeleteDockStartCommitFile(dock.Id);
        }

        /// <summary>
        /// Have the dock's Harbor remove its worktree. Best-effort: a Harbor that is offline or cannot remove it leaves
        /// the folder behind (logged), and the dock is still released.
        /// </summary>
        private async Task RemoveHarborDockAsync(Dock dock, CancellationToken token)
        {
            HarborConnectionManager? harbors = Hosts?.Harbors;
            if (harbors == null)
            {
                _Logging.Warn(_Header + "dock " + dock.Id + " is on Harbor " + dock.HarborId + ", but Harbor delegation is not available; its worktree " + dock.WorktreePath + " stays on that Harbor");
                return;
            }

            string harbor = harbors.Describe(dock.HarborId!);
            try
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(dock.VesselId, token).ConfigureAwait(false);
                Armada.Core.Harbor.HarborDockResult? result = await new HarborDockClient(harbors).ReclaimAsync(dock, vessel, token).ConfigureAwait(false);
                if (result == null)
                    _Logging.Warn(_Header + "Harbor " + harbor + " did not confirm removing dock " + dock.Id + " at " + dock.WorktreePath);
                else if (!result.Success)
                    _Logging.Warn(_Header + "Harbor " + harbor + " could not remove dock " + dock.Id + " at " + dock.WorktreePath + ": " + result.Message);
                else
                    _Logging.Info(_Header + "reclaimed dock " + dock.Id + " on Harbor " + harbor + " at " + dock.WorktreePath);
            }
            catch (InvalidOperationException ex)
            {
                _Logging.Warn(_Header + "could not remove dock " + dock.Id + " on Harbor " + harbor + " (" + ex.Message + "); its worktree " + dock.WorktreePath + " stays on that Harbor");
            }
        }

        private async Task PersistDockStartCommitAsync(string dockId, string headCommit, CancellationToken token)
        {
            string metadataDirectory = Path.Combine(_Settings.LogDirectory, "docks");
            Directory.CreateDirectory(metadataDirectory);
            await File.WriteAllTextAsync(Path.Combine(metadataDirectory, dockId + ".start"), headCommit + "\n", token).ConfigureAwait(false);
        }

        private void TryDeleteDockStartCommitFile(string dockId)
        {
            if (String.IsNullOrEmpty(dockId))
            {
                return;
            }

            try
            {
                string metadataPath = Path.Combine(_Settings.LogDirectory, "docks", dockId + ".start");
                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not remove dock start commit metadata for " + dockId + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Forcefully remove a directory with retry logic to handle locked files.
        /// On Windows, file handles from recently-exited processes can linger and
        /// cause Directory.Delete to fail. This method retries with increasing delays
        /// to give the OS time to release handles.
        /// </summary>
        /// <summary>
        /// Seed an empty bare repo with an initial commit containing a README.md.
        /// Uses a temporary clone to create the commit, then pushes to both the bare repo and the remote.
        /// </summary>
        private async Task SeedEmptyRepoAsync(Vessel vessel, string repoPath, CancellationToken token)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "armada_init_" + Guid.NewGuid().ToString("N"));
            try
            {
                // Use git init (not clone) because cloning an empty repo creates a broken state.
                Directory.CreateDirectory(tempPath);
                await RunGitInDirAsync(tempPath, "init", token).ConfigureAwait(false);
                await RunGitInDirAsync(tempPath, "checkout -b " + vessel.DefaultBranch, token).ConfigureAwait(false);

                // Create README.md
                string readmePath = Path.Combine(tempPath, "README.md");
                await File.WriteAllTextAsync(readmePath, "# " + vessel.Name + "\n", token).ConfigureAwait(false);

                // Commit
                await RunGitInDirAsync(tempPath, "add README.md", token).ConfigureAwait(false);
                await RunGitInDirAsync(tempPath, "commit -m \"Initial commit\"", token).ConfigureAwait(false);

                // Push to the remote
                if (!String.IsNullOrEmpty(vessel.RepoUrl))
                {
                    await RunGitInDirAsync(tempPath, "remote add origin " + vessel.RepoUrl, token).ConfigureAwait(false);
                    await RunGitInDirAsync(tempPath, "push -u origin " + vessel.DefaultBranch, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "pushed initial commit to remote for " + vessel.Name);
                }

                // Delete the stale bare repo (if it exists) and re-clone fresh
                if (Directory.Exists(repoPath))
                {
                    await ForceRemoveDirectoryAsync(repoPath, token).ConfigureAwait(false);
                }
                await _Git.CloneBareAsync(vessel.RepoUrl ?? tempPath, repoPath, token).ConfigureAwait(false);

                _Logging.Info(_Header + "seeded empty repo for " + vessel.Name + " with initial commit");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to seed empty repo for " + vessel.Name + ": " + ex.ToString());
                // Clean up any debris
                try { if (Directory.Exists(repoPath)) await ForceRemoveDirectoryAsync(repoPath, token).ConfigureAwait(false); }
                catch { }
                throw new InvalidOperationException("Repository " + vessel.Name + " is empty and could not be initialized: " + ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true); }
                catch { }
            }
        }

        /// <summary>
        /// Run a git command in a specific directory.
        /// </summary>
        private async Task RunGitInDirAsync(string workDir, string arguments, CancellationToken token)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            GitProcessEnvironment.Apply(psi);
            System.Diagnostics.Process? proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Failed to start git " + arguments);
            await proc.WaitForExitAsync(token).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                string stderr = await proc.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
                throw new InvalidOperationException("git " + arguments + " failed (exit " + proc.ExitCode + "): " + stderr.Trim());
            }
        }

        private async Task ForceRemoveDirectoryAsync(string path, CancellationToken token)
        {
            const int maxAttempts = 5;
            int[] delayMs = { 0, 500, 1000, 2000, 3000 };

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (!Directory.Exists(path)) return;

                if (attempt > 0)
                {
                    _Logging.Debug(_Header + "retry " + attempt + "/" + (maxAttempts - 1) + " removing directory: " + path);
                    await Task.Delay(delayMs[attempt], token).ConfigureAwait(false);
                }

                try
                {
                    // Clear read-only attributes that can block deletion on Windows.
                    // On Unix this is unnecessary (read-only attr maps to file permissions
                    // and Directory.Delete handles it), so skip the expensive enumeration.
                    if (OperatingSystem.IsWindows())
                    {
                        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                FileAttributes attrs = File.GetAttributes(file);
                                if ((attrs & FileAttributes.ReadOnly) != 0)
                                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                            }
                            catch { }
                        }
                    }

                    Directory.Delete(path, recursive: true);
                }
                catch (Exception ex) when (attempt < maxAttempts - 1)
                {
                    _Logging.Debug(_Header + "directory delete attempt " + (attempt + 1) + " failed for " + path + ": " + ex.Message);
                    continue;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "failed to remove directory after " + maxAttempts + " attempts: " + path + ": " + ex.ToString());
                    return;
                }
            }
        }

        #endregion
    }
}
