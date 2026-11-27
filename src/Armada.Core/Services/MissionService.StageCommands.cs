namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Stage check commands: running them on the dock's host and reverting what they change.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Methods

        private async Task<HostCommandResult> RunStageCommandAsync(IHostCommandExecutor executor, bool windows, string workingDirectory, string command, DateTime deadline, CancellationToken token)
        {
            if (DateTime.UtcNow >= deadline) return new HostCommandResult { TimedOut = true };
            return await executor.RunAsync(new HostCommandRequest
            {
                Executable = windows ? "cmd.exe" : "/bin/sh",
                WorkingDirectory = workingDirectory,
                Arguments = windows ? new List<string> { "/c", command } : new List<string> { "-lc", command },
                TimeoutMs = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds),
                Kind = HostCommandKindEnum.CheckRun,
                Label = "Stage check: " + command
            }, token).ConfigureAwait(false);
        }

        private static async Task<HashSet<string>> ReadModifiedTrackedFilesAsync(IHostCommandExecutor executor, bool windows, string worktree, CancellationToken token)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);
            HostCommandResult status = await RunGitAsync(executor, windows, worktree, "git status --porcelain=v1 --untracked-files=no", token).ConfigureAwait(false);
            if (!status.Success) return files;
            foreach (string line in (status.StandardOutput ?? String.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Length > 3) files.Add(line.Substring(3).Trim());
            }
            return files;
        }

        private async Task RevertCheckArtifactsAsync(IHostCommandExecutor executor, bool windows, string worktree, HashSet<string> dirtyBefore, string missionId)
        {
            try
            {
                HashSet<string> dirtyAfter = await ReadModifiedTrackedFilesAsync(executor, windows, worktree, CancellationToken.None).ConfigureAwait(false);
                List<string> regenerated = dirtyAfter.Where(f => !dirtyBefore.Contains(f) && !f.Contains("->", StringComparison.Ordinal)).ToList();
                if (regenerated.Count == 0) return;
                string quoted = String.Join(" ", regenerated.Select(f => "\"" + f.Replace("\"", "") + "\""));
                await RunGitAsync(executor, windows, worktree, "git checkout -- " + quoted, CancellationToken.None).ConfigureAwait(false);
                _Logging.Info(_Header + "stage check for mission " + missionId + " regenerated " + regenerated.Count + " tracked file(s); restored them: " + String.Join(", ", regenerated));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is TimeoutException)
            {
                _Logging.Warn(_Header + "could not restore files the stage check changed for mission " + missionId + ": " + ex.Message);
            }
        }

        // Vessel, base commit and command to whether the command already fails on the base commit.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _BaseCommitCheckFailures = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        private async Task<bool?> FailsOnBaseCommitAsync(IHostCommandExecutor executor, bool windows, string vesselId, string worktree, string baseCommit, string command, DateTime deadline, CancellationToken token)
        {
            string key = vesselId + "|" + baseCommit + "|" + command;
            if (_BaseCommitCheckFailures.TryGetValue(key, out bool known)) return known;

            // Run the same command on the base commit in a throwaway detached worktree next to the dock.
            string baseline = worktree.TrimEnd('/', '\\') + ".base-check";
            bool fails = false;
            try
            {
                await RunGitAsync(executor, windows, worktree, "git worktree remove --force \"" + baseline + "\"", token).ConfigureAwait(false);
                HostCommandResult added = await RunGitAsync(executor, windows, worktree, "git worktree add --detach \"" + baseline + "\" " + baseCommit, token).ConfigureAwait(false);
                if (!added.Success)
                {
                    _Logging.Warn(_Header + "could not create a base-commit worktree to classify a stage check failure: " + (added.StandardError ?? "").Trim());
                    return null;
                }

                HostCommandResult result = await RunStageCommandAsync(executor, windows, baseline, command, deadline, token).ConfigureAwait(false);
                if (result.TimedOut || result.ExitCode == -1 || result.ExitCode == 126 || result.ExitCode == 127) return null;
                fails = result.ExitCode != 0;
            }
            finally
            {
                await RunGitAsync(executor, windows, worktree, "git worktree remove --force \"" + baseline + "\"", CancellationToken.None).ConfigureAwait(false);
            }

            _BaseCommitCheckFailures[key] = fails;
            return fails;
        }

        private static async Task<HostCommandResult> RunGitAsync(IHostCommandExecutor executor, bool windows, string worktree, string command, CancellationToken token)
        {
            return await executor.RunAsync(new HostCommandRequest
            {
                Executable = windows ? "cmd.exe" : "/bin/sh",
                WorkingDirectory = worktree,
                Arguments = windows ? new List<string> { "/c", command } : new List<string> { "-c", command },
                TimeoutMs = 120000,
                Kind = HostCommandKindEnum.CheckRun,
                Label = "Stage check git"
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
