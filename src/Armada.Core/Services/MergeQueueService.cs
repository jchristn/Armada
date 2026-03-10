namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;


    /// <summary>
    /// Bors-style merge queue with batch testing and binary bisection on failure.
    /// </summary>
    public class MergeQueueService : IMergeQueueService
    {
        #region Private-Members

        private string _Header = "[MergeQueue] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService _Git;

        private ConcurrentDictionary<string, MergeEntry> _Entries = new ConcurrentDictionary<string, MergeEntry>();
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
        public Task<MergeEntry> EnqueueAsync(MergeEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            entry.Status = MergeStatusEnum.Queued;
            entry.CreatedUtc = DateTime.UtcNow;
            entry.LastUpdateUtc = DateTime.UtcNow;
            _Entries[entry.Id] = entry;

            _Logging.Info(_Header + "enqueued " + entry.Id + " branch " + entry.BranchName + " -> " + entry.TargetBranch);
            return Task.FromResult(entry);
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
                // Group queued entries by vessel + target branch
                List<MergeEntry> queued = _Entries.Values
                    .Where(e => e.Status == MergeStatusEnum.Queued)
                    .OrderBy(e => e.Priority)
                    .ThenBy(e => e.CreatedUtc)
                    .ToList();

                if (queued.Count == 0) return;

                // Process each vessel/target group
                IEnumerable<IGrouping<string, MergeEntry>> groups = queued.GroupBy(
                    e => (e.VesselId ?? "default") + ":" + e.TargetBranch);

                foreach (IGrouping<string, MergeEntry> group in groups)
                {
                    List<MergeEntry> batch = group.ToList();
                    await ProcessBatchAsync(batch, token).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_ProcessLock) { _Processing = false; }
            }
        }

        /// <inheritdoc />
        public Task CancelAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));

            if (_Entries.TryGetValue(entryId, out MergeEntry? entry))
            {
                entry.Status = MergeStatusEnum.Cancelled;
                entry.LastUpdateUtc = DateTime.UtcNow;
                entry.CompletedUtc = DateTime.UtcNow;
                _Logging.Info(_Header + "cancelled " + entryId);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<List<MergeEntry>> ListAsync(CancellationToken token = default)
        {
            List<MergeEntry> result = _Entries.Values
                .OrderBy(e => e.CreatedUtc)
                .ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<MergeEntry?> ProcessSingleAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));

            if (!_Entries.TryGetValue(entryId, out MergeEntry? entry))
                return null;

            if (entry.Status != MergeStatusEnum.Queued)
                return null;

            _Logging.Info(_Header + "processing single entry " + entryId);
            await ProcessBatchAsync(new List<MergeEntry> { entry }, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public Task<MergeEntry?> GetAsync(string entryId, CancellationToken token = default)
        {
            _Entries.TryGetValue(entryId, out MergeEntry? entry);
            return Task.FromResult(entry);
        }

        #endregion

        #region Private-Methods

        private async Task ProcessBatchAsync(List<MergeEntry> batch, CancellationToken token)
        {
            if (batch.Count == 0) return;

            string batchId = "batch_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            MergeEntry first = batch[0];
            string? repoPath = await GetRepoPathAsync(first, token).ConfigureAwait(false);

            if (repoPath == null)
            {
                _Logging.Warn(_Header + "skipping batch " + batchId + ": unable to resolve repo path for vessel " + first.VesselId);
                foreach (MergeEntry entry in batch)
                {
                    entry.Status = MergeStatusEnum.Failed;
                    entry.TestOutput = "Unable to resolve repository path for vessel " + first.VesselId;
                    entry.CompletedUtc = DateTime.UtcNow;
                    entry.LastUpdateUtc = DateTime.UtcNow;
                }
                return;
            }

            _Logging.Info(_Header + "processing batch " + batchId + " with " + batch.Count + " entries for " + first.TargetBranch);

            // Mark all entries as testing
            foreach (MergeEntry entry in batch)
            {
                entry.Status = MergeStatusEnum.Testing;
                entry.BatchId = batchId;
                entry.TestStartedUtc = DateTime.UtcNow;
                entry.LastUpdateUtc = DateTime.UtcNow;
            }

            // Create integration branch
            string integrationBranch = "armada/merge-queue/" + batchId;

            try
            {
                // Fetch latest
                await _Git.FetchAsync(repoPath, token).ConfigureAwait(false);

                // Create integration branch from target
                string integrationPath = Path.Combine(
                    _Settings.DocksDirectory, "_merge-queue", batchId);

                await _Git.CreateWorktreeAsync(repoPath, integrationPath, integrationBranch, first.TargetBranch, token).ConfigureAwait(false);

                // Merge each branch into the integration branch
                foreach (MergeEntry entry in batch)
                {
                    bool mergeOk = await MergeBranchAsync(integrationPath, entry.BranchName, token).ConfigureAwait(false);
                    if (!mergeOk)
                    {
                        _Logging.Warn(_Header + "merge conflict for " + entry.Id + " branch " + entry.BranchName);
                        entry.Status = MergeStatusEnum.Failed;
                        entry.TestOutput = "Merge conflict with " + first.TargetBranch;
                        entry.CompletedUtc = DateTime.UtcNow;
                        entry.LastUpdateUtc = DateTime.UtcNow;

                        // Remove conflicting entry and retry batch without it
                        List<MergeEntry> remaining = batch.Where(e => e.Id != entry.Id && e.Status == MergeStatusEnum.Testing).ToList();
                        await CleanupWorktreeAsync(integrationPath, token).ConfigureAwait(false);

                        if (remaining.Count > 0)
                        {
                            // Reset remaining to queued for re-processing
                            foreach (MergeEntry r in remaining)
                            {
                                r.Status = MergeStatusEnum.Queued;
                                r.BatchId = null;
                                r.TestStartedUtc = null;
                                r.LastUpdateUtc = DateTime.UtcNow;
                            }
                        }
                        return;
                    }
                }

                // Run tests
                string testCommand = first.TestCommand ?? _Settings.MergeQueueTestCommand ?? "";
                if (!String.IsNullOrEmpty(testCommand))
                {
                    TestResult testResult = await RunTestsAsync(integrationPath, testCommand, token).ConfigureAwait(false);

                    if (testResult.ExitCode == 0)
                    {
                        // Tests passed — land all entries
                        _Logging.Info(_Header + "batch " + batchId + " tests PASSED");
                        await LandBatchAsync(batch, repoPath, integrationBranch, first.TargetBranch, token).ConfigureAwait(false);
                    }
                    else
                    {
                        // Tests failed
                        _Logging.Warn(_Header + "batch " + batchId + " tests FAILED (exit " + testResult.ExitCode + ")");

                        if (batch.Count > 1)
                        {
                            // Binary bisection: split batch and retry each half
                            await BisectBatchAsync(batch, repoPath, first.TargetBranch, testCommand, testResult.Output, token).ConfigureAwait(false);
                        }
                        else
                        {
                            // Single entry failed
                            MergeEntry single = batch[0];
                            single.Status = MergeStatusEnum.Failed;
                            single.TestExitCode = testResult.ExitCode;
                            single.TestOutput = TruncateOutput(testResult.Output);
                            single.CompletedUtc = DateTime.UtcNow;
                            single.LastUpdateUtc = DateTime.UtcNow;
                        }
                    }
                }
                else
                {
                    // No test command — land directly
                    _Logging.Info(_Header + "no test command configured, landing batch " + batchId + " directly");
                    await LandBatchAsync(batch, repoPath, integrationBranch, first.TargetBranch, token).ConfigureAwait(false);
                }

                // Cleanup integration worktree
                await CleanupWorktreeAsync(integrationPath, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "batch " + batchId + " error: " + ex.Message);
                foreach (MergeEntry entry in batch.Where(e => e.Status == MergeStatusEnum.Testing))
                {
                    entry.Status = MergeStatusEnum.Failed;
                    entry.TestOutput = "Queue processing error: " + ex.Message;
                    entry.CompletedUtc = DateTime.UtcNow;
                    entry.LastUpdateUtc = DateTime.UtcNow;
                }
            }
        }

        private async Task BisectBatchAsync(
            List<MergeEntry> batch,
            string repoPath,
            string targetBranch,
            string testCommand,
            string failureOutput,
            CancellationToken token)
        {
            _Logging.Info(_Header + "bisecting batch of " + batch.Count + " entries");

            // Split batch in half
            int mid = batch.Count / 2;
            List<MergeEntry> firstHalf = batch.Take(mid).ToList();
            List<MergeEntry> secondHalf = batch.Skip(mid).ToList();

            // Reset all to queued — they'll be re-batched in smaller groups
            foreach (MergeEntry entry in batch)
            {
                entry.Status = MergeStatusEnum.Queued;
                entry.BatchId = null;
                entry.TestStartedUtc = null;
                entry.LastUpdateUtc = DateTime.UtcNow;
            }

            // Process each half separately (recursive — will further bisect if needed)
            if (firstHalf.Count > 0)
            {
                await ProcessBatchAsync(firstHalf, token).ConfigureAwait(false);
            }

            if (secondHalf.Count > 0)
            {
                await ProcessBatchAsync(secondHalf, token).ConfigureAwait(false);
            }
        }

        private async Task LandBatchAsync(
            List<MergeEntry> batch,
            string repoPath,
            string integrationBranch,
            string targetBranch,
            CancellationToken token)
        {
            // Push integration branch and update target
            try
            {
                // The integration branch already has all changes merged
                // Push it to update the remote target branch
                await RunGitAsync(repoPath, "push origin " + integrationBranch + ":" + targetBranch, token).ConfigureAwait(false);

                foreach (MergeEntry entry in batch)
                {
                    entry.Status = MergeStatusEnum.Landed;
                    entry.CompletedUtc = DateTime.UtcNow;
                    entry.LastUpdateUtc = DateTime.UtcNow;
                    _Logging.Info(_Header + "landed " + entry.Id + " branch " + entry.BranchName);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to land batch: " + ex.Message);
                foreach (MergeEntry entry in batch)
                {
                    entry.Status = MergeStatusEnum.Failed;
                    entry.TestOutput = "Landing failed: " + ex.Message;
                    entry.CompletedUtc = DateTime.UtcNow;
                    entry.LastUpdateUtc = DateTime.UtcNow;
                }
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
                if (vessel == null || String.IsNullOrEmpty(vessel.LocalPath))
                {
                    _Logging.Warn(_Header + "vessel not found or LocalPath is empty for vessel ID " + entry.VesselId);
                    return null;
                }
                return vessel.LocalPath;
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
