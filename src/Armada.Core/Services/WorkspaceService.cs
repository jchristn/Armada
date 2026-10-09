namespace Armada.Core.Services
{
    using System.Diagnostics;
    using System.Text;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Safe workspace browsing and editing over a vessel's checkout. The overloads that take a <see cref="Vessel"/> work in
    /// its working directory on the Admiral host; the overloads that take a <see cref="VesselHost"/> work wherever the
    /// checkout lives (the Admiral host or a Harbor, see <see cref="VesselHostResolver"/>), with the same rules on both.
    /// </summary>
    public class WorkspaceService : IWorkspaceService
    {
        #region Private-Members

        private const int _GitCommandTimeoutSeconds = 30;
        private const int _MaxExecOutputChars = 256 * 1024;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<WorkspaceTreeResult> GetTreeAsync(Vessel vessel, string? path = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            WorkspaceTreeResult result = WorkspaceFileEngine.GetTree(GetWorkspaceRoot(vessel), path);
            result.VesselId = vessel.Id;
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<WorkspaceTreeResult> GetTreeAsync(VesselHost host, string? path = null, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            WorkspaceTreeResult result = await host.Files.GetTreeAsync(path, token).ConfigureAwait(false);
            result.VesselId = host.Vessel.Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<WorkspaceFileResponse> GetFileAsync(Vessel vessel, string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            WorkspaceFileResponse result = await WorkspaceFileEngine.GetFileAsync(GetWorkspaceRoot(vessel), path, token).ConfigureAwait(false);
            result.VesselId = vessel.Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<WorkspaceFileResponse> GetFileAsync(VesselHost host, string path, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            WorkspaceFileResponse result = await host.Files.GetFileAsync(path, token).ConfigureAwait(false);
            result.VesselId = host.Vessel.Id;
            return result;
        }

        /// <inheritdoc />
        public Task<WorkspaceSaveResult> SaveFileAsync(Vessel vessel, WorkspaceSaveRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));
            token.ThrowIfCancellationRequested();
            return WorkspaceFileEngine.SaveFileAsync(GetWorkspaceRoot(vessel), request, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceSaveResult> SaveFileAsync(VesselHost host, WorkspaceSaveRequest request, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));
            return host.Files.SaveFileAsync(request, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> CreateDirectoryAsync(Vessel vessel, WorkspaceCreateDirectoryRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));
            token.ThrowIfCancellationRequested();
            return Task.FromResult(WorkspaceFileEngine.CreateDirectory(GetWorkspaceRoot(vessel), request));
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> CreateDirectoryAsync(VesselHost host, WorkspaceCreateDirectoryRequest request, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));
            return host.Files.CreateDirectoryAsync(request, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> RenameAsync(Vessel vessel, WorkspaceRenameRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path) || String.IsNullOrWhiteSpace(request.NewPath))
                throw new ArgumentException("Path and NewPath are required.", nameof(request));
            token.ThrowIfCancellationRequested();
            return Task.FromResult(WorkspaceFileEngine.Rename(GetWorkspaceRoot(vessel), request));
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> RenameAsync(VesselHost host, WorkspaceRenameRequest request, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path) || String.IsNullOrWhiteSpace(request.NewPath))
                throw new ArgumentException("Path and NewPath are required.", nameof(request));
            return host.Files.RenameAsync(request, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> DeleteAsync(Vessel vessel, string path, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            token.ThrowIfCancellationRequested();
            return Task.FromResult(WorkspaceFileEngine.Delete(GetWorkspaceRoot(vessel), path));
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> DeleteAsync(VesselHost host, string path, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            return host.Files.DeleteAsync(path, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceSearchResult> SearchAsync(Vessel vessel, string query, int maxResults = 200, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query is required.", nameof(query));
            token.ThrowIfCancellationRequested();
            return WorkspaceFileEngine.SearchAsync(GetWorkspaceRoot(vessel), query, maxResults, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceSearchResult> SearchAsync(VesselHost host, string query, int maxResults = 200, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query is required.", nameof(query));
            return host.Files.SearchAsync(query, maxResults, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceChangesResult> GetChangesAsync(Vessel vessel, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return GetChangesAsync(GetWorkspaceRoot(vessel), new LocalHostCommandExecutor(), token);
        }

        /// <inheritdoc />
        public Task<WorkspaceChangesResult> GetChangesAsync(VesselHost host, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return GetChangesAsync(host.WorkingDirectory, host.Commands, token);
        }

        /// <inheritdoc />
        public async Task<WorkspaceStatusResult> GetStatusAsync(
            Vessel vessel,
            IReadOnlyList<WorkspaceActiveMission>? activeMissions = null,
            CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            token.ThrowIfCancellationRequested();

            string? normalizedRoot = null;
            if (!String.IsNullOrWhiteSpace(vessel.WorkingDirectory))
            {
                normalizedRoot = Path.GetFullPath(vessel.WorkingDirectory);
            }

            if (String.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
                return Unavailable(vessel, activeMissions, "No working directory configured or directory does not exist.", normalizedRoot);

            WorkspaceChangesResult changes = await GetChangesAsync(vessel, token).ConfigureAwait(false);
            return BuildStatus(vessel, normalizedRoot, null, "Admiral", changes, activeMissions);
        }

        /// <inheritdoc />
        public async Task<WorkspaceStatusResult> GetStatusAsync(
            VesselHost host,
            IReadOnlyList<WorkspaceActiveMission>? activeMissions = null,
            CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            WorkspaceChangesResult changes = await GetChangesAsync(host, token).ConfigureAwait(false);
            return BuildStatus(host.Vessel, host.WorkingDirectory, host.HarborId, host.HostLabel, changes, activeMissions);
        }

        /// <summary>
        /// The status of a vessel that has no checkout Armada can use.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="activeMissions">Active missions.</param>
        /// <param name="error">Why there is no checkout and what to set.</param>
        /// <param name="rootPath">The configured working directory, or null.</param>
        /// <returns>The status.</returns>
        public static WorkspaceStatusResult Unavailable(Vessel vessel, IReadOnlyList<WorkspaceActiveMission>? activeMissions, string error, string? rootPath)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            return new WorkspaceStatusResult
            {
                VesselId = vessel.Id,
                HasWorkingDirectory = false,
                RootPath = rootPath,
                ActiveMissionCount = activeMissions?.Count ?? 0,
                ActiveMissions = activeMissions?.ToList() ?? new List<WorkspaceActiveMission>(),
                Error = error
            };
        }

        /// <inheritdoc />
        public async Task<WorkspaceExecResult> ExecAsync(Vessel vessel, WorkspaceExecRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Command)) throw new ArgumentException("Command is required.", nameof(request));

            string rootPath = GetWorkspaceRoot(vessel);
            int timeoutSeconds = ClampTimeout(request.TimeoutSeconds);

            WorkspaceExecResult result = new WorkspaceExecResult
            {
                Command = request.Command,
                WorkingDirectory = rootPath
            };

            ProcessStartInfo psi = new ProcessStartInfo
            {
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(request.Command);
            }
            else
            {
                psi.FileName = "/bin/sh";
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(request.Command);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            using Process process = new Process { StartInfo = psi };

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null && stdout.Length < _MaxExecOutputChars) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null && stderr.Length < _MaxExecOutputChars) stderr.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                    result.ExitCode = process.ExitCode;
                }
                catch (OperationCanceledException)
                {
                    result.TimedOut = !token.IsCancellationRequested;
                    try { process.Kill(entireProcessTree: true); } catch { }
                    try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    result.ExitCode = -1;
                }
            }
            catch (Exception ex)
            {
                stderr.AppendLine("Failed to start command: " + ex.Message);
                result.ExitCode = -1;
            }
            finally
            {
                stopwatch.Stop();
            }

            result.Stdout = stdout.ToString();
            result.Stderr = stderr.ToString();
            result.DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
            return result;
        }

        /// <inheritdoc />
        public async Task<WorkspaceExecResult> ExecAsync(VesselHost host, WorkspaceExecRequest request, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Command)) throw new ArgumentException("Command is required.", nameof(request));
            if (!host.IsHarbor)
            {
                WorkspaceExecResult local = await ExecAsync(host.Vessel, request, token).ConfigureAwait(false);
                local.Host = host.HostLabel;
                return local;
            }

            int timeoutSeconds = ClampTimeout(request.TimeoutSeconds);
            WorkspaceExecResult result = new WorkspaceExecResult
            {
                Command = request.Command,
                WorkingDirectory = host.WorkingDirectory,
                Host = host.HostLabel
            };

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                HostCommandRequest command = host.BuildShellCommand(request.Command, timeoutSeconds * 1000, false);
                command.Kind = HostCommandKindEnum.Command;
                command.Label = "Workspace";
                HostCommandResult run = await host.Commands.RunAsync(command, token).ConfigureAwait(false);
                result.ExitCode = run.TimedOut ? -1 : run.ExitCode;
                result.TimedOut = run.TimedOut;
                result.Stdout = Truncate(run.StandardOutput);
                result.Stderr = Truncate(run.StandardError);
            }
            catch (InvalidOperationException ex)
            {
                result.ExitCode = -1;
                result.Stderr = "Failed to start command on " + host.HostLabel + ": " + ex.Message + Environment.NewLine;
            }
            finally
            {
                stopwatch.Stop();
            }

            result.DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
            return result;
        }

        /// <inheritdoc />
        public Task<WorkspaceDiffResult> GetDiffAsync(Vessel vessel, string? path = null, CancellationToken token = default)
        {
            return GetDiffAsync(GetWorkspaceRoot(vessel), new LocalHostCommandExecutor(), path, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceDiffResult> GetDiffAsync(VesselHost host, string? path = null, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return GetDiffAsync(host.WorkingDirectory, host.Commands, path, token);
        }

        #endregion

        #region Private-Methods

        private static WorkspaceStatusResult BuildStatus(
            Vessel vessel,
            string rootPath,
            string? harborId,
            string hostLabel,
            WorkspaceChangesResult changes,
            IReadOnlyList<WorkspaceActiveMission>? activeMissions)
        {
            return new WorkspaceStatusResult
            {
                VesselId = vessel.Id,
                HasWorkingDirectory = true,
                RootPath = rootPath,
                BranchName = String.IsNullOrWhiteSpace(changes.BranchName) ? null : changes.BranchName,
                IsDirty = changes.IsDirty,
                CommitsAhead = changes.CommitsAhead,
                CommitsBehind = changes.CommitsBehind,
                ActiveMissionCount = activeMissions?.Count ?? 0,
                ActiveMissions = activeMissions?.ToList() ?? new List<WorkspaceActiveMission>(),
                Error = changes.Error,
                HarborId = harborId,
                Host = hostLabel
            };
        }

        private static async Task<WorkspaceChangesResult> GetChangesAsync(string rootPath, IHostCommandExecutor commands, CancellationToken token)
        {
            try
            {
                try
                {
                    await RunGitCommandAsync(commands, rootPath, token, "fetch", "origin", "--quiet").ConfigureAwait(false);
                }
                catch
                {
                }

                string output = await RunGitCommandAsync(commands, rootPath, token, "status", "--porcelain=v2", "-z", "--branch", "--untracked-files=all").ConfigureAwait(false);
                return ParseGitStatus(output);
            }
            catch (Exception ex)
            {
                return new WorkspaceChangesResult
                {
                    BranchName = String.Empty,
                    IsDirty = false,
                    CommitsAhead = 0,
                    CommitsBehind = 0,
                    Changes = new List<WorkspaceChangeEntry>(),
                    Error = ex.Message
                };
            }
        }

        private static async Task<WorkspaceDiffResult> GetDiffAsync(string rootPath, IHostCommandExecutor commands, string? path, CancellationToken token)
        {
            WorkspaceDiffResult result = new WorkspaceDiffResult
            {
                Path = String.IsNullOrWhiteSpace(path) ? null : path
            };

            try
            {
                // Fast guard: only diff when this exact path is the root of a git repository. Without it,
                // a non-repo path makes `git diff HEAD` walk up to any ancestor repository and scan it
                // (potentially huge and slow) before failing -- e.g. under a temp directory whose parent
                // happens to be a repo. rev-parse is fast and lets us fail closed immediately.
                string topLevel;
                try
                {
                    topLevel = (await RunGitCommandAsync(commands, rootPath, token, "rev-parse", "--show-toplevel").ConfigureAwait(false)).Trim();
                }
                catch
                {
                    result.Error = "Not a git repository.";
                    return result;
                }

                if (String.IsNullOrWhiteSpace(topLevel)
                    || !SamePath(rootPath, topLevel))
                {
                    result.Error = "Not a git repository rooted at this path.";
                    return result;
                }

                string diff;
                if (!String.IsNullOrWhiteSpace(path))
                {
                    string normalized = WorkspaceFileEngine.NormalizeRequestedPath(path);
                    if (normalized.Length == 0 || normalized.StartsWith(".git", StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("That path is not accessible.");
                    diff = await RunGitCommandAsync(commands, rootPath, token, "diff", "HEAD", "--", normalized).ConfigureAwait(false);
                }
                else
                {
                    diff = await RunGitCommandAsync(commands, rootPath, token, "diff", "HEAD").ConfigureAwait(false);
                }

                result.Diff = diff ?? String.Empty;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        private static bool SamePath(string rootPath, string topLevel)
        {
            if (PathCanonicalizer.AreEquivalent(rootPath, topLevel)) return true;

            // A path on another host (a Harbor) cannot be canonicalized here; compare it as text.
            string a = rootPath.Replace('\\', '/').TrimEnd('/');
            string b = topLevel.Replace('\\', '/').TrimEnd('/');
            return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int ClampTimeout(int timeoutSeconds)
        {
            return timeoutSeconds < 1 ? 1 : (timeoutSeconds > 600 ? 600 : timeoutSeconds);
        }

        private static string Truncate(string? value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            return value.Length <= _MaxExecOutputChars ? value : value.Substring(0, _MaxExecOutputChars);
        }

        private static string GetWorkspaceRoot(Vessel vessel)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            return WorkspaceFileEngine.RequireRoot(vessel.WorkingDirectory);
        }

        private static async Task<string> RunGitCommandAsync(IHostCommandExecutor commands, string workingDirectory, CancellationToken token, params string[] args)
        {
            HostCommandRequest request = new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string>(args),
                TimeoutMs = _GitCommandTimeoutSeconds * 1000,
                // Never prompt for credentials or invoke a pager -- either would hang the request.
                Environment = new Dictionary<string, string>
                {
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["GIT_PAGER"] = "cat"
                }
            };

            HostCommandResult result = await commands.RunAsync(request, token).ConfigureAwait(false);
            if (result.TimedOut)
                throw new InvalidOperationException("git command timed out after " + _GitCommandTimeoutSeconds + " seconds.");
            if (result.ExitCode != 0)
                throw new InvalidOperationException("git exited with code " + result.ExitCode + ": " + result.StandardError.Trim());
            return result.StandardOutput;
        }

        /// <summary>
        /// Parse git status --porcelain=v2 -z --branch output. Status codes keep the porcelain v1 shape
        /// ("M", "A", "R", "MM", "??", "UU") with '.' (unchanged) rendered as a space and trimmed.
        /// </summary>
        private static WorkspaceChangesResult ParseGitStatus(string output)
        {
            WorkspaceChangesResult result = new WorkspaceChangesResult();
            List<string> fields = GitMachineOutputParser.SplitNul(output);
            for (int i = 0; i < fields.Count; i++)
            {
                string field = fields[i];
                if (field.StartsWith("# ", StringComparison.Ordinal))
                {
                    ParseBranchHeader(result, field.Substring(2));
                    continue;
                }

                if (field.Length < 3 || field[1] != ' ')
                    continue;

                switch (field[0])
                {
                    case '1':
                        AddChange(result, field, 8, null);
                        break;
                    case '2':
                        // Rename/copy: the original path follows as the next NUL-terminated field.
                        string? originalPath = i + 1 < fields.Count ? fields[i + 1].Replace('\\', '/') : null;
                        i++;
                        AddChange(result, field, 9, originalPath);
                        break;
                    case 'u':
                        AddChange(result, field, 10, null);
                        break;
                    case '?':
                        result.Changes.Add(new WorkspaceChangeEntry
                        {
                            Path = field.Substring(2).Replace('\\', '/'),
                            Status = "??",
                            OriginalPath = null
                        });
                        break;
                }
            }

            result.IsDirty = result.Changes.Count > 0;
            return result;
        }

        private static void AddChange(WorkspaceChangesResult result, string record, int fieldsBeforePath, string? originalPath)
        {
            // Fixed-width fields are space separated; the path is everything after them (it may contain spaces).
            string[] parts = record.Split(' ', fieldsBeforePath + 1);
            if (parts.Length <= fieldsBeforePath) return;

            string xy = parts[1];
            string status = xy.Replace('.', ' ').Trim();
            result.Changes.Add(new WorkspaceChangeEntry
            {
                Path = parts[fieldsBeforePath].Replace('\\', '/'),
                Status = status,
                OriginalPath = originalPath
            });
        }

        private static void ParseBranchHeader(WorkspaceChangesResult result, string header)
        {
            int space = header.IndexOf(' ');
            if (space < 0) return;
            string key = header.Substring(0, space);
            string value = header.Substring(space + 1);

            if (String.Equals(key, "branch.head", StringComparison.Ordinal))
            {
                // Porcelain v1 reported a detached HEAD as "HEAD"; keep that value for API compatibility.
                result.BranchName = String.Equals(value, "(detached)", StringComparison.Ordinal) ? "HEAD" : value;
            }
            else if (String.Equals(key, "branch.ab", StringComparison.Ordinal))
            {
                // "+<ahead> -<behind>"
                string[] counts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (string count in counts)
                {
                    if (count.Length < 2) continue;
                    if (!int.TryParse(count.Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed)) continue;
                    if (count[0] == '+') result.CommitsAhead = parsed;
                    else if (count[0] == '-') result.CommitsBehind = parsed;
                }
            }
        }

        #endregion
    }
}
