namespace Armada.Core.Services
{
    using System.Diagnostics;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;


    /// <summary>
    /// Merge queue that processes vessel+target-branch groups in parallel, while
    /// entries within each group are processed sequentially one at a time.  Each
    /// successful merge is landed immediately so the next entry in the same group
    /// merges against the up-to-date target branch, eliminating the cascade
    /// failures that occur with batch-style processing.
    /// </summary>
    public class MergeQueueService : IMergeQueueService
    {
        #region Private-Members

        private string _Header = "[MergeQueue] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService _Git;

        private bool _Processing = false;
        private readonly object _ProcessLock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="git">Git service.</param>
        public MergeQueueService(LoggingModule logging, DatabaseDriver database, ArmadaSettings settings, IGitService git)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<MergeEntry> EnqueueAsync(MergeEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            entry.Status = MergeStatusEnum.Queued;
            entry.CreatedUtc = DateTime.UtcNow;
            entry.LastUpdateUtc = DateTime.UtcNow;
            await _Database.MergeEntries.CreateAsync(entry, token).ConfigureAwait(false);

            _Logging.Info(_Header + "enqueued " + entry.Id + " branch " + entry.BranchName + " -> " + entry.TargetBranch);
            return entry;
        }

        /// <inheritdoc />
        public async Task ProcessQueueAsync(CancellationToken token = default)
        {
            lock (_ProcessLock)
            {
                if (_Processing) return;
                _Processing = true;
            }

            try
            {
                // Get all queued entries ordered by priority then created_utc
                List<MergeEntry> queued = await _Database.MergeEntries.EnumerateByStatusAsync(MergeStatusEnum.Queued, token).ConfigureAwait(false);

                if (queued.Count == 0) return;

                // Group by vessel + target branch (independent repos can process independently)
                IEnumerable<IGrouping<string, MergeEntry>> groups = queued.GroupBy(
                    e => (e.VesselId ?? "default") + ":" + e.TargetBranch);

                List<Task> groupTasks = new List<Task>();

                foreach (IGrouping<string, MergeEntry> group in groups)
                {
                    // Order by priority (lower = higher priority) then by creation time ascending
                    List<MergeEntry> entries = group
                        .OrderBy(e => e.Priority)
                        .ThenBy(e => e.CreatedUtc)
                        .ToList();

                    groupTasks.Add(ProcessGroupSafeAsync(entries, token));
                }

                await Task.WhenAll(groupTasks).ConfigureAwait(false);
            }
            finally
            {
                lock (_ProcessLock) { _Processing = false; }
            }
        }

        /// <inheritdoc />
        public async Task CancelAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));

            MergeEntry? entry = await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            if (entry != null)
            {
                entry.Status = MergeStatusEnum.Cancelled;
                entry.LastUpdateUtc = DateTime.UtcNow;
                entry.CompletedUtc = DateTime.UtcNow;
                await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);
                _Logging.Info(_Header + "cancelled " + entryId);
            }
        }

        /// <inheritdoc />
        public async Task<List<MergeEntry>> ListAsync(CancellationToken token = default)
        {
            List<MergeEntry> results = await _Database.MergeEntries.EnumerateAsync(token).ConfigureAwait(false);
            return results;
        }

        /// <inheritdoc />
        public async Task<MergeEntry?> ProcessSingleAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));

            MergeEntry? entry = await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            if (entry == null) return null;
            if (entry.Status != MergeStatusEnum.Queued) return null;

            _Logging.Info(_Header + "processing single entry " + entryId);
            await ProcessEntryAsync(entry, token).ConfigureAwait(false);

            // Re-read from DB to get updated state
            return await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MergeEntry?> GetAsync(string entryId, CancellationToken token = default)
        {
            MergeEntry? entry = await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));

            MergeEntry? entry = await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            if (entry == null) return false;

            // Only allow deletion of terminal entries
            if (entry.Status != MergeStatusEnum.Cancelled &&
                entry.Status != MergeStatusEnum.Landed &&
                entry.Status != MergeStatusEnum.Failed)
            {
                _Logging.Warn(_Header + "cannot delete " + entryId + " in non-terminal status " + entry.Status);
                return false;
            }

            // Clean up git branches associated with this entry
            if (!String.IsNullOrEmpty(entry.BranchName))
            {
                string? repoPath = await GetRepoPathAsync(entry, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(repoPath))
                {
                    // Delete remote branch
                    try
                    {
                        await RunGitAsync(repoPath, "push origin --delete " + entry.BranchName, token).ConfigureAwait(false);
                        _Logging.Info(_Header + "deleted remote branch " + entry.BranchName);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Debug(_Header + "remote branch delete for " + entry.BranchName + " skipped: " + ex.Message);
                    }

                    // Delete local branch
                    try
                    {
                        await _Git.DeleteLocalBranchAsync(repoPath, entry.BranchName, token).ConfigureAwait(false);
                        _Logging.Info(_Header + "deleted local branch " + entry.BranchName);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Debug(_Header + "local branch delete for " + entry.BranchName + " skipped: " + ex.Message);
                    }
                }
            }

            await _Database.MergeEntries.DeleteAsync(entryId, token).ConfigureAwait(false);
            _Logging.Info(_Header + "deleted " + entryId);
            return true;
        }

        /// <inheritdoc />
        public async Task<int> PurgeTerminalAsync(string? vesselId = null, MergeStatusEnum? status = null, CancellationToken token = default)
        {
            List<MergeStatusEnum> terminalStatuses = new List<MergeStatusEnum>
            {
                MergeStatusEnum.Landed,
                MergeStatusEnum.Failed,
                MergeStatusEnum.Cancelled
            };

            if (status != null && !terminalStatuses.Contains(status.Value))
            {
                _Logging.Warn(_Header + "purge requested for non-terminal status " + status.Value);
                return 0;
            }

            List<MergeStatusEnum> statusesToPurge = status != null
                ? new List<MergeStatusEnum> { status.Value }
                : terminalStatuses;

            List<MergeEntry> candidates = new List<MergeEntry>();
            foreach (MergeStatusEnum s in statusesToPurge)
            {
                List<MergeEntry> entries = await _Database.MergeEntries.EnumerateByStatusAsync(s, token).ConfigureAwait(false);
                candidates.AddRange(entries);
            }

            if (!String.IsNullOrEmpty(vesselId))
            {
                candidates = candidates.Where(e => e.VesselId == vesselId).ToList();
            }

            int deleted = 0;
            foreach (MergeEntry entry in candidates)
            {
                bool result = await DeleteAsync(entry.Id, token).ConfigureAwait(false);
                if (result) deleted++;
            }

            _Logging.Info(_Header + "purged " + deleted + " terminal entries");
            return deleted;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Wraps <see cref="ProcessGroupAsync"/> in a try-catch so that an
        /// unexpected exception in one group does not cancel other groups.
        /// </summary>
        private async Task ProcessGroupSafeAsync(List<MergeEntry> entries, CancellationToken token)
        {
            try
            {
                await ProcessGroupAsync(entries, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "group processing error: " + ex.Message);
            }
        }

        /// <summary>
        /// Process a group of entries that share the same vessel and target branch.
        /// Entries are processed one at a time in priority/creation order.  Each
        /// successful merge is landed immediately so the next entry merges against
        /// the updated target.
        /// </summary>
        private async Task ProcessGroupAsync(List<MergeEntry> entries, CancellationToken token)
        {
            if (entries.Count == 0) return;

            MergeEntry first = entries[0];
            string? repoPath = await GetRepoPathAsync(first, token).ConfigureAwait(false);

            if (repoPath == null)
            {
                _Logging.Warn(_Header + "unable to resolve repo path for vessel " + first.VesselId + " — failing all entries");
                foreach (MergeEntry entry in entries)
                {
                    entry.Status = MergeStatusEnum.Failed;
                    entry.TestOutput = "Unable to resolve repository path for vessel " + first.VesselId;
                    entry.CompletedUtc = DateTime.UtcNow;
                    entry.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);
                }
                return;
            }

            _Logging.Info(_Header + "processing " + entries.Count + " entries for " + first.TargetBranch + " on vessel " + (first.VesselId ?? "default"));

            foreach (MergeEntry entry in entries)
            {
                await ProcessEntryAsync(entry, repoPath, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Process a single merge entry: fetch, merge, test, land.
        /// If any step fails the entry is marked Failed and processing continues
        /// to the next entry in the group.
        /// </summary>
        private async Task ProcessEntryAsync(MergeEntry entry, CancellationToken token)
        {
            string? repoPath = await GetRepoPathAsync(entry, token).ConfigureAwait(false);
            if (repoPath == null)
            {
                entry.Status = MergeStatusEnum.Failed;
                entry.TestOutput = "Unable to resolve repository path for vessel " + entry.VesselId;
                entry.CompletedUtc = DateTime.UtcNow;
                entry.LastUpdateUtc = DateTime.UtcNow;
                await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);
                return;
            }

            await ProcessEntryAsync(entry, repoPath, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Core single-entry processing with a known repo path.
        /// </summary>
        private async Task ProcessEntryAsync(MergeEntry entry, string repoPath, CancellationToken token)
        {
            string entryTag = entry.Id + " branch " + entry.BranchName;
            _Logging.Info(_Header + "processing " + entryTag);

            // Mark as testing
            entry.Status = MergeStatusEnum.Testing;
            entry.TestStartedUtc = DateTime.UtcNow;
            entry.LastUpdateUtc = DateTime.UtcNow;
            await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);

            // Each entry gets its own temporary worktree so the merge is always
            // attempted against the current state of the target branch.
            string worktreeId = "mq_" + entry.Id + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string integrationBranch = "armada/merge-queue/" + worktreeId;
            string integrationPath = Path.Combine(_Settings.DocksDirectory, "_merge-queue", worktreeId);

            try
            {
                // Fetch latest so the target branch ref is up to date
                await _Git.FetchAsync(repoPath, token).ConfigureAwait(false);

                // Create a temporary worktree from the current target branch
                await _Git.CreateWorktreeAsync(repoPath, integrationPath, integrationBranch, entry.TargetBranch, token).ConfigureAwait(false);

                // Merge the entry's branch
                bool mergeOk = await MergeBranchAsync(integrationPath, entry.BranchName, token).ConfigureAwait(false);
                if (!mergeOk)
                {
                    _Logging.Warn(_Header + "merge conflict for " + entryTag);
                    entry.Status = MergeStatusEnum.Failed;
                    entry.TestOutput = "Merge conflict with " + entry.TargetBranch;
                    entry.CompletedUtc = DateTime.UtcNow;
                    entry.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);
                    await CleanupWorktreeAsync(integrationPath, token).ConfigureAwait(false);
                    return;
                }

                // Run tests if configured
                string testCommand = entry.TestCommand ?? _Settings.MergeQueueTestCommand ?? "";
                if (!String.IsNullOrEmpty(testCommand))
                {
                    TestResult testResult = await RunTestsAsync(integrationPath, testCommand, token).ConfigureAwait(false);
                    if (testResult.ExitCode != 0)
                    {
                        _Logging.Warn(_Header + "tests FAILED for " + entryTag + " (exit " + testResult.ExitCode + ")");
                        entry.Status = MergeStatusEnum.Failed;
                        entry.TestExitCode = testResult.ExitCode;
                        entry.TestOutput = TruncateOutput(testResult.Output);
                        entry.CompletedUtc = DateTime.UtcNow;
                        entry.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);
                        await CleanupWorktreeAsync(integrationPath, token).ConfigureAwait(false);
                        return;
                    }

                    _Logging.Info(_Header + "tests PASSED for " + entryTag);
                }

                // Land immediately — push the integration branch to update the target
                await LandEntryAsync(entry, repoPath, integrationBranch, token).ConfigureAwait(false);

                // Cleanup
                await CleanupWorktreeAsync(integrationPath, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error processing " + entryTag + ": " + ex.Message);
                entry.Status = MergeStatusEnum.Failed;
                entry.TestOutput = "Queue processing error: " + ex.Message;
                entry.CompletedUtc = DateTime.UtcNow;
                entry.LastUpdateUtc = DateTime.UtcNow;
                await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);

                // Best-effort cleanup
                await CleanupWorktreeAsync(integrationPath, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Land a single entry by pushing the integration branch to the target.
        /// </summary>
        private async Task LandEntryAsync(MergeEntry entry, string repoPath, string integrationBranch, CancellationToken token)
        {
            try
            {
                await RunGitAsync(repoPath, "push origin " + integrationBranch + ":" + entry.TargetBranch, token).ConfigureAwait(false);

                entry.Status = MergeStatusEnum.Landed;
                entry.CompletedUtc = DateTime.UtcNow;
                entry.LastUpdateUtc = DateTime.UtcNow;
                await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);
                _Logging.Info(_Header + "landed " + entry.Id + " branch " + entry.BranchName);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to land " + entry.Id + ": " + ex.Message);
                entry.Status = MergeStatusEnum.Failed;
                entry.TestOutput = "Landing failed: " + ex.Message;
                entry.CompletedUtc = DateTime.UtcNow;
                entry.LastUpdateUtc = DateTime.UtcNow;
                await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);
            }
        }

        private async Task<bool> MergeBranchAsync(string worktreePath, string branchName, CancellationToken token)
        {
            try
            {
                await RunGitAsync(worktreePath, "merge --no-ff " + branchName, token).ConfigureAwait(false);
                return true;
            }
            catch
            {
                // Abort the failed merge
                try { await RunGitAsync(worktreePath, "merge --abort", token).ConfigureAwait(false); }
                catch { }
                return false;
            }
        }

        private async Task<TestResult> RunTestsAsync(string workingDir, string testCommand, CancellationToken token)
        {
            _Logging.Info(_Header + "running tests: " + testCommand + " in " + workingDir);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = GetShell(),
                Arguments = GetShellArgs(testCommand),
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();

                string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

                await process.WaitForExitAsync(token).ConfigureAwait(false);

                string output = stdout;
                if (!String.IsNullOrEmpty(stderr))
                    output += "\n--- STDERR ---\n" + stderr;

                return new TestResult(process.ExitCode, output);
            }
        }

        private async Task RunGitAsync(string workingDir, string args, CancellationToken token)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();

                string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(token).ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("git " + args + " failed: " + stderr);
                }
            }
        }

        private async Task CleanupWorktreeAsync(string worktreePath, CancellationToken token)
        {
            try
            {
                await _Git.RemoveWorktreeAsync(worktreePath, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "cleanup error for " + worktreePath + ": " + ex.Message);
            }
        }

        private async Task<string?> GetRepoPathAsync(MergeEntry entry, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(entry.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(entry.VesselId, token).ConfigureAwait(false);
                if (vessel == null)
                {
                    _Logging.Warn(_Header + "vessel not found for vessel ID " + entry.VesselId);
                    return null;
                }
                if (!String.IsNullOrEmpty(vessel.LocalPath))
                    return vessel.LocalPath;

                // Fallback to default path, same as DockService
                string defaultPath = Path.Combine(_Settings.ReposDirectory, vessel.Name + ".git");
                _Logging.Warn(_Header + "vessel LocalPath is empty for vessel " + vessel.Name + ", falling back to default: " + defaultPath);
                return defaultPath;
            }
            return _Settings.ReposDirectory;
        }

        private string GetShell()
        {
            if (OperatingSystem.IsWindows()) return "cmd.exe";
            return "/bin/bash";
        }

        private string GetShellArgs(string command)
        {
            if (OperatingSystem.IsWindows()) return "/c " + command;
            return "-c \"" + command.Replace("\"", "\\\"") + "\"";
        }

        private string TruncateOutput(string output)
        {
            if (String.IsNullOrEmpty(output)) return "";
            if (output.Length > 4096) return output.Substring(0, 4096) + "\n... (truncated)";
            return output;
        }

        #endregion
    }
}
