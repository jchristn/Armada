namespace Armada.Core.Services
{
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for dock (worktree) lifecycle management.
    /// </summary>
    public class DockService : IDockService
    {
        #region Private-Members

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

            // Use per-mission dock path when missionId is provided (eliminates path-reuse races).
            // Falls back to per-captain path for backward compatibility.
            string dockDirName = !String.IsNullOrEmpty(missionId) ? missionId : captain.Name;
            string worktreePath = Path.Combine(_Settings.DocksDirectory, vessel.Name, dockDirName);

            try
            {
                // Ensure bare clone exists
                if (!await _Git.IsRepositoryAsync(repoPath, token).ConfigureAwait(false))
                {
                    if (String.IsNullOrEmpty(vessel.RepoUrl))
                        throw new InvalidOperationException("Vessel " + vessel.Name + " has no remote URL configured");
                    await _Git.CloneBareAsync(vessel.RepoUrl, repoPath, token).ConfigureAwait(false);
                    vessel.LocalPath = repoPath;
                    await _Database.Vessels.UpdateAsync(vessel, token).ConfigureAwait(false);
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
                                _Logging.Info(_Header + "cleaning up stale worktree from previous captain: " + existingDir);
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
                        _Logging.Info(_Header + "removing stale dock directory: " + worktreePath);
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

                // Delete stale branch only if it actually exists
                bool branchExists = await _Git.BranchExistsAsync(repoPath, branchName, token).ConfigureAwait(false);
                if (branchExists)
                {
                    _Logging.Debug(_Header + "deleting stale branch: " + branchName);
                    try
                    {
                        await _Git.DeleteLocalBranchAsync(repoPath, branchName, token).ConfigureAwait(false);
                    }
                    catch { }
                }

                // Create worktree
                await _Git.CreateWorktreeAsync(repoPath, worktreePath, branchName, vessel.DefaultBranch, token).ConfigureAwait(false);

                // Create dock record
                Dock dock = new Dock(vessel.Id);
                dock.TenantId = vessel.TenantId;
                dock.UserId = vessel.UserId;
                dock.CaptainId = captain.Id;
                dock.WorktreePath = worktreePath;
                dock.BranchName = branchName;
                dock = await _Database.Docks.CreateAsync(dock, token).ConfigureAwait(false);

                _Logging.Info(_Header + "provisioned dock " + dock.Id + " at " + worktreePath);
                return dock;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "provisioning failed for vessel " + vessel.Id + " captain " + captain.Id + " repo " + (vessel.RepoUrl ?? "unknown") + ": " + ex.Message);

                // Clean up partial state -- remove worktree directory if it was partially created
                if (Directory.Exists(worktreePath))
                {
                    await ForceRemoveDirectoryAsync(worktreePath, CancellationToken.None).ConfigureAwait(false);
                }

                return null;
            }
        }

        /// <inheritdoc />
        public async Task ReclaimAsync(string dockId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Docks.ReadAsync(tenantId, dockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) return;

            // Idempotency guard: if the dock is already inactive, it was already reclaimed.
            // This prevents duplicate worktree removal when both MissionService (background finalizer)
            // and ArmadaServer (HandleMissionCompleteAsync) both call ReclaimAsync for the same dock.
            if (!dock.Active)
            {
                _Logging.Debug(_Header + "dock " + dockId + " already reclaimed (Active=false) -- skipping");
                return;
            }

            if (!String.IsNullOrEmpty(dock.WorktreePath))
            {
                try
                {
                    await _Git.RemoveWorktreeAsync(dock.WorktreePath, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "reclaimed dock " + dockId + " at " + dock.WorktreePath);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error removing worktree for dock " + dockId + ": " + ex.Message);
                }

                // Ensure the directory is actually removed -- on Windows, file handles
                // from the just-exited agent process can linger and block deletion.
                await ForceRemoveDirectoryAsync(dock.WorktreePath, token).ConfigureAwait(false);
            }

            // Update the dock record so DataExpiryService can purge it
            dock.Active = false;
            dock.CaptainId = null;
            dock.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Docks.UpdateAsync(dock, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RepairAsync(string dockId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Docks.ReadAsync(tenantId, dockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) throw new InvalidOperationException("Dock not found: " + dockId);

            if (!String.IsNullOrEmpty(dock.WorktreePath))
            {
                await _Git.RepairWorktreeAsync(dock.WorktreePath, token).ConfigureAwait(false);
                _Logging.Info(_Header + "repaired dock " + dockId);
            }
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string dockId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            Dock? dock = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Docks.ReadAsync(tenantId, dockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) throw new InvalidOperationException("Dock not found: " + dockId);

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
            if (dock == null) throw new InvalidOperationException("Dock not found: " + dockId);

            await CleanupWorktreeAsync(dock, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(tenantId))
                await _Database.Docks.DeleteAsync(tenantId, dockId, token).ConfigureAwait(false);
            else
                await _Database.Docks.DeleteAsync(dockId, token).ConfigureAwait(false);

            _Logging.Info(_Header + "purged dock " + dockId + " (force)");
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Clean up a dock's worktree by removing the git worktree and directory.
        /// </summary>
        private async Task CleanupWorktreeAsync(Dock dock, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(dock.WorktreePath))
            {
                try
                {
                    await _Git.RemoveWorktreeAsync(dock.WorktreePath, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "removed worktree for dock " + dock.Id + " at " + dock.WorktreePath);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error removing worktree for dock " + dock.Id + ": " + ex.Message);
                }

                await ForceRemoveDirectoryAsync(dock.WorktreePath, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Forcefully remove a directory with retry logic to handle locked files.
        /// On Windows, file handles from recently-exited processes can linger and
        /// cause Directory.Delete to fail. This method retries with increasing delays
        /// to give the OS time to release handles.
        /// </summary>
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
                    _Logging.Warn(_Header + "failed to remove directory after " + maxAttempts + " attempts: " + path + ": " + ex.Message);
                    return;
                }
            }
        }

        #endregion
    }
}
