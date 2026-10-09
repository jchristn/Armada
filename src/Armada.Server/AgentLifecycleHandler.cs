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
        #region Public-Members

        /// <summary>
        /// Time source. In-process intervals (the mission-heartbeat write throttle and how long a handled process
        /// exit is remembered) are measured on its monotonic clock, never its wall clock, so a wall-clock jump (the
        /// host sleeping and waking, an NTP step) cannot forget a handled exit early or suppress heartbeat writes.
        /// Defaults to <see cref="TimeProvider.System"/>; tests substitute a provider whose wall clock jumps.
        /// </summary>
        internal TimeProvider Time
        {
            get => _Time;
            set => _Time = value ?? throw new ArgumentNullException(nameof(Time));
        }

        #endregion

        #region Private-Members

        private string _Header = "[AgentLifecycle] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private AgentRuntimeFactory _RuntimeFactory;
        private IHostProcessExecutor _HostProcessExecutor;
        private ISessionTokenService? _SessionTokens = null;
        private CliPermissionService? _CliPermissions = null;
        private HarborConnectionManager? _HarborConnections;
        private Func<string, ModelEndpoint?>? _EndpointResolver;
        private MuxCliService _MuxCli;
        private IAdmiralService _Admiral;
        private IMessageTemplateService _TemplateService;
        private IPromptTemplateService? _PromptTemplateService;
        private ArmadaWebSocketHub? _WebSocketHub;
        private Func<string, string, string?, string?, string?, string?, string?, string?, Task> _EmitEventAsync;
        private readonly TimeSpan _ModelValidationTimeout = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _MissionHeartbeatPersistInterval = TimeSpan.FromSeconds(15);
        private readonly TimeSpan _HandledProcessExitRetention = TimeSpan.FromMinutes(5);
        private TimeProvider _Time = TimeProvider.System;
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
        /// Values are monotonic timestamps (<see cref="Time"/>) of the last persisted write.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, long> _MissionHeartbeatWrites = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();

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

        /// <summary>
        /// Where a mission launch runs, and, when it fell back to the Admiral host because the chosen Harbor does not
        /// have the mission's dock, which Harbor that was and why.
        /// </summary>
        private sealed class LaunchExecutorResolution
        {
            public IHostProcessExecutor Executor { get; set; }

            public string? DockMissingHarborId { get; set; } = null;

            public string? DockMissingReason { get; set; } = null;

            public LaunchExecutorResolution(IHostProcessExecutor executor)
            {
                Executor = executor;
            }
        }

        /// <summary>
        /// How long to wait for a Harbor to confirm that a mission's dock exists on its host before a launch.
        /// </summary>
        private const int _HarborDockProbeTimeoutMs = 15000;

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
        /// Entries are pruned after 5 minutes. Values are monotonic timestamps (<see cref="Time"/>).
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<int, long> _HandledProcessExits = new System.Collections.Concurrent.ConcurrentDictionary<int, long>();

        /// <summary>
        /// The last structured provider error each running process reported (see IAgentRuntime.OnProviderError).
        /// Consumed when the process exits to decide its typed exit outcome.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<int, RuntimeProviderError> _ProcessProviderErrors = new System.Collections.Concurrent.ConcurrentDictionary<int, RuntimeProviderError>();

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
        /// is eligible, launches fall back to in-process (local) execution, unless requireHarborForLaunch is on
        /// (then the launch is refused).
        /// </summary>
        /// <param name="manager">Harbor connection manager, or null to force local execution.</param>
        public void SetHarborConnections(HarborConnectionManager? manager)
        {
            _HarborConnections = manager;
        }

        /// <summary>
        /// Choose where a mission's dock is created, before it is provisioned. Routing picks a connected, eligible Harbor
        /// the same way a launch does (dock affinity, the vessel's preferred Harbor, capabilities, load, and
        /// requireHarborForLaunch), then asks it whether it can serve the vessel (a checkout named in its settings or
        /// discovered under its root folders, or a clone of the vessel's URL). A Harbor that cannot is skipped and the next
        /// one is asked. When none can: a mission whose branch lives on a Harbor waits for that Harbor; with
        /// requireHarborForLaunch on the mission waits; otherwise the dock is created on the Admiral host as before.
        /// A Harbor that predates Harbor-side docks is never asked; with only such Harbors the dock is created on the
        /// Admiral host and the launch-time dock check decides where the captain runs.
        /// </summary>
        /// <param name="mission">Mission about to be assigned.</param>
        /// <param name="captain">Captain chosen for it.</param>
        /// <param name="vessel">Its vessel.</param>
        /// <returns>The placement.</returns>
        public async Task<DockPlacement> ResolveDockPlacementAsync(Mission mission, Captain captain, Vessel vessel)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            DockPlacementRequest request = new DockPlacementRequest(captain, vessel)
            {
                Purpose = "mission " + mission.Id,
                TenantId = mission.TenantId,
                UserId = mission.UserId,
                BranchName = mission.BranchName,
                BranchHarborId = await FindBranchHarborAsync(mission, vessel).ConfigureAwait(false)
            };
            return await ResolveDockPlacementAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// Choose where a dock is created for any captain work on a vessel (a mission, a planning session, or a Model
        /// Context build), by the same rules as <see cref="ResolveDockPlacementAsync(Mission, Captain, Vessel)"/>.
        /// </summary>
        /// <param name="request">What the dock is for.</param>
        /// <returns>The placement.</returns>
        public async Task<DockPlacement> ResolveDockPlacementAsync(DockPlacementRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Captain captain = request.Captain;
            Vessel vessel = request.Vessel;

            HarborConnectionManager? harbors = _HarborConnections;
            string? branchHarborId = request.BranchHarborId;
            if (harbors == null || !harbors.HasConnectedHarbor())
            {
                if (branchHarborId != null)
                    return DockPlacement.WaitFor("its branch " + request.BranchName + " is in the repository on Harbor " + branchHarborId + ", which is not connected; the work runs when that Harbor reconnects");
                if (_Settings.RequireHarborForLaunch)
                    return DockPlacement.WaitFor("requireHarborForLaunch is on and no Harbor is connected");
                return DockPlacement.OnAdmiral("no Harbor is connected");
            }

            CaptainLaunchRouter router = new CaptainLaunchRouter(_Settings, _RuntimeFactory, harbors, _EndpointResolver, _Logging);
            CaptainLaunchContext context = new CaptainLaunchContext(captain, request.Purpose)
            {
                TenantId = request.TenantId,
                UserId = request.UserId,
                Vessel = vessel,
                PinnedHarborId = branchHarborId,
                AllowScratchWorkingDirectory = false
            };

            HarborDockClient client = new HarborDockClient(harbors);
            List<string> refusals = new List<string>();
            string? olderHarborId = null;
            CaptainLaunchDecision decision;
            while (true)
            {
                decision = await router.DecideAsync(context).ConfigureAwait(false);
                if (decision.HarborId == null) break;

                string harborId = decision.HarborId;
                context.ExcludedHarborIds.Add(harborId);
                string harbor = harbors.Describe(harborId);
                if (!harbors.HostsDocks(harborId))
                {
                    if (olderHarborId == null) olderHarborId = harborId;
                    refusals.Add("Harbor " + harbor + " predates Harbor-side docks (update it)");
                    continue;
                }

                Armada.Core.Harbor.HarborDockResult? resolved;
                try
                {
                    resolved = await client.ResolveAsync(harborId, vessel).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    refusals.Add("Harbor " + harbor + ": " + ex.Message);
                    continue;
                }

                if (resolved == null)
                {
                    refusals.Add("Harbor " + harbor + " did not say whether it can serve vessel " + vessel.Name);
                    continue;
                }

                if (resolved.Success)
                {
                    string source = resolved.Source == Armada.Core.Harbor.HarborRepositorySourceEnum.Clone
                        ? "its own clone " + resolved.RepositoryPath
                        : "checkout " + resolved.CheckoutPath;
                    _Logging.Info(_Header + request.Purpose + " dock goes on Harbor " + harbor + ", which serves vessel " + vessel.Name + " from " + source + " (" + decision.Reason + ")");
                    return DockPlacement.OnHarbor(harborId, "Harbor " + harbor + " serves vessel " + vessel.Name + " from " + source);
                }

                refusals.Add("Harbor " + harbor + " cannot serve vessel " + vessel.Name + ": " + (resolved.Message ?? "it did not say why"));
            }

            string why = refusals.Count > 0 ? String.Join("; ", refusals) : decision.Reason;
            if (branchHarborId != null)
                return DockPlacement.WaitFor("its branch " + request.BranchName + " is in the repository on Harbor " + harbors.Describe(branchHarborId) + ", which cannot take it now (" + why + ")");

            if (olderHarborId != null && refusals.Count == 1)
                return DockPlacement.OnAdmiral(why + "; the dock is created on the Admiral host");

            if (decision.HarborRequired)
                return DockPlacement.WaitFor("requireHarborForLaunch is on and no connected Harbor can serve vessel " + vessel.Name + " (" + why + ")");

            if (refusals.Count > 0)
                _Logging.Warn(_Header + request.Purpose + " dock goes on the Admiral host because no connected Harbor can serve vessel " + vessel.Name + ": " + why);
            return DockPlacement.OnAdmiral(why);
        }

        /// <summary>
        /// Provide the session token service used to mint mission-scoped MCP tokens for captain launches
        /// (<c>Mcp.MissionScopedTokens</c>). When null, captains launch without a token, as before.
        /// </summary>
        /// <param name="tokens">Session token service, or null.</param>
        public void SetSessionTokenService(ISessionTokenService? tokens)
        {
            _SessionTokens = tokens;
        }

        /// <summary>
        /// Provide the CLI permission service: pending permission prompts of a mission are cancelled when its captain
        /// process exits.
        /// </summary>
        /// <param name="service">Service, or null.</param>
        public void SetCliPermissionService(CliPermissionService? service)
        {
            _CliPermissions = service;
        }

        /// <summary>
        /// Mint a mission-scoped MCP session token for a captain launch, or null when tokens are disabled, no token
        /// service is configured, or the mission has no owner. The token lives at most as long as a mission may run
        /// (plus slack) and stops working as soon as the mission leaves Assigned / InProgress or changes captain.
        /// </summary>
        /// <param name="captain">Captain being launched.</param>
        /// <param name="mission">Mission the captain runs.</param>
        /// <returns>Token, or null.</returns>
        public string? MintMissionToken(Captain captain, Mission mission)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (_SessionTokens == null || !_Settings.Mcp.MissionScopedTokens) return null;
            if (String.IsNullOrEmpty(mission.TenantId) || String.IsNullOrEmpty(mission.UserId)) return null;
            int runtimeMinutes = _Settings.MaxMissionRuntimeMinutes > 0 ? _Settings.MaxMissionRuntimeMinutes : 24 * 60;
            TimeSpan lifetime = TimeSpan.FromMinutes(runtimeMinutes + 30);
            return _SessionTokens.CreateMissionScopedToken(mission.TenantId!, mission.UserId!, mission.Id, captain.Id, lifetime).Token;
        }

        /// <summary>
        /// Provide the model-endpoint resolver so an API-endpoint captain delegated to a Harbor can have its
        /// inference endpoint shipped in the launch (the Harbor has no database to resolve it).
        /// </summary>
        /// <param name="resolver">Resolver mapping a model-endpoint id to a ModelEndpoint, or null.</param>
        public void SetEndpointResolver(Func<string, ModelEndpoint?>? resolver)
        {
            _EndpointResolver = resolver;
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
                    _Logging.Warn(_Header + "error reading final message artifact for mission " + missionId + ": " + ex.ToString());
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
            // Prune entries older than 5 minutes, on the monotonic clock: on the wall clock a sleep/wake jump forgot a
            // just-handled exit, and the health check could then race the exit handler.
            long now = _Time.GetTimestamp();
            foreach (System.Collections.Generic.KeyValuePair<int, long> kvp in _HandledProcessExits)
            {
                if (_Time.GetElapsedTime(kvp.Value, now) > _HandledProcessExitRetention)
                    _HandledProcessExits.TryRemove(kvp.Key, out _);
            }

            return _HandledProcessExits.ContainsKey(processId);
        }

        /// <summary>
        /// Check whether a process was launched by this handler and is still awaiting its runtime's exit callback.
        /// Such a process is owned by that callback; the health check treats it as alive instead of probing the
        /// operating system, which cannot see an in-process ApiEndpoint loop (synthetic process id) or a Harbor-hosted
        /// process and would misreport either as vanished.
        /// </summary>
        /// <param name="processId">Process ID (OS, synthetic, or Harbor-mapped) returned by the runtime at launch.</param>
        /// <returns>True while the process is mapped to a captain and its exit has not been received.</returns>
        public bool IsProcessTracked(int processId)
        {
            lock (_ProcessToCaptain)
            {
                return _ProcessToCaptain.ContainsKey(processId);
            }
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
        public async Task<string?> ValidateCaptainModelAsync(Captain captain, CancellationToken token = default)
        {
            CaptainModelValidationFailure? failure = await ValidateCaptainModelDetailedAsync(captain, token).ConfigureAwait(false);
            return failure?.Message;
        }

        /// <summary>
        /// Validate that the captain's configured model can be launched by its runtime, returning a typed failure
        /// (machine-readable reason plus message) or null when valid.
        /// </summary>
        /// <param name="captain">Captain to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Null if valid, otherwise the failure.</returns>
        public Task<CaptainModelValidationFailure?> ValidateCaptainModelDetailedAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint)
                return ValidateApiEndpointCaptainAsync(captain, token);
            if (captain.Runtime == AgentRuntimeEnum.Mux)
                return ValidateMuxCaptainAsync(captain, token);
            return ValidateModelDetailedAsync(captain.Runtime, captain.Model, token);
        }

        /// <summary>
        /// Validate an API-endpoint captain: it must reference an existing, enabled, Inference-kind model
        /// endpoint. Returns null when valid, otherwise a human-readable error.
        /// </summary>
        /// <param name="captain">Captain to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Null when valid, otherwise an error message.</returns>
        private async Task<CaptainModelValidationFailure?> ValidateApiEndpointCaptainAsync(Captain captain, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captain.ModelEndpointId))
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointRequired, "An API-endpoint captain must reference an inference model endpoint. Choose one under Configuration > Endpoints.");

            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId, token).ConfigureAwait(false);
            if (endpoint == null)
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointNotFound, "The referenced model endpoint (" + captain.ModelEndpointId + ") does not exist.");
            if (endpoint.Kind != ModelEndpointKindEnum.Inference)
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointNotInference, "The referenced model endpoint '" + endpoint.Name + "' is an " + endpoint.Kind + " endpoint; an API-endpoint captain requires an Inference endpoint.");
            if (!endpoint.Enabled)
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointDisabled, "The referenced model endpoint '" + endpoint.Name + "' is disabled. Enable it or choose another.");

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
            CaptainModelValidationFailure? failure = await ValidateModelDetailedAsync(runtimeType, model, token).ConfigureAwait(false);
            return failure?.Message;
        }

        /// <summary>
        /// Validate that the given runtime can start with the requested model, returning a typed failure or null.
        /// </summary>
        /// <param name="runtimeType">Runtime to validate.</param>
        /// <param name="model">Model to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Null if valid, otherwise the failure.</returns>
        public async Task<CaptainModelValidationFailure?> ValidateModelDetailedAsync(AgentRuntimeEnum runtimeType, string? model, CancellationToken token = default)
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
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.RuntimeUnavailable, "Unable to create runtime " + runtimeType + " for model validation: " + ex.Message);
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
                        return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.ModelRejected, "Model '" + model + "' failed validation for runtime " + runtimeType + ": " + details);
                    }

                    return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.ModelRejected, "Model '" + model + "' failed validation for runtime " + runtimeType + " with exit code " + exitCode.Value + ".");
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

                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.TimedOut, timeoutMessage);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.ModelRejected, "Model '" + model + "' failed validation for runtime " + runtimeType + ": " + ex.Message);
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
            LaunchExecutorResolution resolution = await ResolveLaunchExecutorAsync(captain, mission, dock).ConfigureAwait(false);
            Armada.Runtimes.Interfaces.IAgentRuntime runtime = resolution.Executor.CreateRuntime(captain.Runtime);
            string launchKey = captain.Id + ":" + mission.Id;
            _PendingLaunches[launchKey] = new PendingLaunchInfo
            {
                CaptainId = captain.Id,
                MissionId = mission.Id
            };
            runtime.OnProcessStarted += processId => HandleProcessStarted(processId, launchKey);
            runtime.OnOutputReceived += HandleAgentOutput;
            runtime.OnOutputReceived += HandleAgentHeartbeat;
            runtime.OnStdoutReceived += HandleAgentStdout;
            runtime.OnProviderError += HandleAgentProviderError;
            runtime.OnProcessExited += HandleAgentProcessExited;
            string exitedMissionId = mission.Id;
            runtime.OnProcessExited += (exitedPid, exitCode) =>
            {
                CliPermissionService? permissions = _CliPermissions;
                if (permissions == null) return;
                _ = Task.Run(async () =>
                {
                    try { await permissions.CancelPendingAsync(null, exitedMissionId).ConfigureAwait(false); }
                    catch (Exception ex) { _Logging.Debug(_Header + "could not cancel CLI permission requests of " + exitedMissionId + ": " + ex.Message); }
                });
            };

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

            // Commit message guidance always applies; the Armada trailers are added only when commit metadata is on.
            Dictionary<string, string> templateContext = _TemplateService.BuildContext(mission, captain, null, null, dock);
            string commitInstructions = await _TemplateService.RenderCommitInstructionsAsync(_Settings.MessageTemplates, templateContext).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(commitInstructions))
                prompt += "\n\n" + commitInstructions;

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
                // O-20 / O-04: a mission-scoped MCP token binds the captain's Armada MCP connection to the mission's
                // owner, so the captain no longer relies on the unauthenticated loopback identity.
                string? missionToken = MintMissionToken(captain, mission);
                // CLI tool permissions: vessel auto-approve override, then the captain's policy (or legacy autoApprove),
                // then Permissions.MissionDefaultPolicy. Bypass maps to the runtime's bypass flag; ApproveInArmada routes
                // Claude Code's permission prompts to Armada (it needs the mission token; Harbor launches fall back).
                CliPermissionResolution permission = CliPermissionPolicyResolver.ResolveForMission(_Settings, captain, vessel, missionToken != null, runtime is RemoteAgentRuntime);
                Captain launchCaptain = CaptainRuntimeOptions.WithEffectiveAutoApprove(captain, permission.Effective == CliPermissionPolicyEnum.Bypass);
                CliPermissionService? permissions = _CliPermissions;
                Func<string, string, CancellationToken, Task<CliPermissionPromptOutcome>>? inProcessPrompt = null;
                if (permissions != null)
                {
                    CliPermissionPromptContext promptContext = new CliPermissionPromptContext
                    {
                        TenantId = mission.TenantId,
                        UserId = mission.UserId,
                        CaptainId = captain.Id,
                        Runtime = captain.Runtime,
                        MissionId = mission.Id,
                        VoyageId = mission.VoyageId,
                        VesselId = mission.VesselId,
                        WorkingDirectory = dock.WorktreePath
                    };
                    inProcessPrompt = (tool, input, cancel) => permissions.PromptAsync(promptContext, tool, input, cancel);
                }

                CliPermissionLaunch.Apply(runtime, permission.Effective, _Settings.Permissions.PromptTimeoutSeconds, inProcessPrompt);

                WriteMissionLogNote(logFilePath, permission.Note);
                _Logging.Info(_Header + "mission " + mission.Id + " " + permission.Note);
                bool isolateLaunch = _Settings.IsolateCaptainLaunch;
                Dictionary<string, string>? environment = null;
                if (missionToken != null)
                {
                    environment = new Dictionary<string, string>
                    {
                        ["ARMADA_MCP_URL"] = Armada.Core.Services.ArmadaMcpConfigBuilder.GetMcpUrl(_Settings.McpPort, Armada.Core.Services.ArmadaMcpConfigBuilder.ClientHostFor(_Settings.Rest.Hostname)),
                        [Armada.Core.Services.CaptainThreadMcpPlanner.TokenEnvironmentVariable] = missionToken
                    };
                }

                if (runtime is BaseAgentRuntime hostedRuntime)
                {
                    // Isolated launches write an Armada MCP URL; it must use the host the MCP listener is bound with.
                    hostedRuntime.McpHost = Armada.Core.Services.ArmadaMcpConfigBuilder.ClientHostFor(_Settings.Rest.Hostname);
                    if (missionToken != null)
                    {
                        // With IsolateCaptainLaunch the existing isolation plan carries the token; otherwise the token is
                        // bound per invocation (never by writing client files into the repository worktree).
                        hostedRuntime.McpSessionToken = missionToken;
                        hostedRuntime.McpTokenWithFullIsolation = _Settings.IsolateCaptainLaunch;
                        hostedRuntime.McpAllowWorkingDirectoryFiles = false;
                        isolateLaunch = true;
                    }
                }
                else if (runtime is RemoteAgentRuntime remoteRuntime)
                {
                    // Tell the Harbor what it runs, for its job list and logs.
                    remoteRuntime.JobKind = Armada.Core.Harbor.HarborJobKindEnum.Mission;
                    remoteRuntime.MissionId = mission.Id;
                    if (missionToken != null)
                    {
                        // The Harbor binds the token against the MCP URL it was given in the handshake.
                        remoteRuntime.McpSessionToken = missionToken;
                        environment = null;
                    }
                }

                processId = await runtime.StartAsync(
                    dock.WorktreePath ?? throw new InvalidOperationException("Dock worktree path is null"),
                    prompt,
                    environment,
                    logFilePath: logFilePath,
                    finalMessageFilePath: finalMessageFilePath,
                    model: captain.Model,
                    captain: launchCaptain,
                    isolateLaunch: isolateLaunch,
                    mcpPort: _Settings.McpPort).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _PendingLaunches.TryRemove(launchKey, out _);
                _MissionFinalMessageFiles.TryRemove(mission.Id, out _);

                // The launch fell back to the Admiral host only because the Harbor does not have the dock, and the
                // Admiral host cannot run the runtime either: the mission cannot run anywhere until a setting changes.
                if (ex is AgentRuntimeNotInstalledException notInstalled && resolution.DockMissingReason != null && resolution.DockMissingHarborId != null)
                {
                    string message = resolution.DockMissingReason
                        + " The launch fell back to the Admiral host, where the " + notInstalled.Runtime + " CLI ('" + notInstalled.Executable + "') is not installed."
                        + " Install that CLI on the Admiral host, or run the Admiral on the Harbor's machine.";
                    _Logging.Warn(_Header + message);
                    throw new HarborDockNotFoundException(resolution.DockMissingHarborId, dock.WorktreePath ?? String.Empty, message, notInstalled);
                }

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
                _WebSocketHub.BroadcastCaptainChange(captain);
                _WebSocketHub.BroadcastMissionChange(mission, mission.Status.ToString());
            }

            return processId;
        }

        /// <summary>
        /// Append an Armada note (for example the CLI tool permission policy and where to change it) to a mission log
        /// before the captain starts, so a refused tool in the log has its explanation above it.
        /// </summary>
        private void WriteMissionLogNote(string logFilePath, string note)
        {
            if (String.IsNullOrEmpty(logFilePath) || String.IsNullOrEmpty(note)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                File.AppendAllText(logFilePath, "[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "] " + CliPermissionPolicyResolver.MissionLogNoteLabel + note + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not write the mission log note: " + ex.Message);
            }
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

            bool persistMissionHeartbeat = !String.IsNullOrEmpty(missionId) && ShouldPersistMissionHeartbeat(missionId);

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

            _Logging.Debug(_Header + "progress signal from captain " + captainId + ": [" + signal.Type + "] " + signal.Value);

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

                    // Status lines are recorded here as informational progress only. A mission status change is
                    // applied by HandleAgentStdout, from the agent's own stdout, and only for the agent-reportable
                    // InProgress/Testing phase toggle (MissionStateMachine.IsAgentReportableTransition).
                    Signal dbSignal = new Signal(SignalTypeEnum.Progress, "[" + signal.Type + "] " + signal.Value);
                    dbSignal.FromCaptainId = capturedCaptainId;
                    await _Database.Signals.CreateAsync(dbSignal).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error processing progress signal: " + ex.ToString());
                }
            });
        }

        /// <summary>
        /// Handle one stdout line from an agent process: apply an <c>[ARMADA:STATUS]</c> protocol line as a mission
        /// status change. Only stdout counts, because agent CLIs print tool and command output (for example a file the
        /// agent printed with cat) on stderr in text mode, and only the informational InProgress/Testing phase toggle
        /// is honored (see <see cref="MissionStateMachine.IsAgentReportableTransition"/>). Review, completion, failure,
        /// and cancellation are never taken from output; Armada decides them from the process exit and the completion
        /// pipeline. The combined-output handler records the line as an informational progress signal.
        /// </summary>
        /// <param name="processId">Process ID.</param>
        /// <param name="line">One stdout record.</param>
        public void HandleAgentStdout(int processId, string line)
        {
            ProgressParser.ProgressSignal? signal = ProgressParser.TryParse(line);
            if (signal == null || signal.Type != "status" || !signal.MissionStatus.HasValue) return;

            string? captainId = null;
            string? missionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.TryGetValue(processId, out captainId);
                _ProcessToMission.TryGetValue(processId, out missionId);
            }

            if (String.IsNullOrEmpty(captainId) || String.IsNullOrEmpty(missionId)) return;

            MissionStatusEnum requested = signal.MissionStatus.Value;
            string capturedMissionId = missionId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await ApplyAgentReportedStatusAsync(capturedMissionId, requested).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error applying agent status signal: " + ex.ToString());
                }
            });
        }

        /// <summary>
        /// Apply an agent-reported mission status when it is an agent-reportable transition from the mission's
        /// current status. Returns whether the status changed.
        /// </summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="requested">Status named by the agent.</param>
        /// <returns>True when the mission status changed.</returns>
        public async Task<bool> ApplyAgentReportedStatusAsync(string missionId, MissionStatusEnum requested)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission? mission = await _Database.Missions.ReadAsync(missionId).ConfigureAwait(false);
            if (mission == null) return false;

            if (!MissionStateMachine.IsAgentReportableTransition(mission.Status, requested))
            {
                _Logging.Debug(_Header + "ignoring agent status " + requested + " for mission " + mission.Id + " in " + mission.Status);
                return false;
            }

            mission.Status = requested;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
            _Logging.Info(_Header + "mission " + mission.Id + " transitioned to " + requested + " via agent signal");
            return true;
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

                    ArmadaEvent papercutEvent = PapercutService.ToEvent(parsed);
                    papercutEvent.TenantId = mission?.TenantId ?? captain?.TenantId;
                    papercutEvent.UserId = mission?.UserId ?? captain?.UserId;
                    await _Database.Events.CreateAsync(papercutEvent).ConfigureAwait(false);

                    _Logging.Debug(_Header + "papercut from captain " + capturedCaptainId + " [" +
                        parsed.Category + "/" + parsed.Severity + "] " + parsed.Title);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error storing papercut: " + ex.ToString());
                }
            });
        }

        /// <summary>
        /// Record a structured provider error reported by a running agent process. The last error wins; it is
        /// combined with the exit code when the process exits.
        /// </summary>
        /// <param name="processId">Process ID.</param>
        /// <param name="error">Structured provider error.</param>
        public void HandleAgentProviderError(int processId, RuntimeProviderError error)
        {
            if (error == null) return;
            _ProcessProviderErrors[processId] = error;
            _Logging.Debug(_Header + "process " + processId + " reported " + error.ToString());
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
                        _Logging.Debug(_Header + "process " + processId + " exit handler resolved mapping after " + (attempt + 1) + " retries");
                        break;
                    }
                }
            }

            if (String.IsNullOrEmpty(captainId) || String.IsNullOrEmpty(missionId))
            {
                _Logging.Warn(_Header + "process " + processId + " exited (code " + (exitCode?.ToString() ?? "unknown") + ") but no captain/mission mapping found after retries -- exit may be lost");
                _ProcessProviderErrors.TryRemove(processId, out _);
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
            _HandledProcessExits[processId] = _Time.GetTimestamp();

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
                    _Logging.Warn(_Header + "error handling process exit for captain " + capturedCaptainId + " mission " + capturedMissionId + ": " + ex.ToString());
                }
            });
        }

        /// <summary>
        /// Async handler for agent process exit, delegating to the admiral service.
        /// </summary>
        public async Task HandleAgentProcessExitedAsync(int processId, int? exitCode, string captainId, string missionId)
        {
            _ProcessProviderErrors.TryRemove(processId, out RuntimeProviderError? providerError);
            RuntimeExitInfo exitInfo = RuntimeFailureClassifier.Decide(exitCode, providerError);
            await _Admiral.HandleProcessExitAsync(processId, exitInfo, captainId, missionId).ConfigureAwait(false);
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
        /// Whether a mission heartbeat should be written now: the first one for a mission, then at most one per
        /// persist interval. The interval is measured on the monotonic clock: on the wall clock a backward step
        /// suppressed writes until the clock caught up, and the mission looked stalled.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <returns>True when the heartbeat should be persisted.</returns>
        internal bool ShouldPersistMissionHeartbeat(string missionId)
        {
            bool persist = false;
            long now = _Time.GetTimestamp();
            _MissionHeartbeatWrites.AddOrUpdate(
                missionId,
                _ =>
                {
                    persist = true;
                    return now;
                },
                (_, previous) =>
                {
                    if (_Time.GetElapsedTime(previous, now) >= _MissionHeartbeatPersistInterval)
                    {
                        persist = true;
                        return now;
                    }

                    persist = false;
                    return previous;
                });
            return persist;
        }

        /// <summary>
        /// Resolve the process executor for a launch. Prefers a connected, eligible Harbor (making Harbor the
        /// default execution path); falls back to local in-process execution when no Harbor is connected, no
        /// Harbor is eligible, or routing fails. A dock pinned to a Harbor relaunches on that Harbor when it is
        /// available; otherwise it falls back to the Admiral host (the worktree is created on the Admiral) with a
        /// warning. When requireHarborForLaunch is on, the launch is refused with
        /// <see cref="HarborLaunchUnavailableException"/> instead of running on the Admiral host.
        /// Docks are provisioned on the Admiral host, so before a launch goes to a Harbor the Harbor is asked whether
        /// the dock's worktree exists there. A Harbor that does not have it never receives the launch: with the policy
        /// off the launch runs on the Admiral host (where the dock is); with it on the launch is refused with
        /// <see cref="HarborDockNotFoundException"/>, which fails the mission, because retrying cannot help.
        /// </summary>
        private async Task<LaunchExecutorResolution> ResolveLaunchExecutorAsync(Captain captain, Mission mission, Dock dock)
        {
            string? pinnedHarborId = String.IsNullOrWhiteSpace(dock.HarborId) ? null : dock.HarborId;

            Vessel? vessel = null;
            if (_HarborConnections != null && _HarborConnections.HasConnectedHarbor() && !String.IsNullOrEmpty(mission.VesselId))
            {
                try { vessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "could not read vessel " + mission.VesselId + " for Harbor routing: " + ex.Message); }
            }

            // The same routing every captain launch uses (missions and interactive launches alike).
            CaptainLaunchContext context = new CaptainLaunchContext(captain, "mission " + mission.Id)
            {
                Kind = Armada.Core.Harbor.HarborJobKindEnum.Mission,
                MissionId = mission.Id,
                TenantId = mission.TenantId,
                UserId = mission.UserId,
                Vessel = vessel,
                PinnedHarborId = pinnedHarborId,
                AllowScratchWorkingDirectory = false
            };

            CaptainLaunchRouter router = new CaptainLaunchRouter(_Settings, _RuntimeFactory, _HarborConnections, _EndpointResolver, _Logging);
            CaptainLaunchDecision decision = await router.DecideAsync(context).ConfigureAwait(false);
            bool harborDock = DockHostResolver.IsHarborDock(dock);
            if (harborDock && (decision.HarborId == null || _HarborConnections == null))
            {
                // The dock exists only on its Harbor: the captain can run nowhere else. The mission returns to Pending.
                string waiting = "Mission " + mission.Id + " cannot launch now: its dock " + dock.WorktreePath + " is on Harbor " + dock.HarborId
                    + ", which is not available (" + decision.Reason + "); it will not run on the Admiral host.";
                _Logging.Warn(_Header + waiting);
                throw new HarborLaunchUnavailableException(dock.HarborId, decision.HarborRequired, waiting);
            }

            if (decision.HarborId != null && _HarborConnections != null)
            {
                string worktreePath = dock.WorktreePath ?? String.Empty;
                Armada.Core.Harbor.HarborGitResult? probe = await ProbeDockOnHarborAsync(decision.HarborId, worktreePath).ConfigureAwait(false);
                if (probe == null)
                {
                    // The Harbor did not answer: a transient condition, handled like an unavailable Harbor.
                    string unconfirmed = "Harbor " + decision.HarborId + " did not confirm that dock " + worktreePath + " exists on its host";
                    if (decision.HarborRequired) throw HarborUnavailable(mission, pinnedHarborId, true, unconfirmed);
                    return new LaunchExecutorResolution(LocalFallback(mission, pinnedHarborId, unconfirmed));
                }

                if (probe.ExitCode != 0 && harborDock)
                {
                    string gone = "Mission " + mission.Id + " cannot run: its dock " + worktreePath + " no longer exists on Harbor "
                        + _HarborConnections.Describe(decision.HarborId) + ", where it was created (" + (probe.StandardError ?? String.Empty).Trim() + ").";
                    _Logging.Warn(_Header + gone);
                    throw new HarborDockNotFoundException(decision.HarborId, worktreePath, gone);
                }

                if (probe.ExitCode != 0)
                {
                    string missing = BuildDockMissingReason(mission, decision.HarborId, worktreePath, probe);
                    if (decision.HarborRequired)
                    {
                        string refused = missing + " requireHarborForLaunch is on, so the mission will not run on the Admiral host either."
                            + " Run the Admiral on the Harbor's machine, or turn requireHarborForLaunch off and install the "
                            + captain.Runtime + " CLI on the Admiral host so missions run there.";
                        _Logging.Warn(_Header + refused);
                        throw new HarborDockNotFoundException(decision.HarborId, worktreePath, refused);
                    }

                    _Logging.Warn(_Header + missing + " Running the captain on the Admiral host, where the dock is.");
                    LaunchExecutorResolution fallback = new LaunchExecutorResolution(_HostProcessExecutor);
                    fallback.DockMissingHarborId = decision.HarborId;
                    fallback.DockMissingReason = missing;
                    return fallback;
                }

                await RecordHarborAffinityAsync(mission, dock, decision.HarborId).ConfigureAwait(false);
                _Logging.Info(_Header + "delegating captain launch to Harbor " + decision.HarborId + " (" + decision.Reason + ")");
                return new LaunchExecutorResolution(new Armada.Runtimes.RemoteHostProcessExecutor(_HarborConnections, decision.HarborId, _EndpointResolver));
            }

            if (decision.HarborRequired) throw HarborUnavailable(mission, pinnedHarborId, true, decision.Reason);
            return new LaunchExecutorResolution(LocalFallback(mission, pinnedHarborId, decision.Reason));
        }

        /// <summary>
        /// The Harbor whose repository holds a mission's existing branch (a later pipeline stage, a retry, or rework
        /// continues on the branch an earlier dock made). The most recent dock of the vessel on that branch decides: when it
        /// was created on a Harbor, the branch lives in that Harbor's repository and the mission must run there.
        /// </summary>
        private async Task<string?> FindBranchHarborAsync(Mission mission, Vessel vessel)
        {
            if (String.IsNullOrEmpty(mission.BranchName)) return null;
            List<Dock> docks = await _Database.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
            Dock? latest = null;
            foreach (Dock candidate in docks)
            {
                if (!String.Equals(candidate.BranchName, mission.BranchName, StringComparison.Ordinal)) continue;
                if (latest == null || candidate.CreatedUtc >= latest.CreatedUtc) latest = candidate;
            }

            return DockHostResolver.IsHarborDock(latest) ? latest!.HarborId : null;
        }

        /// <summary>
        /// Ask a Harbor whether a dock's worktree exists on its host (<c>git -C path rev-parse --git-dir</c>). Returns the
        /// result (exit 0 when the worktree is there), or null when the Harbor did not answer or its link closed.
        /// </summary>
        private async Task<Armada.Core.Harbor.HarborGitResult?> ProbeDockOnHarborAsync(string harborId, string worktreePath)
        {
            if (_HarborConnections == null) return null;
            if (String.IsNullOrWhiteSpace(worktreePath))
                return new Armada.Core.Harbor.HarborGitResult { ExitCode = 1, StandardError = "the dock has no worktree path" };

            Armada.Core.Harbor.HarborGitRequest request = new Armada.Core.Harbor.HarborGitRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Executable = "git",
                WorkingDirectory = String.Empty,
                Arguments = new List<string> { "-C", worktreePath, "rev-parse", "--git-dir" }
            };

            try
            {
                return await _HarborConnections.SendGitAsync(harborId, request, _HarborDockProbeTimeoutMs).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _Logging.Warn(_Header + "could not ask Harbor " + harborId + " for dock " + worktreePath + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Describe a dock that does not exist on the Harbor chosen for its mission, and what makes a Harbor able to run it.
        /// </summary>
        private string BuildDockMissingReason(Mission mission, string harborId, string worktreePath, Armada.Core.Harbor.HarborGitResult probe)
        {
            string detail = (probe.StandardError ?? String.Empty).Trim();
            return "Mission " + mission.Id + " cannot run on Harbor " + harborId + ": its dock " + worktreePath
                + " is a git worktree on the Admiral host and does not exist on that Harbor's host"
                + (detail.Length > 0 ? " (" + detail + ")" : String.Empty) + "."
                + " Docks are created on the Admiral host, so a Harbor can run missions only when it sees the Admiral's docks directory ("
                + _Settings.DocksDirectory + ") at the same path, for example a Harbor on the Admiral's own machine.";
        }

        /// <summary>
        /// Run a launch on the Admiral host. A dock pinned to a Harbor that is unavailable is logged as a warning
        /// naming that Harbor (stall-detection recovery of a mission whose Harbor went away).
        /// </summary>
        private IHostProcessExecutor LocalFallback(Mission mission, string? pinnedHarborId, string reason)
        {
            if (pinnedHarborId != null)
                _Logging.Warn(_Header + "mission " + mission.Id + " dock is pinned to Harbor " + pinnedHarborId + ", which is unavailable (" + reason + "); running the captain on the Admiral host");
            else
                _Logging.Debug(_Header + reason + "; running captain locally");
            return _HostProcessExecutor;
        }

        /// <summary>
        /// Build the typed refusal for a launch that must run on a Harbor and has none to run on.
        /// </summary>
        private HarborLaunchUnavailableException HarborUnavailable(Mission mission, string? pinnedHarborId, bool requiredByPolicy, string reason)
        {
            string message = "Mission " + mission.Id + " cannot launch: requireHarborForLaunch is on and no eligible Harbor owned by the mission's user is available"
                + (pinnedHarborId != null ? " (its dock is pinned to Harbor " + pinnedHarborId + ")" : String.Empty)
                + " (" + reason + "); it will not run on the Admiral host.";
            _Logging.Warn(_Header + message);
            return new HarborLaunchUnavailableException(pinnedHarborId, requiredByPolicy, message);
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
                _Logging.Warn(_Header + "could not persist Harbor affinity for mission " + mission.Id + ": " + ex.ToString());
            }
        }

        private async Task<CaptainModelValidationFailure?> ValidateMuxCaptainAsync(Captain captain, CancellationToken token)
        {
            MuxCaptainOptions? options;
            try
            {
                options = CaptainRuntimeOptions.GetMuxOptions(captain);
            }
            catch (Exception ex)
            {
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.InvalidRuntimeOptions, "Mux runtime options are invalid JSON: " + ex.Message);
            }

            if (options == null)
            {
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.NamedEndpointRequired, "Mux captains require runtime options containing at least a named endpoint.");
            }

            if (String.IsNullOrWhiteSpace(options.Endpoint))
            {
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.NamedEndpointRequired, "Mux captains require a named endpoint.");
            }

            try
            {
                MuxProbeResult probe = await _MuxCli.ProbeAsync(captain, token).ConfigureAwait(false);
                if (probe.ContractVersion != 1)
                {
                    return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.UnsupportedContractVersion, "Mux returned structured output contract version " + probe.ContractVersion +
                        ", but Armada currently supports version 1.");
                }

                if (!probe.Success)
                {
                    string error = !String.IsNullOrWhiteSpace(probe.ErrorMessage)
                        ? probe.ErrorMessage
                        : "Mux probe failed with error code " + (String.IsNullOrWhiteSpace(probe.ErrorCode) ? "unknown" : probe.ErrorCode) + ".";
                    return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointProbeFailed, "Mux endpoint '" + options.Endpoint + "' failed validation: " + error);
                }

                if (!probe.ToolsEnabled || probe.EffectiveToolCount <= 0)
                {
                    return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointNotToolEnabled, "Mux endpoint '" + options.Endpoint + "' is not tool-enabled for Armada missions.");
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointProbeFailed, "Mux endpoint '" + options.Endpoint + "' failed validation: " + ex.Message);
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
            Armada.Core.Services.GitProcessEnvironment.Apply(startInfo);

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
