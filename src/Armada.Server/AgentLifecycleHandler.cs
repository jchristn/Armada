namespace Armada.Server
{
    using System.Diagnostics;
    using System.IO;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using Armada.Server.WebSocket;

    /// <summary>
    /// Handles agent process lifecycle: launching, heartbeats, output parsing, process exit, and stopping.
    /// </summary>
    public class AgentLifecycleHandler
    {
        #region Private-Members

        private string _Header = "[AgentLifecycle] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private AgentRuntimeFactory _RuntimeFactory;
        private IHostProcessExecutor _HostProcessExecutor;
        private HarborConnectionManager? _HarborConnections;
        private MuxCliService _MuxCli;
        private IAdmiralService _Admiral;
        private IMessageTemplateService _TemplateService;
        private IPromptTemplateService? _PromptTemplateService;
        private ArmadaWebSocketHub? _WebSocketHub;
        private Func<string, string, string?, string?, string?, string?, string?, string?, Task> _EmitEventAsync;
        private readonly TimeSpan _ModelValidationTimeout = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _MissionHeartbeatPersistInterval = TimeSpan.FromSeconds(15);
        private const int _MaxMissionOutputChars = 262144;

        /// <summary>
        /// Bounds how many papercuts one mission can store. A captain is asked for ten at most,
        /// so the store never receives more than the brief requested.
        /// </summary>
        private const int _MaxPapercutsPerMission = 10;

        /// <summary>
        /// Accumulates agent stdout per mission for pipeline handoff.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.StringBuilder> _MissionOutput = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.StringBuilder>();

        /// <summary>
        /// Tracks how many papercuts each mission has stored, so a runaway reporter is capped.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, int> _MissionPapercutCounts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();

        /// <summary>
        /// Throttles mission heartbeat persistence so verbose logs do not rewrite mission/voyage rows on every output line.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _MissionHeartbeatWrites = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();

        /// <summary>
        /// Tracks per-mission final response artifacts so canonical agent output can be recovered even if live streaming is noisy.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, string> _MissionFinalMessageFiles = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        /// <summary>
        /// Tracks launches that have started but have not yet completed HandleLaunchAgentAsync registration.
        /// This closes the race where a fast process can emit output or exit before the PID mapping is written.
        /// </summary>
        private sealed class PendingLaunchInfo
        {
            public string CaptainId { get; set; } = String.Empty;

            public string MissionId { get; set; } = String.Empty;
        }

        private System.Collections.Concurrent.ConcurrentDictionary<string, PendingLaunchInfo> _PendingLaunches = new System.Collections.Concurrent.ConcurrentDictionary<string, PendingLaunchInfo>();

        /// <summary>
        /// Maps process IDs to captain IDs for progress tracking.
        /// </summary>
        private Dictionary<int, string> _ProcessToCaptain = new Dictionary<int, string>();

        /// <summary>
        /// Maps process IDs to mission IDs for per-mission progress tracking.
        /// </summary>
        private Dictionary<int, string> _ProcessToMission = new Dictionary<int, string>();

        /// <summary>
        /// Tracks process IDs whose exit has been received via the OnProcessExited callback.
        /// Used by the health check to avoid racing with the async exit handler.
        /// Entries are pruned after 5 minutes.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _HandledProcessExits = new System.Collections.Concurrent.ConcurrentDictionary<int, DateTime>();

        /// <summary>
        /// Tracks per-process liveness heartbeat loops so silent-but-busy runtimes still refresh telemetry.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<int, CancellationTokenSource> _ProcessHeartbeatLoops = new System.Collections.Concurrent.ConcurrentDictionary<int, CancellationTokenSource>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="runtimeFactory">Agent runtime factory.</param>
        /// <param name="admiral">Admiral service.</param>
        /// <param name="templateService">Message template service.</param>
        /// <param name="promptTemplateService">Prompt template service (optional).</param>
        /// <param name="webSocketHub">WebSocket hub (nullable).</param>
        /// <param name="emitEventAsync">Delegate to emit events.</param>
        public AgentLifecycleHandler(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            AgentRuntimeFactory runtimeFactory,
            IAdmiralService admiral,
            IMessageTemplateService templateService,
            IPromptTemplateService? promptTemplateService,
            ArmadaWebSocketHub? webSocketHub,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEventAsync)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _RuntimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            _HostProcessExecutor = new LocalHostProcessExecutor(_RuntimeFactory);
            _MuxCli = new MuxCliService(_Logging);
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));
            _TemplateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _PromptTemplateService = promptTemplateService;
            _WebSocketHub = webSocketHub;
            _EmitEventAsync = emitEventAsync ?? throw new ArgumentNullException(nameof(emitEventAsync));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Provide the Harbor connection manager so captain launches are delegated to a connected Harbor by
        /// default. When set and a Harbor is eligible, launches run on the Harbor host; when null or no Harbor
        /// is eligible, launches fall back to in-process (local) execution.
        /// </summary>
        /// <param name="manager">Harbor connection manager, or null to force local execution.</param>
        public void SetHarborConnections(HarborConnectionManager? manager)
        {
            _HarborConnections = manager;
        }

        /// <summary>
        /// Retrieve and clear accumulated stdout output for a mission.
        /// Used by pipeline handoff to pass agent output to the next stage.
        /// </summary>
        public string? GetAndClearMissionOutput(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return null;
            _MissionPapercutCounts.TryRemove(missionId, out _);
            string? streamedOutput = null;
            if (_MissionOutput.TryRemove(missionId, out System.Text.StringBuilder? sb))
            {
                streamedOutput = sb.ToString().Trim();
                if (String.IsNullOrEmpty(streamedOutput))
                    streamedOutput = null;
            }

            if (_MissionFinalMessageFiles.TryRemove(missionId, out string? finalMessageFilePath) &&
                !String.IsNullOrEmpty(finalMessageFilePath))
            {
                try
                {
                    if (File.Exists(finalMessageFilePath))
                    {
                        string finalMessage = File.ReadAllText(finalMessageFilePath).Trim();
                        try { File.Delete(finalMessageFilePath); } catch { }
                        if (!String.IsNullOrEmpty(finalMessage))
                            return finalMessage;
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error reading final message artifact for mission " + missionId + ": " + ex.Message);
                }
            }

            return streamedOutput;
        }

        /// <summary>
        /// Check whether a process exit has already been received for the given PID.
        /// The health check uses this to avoid triggering recovery for a process
        /// whose exit is already being handled by the async exit callback.
        /// </summary>
        /// <param name="processId">OS process ID to check.</param>
        /// <returns>True if the exit callback has already fired for this PID.</returns>
        public bool IsProcessExitHandled(int processId)
        {
            // Prune stale entries older than 5 minutes
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-5);
            foreach (System.Collections.Generic.KeyValuePair<int, DateTime> kvp in _HandledProcessExits)
            {
                if (kvp.Value < cutoff)
                    _HandledProcessExits.TryRemove(kvp.Key, out _);
            }

            return _HandledProcessExits.ContainsKey(processId);
        }

        /// <summary>
        /// Set or update the WebSocket hub reference (created after this handler).
        /// </summary>
        /// <param name="hub">WebSocket hub instance, or null.</param>
        public void SetWebSocketHub(ArmadaWebSocketHub? hub)
        {
            _WebSocketHub = hub;
        }

        /// <summary>
        /// Validate that the captain's configured model can be launched by its runtime.
        /// Returns null if validation succeeds, otherwise an error message.
        /// </summary>
        /// <param name="captain">Captain to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Null if valid, otherwise an error message.</returns>
        public Task<string?> ValidateCaptainModelAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint)
                return ValidateApiEndpointCaptainAsync(captain, token);
            if (captain.Runtime == AgentRuntimeEnum.Mux)
                return ValidateMuxCaptainAsync(captain, token);
            return ValidateModelAsync(captain.Runtime, captain.Model, token);
        }

        /// <summary>
        /// Validate an API-endpoint captain: it must reference an existing, enabled, Inference-kind model
        /// endpoint. Returns null when valid, otherwise a human-readable error.
        /// </summary>
        /// <param name="captain">Captain to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Null when valid, otherwise an error message.</returns>
        private async Task<string?> ValidateApiEndpointCaptainAsync(Captain captain, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captain.ModelEndpointId))
                return "An API-endpoint captain must reference an inference model endpoint. Choose one under Configuration > Endpoints.";

            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId, token).ConfigureAwait(false);
            if (endpoint == null)
                return "The referenced model endpoint (" + captain.ModelEndpointId + ") does not exist.";
            if (endpoint.Kind != ModelEndpointKindEnum.Inference)
                return "The referenced model endpoint '" + endpoint.Name + "' is an " + endpoint.Kind + " endpoint; an API-endpoint captain requires an Inference endpoint.";
            if (!endpoint.Enabled)
                return "The referenced model endpoint '" + endpoint.Name + "' is disabled. Enable it or choose another.";

            return null;
        }

        /// <summary>
        /// Validate that the given runtime can start with the requested model.
        /// Returns null if validation succeeds, otherwise an error message.
        /// </summary>
        /// <param name="runtimeType">Runtime to validate.</param>
        /// <param name="model">Model to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Null if valid, otherwise an error message.</returns>
        public async Task<string?> ValidateModelAsync(AgentRuntimeEnum runtimeType, string? model, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(model))
                return null;

            string validationDirectory = Path.Combine(Path.GetTempPath(), "armada-model-validation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(validationDirectory);

            Armada.Runtimes.Interfaces.IAgentRuntime runtime;
            try
            {
                runtime = _HostProcessExecutor.CreateRuntime(runtimeType);
            }
            catch (Exception ex)
            {
                try { Directory.Delete(validationDirectory, true); } catch { }
                return "Unable to create runtime " + runtimeType + " for model validation: " + ex.Message;
            }

            object outputLock = new object();
            System.Text.StringBuilder output = new System.Text.StringBuilder();
            TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.OnOutputReceived += (processId, line) =>
            {
                lock (outputLock)
                {
                    if (output.Length < 4096)
                    {
                        output.AppendLine(line);
                    }
                }
            };
            runtime.OnProcessExited += (processId, exitCode) => exitSource.TrySetResult(exitCode);

            int? processId = null;

            try
            {
                await InitializeValidationWorkspaceAsync(runtimeType, validationDirectory, token).ConfigureAwait(false);

                processId = await runtime.StartAsync(
                    validationDirectory,
                    "Respond with the single word OK.",
                    model: model,
                    captain: null,
                    token: token).ConfigureAwait(false);

                Task completedTask = await Task.WhenAny(
                    exitSource.Task,
                    Task.Delay(_ModelValidationTimeout, token)).ConfigureAwait(false);

                if (completedTask == exitSource.Task)
                {
                    int? exitCode = await exitSource.Task.ConfigureAwait(false);
                    if (!exitCode.HasValue || exitCode.Value == 0)
                    {
                        return null;
                    }

                    string? details;
                    lock (outputLock)
                    {
                        details = ExtractModelValidationError(output.ToString());
                    }

                    if (!String.IsNullOrEmpty(details))
                    {
                        return "Model '" + model + "' failed validation for runtime " + runtimeType + ": " + details;
                    }

                    return "Model '" + model + "' failed validation for runtime " + runtimeType + " with exit code " + exitCode.Value + ".";
                }

                token.ThrowIfCancellationRequested();

                string? timeoutDetails;
                lock (outputLock)
                {
                    timeoutDetails = ExtractModelValidationError(output.ToString());
                }

                string timeoutMessage =
                    "Model '" + model + "' failed validation for runtime " + runtimeType +
                    ": validation timed out after " + _ModelValidationTimeout.TotalSeconds.ToString("0") + " seconds.";

                if (!String.IsNullOrEmpty(timeoutDetails))
                {
                    timeoutMessage += " " + timeoutDetails;
                }

                return timeoutMessage;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "Model '" + model + "' failed validation for runtime " + runtimeType + ": " + ex.Message;
            }
            finally
            {
                if (processId.HasValue)
                {
                    try
                    {
                        await runtime.StopAsync(processId.Value, token).ConfigureAwait(false);
                    }
                    catch { }
                }

                try { Directory.Delete(validationDirectory, true); } catch { }
            }
        }

        /// <summary>
        /// Launch an agent process for the given captain, mission, and dock.
        /// </summary>
        public async Task<int> HandleLaunchAgentAsync(Captain captain, Mission mission, Dock dock)
        {
            _Logging.Info(_Header + "launching " + captain.Runtime + " agent for captain " + captain.Id);
            IHostProcessExecutor executor = await ResolveLaunchExecutorAsync(captain, mission, dock).ConfigureAwait(false);
            Armada.Runtimes.Interfaces.IAgentRuntime runtime = executor.CreateRuntime(captain.Runtime);
            string launchKey = captain.Id + ":" + mission.Id;
            _PendingLaunches[launchKey] = new PendingLaunchInfo
            {
                CaptainId = captain.Id,
                MissionId = mission.Id
            };
            runtime.OnProcessStarted += processId => HandleProcessStarted(processId, launchKey);
            runtime.OnOutputReceived += HandleAgentOutput;
            runtime.OnOutputReceived += HandleAgentHeartbeat;
            runtime.OnProcessExited += HandleAgentProcessExited;

            Vessel? vessel = null;
            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                vessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
            }
            Vessel launchVessel = vessel ?? new Vessel
            {
                Name = "Unknown Vessel",
                DefaultBranch = dock.BranchName ?? mission.BranchName ?? "main"
            };
            string prompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                mission,
                launchVessel,
                captain,
                dock,
                _PromptTemplateService).ConfigureAwait(false);

            if (_Settings.MessageTemplates.EnableCommitMetadata)
            {
                Dictionary<string, string> templateContext = _TemplateService.BuildContext(mission, captain, null, null, dock);
                string commitInstructions = _TemplateService.RenderCommitInstructions(_Settings.MessageTemplates, templateContext);
                if (!String.IsNullOrEmpty(commitInstructions))
                    prompt += "\n\n" + commitInstructions;
            }

            string missionLogDir = Path.Combine(_Settings.LogDirectory, "missions");
            string logFilePath = Path.Combine(missionLogDir, mission.Id + ".log");
            string finalMessageDir = Path.Combine(_Settings.LogDirectory, "final-messages");
            Directory.CreateDirectory(finalMessageDir);
            string finalMessageFilePath = Path.Combine(finalMessageDir, mission.Id + ".txt");
            try
            {
                if (File.Exists(finalMessageFilePath))
                    File.Delete(finalMessageFilePath);
            }
            catch { }
            _MissionFinalMessageFiles[mission.Id] = finalMessageFilePath;
            string captainLogDir = Path.Combine(_Settings.LogDirectory, "captains");
            Directory.CreateDirectory(captainLogDir);
            string captainLogPointer = Path.Combine(captainLogDir, captain.Id + ".current");
            File.WriteAllText(captainLogPointer, logFilePath);

            int processId;
            try
            {
                processId = await runtime.StartAsync(
                    dock.WorktreePath ?? throw new InvalidOperationException("Dock worktree path is null"),
                    prompt,
                    logFilePath: logFilePath,
                    finalMessageFilePath: finalMessageFilePath,
                    model: captain.Model,
                    captain: captain,
                    isolateLaunch: _Settings.IsolateCaptainLaunch,
                    mcpPort: _Settings.McpPort).ConfigureAwait(false);
            }
            catch
            {
                _PendingLaunches.TryRemove(launchKey, out _);
                _MissionFinalMessageFiles.TryRemove(mission.Id, out _);
                throw;
            }

            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain[processId] = captain.Id;
                _ProcessToMission[processId] = mission.Id;
            }
            _PendingLaunches.TryRemove(launchKey, out _);

            _Logging.Info(_Header + "agent process " + processId + " started for captain " + captain.Id + " (log: " + logFilePath + ")");
            StartProcessLivenessHeartbeat(processId, captain.Id, mission.Id);

            await _EmitEventAsync("captain.launched", "Agent process started for captain " + captain.Name,
                "captain", captain.Id,
                captain.Id, mission.Id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);

            if (_WebSocketHub != null)
            {
                _WebSocketHub.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);
                _WebSocketHub.BroadcastMissionChange(mission.Id, mission.Status.ToString(), mission.Title);
            }

            return processId;
        }

        /// <summary>
        /// Start a periodic heartbeat loop for a tracked process so telemetry stays fresh
        /// even when the runtime is busy but not emitting output.
        /// </summary>
        private void StartProcessLivenessHeartbeat(int processId, string captainId, string missionId)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            if (!_ProcessHeartbeatLoops.TryAdd(processId, cts))
            {
                cts.Dispose();
                return;
            }

            TimeSpan interval = TimeSpan.FromSeconds(Math.Max(5, _Settings.HeartbeatIntervalSeconds));
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                        if (cts.Token.IsCancellationRequested) break;
                        if (!IsTrackedProcessAlive(processId)) break;

                        string? mappedCaptainId = null;
                        string? mappedMissionId = null;
                        lock (_ProcessToCaptain)
                        {
                            _ProcessToCaptain.TryGetValue(processId, out mappedCaptainId);
                            _ProcessToMission.TryGetValue(processId, out mappedMissionId);
                        }

                        if (!String.Equals(mappedCaptainId, captainId, StringComparison.Ordinal) ||
                            !String.Equals(mappedMissionId, missionId, StringComparison.Ordinal))
                        {
                            break;
                        }

                        // Refresh process-liveness telemetry ONLY. The output heartbeat
                        // (LastHeartbeatUtc, advanced by HandleAgentHeartbeat on real agent output)
                        // must NOT be touched here: stall detection measures time since last output,
                        // so refreshing the heartbeat for a merely-alive process would mask a stalled
                        // agent that is running but producing nothing.
                        try { await _Database.Captains.UpdateProcessAliveAsync(captainId).ConfigureAwait(false); }
                        catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    if (_ProcessHeartbeatLoops.TryRemove(processId, out CancellationTokenSource? removed))
                    {
                        removed.Dispose();
                    }
                }
            });
        }

        /// <summary>
        /// Stop the periodic heartbeat loop for a tracked process.
        /// </summary>
        private void StopProcessLivenessHeartbeat(int processId)
        {
            if (_ProcessHeartbeatLoops.TryRemove(processId, out CancellationTokenSource? cts))
            {
                try { cts.Cancel(); }
                catch { }
                cts.Dispose();
            }
        }

        /// <summary>
        /// Determine whether the tracked wrapper process is still alive.
        /// </summary>
        private static bool IsTrackedProcessAlive(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Register PID-to-captain/mission mapping as soon as a launched process exposes a PID.
        /// </summary>
        private void HandleProcessStarted(int processId, string launchKey)
        {
            if (!_PendingLaunches.TryGetValue(launchKey, out PendingLaunchInfo? launch) || launch == null)
                return;

            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain[processId] = launch.CaptainId;
                _ProcessToMission[processId] = launch.MissionId;
            }
        }

        /// <summary>
        /// Handle heartbeat from an agent process output line.
        /// </summary>
        public void HandleAgentHeartbeat(int processId, string line)
        {
            string? captainId = null;
            string? missionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.TryGetValue(processId, out captainId);
                _ProcessToMission.TryGetValue(processId, out missionId);
            }
            if (String.IsNullOrEmpty(captainId)) return;

            bool persistMissionHeartbeat = false;
            DateTime nowUtc = DateTime.UtcNow;
            if (!String.IsNullOrEmpty(missionId))
            {
                string capturedMissionId = missionId;
                _MissionHeartbeatWrites.AddOrUpdate(
                    capturedMissionId,
                    _ =>
                    {
                        persistMissionHeartbeat = true;
                        return nowUtc;
                    },
                    (_, previous) =>
                    {
                        if (nowUtc - previous >= _MissionHeartbeatPersistInterval)
                        {
                            persistMissionHeartbeat = true;
                            return nowUtc;
                        }

                        return previous;
                    });
            }

            string capturedCaptainId = captainId;
            _ = Task.Run(async () =>
            {
                try { await _Database.Captains.UpdateHeartbeatAsync(captainId).ConfigureAwait(false); }
                catch { }

                if (!persistMissionHeartbeat || String.IsNullOrEmpty(missionId)) return;

                try
                {
                    await _Database.Missions.UpdateHeartbeatAsync(missionId).ConfigureAwait(false);
                }
                catch
                {
                    _MissionHeartbeatWrites.TryRemove(missionId, out _);
                }
            });
        }

        /// <summary>
        /// Handle output from an agent process, parsing progress signals.
        /// </summary>
        public void HandleAgentOutput(int processId, string line)
        {
            // Accumulate stdout for pipeline handoff
            string? outputMissionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToMission.TryGetValue(processId, out outputMissionId);
            }
            if (!String.IsNullOrEmpty(outputMissionId))
            {
                System.Text.StringBuilder sb = _MissionOutput.GetOrAdd(outputMissionId, _ => new System.Text.StringBuilder());
                BoundedTextBuffer.AppendLine(sb, line, _MaxMissionOutputChars);
            }

            ProgressParser.ProgressSignal? signal = ProgressParser.TryParse(line);
            if (signal == null) return;

            string? captainId = null;
            string? missionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.TryGetValue(processId, out captainId);
                _ProcessToMission.TryGetValue(processId, out missionId);
            }

            if (String.IsNullOrEmpty(captainId)) return;

            // A papercut is a report about the work, not a report of progress. It takes its own path so
            // it never transitions a mission and never lands in the progress signal stream.
            if (String.Equals(signal.Type, PapercutParser.SignalType, StringComparison.OrdinalIgnoreCase))
            {
                HandlePapercutSignal(captainId, missionId, signal.Value);
                return;
            }

            _Logging.Info(_Header + "progress signal from captain " + captainId + ": [" + signal.Type + "] " + signal.Value);

            string capturedCaptainId = captainId;
            string? capturedMissionId = missionId;
            _ = Task.Run(async () =>
            {
                try
                {
                    string? targetMissionId = capturedMissionId;
                    if (String.IsNullOrEmpty(targetMissionId))
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(capturedCaptainId).ConfigureAwait(false);
                        targetMissionId = captain?.CurrentMissionId;
                    }

                    if (String.IsNullOrEmpty(targetMissionId)) return;

                    if (signal.Type == "status" && signal.MissionStatus.HasValue)
                    {
                        Mission? mission = await _Database.Missions.ReadAsync(targetMissionId).ConfigureAwait(false);
                        if (mission != null && IsValidTransition(mission.Status, signal.MissionStatus.Value))
                        {
                            mission.Status = signal.MissionStatus.Value;
                            mission.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                            _Logging.Info(_Header + "mission " + mission.Id + " transitioned to " + signal.MissionStatus.Value + " via agent signal");
                        }
                    }

                    Signal dbSignal = new Signal(SignalTypeEnum.Progress, "[" + signal.Type + "] " + signal.Value);
                    dbSignal.FromCaptainId = capturedCaptainId;
                    await _Database.Signals.CreateAsync(dbSignal).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error processing progress signal: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Store one captain-reported papercut as an event. Never throws into the output path: a
        /// malformed complaint must not disturb the mission that reported it.
        /// </summary>
        /// <param name="captainId">Reporting captain identifier.</param>
        /// <param name="missionId">Mission identifier, when the process is mapped to one.</param>
        /// <param name="value">Marker value that followed [ARMADA:PAPERCUT].</param>
        private void HandlePapercutSignal(string captainId, string? missionId, string value)
        {
            Papercut? parsed = PapercutParser.TryParseValue(value);
            if (parsed == null)
            {
                _Logging.Debug(_Header + "unparseable papercut from captain " + captainId);
                return;
            }

            string capturedCaptainId = captainId;
            string? capturedMissionId = missionId;

            _ = Task.Run(async () =>
            {
                try
                {
                    string? targetMissionId = capturedMissionId;
                    if (String.IsNullOrEmpty(targetMissionId))
                    {
                        Captain? owner = await _Database.Captains.ReadAsync(capturedCaptainId).ConfigureAwait(false);
                        targetMissionId = owner?.CurrentMissionId;
                    }

                    Mission? mission = null;
                    if (!String.IsNullOrEmpty(targetMissionId))
                    {
                        mission = await _Database.Missions.ReadAsync(targetMissionId!).ConfigureAwait(false);
                    }

                    // A judge reviews the work under review and already has a verdict channel for what
                    // it finds there. Letting it file papercuts as well splits review feedback across two
                    // surfaces, and the operator reads only one of them.
                    if (mission != null && PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge))
                    {
                        _Logging.Debug(_Header + "ignoring papercut from judge mission " + mission.Id);
                        return;
                    }

                    if (!String.IsNullOrEmpty(targetMissionId))
                    {
                        int stored = _MissionPapercutCounts.AddOrUpdate(targetMissionId!, 1, (_, existing) => existing + 1);
                        if (stored > _MaxPapercutsPerMission)
                        {
                            if (stored == _MaxPapercutsPerMission + 1)
                            {
                                _Logging.Info(_Header + "mission " + targetMissionId +
                                    " reached the papercut cap of " + _MaxPapercutsPerMission + "; later reports are dropped");
                            }

                            return;
                        }
                    }

                    Captain? captain = await _Database.Captains.ReadAsync(capturedCaptainId).ConfigureAwait(false);

                    parsed.CaptainId = capturedCaptainId;
                    parsed.MissionId = targetMissionId;
                    parsed.VesselId = mission?.VesselId;
                    parsed.VoyageId = mission?.VoyageId;
                    parsed.Persona = mission?.Persona;
                    parsed.Runtime = captain?.Runtime.ToString();
                    parsed.ReportedUtc = DateTime.UtcNow;

                    await _Database.Events.CreateAsync(PapercutService.ToEvent(parsed)).ConfigureAwait(false);

                    _Logging.Info(_Header + "papercut from captain " + capturedCaptainId + " [" +
                        parsed.Category + "/" + parsed.Severity + "] " + parsed.Title);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error storing papercut: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Handle agent process exit event.
        /// </summary>
        public void HandleAgentProcessExited(int processId, int? exitCode)
        {
            StopProcessLivenessHeartbeat(processId);

            string? captainId = null;
            string? missionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.TryGetValue(processId, out captainId);
                _ProcessToMission.TryGetValue(processId, out missionId);
            }

            // The process exit event can fire before HandleLaunchAgentAsync finishes
            // registering the PID-to-captain/mission mapping (race between process.Start()
            // returning and the mapping being written). Retry briefly to close this window.
            if (String.IsNullOrEmpty(captainId) || String.IsNullOrEmpty(missionId))
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    Thread.Sleep(100);
                    lock (_ProcessToCaptain)
                    {
                        _ProcessToCaptain.TryGetValue(processId, out captainId);
                        _ProcessToMission.TryGetValue(processId, out missionId);
                    }
                    if (!String.IsNullOrEmpty(captainId) && !String.IsNullOrEmpty(missionId))
                    {
                        _Logging.Info(_Header + "process " + processId + " exit handler resolved mapping after " + (attempt + 1) + " retries");
                        break;
                    }
                }
            }

            if (String.IsNullOrEmpty(captainId) || String.IsNullOrEmpty(missionId))
            {
                _Logging.Warn(_Header + "process " + processId + " exited (code " + (exitCode?.ToString() ?? "unknown") + ") but no captain/mission mapping found after retries -- exit may be lost");
                return;
            }

            _Logging.Info(_Header + "process " + processId + " exited (code " + (exitCode?.ToString() ?? "unknown") + ") for captain " + captainId + " mission " + missionId);

            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.Remove(processId);
                _ProcessToMission.Remove(processId);
            }
            _MissionHeartbeatWrites.TryRemove(missionId, out _);

            // Track this PID as handled BEFORE the async work begins.
            // The health check consults this set to avoid racing with the async exit handler
            // (e.g. triggering recovery for a process that exited cleanly but whose completion
            // handler hasn't finished yet).
            _HandledProcessExits[processId] = DateTime.UtcNow;

            string capturedCaptainId = captainId;
            string capturedMissionId = missionId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAgentProcessExitedAsync(processId, exitCode, capturedCaptainId, capturedMissionId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error handling process exit for captain " + capturedCaptainId + " mission " + capturedMissionId + ": " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Async handler for agent process exit, delegating to the admiral service.
        /// </summary>
        public async Task HandleAgentProcessExitedAsync(int processId, int? exitCode, string captainId, string missionId)
        {
            await _Admiral.HandleProcessExitAsync(processId, exitCode, captainId, missionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Stop the agent process for the given captain.
        /// </summary>
        public async Task HandleStopAgentAsync(Captain captain)
        {
            if (!captain.ProcessId.HasValue) return;
            _Logging.Info(_Header + "stopping agent process " + captain.ProcessId.Value + " for captain " + captain.Id);
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.Remove(captain.ProcessId.Value);
                _ProcessToMission.Remove(captain.ProcessId.Value);
            }
            IHostProcessExecutor stopExecutor = ResolveStopExecutor(captain.ProcessId.Value);
            Armada.Runtimes.Interfaces.IAgentRuntime runtime = stopExecutor.CreateRuntime(captain.Runtime);
            await runtime.StopAsync(captain.ProcessId.Value).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Resolve the process executor for a launch. Prefers a connected, eligible Harbor (making Harbor the
        /// default execution path); falls back to local in-process execution when no Harbor is connected, no
        /// Harbor is eligible, or routing fails.
        /// </summary>
        private async Task<IHostProcessExecutor> ResolveLaunchExecutorAsync(Captain captain, Mission mission, Dock dock)
        {
            if (_HarborConnections == null)
            {
                _Logging.Info(_Header + "Harbor delegation disabled (no connection manager wired); running locally");
                return _HostProcessExecutor;
            }

            if (!_HarborConnections.HasConnectedHarbor())
            {
                _Logging.Info(_Header + "no Harbor connected; running captain locally");
                return _HostProcessExecutor;
            }

            try
            {
                Vessel? vessel = !String.IsNullOrEmpty(mission.VesselId)
                    ? await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false)
                    : null;

                HarborRoutingRequest request = new HarborRoutingRequest
                {
                    ExistingHarborId = String.IsNullOrWhiteSpace(dock.HarborId) ? null : dock.HarborId,
                    PreferredHarborId = vessel?.PreferredHarborId,
                    RequestedRuntime = captain.Runtime.ToString(),
                    RequiredCapabilities = SplitCapabilities(vessel?.RequiredCapabilities)
                };

                _Logging.Info(_Header + "Harbor routing for mission " + mission.Id + ": tenant=" + (mission.TenantId ?? "(none)")
                    + " runtime=" + request.RequestedRuntime + " dockHarbor=" + (request.ExistingHarborId ?? "(none)")
                    + " connected=[" + String.Join(",", _HarborConnections.ConnectedHarborIds) + "]");

                HarborRoutingDecision decision = await _HarborConnections.SelectHarborAsync(mission.TenantId, request).ConfigureAwait(false);
                if (decision.Success && !String.IsNullOrWhiteSpace(decision.HarborId))
                {
                    await RecordHarborAffinityAsync(mission, dock, decision.HarborId!).ConfigureAwait(false);
                    _Logging.Info(_Header + "delegating captain launch to Harbor " + decision.HarborId + " (" + decision.Reason + ")");
                    return new Armada.Runtimes.RemoteHostProcessExecutor(_HarborConnections, decision.HarborId!);
                }

                _Logging.Info(_Header + "no eligible Harbor for this launch (" + decision.Reason + "); running locally");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "Harbor routing failed; running locally: " + ex.Message);
            }

            return _HostProcessExecutor;
        }

        /// <summary>
        /// Resolve the process executor for a stop. When the process id maps to a delegated Harbor job, stop
        /// on that Harbor; otherwise stop the local process.
        /// </summary>
        private IHostProcessExecutor ResolveStopExecutor(int processId)
        {
            if (_HarborConnections != null && _HarborConnections.TryGetHarborForProcess(processId, out string? harborId) && !String.IsNullOrWhiteSpace(harborId))
                return new Armada.Runtimes.RemoteHostProcessExecutor(_HarborConnections, harborId!);
            return _HostProcessExecutor;
        }

        /// <summary>
        /// Pin a mission and its dock to the Harbor that ran its launch so subsequent launches and stops route
        /// to the same host (dock affinity). Best-effort: a persistence failure does not abort the launch.
        /// </summary>
        private async Task RecordHarborAffinityAsync(Mission mission, Dock dock, string harborId)
        {
            try
            {
                if (!String.Equals(dock.HarborId, harborId, StringComparison.Ordinal))
                {
                    dock.HarborId = harborId;
                    await _Database.Docks.UpdateAsync(dock).ConfigureAwait(false);
                }

                if (!String.Equals(mission.AssignedHarborId, harborId, StringComparison.Ordinal))
                {
                    mission.AssignedHarborId = harborId;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not persist Harbor affinity for mission " + mission.Id + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Split a comma or whitespace separated capability list into distinct required capability names.
        /// </summary>
        private static List<string> SplitCapabilities(string? capabilities)
        {
            List<string> result = new List<string>();
            if (String.IsNullOrWhiteSpace(capabilities)) return result;
            string[] parts = capabilities.Split(new char[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0 && !result.Contains(trimmed)) result.Add(trimmed);
            }

            return result;
        }

        private static bool IsValidTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            // Delegated to the single authoritative table so this handler and the services agree.
            return MissionStateMachine.IsValidTransition(current, target);
        }

        private async Task<string?> ValidateMuxCaptainAsync(Captain captain, CancellationToken token)
        {
            MuxCaptainOptions? options;
            try
            {
                options = CaptainRuntimeOptions.GetMuxOptions(captain);
            }
            catch (Exception ex)
            {
                return "Mux runtime options are invalid JSON: " + ex.Message;
            }

            if (options == null)
            {
                return "Mux captains require runtime options containing at least a named endpoint.";
            }

            if (String.IsNullOrWhiteSpace(options.Endpoint))
            {
                return "Mux captains require a named endpoint.";
            }

            try
            {
                MuxProbeResult probe = await _MuxCli.ProbeAsync(captain, token).ConfigureAwait(false);
                if (probe.ContractVersion != 1)
                {
                    return "Mux returned structured output contract version " + probe.ContractVersion +
                        ", but Armada currently supports version 1.";
                }

                if (!probe.Success)
                {
                    string error = !String.IsNullOrWhiteSpace(probe.ErrorMessage)
                        ? probe.ErrorMessage
                        : "Mux probe failed with error code " + (String.IsNullOrWhiteSpace(probe.ErrorCode) ? "unknown" : probe.ErrorCode) + ".";
                    return "Mux endpoint '" + options.Endpoint + "' failed validation: " + error;
                }

                if (!probe.ToolsEnabled || probe.EffectiveToolCount <= 0)
                {
                    return "Mux endpoint '" + options.Endpoint + "' is not tool-enabled for Armada missions.";
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "Mux endpoint '" + options.Endpoint + "' failed validation: " + ex.Message;
            }
        }

        private static string? ExtractModelValidationError(string output)
        {
            if (String.IsNullOrWhiteSpace(output))
                return null;

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (String.IsNullOrEmpty(line))
                    continue;

                if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return line;
                }
            }

            return lines[lines.Length - 1].Trim();
        }

        private static async Task InitializeValidationWorkspaceAsync(AgentRuntimeEnum runtimeType, string workingDirectory, CancellationToken token)
        {
            if (runtimeType != AgentRuntimeEnum.Codex)
                return;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("init");
            startInfo.ArgumentList.Add("--quiet");

            using (Process process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                    throw new InvalidOperationException("Failed to initialize temporary validation repository.");

                await process.WaitForExitAsync(token).ConfigureAwait(false);
                if (process.ExitCode == 0)
                    return;

                string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                string details = !String.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                if (String.IsNullOrWhiteSpace(details))
                    details = "git init exited with code " + process.ExitCode + ".";

                throw new InvalidOperationException("Failed to initialize temporary validation repository: " + details);
            }
        }

        #endregion
    }
}
