namespace Armada.Server
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server.Mcp.Tools;
    using SyslogLogging;

    /// <summary>
    /// Orchestrates an Admiral self-rebuild: resolve the source repository, publish the new server (and,
    /// best-effort, the dashboard) into a fresh slot from a detached worktree at the chosen ref, back up the
    /// database, flip the slot pointer, and hand over to the new slot via the replacement-process baton. The
    /// server build runs while this instance keeps serving; only a successful publish triggers a cutover, so a
    /// failed build never disturbs the running server. See docs/SERVER_REBUILD.md.
    /// </summary>
    public class ServerRebuildService
    {
        #region Public-Members

        /// <summary>
        /// The most recent rebuild status, or null if none has run in this process (and none was persisted).
        /// </summary>
        public ServerRebuildStatus? Latest
        {
            get
            {
                lock (_Lock) { return _Latest; }
            }
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[ServerRebuildService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly ISlotManager _Slots;
        private readonly IHostCommandExecutor _Host;
        private readonly LoggingModule _Logging;
        private readonly Action _StopCallback;
        private readonly HarborConnectionManager? _HarborConnections;
        private readonly string _TargetFramework;
        private readonly string _StatusFilePath;
        private readonly object _Lock = new object();
        private readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private ServerRebuildStatus? _Latest = null;
        private bool _InProgress = false;
        private const int _PublishTimeoutMs = 600000;
        private const int _GitTimeoutMs = 120000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver (used to resolve the self vessel and the schema version).</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="slots">Slot manager owning the slot layout and pointer.</param>
        /// <param name="host">Host-command executor for git and dotnet (Local mode by default).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="stopCallback">Callback that gracefully stops this Admiral, invoked after the replacement
        /// is launched at cutover.</param>
        /// <param name="harborConnections">Optional Harbor connection manager. When a supervising Harbor is
        /// configured (<c>settings.RebuildSupervisorHarborId</c>) and connected, the cutover is delegated to it
        /// for a health-gated rollback; otherwise the in-process baton is used.</param>
        /// <param name="targetFramework">Target framework moniker passed to <c>dotnet publish -f</c>; defaults to
        /// <c>net10.0</c>.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public ServerRebuildService(
            DatabaseDriver database,
            ArmadaSettings settings,
            ISlotManager slots,
            IHostCommandExecutor host,
            LoggingModule logging,
            Action stopCallback,
            HarborConnectionManager? harborConnections = null,
            string targetFramework = "net10.0")
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Slots = slots ?? throw new ArgumentNullException(nameof(slots));
            _Host = host ?? throw new ArgumentNullException(nameof(host));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _StopCallback = stopCallback ?? throw new ArgumentNullException(nameof(stopCallback));
            _HarborConnections = harborConnections;
            _TargetFramework = String.IsNullOrWhiteSpace(targetFramework) ? "net10.0" : targetFramework;
            _StatusFilePath = Path.Combine(_Settings.DataDirectory, "rebuild-status.json");
            _Slots.RetentionCount = _Settings.RebuildSlotRetentionCount;

            LoadPersistedStatus();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the most recent rebuild status by id, or null when it does not match the latest known rebuild.
        /// </summary>
        /// <param name="rebuildId">Rebuild id.</param>
        /// <returns>The matching status, or null.</returns>
        public ServerRebuildStatus? GetById(string rebuildId)
        {
            if (String.IsNullOrWhiteSpace(rebuildId)) return null;
            lock (_Lock)
            {
                if (_Latest != null && String.Equals(_Latest.RebuildId, rebuildId, StringComparison.Ordinal))
                    return _Latest;
                return null;
            }
        }

        /// <summary>
        /// Begin a rebuild. Validates preconditions, records a <see cref="ServerRebuildStatusEnum.Building"/>
        /// status, launches the build on a background task, and returns immediately with the initial snapshot.
        /// </summary>
        /// <param name="request">Rebuild request.</param>
        /// <returns>The initial rebuild status.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a rebuild is already in progress.</exception>
        public ServerRebuildStatus StartRebuild(ServerRebuildRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            lock (_Lock)
            {
                if (_InProgress) throw new InvalidOperationException("A rebuild is already in progress.");
                _InProgress = true;
            }

            ServerRebuildStatus status = new ServerRebuildStatus
            {
                Ref = String.IsNullOrWhiteSpace(request.Ref) ? null : request.Ref!.Trim(),
                Status = ServerRebuildStatusEnum.Building
            };

            lock (_Lock) { _Latest = status; }
            Append(status, "Rebuild " + status.RebuildId + " started.");
            Persist(status);

            _ = Task.Run(() => RunAsync(status, request));
            return Snapshot(status);
        }

        /// <summary>
        /// Roll back to the previous slot after a successful cutover. When the rebuild migrated the database
        /// schema (the live schema version differs from the version recorded before the rebuild), the
        /// pre-rebuild backup is restored first, which discards everything written on the new build since
        /// cutover; otherwise the previous slot is simply relaunched. Flips the slot pointer to the previous
        /// slot, launches it, and stops this instance.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated rebuild status.</returns>
        /// <exception cref="RebuildException">Thrown when there is nothing to roll back to.</exception>
        public async Task<ServerRebuildStatus> RollbackAsync(CancellationToken token = default)
        {
            ServerRebuildStatus? latest = Latest;
            if (latest == null || String.IsNullOrWhiteSpace(latest.PreviousSlot))
                throw new RebuildException("There is no previous slot to roll back to.");

            string previousSlot = latest.PreviousSlot!;
            string previousExe = _Slots.GetSlotExecutablePath(previousSlot);
            if (!File.Exists(previousExe))
                throw new RebuildException("Previous slot executable not found at " + previousExe + "; cannot roll back.");

            // Decide whether a database restore is required: only when the schema version changed.
            bool migrated = false;
            if (latest.PreRebuildSchemaVersion.HasValue)
            {
                int current = await _Database.GetSchemaVersionAsync(token).ConfigureAwait(false);
                migrated = current != latest.PreRebuildSchemaVersion.Value;
            }

            if (migrated)
            {
                if (String.IsNullOrWhiteSpace(latest.BackupPath) || !File.Exists(latest.BackupPath))
                    throw new RebuildException("The rebuild migrated the database but its pre-rebuild backup is missing; automatic rollback is not possible. Restore a backup manually.");

                Append(latest, "Rollback: schema changed since rebuild; restoring pre-rebuild backup " + latest.BackupPath + " (data written since cutover will be lost).");
                await McpToolHelpers.PerformRestoreAsync(_Database, _Settings, latest.BackupPath!, "pre-rebuild backup").ConfigureAwait(false);
            }
            else
            {
                Append(latest, "Rollback: schema unchanged; relaunching previous slot without a database restore.");
            }

            await _Slots.WriteCurrentAsync(previousSlot, token).ConfigureAwait(false);
            Append(latest, "Flipped current pointer back to " + previousSlot + ".");

            lock (_Lock)
            {
                latest.Status = ServerRebuildStatusEnum.RolledBack;
                latest.CompletedUtc = DateTime.UtcNow;
            }
            Persist(latest);

            bool launched = ReplacementProcessLauncher.Launch(previousExe, _Logging, _Header);
            if (!launched)
                throw new RebuildException("Failed to launch the previous slot at " + previousExe + "; the pointer was rolled back but a manual restart is required.");

            ServerRebuildStatus snapshot = Snapshot(latest);
            _ = Task.Run(async () =>
            {
                await Task.Delay(500).ConfigureAwait(false);
                _StopCallback();
            });
            return snapshot;
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(ServerRebuildStatus status, ServerRebuildRequest request)
        {
            string? worktree = null;
            try
            {
                string sourcePath = await ResolveSourcePathAsync(request, CancellationToken.None).ConfigureAwait(false);
                Append(status, "Source: " + sourcePath);

                string refOrHead = String.IsNullOrWhiteSpace(request.Ref) ? "HEAD" : request.Ref!.Trim();
                string sha = await ResolveShaAsync(sourcePath, refOrHead, status).ConfigureAwait(false);
                status.Sha = sha;

                string slot = _Slots.ComposeSlotName(DateTime.UtcNow, sha);
                status.Slot = slot;
                status.PreviousSlot = await _Slots.ReadCurrentAsync(CancellationToken.None).ConfigureAwait(false);
                Append(status, "Target slot: " + slot + (status.PreviousSlot != null ? " (previous: " + status.PreviousSlot + ")" : ""));

                // Record the schema version so rollback can tell whether a migration occurred.
                try { status.PreRebuildSchemaVersion = await _Database.GetSchemaVersionAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception e) { _Logging.Warn(_Header + "could not read schema version: " + e.Message); }

                // Back up the database before any cutover so a bad migration is recoverable.
                await BackupDatabaseAsync(status, slot).ConfigureAwait(false);

                // Build the server from a throwaway detached worktree so the operator's checkout is untouched.
                worktree = Path.Combine(Path.GetTempPath(), "armada-rebuild-" + Guid.NewGuid().ToString("N"));
                await AddWorktreeAsync(sourcePath, worktree, refOrHead, status).ConfigureAwait(false);

                string slotDir = _Slots.GetSlotDirectory(slot);
                await PublishServerAsync(worktree, slotDir, status).ConfigureAwait(false);

                if (!request.SkipDashboard)
                    await BuildDashboardBestEffortAsync(sourcePath, status).ConfigureAwait(false);

                // Publish succeeded: flip the pointer and cut over.
                await _Slots.WriteCurrentAsync(slot, CancellationToken.None).ConfigureAwait(false);
                Append(status, "Flipped current pointer to " + slot + ".");

                lock (_Lock) { status.Status = ServerRebuildStatusEnum.CuttingOver; }
                Persist(status);

                string slotExe = _Slots.GetSlotExecutablePath(slot);

                // Prefer a supervising Harbor for a health-gated cutover (it can roll back if the new slot fails
                // to come up); fall back to the in-process baton when no Harbor is armed.
                bool harborArmed = await TryDelegateCutoverToHarborAsync(status, slot, slotExe, request).ConfigureAwait(false);
                if (!harborArmed)
                {
                    bool launched = ReplacementProcessLauncher.Launch(slotExe, _Logging, _Header);
                    if (!launched)
                    {
                        Fail(status, "Publish succeeded but the replacement process could not be launched from " + slotExe + ". The current pointer now names the new slot; a manual restart will pick it up.");
                        return;
                    }
                    Append(status, "Replacement launched from " + slotExe + " (baton); stopping this instance for handover.");
                }

                await _Slots.PruneAsync(CancellationToken.None).ConfigureAwait(false);

                lock (_Lock)
                {
                    status.Status = ServerRebuildStatusEnum.Succeeded;
                    status.CompletedUtc = DateTime.UtcNow;
                }
                Persist(status);

                // Give the replacement a moment to register the predecessor wait, then stop so the port frees.
                await Task.Delay(500).ConfigureAwait(false);
                _StopCallback();
            }
            catch (Exception e)
            {
                Fail(status, e.Message);
                _Logging.Warn(_Header + "rebuild " + status.RebuildId + " failed: " + e.ToString());
            }
            finally
            {
                if (worktree != null) await RemoveWorktreeAsync(worktree, status).ConfigureAwait(false);
                lock (_Lock) { _InProgress = false; }
            }
        }

        private async Task<bool> TryDelegateCutoverToHarborAsync(ServerRebuildStatus status, string slot, string slotExe, ServerRebuildRequest request)
        {
            string? harborId = _Settings.RebuildSupervisorHarborId;
            if (_HarborConnections == null || String.IsNullOrWhiteSpace(harborId)) return false;
            if (!_HarborConnections.IsConnected(harborId!))
            {
                Append(status, "Supervising Harbor '" + harborId + "' is not connected; using the in-process baton.");
                return false;
            }

            string? fallbackExe = String.IsNullOrWhiteSpace(status.PreviousSlot) ? null : _Slots.GetSlotExecutablePath(status.PreviousSlot!);
            HarborDeferredLaunchRequest deferred = new HarborDeferredLaunchRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                LaunchExePath = slotExe,
                WaitForPid = Environment.ProcessId,
                WorkingDirectory = _Slots.GetSlotDirectory(slot),
                HealthUrl = "http://127.0.0.1:" + _Settings.AdmiralPort + "/api/v1/status/health",
                HealthTimeoutSeconds = request.RollbackTimeoutSeconds,
                FallbackExePath = fallbackExe ?? String.Empty,
                FallbackSlot = status.PreviousSlot ?? String.Empty,
                CurrentPointerPath = _Slots.CurrentPointerPath
            };

            try
            {
                HarborDeferredLaunchAck? ack = await _HarborConnections.SendDeferredLaunchAsync(harborId!, deferred, 10000, CancellationToken.None).ConfigureAwait(false);
                if (ack != null && ack.Armed)
                {
                    Append(status, "Cutover delegated to Harbor '" + harborId + "' with health-gated rollback (timeout " + deferred.HealthTimeoutSeconds + "s); stopping this instance for handover.");
                    return true;
                }

                Append(status, "Harbor '" + harborId + "' did not arm the cutover" + (ack?.Message != null ? " (" + ack.Message + ")" : "") + "; using the in-process baton.");
                return false;
            }
            catch (Exception e)
            {
                Append(status, "Harbor cutover delegation failed (" + e.Message + "); using the in-process baton.");
                return false;
            }
        }

        private async Task<string> ResolveSourcePathAsync(ServerRebuildRequest request, CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(request.SourcePath))
            {
                if (!Directory.Exists(request.SourcePath))
                    throw new RebuildException("Supplied source path does not exist: " + request.SourcePath);
                return request.SourcePath!;
            }

            if (String.IsNullOrWhiteSpace(_Settings.SelfVesselId))
                throw new RebuildException("No source path supplied and settings.SelfVesselId is not set. Designate the Armada vessel in Settings before rebuilding.");

            Vessel? vessel = await _Database.Vessels.ReadAsync(_Settings.SelfVesselId!, token).ConfigureAwait(false);
            if (vessel == null)
                throw new RebuildException("SelfVesselId '" + _Settings.SelfVesselId + "' does not resolve to a known vessel.");

            string? path = !String.IsNullOrWhiteSpace(vessel.WorkingDirectory) ? vessel.WorkingDirectory : vessel.LocalPath;
            if (String.IsNullOrWhiteSpace(path))
                throw new RebuildException("Vessel '" + vessel.Id + "' has neither a WorkingDirectory nor a LocalPath to build from.");
            if (!Directory.Exists(path))
                throw new RebuildException("Resolved source path does not exist: " + path);

            return path!;
        }

        private async Task<string> ResolveShaAsync(string sourcePath, string refOrHead, ServerRebuildStatus status)
        {
            HostCommandResult result = await RunGitAsync(sourcePath, status, _GitTimeoutMs, "rev-parse", refOrHead).ConfigureAwait(false);
            if (!result.Success)
                throw new RebuildException("Failed to resolve ref '" + refOrHead + "': " + result.StandardError.Trim());
            return result.StandardOutput.Trim();
        }

        private async Task AddWorktreeAsync(string sourcePath, string worktree, string refOrHead, ServerRebuildStatus status)
        {
            Append(status, "Creating detached worktree at " + worktree + " (" + refOrHead + ")...");
            HostCommandResult result = await RunGitAsync(sourcePath, status, _GitTimeoutMs, "worktree", "add", "--detach", worktree, refOrHead).ConfigureAwait(false);
            if (!result.Success)
                throw new RebuildException("git worktree add failed: " + result.StandardError.Trim());
        }

        private async Task RemoveWorktreeAsync(string worktree, ServerRebuildStatus status)
        {
            try
            {
                // Remove via git against the worktree itself; --force clears build artifacts (obj/bin).
                HostCommandResult result = await RunGitAsync(worktree, status, _GitTimeoutMs, "worktree", "remove", "--force", worktree).ConfigureAwait(false);
                if (!result.Success && Directory.Exists(worktree))
                {
                    try { Directory.Delete(worktree, true); }
                    catch (Exception e) { _Logging.Warn(_Header + "could not delete worktree " + worktree + ": " + e.Message); }
                }
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "worktree cleanup failed for " + worktree + ": " + e.Message);
            }
        }

        private async Task PublishServerAsync(string worktree, string slotDir, ServerRebuildStatus status)
        {
            Append(status, "Publishing server (dotnet publish -f " + _TargetFramework + ") to " + slotDir + "...");
            HostCommandResult result = await _Host.RunAsync(new HostCommandRequest
            {
                Executable = "dotnet",
                WorkingDirectory = worktree,
                Arguments = { "publish", "src/Armada.Server", "-c", "Release", "-f", _TargetFramework, "-o", slotDir },
                TimeoutMs = _PublishTimeoutMs
            }, CancellationToken.None).ConfigureAwait(false);

            AppendCommandOutput(status, result);
            if (!result.Success)
                throw new RebuildException("dotnet publish failed with exit code " + result.ExitCode + (result.TimedOut ? " (timed out)" : "") + ".");

            Append(status, "Server publish complete.");
        }

        private async Task BuildDashboardBestEffortAsync(string sourcePath, ServerRebuildStatus status)
        {
            string dashboardDir = Path.Combine(sourcePath, "src", "Armada.Dashboard");
            if (!Directory.Exists(dashboardDir))
            {
                Append(status, "Dashboard source not found at " + dashboardDir + "; skipping dashboard build.");
                return;
            }

            Append(status, "Building dashboard (npm run build) from " + dashboardDir + " ...");
            // On Windows, invoke npm through cmd.exe rather than spawning npm.cmd directly. The npm.cmd shim
            // locates its own CLI relative to how it was launched; started directly via CreateProcess (no
            // shell) it can misresolve and fail with "Cannot find module ...\npm\bin\npm-cli.js". Going through
            // cmd.exe /c resolves npm from PATH exactly like an interactive shell does.
            HostCommandRequest buildRequest = new HostCommandRequest
            {
                WorkingDirectory = dashboardDir,
                TimeoutMs = _PublishTimeoutMs
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                buildRequest.Executable = "cmd.exe";
                buildRequest.Arguments.Add("/d");
                buildRequest.Arguments.Add("/s");
                buildRequest.Arguments.Add("/c");
                buildRequest.Arguments.Add("npm run build");
            }
            else
            {
                buildRequest.Executable = "npm";
                buildRequest.Arguments.Add("run");
                buildRequest.Arguments.Add("build");
            }
            HostCommandResult build = await _Host.RunAsync(buildRequest, CancellationToken.None).ConfigureAwait(false);

            AppendCommandOutput(status, build);
            if (!build.Success)
            {
                // Best-effort: a dashboard build failure does not abort the server cutover. The running/new
                // server falls back to the previously deployed dashboard bundle.
                Append(status, "Dashboard build failed (exit " + build.ExitCode + "); continuing with the existing dashboard bundle.");
                return;
            }

            string distDir = Path.Combine(dashboardDir, "dist");
            string targetDir = Path.Combine(_Settings.DataDirectory, "dashboard");
            if (!File.Exists(Path.Combine(distDir, "index.html")))
            {
                Append(status, "Dashboard build produced no dist/index.html; skipping deploy.");
                return;
            }

            try
            {
                CopyDirectory(distDir, targetDir);
                Append(status, "Dashboard deployed to " + targetDir + ".");
            }
            catch (Exception e)
            {
                Append(status, "Dashboard deploy failed: " + e.Message + "; continuing with the existing bundle.");
            }
        }

        private async Task BackupDatabaseAsync(ServerRebuildStatus status, string slot)
        {
            try
            {
                string backupsDir = Path.Combine(ArmadaConstants.DefaultDataDirectory, "backups");
                Directory.CreateDirectory(backupsDir);
                string backupPath = Path.Combine(backupsDir, "pre-rebuild-" + slot + ".zip");
                await McpToolHelpers.PerformBackupAsync(_Database, _Settings, backupPath).ConfigureAwait(false);
                status.BackupPath = backupPath;
                Append(status, "Database backed up to " + backupPath + ".");

                // Retain only the most recent pre-rebuild backups (the newest is this rebuild's rollback
                // safety net); older ones from previous rebuilds are no longer needed and would otherwise
                // accumulate forever. Bounded by the same retention count as published slots.
                PruneOldBackups(status, backupsDir);
            }
            catch (Exception e)
            {
                // A backup failure is not fatal to the build, but it removes the rollback safety net, so it is
                // surfaced loudly in the log.
                Append(status, "WARNING: database backup failed: " + e.Message + ". Rollback after this rebuild will not be possible.");
                _Logging.Warn(_Header + "database backup failed before rebuild " + status.RebuildId + ": " + e.Message);
            }
        }

        /// <summary>
        /// Delete old pre-rebuild database backups, keeping only the most recent
        /// <see cref="ArmadaSettings.RebuildSlotRetentionCount"/> (floored at 1). Called after each new backup
        /// is written, so backups are pruned on every rebuild rather than growing without bound. Best-effort:
        /// a failure to delete an old backup is logged but never fails the rebuild.
        /// </summary>
        private void PruneOldBackups(ServerRebuildStatus status, string backupsDir)
        {
            try
            {
                int keep = Math.Max(1, _Settings.RebuildSlotRetentionCount);
                string[] backups = Directory.GetFiles(backupsDir, "pre-rebuild-*.zip");
                if (backups.Length <= keep) return;

                List<FileInfo> ordered = new List<FileInfo>();
                foreach (string path in backups) ordered.Add(new FileInfo(path));
                ordered.Sort((left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));

                int removed = 0;
                for (int i = keep; i < ordered.Count; i++)
                {
                    try { ordered[i].Delete(); removed++; }
                    catch (Exception e) { _Logging.Warn(_Header + "could not delete old backup " + ordered[i].FullName + ": " + e.Message); }
                }

                if (removed > 0)
                    Append(status, "Pruned " + removed + " old database backup(s), keeping the " + keep + " most recent.");
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "backup pruning failed: " + e.Message);
            }
        }

        private async Task<HostCommandResult> RunGitAsync(string workingDirectory, ServerRebuildStatus status, int timeoutMs, params string[] arguments)
        {
            HostCommandRequest request = new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                TimeoutMs = timeoutMs
            };
            foreach (string argument in arguments) request.Arguments.Add(argument);
            return await _Host.RunAsync(request, CancellationToken.None).ConfigureAwait(false);
        }

        private void Fail(ServerRebuildStatus status, string error)
        {
            lock (_Lock)
            {
                status.Status = ServerRebuildStatusEnum.Failed;
                status.Error = error;
                status.CompletedUtc = DateTime.UtcNow;
            }
            Append(status, "FAILED: " + error);
            Persist(status);
        }

        private void Append(ServerRebuildStatus status, string line)
        {
            lock (_Lock)
            {
                StringBuilder sb = new StringBuilder(status.Log);
                sb.Append('[').Append(DateTime.UtcNow.ToString("HH:mm:ss")).Append("] ").Append(line).Append('\n');
                status.Log = sb.ToString();
            }
            _Logging.Debug(_Header + line);
        }

        private void AppendCommandOutput(ServerRebuildStatus status, HostCommandResult result)
        {
            if (!String.IsNullOrWhiteSpace(result.StandardOutput)) Append(status, result.StandardOutput.TrimEnd());
            if (!String.IsNullOrWhiteSpace(result.StandardError)) Append(status, result.StandardError.TrimEnd());
        }

        private ServerRebuildStatus Snapshot(ServerRebuildStatus status)
        {
            lock (_Lock)
            {
                return new ServerRebuildStatus
                {
                    RebuildId = status.RebuildId,
                    Slot = status.Slot,
                    PreviousSlot = status.PreviousSlot,
                    Sha = status.Sha,
                    Ref = status.Ref,
                    BackupPath = status.BackupPath,
                    Status = status.Status,
                    StartedUtc = status.StartedUtc,
                    CompletedUtc = status.CompletedUtc,
                    Error = status.Error,
                    Log = status.Log
                };
            }
        }

        private void Persist(ServerRebuildStatus status)
        {
            try
            {
                ServerRebuildStatus snapshot = Snapshot(status);
                string json = JsonSerializer.Serialize(snapshot, _JsonOptions);
                File.WriteAllText(_StatusFilePath, json);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not persist rebuild status: " + e.Message);
            }
        }

        private void LoadPersistedStatus()
        {
            try
            {
                if (!File.Exists(_StatusFilePath)) return;
                string json = File.ReadAllText(_StatusFilePath);
                ServerRebuildStatus? loaded = JsonSerializer.Deserialize<ServerRebuildStatus>(json, _JsonOptions);
                if (loaded != null)
                {
                    // A rebuild that was mid-cutover when this (replacement) process started is, by virtue of
                    // this process running, effectively complete.
                    if (loaded.Status == ServerRebuildStatusEnum.CuttingOver)
                    {
                        loaded.Status = ServerRebuildStatusEnum.Succeeded;
                        loaded.CompletedUtc ??= DateTime.UtcNow;
                    }
                    lock (_Lock) { _Latest = loaded; }
                }
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not load persisted rebuild status: " + e.Message);
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                string destination = Path.Combine(targetDir, relative);
                string? destinationDir = Path.GetDirectoryName(destination);
                if (!String.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);
                File.Copy(file, destination, true);
            }
        }

        #endregion
    }
}
