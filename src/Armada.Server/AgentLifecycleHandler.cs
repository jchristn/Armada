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

        /// <summary>
        /// The latest activity of each running mission's captain (from its runtime's structured output), read by the
        /// mission REST route and the WebSocket mission events.
        /// </summary>
        public MissionActivityTracker MissionActivity { get; } = new MissionActivityTracker();

        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, RuntimeToolWaitTracker> _ToolWaits = new System.Collections.Concurrent.ConcurrentDictionary<int, RuntimeToolWaitTracker>();

        /// <summary>Oldest outstanding tool for this process launch, or null when it is not waiting on a tool.</summary>
        public DateTime? GetToolWaitStartedUtc(int processId)
        {
            return _ToolWaits.TryGetValue(processId, out RuntimeToolWaitTracker? tracker) ? tracker.StartedUtc : null;
        }

        /// <summary>Bound on one model-validation process after launch; tests may shorten it.</summary>
        internal TimeSpan ModelValidationTimeout
        {
            get => _ModelValidationTimeout;
            set => _ModelValidationTimeout = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value));
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
        private MuxHostResolver _MuxHosts;
        private IAdmiralService _Admiral;
        private IMessageTemplateService _TemplateService;
        private IPromptTemplateService? _PromptTemplateService;
        private ArmadaWebSocketHub? _WebSocketHub;
        private Func<string, string, string?, string?, string?, string?, string?, string?, Task> _EmitEventAsync;
        private TimeSpan _ModelValidationTimeout = TimeSpan.FromSeconds(30);
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
        private System.Collections.Concurrent.ConcurrentDictionary<string, long> _CaptainHeartbeatWrites = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();

        /// <summary>
        /// Tracks per-mission final response artifacts so canonical agent output can be recovered even if live streaming is noisy.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, string> _MissionFinalMessageFiles = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        /// <summary>
        /// Per-mission telemetry of the running captain, observed over the CLI's raw stdout (before any structured
        /// decoding), so the runtime's own usage report (Claude Code's result event, Codex's turn.completed, Mux's
        /// run_completed) and model are known when the mission completes. Taken by <see cref="TakeMissionTelemetry"/>.
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _StoppingProcesses = new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RemoteAgentRuntime> _RemoteMissionRuntimes = new System.Collections.Concurrent.ConcurrentDictionary<string, RemoteAgentRuntime>(StringComparer.Ordinal);

        private System.Collections.Concurrent.ConcurrentDictionary<string, ChatTurnTelemetryRecorder> _MissionTelemetry = new System.Collections.Concurrent.ConcurrentDictionary<string, ChatTurnTelemetryRecorder>(StringComparer.Ordinal);

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
        /// Per running mission, the throttle that broadcasts its captain's activity on the WebSocket (at most about once a
        /// second, newest wins).
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, LatestValueThrottle<RuntimeActivity>> _ActivityBroadcasts = new System.Collections.Concurrent.ConcurrentDictionary<string, LatestValueThrottle<RuntimeActivity>>(StringComparer.Ordinal);

        private static readonly TimeSpan _ActivityBroadcastInterval = TimeSpan.FromSeconds(1);

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
            _MuxHosts = new MuxHostResolver(_Settings, _Database, () => _HarborConnections, _Logging);
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
                BranchName = mission.BranchName
            };
            Dock? branchDock = await FindLatestBranchDockAsync(mission, vessel).ConfigureAwait(false);
            if (DockHostResolver.IsHarborDock(branchDock)) request.BranchHarborId = branchDock!.HarborId;
            else if (branchDock != null) request.BranchOnAdmiral = true;
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

            // A branch an earlier stage built in the Admiral's repository exists only there: a Harbor would start the stage
            // from the default branch and silently drop that work. Keep the dock on the Admiral unless policy forbids it,
            // in which case assignment's commit-continuity check refuses the stage loudly instead.
            if (request.BranchOnAdmiral && branchHarborId == null && !_Settings.RequireHarborForLaunch)
                return DockPlacement.OnAdmiral("its branch " + request.BranchName + " is in the Admiral host's repository, where the earlier stage ran");

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

            // Every Harbor that can run this work is busy: wait for a slot instead of starting it on the Admiral host,
            // where the captain's runtime may not even be installed.
            if (decision.CapableHarborsAtCapacity && refusals.Count == 0)
                return DockPlacement.WaitFor("every connected Harbor that can run it is at capacity; it starts when a slot frees up");

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
        /// Read a mission's output without consuming it: the final-message artifact when present, otherwise the
        /// accumulated stdout. Used before completion (the stage check) to see the stage's reported outcome.
        /// </summary>
        /// <param name="missionId">The mission.</param>
        /// <returns>The output, or null.</returns>
        public string? PeekMissionOutput(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return null;
            if (_MissionFinalMessageFiles.TryGetValue(missionId, out string? finalMessageFilePath) && !String.IsNullOrEmpty(finalMessageFilePath))
            {
                try
                {
                    if (File.Exists(finalMessageFilePath))
                    {
                        string finalMessage = File.ReadAllText(finalMessageFilePath).Trim();
                        if (!String.IsNullOrEmpty(finalMessage)) return finalMessage;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _Logging.Debug(_Header + "could not read the final message artifact for mission " + missionId + ": " + ex.Message);
                }
            }
            if (_MissionOutput.TryGetValue(missionId, out System.Text.StringBuilder? sb))
            {
                lock (sb)
                {
                    string text = sb.ToString().Trim();
                    return text.Length > 0 ? text : null;
                }
            }
            return null;
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
        /// Take (and forget) the telemetry recorded for a mission's latest launch: the token counts, cost, and model its
        /// runtime reported in its raw output, observed on the Admiral host and for a Harbor run alike. Null when the
        /// mission has no launch recorded, or it was already taken.
        /// </summary>
        /// <param name="missionId">Mission ID.</param>
        /// <returns>The recorder, or null.</returns>
        public ChatTurnTelemetryRecorder? TakeMissionTelemetry(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return null;
            return _MissionTelemetry.TryRemove(missionId, out ChatTurnTelemetryRecorder? recorder) ? recorder : null;
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
        /// Whether a merged captain update changes the model binding.
        /// The binding is <see cref="Captain.Model"/>, <see cref="Captain.Runtime"/>, and
        /// <see cref="Captain.ModelEndpointId"/>.
        /// </summary>
        /// <param name="stored">Captain currently stored.</param>
        /// <param name="updated">Captain after the update is merged.</param>
        /// <returns>True when any of the three fields differ.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stored"/> or <paramref name="updated"/> is null.</exception>
        public static bool ModelBindingChanged(Captain stored, Captain updated)
        {
            if (stored == null) throw new ArgumentNullException(nameof(stored));
            if (updated == null) throw new ArgumentNullException(nameof(updated));
            if (stored.Runtime != updated.Runtime) return true;
            if (!String.Equals(stored.Model, updated.Model, StringComparison.Ordinal)) return true;
            if (!String.Equals(stored.ModelEndpointId, updated.ModelEndpointId, StringComparison.Ordinal)) return true;
            return false;
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
            return ValidateModelDetailedCoreAsync(captain.Runtime, captain.Model, captain, token);
        }

        /// <summary>
        /// Validate an API-endpoint captain: it must reference an existing, enabled, Inference-kind model
        /// endpoint. Returns null when valid, otherwise a human-readable error.
        /// </summary>
        /// <param name="captain">Captain to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="forMission">Require tool execution for mission bootstrap; plain-chat validation remains permissive.</param>
        /// <returns>Null when valid, otherwise an error message.</returns>
        private async Task<CaptainModelValidationFailure?> ValidateApiEndpointCaptainAsync(Captain captain, CancellationToken token = default, bool forMission = false)
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

            if (forMission && (!endpoint.InferenceOptions.ToolsEnabled || endpoint.InferenceOptions.AllowedTools?.Count == 0))
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointNotToolEnabled,
                    "This API endpoint cannot run an Armada mission because tool calls are disabled or its allowed-tool list is empty. "
                    + "Missions need tools to read local instructions and perform their assigned work. In Configuration > Endpoints, "
                    + "enable tool calls and allow the tools needed by the mission (or leave the allowlist unrestricted), then restart the mission. "
                    + "Plain Ask Armada chats may keep tools disabled.");

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
        public Task<CaptainModelValidationFailure?> ValidateModelDetailedAsync(AgentRuntimeEnum runtimeType, string? model, CancellationToken token = default)
        {
            return ValidateModelDetailedCoreAsync(runtimeType, model, null, token);
        }

        private async Task<CaptainModelValidationFailure?> ValidateModelDetailedCoreAsync(AgentRuntimeEnum runtimeType, string? model, Captain? captain, CancellationToken token)
        {
            if (String.IsNullOrEmpty(model))
                return null;

            string validationDirectory = Path.Combine(Path.GetTempPath(), "armada-model-validation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(validationDirectory);

            Armada.Runtimes.Interfaces.IAgentRuntime runtime;
            bool remoteValidation = false;
            try
            {
                if (captain == null)
                {
                    runtime = _HostProcessExecutor.CreateRuntime(runtimeType);
                }
                else
                {
                    CaptainLaunchRouter router = new CaptainLaunchRouter(_Settings, _RuntimeFactory, _HarborConnections, _EndpointResolver, _Logging);
                    CaptainLaunchTarget target = await router.SelectAsync(new CaptainLaunchContext(captain, "model validation")
                    {
                        TenantId = captain.TenantId,
                        UserId = captain.UserId,
                        AllowScratchWorkingDirectory = true
                    }, token).ConfigureAwait(false);
                    runtime = target.Runtime;
                    remoteValidation = target.IsRemote;
                }
            }
            catch (OperationCanceledException)
            {
                try { Directory.Delete(validationDirectory, true); } catch { }
                throw;
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
                if (!remoteValidation)
                    await InitializeValidationWorkspaceAsync(runtimeType, validationDirectory, token).ConfigureAwait(false);

                processId = await runtime.StartAsync(
                    remoteValidation ? String.Empty : validationDirectory,
                    "Respond with the single word OK.",
                    model: model,
                    captain: captain,
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
                if (CaptainLaunchRouter.TryDescribeFailure(ex, out string launchError, out _))
                    return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.RuntimeUnavailable, launchError);
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.ModelRejected, "Model '" + model + "' failed validation for runtime " + runtimeType + ": " + ex.Message);
            }
            finally
            {
                if (processId.HasValue)
                {
                    try
                    {
                        await runtime.StopAsync(processId.Value, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch { }
                }

                try { Directory.Delete(validationDirectory, true); } catch { }
            }
        }

        /// <summary>Release local observers while leaving Harbor mission processes running.</summary>
        public void DetachHarborMissions()
        {
            foreach (KeyValuePair<string, RemoteAgentRuntime> pair in _RemoteMissionRuntimes)
            {
                if (_RemoteMissionRuntimes.TryRemove(pair)) pair.Value.Detach();
            }
        }

        private void ObserveMissionRuntime(Armada.Runtimes.Interfaces.IAgentRuntime runtime, Captain captain, Mission mission,
            out ChatTurnTelemetryRecorder missionTelemetry, out LatestValueThrottle<RuntimeActivity> missionActivity)
        {
            if (runtime is RemoteAgentRuntime remote)
            {
                _RemoteMissionRuntimes[mission.Id] = remote;
                runtime.OnProcessExited += (_, _) => _RemoteMissionRuntimes.TryRemove(new KeyValuePair<string, RemoteAgentRuntime>(mission.Id, remote));
            }
            ChatTurnTelemetryRecorder telemetry = new ChatTurnTelemetryRecorder();
            AgentRuntimeEnum telemetryRuntime = captain.Runtime;
            _MissionTelemetry[mission.Id] = telemetry;
            if (runtime is IRawStdoutSource rawSource)
                rawSource.OnRawStdoutReceived += (processId, line) => telemetry.ObserveLine(telemetryRuntime, line);
            else
                runtime.OnStdoutReceived += (processId, line) => telemetry.ObserveLine(telemetryRuntime, line);

            // Raw CLI events are available locally and over Harbor; API lifecycle diagnostics travel on
            // general output forwarded by Harbor. Keep this separate from output heartbeats.
            Action<int, string> observeTool = (pid, line) =>
                _ToolWaits.GetOrAdd(pid, _ => new RuntimeToolWaitTracker()).Observe(captain.Runtime, line, DateTime.UtcNow);
            if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint)
                runtime.OnOutputReceived += observeTool;
            else if (runtime is IRawStdoutSource toolSource)
                toolSource.OnRawStdoutReceived += observeTool;
            else
                runtime.OnStdoutReceived += observeTool;
            runtime.OnProcessExited += (pid, exitCode) => _ToolWaits.TryRemove(pid, out _);

            runtime.OnOutputReceived += HandleAgentOutput;
            runtime.OnOutputReceived += HandleAgentHeartbeat;
            runtime.OnStdoutReceived += HandleAgentStdout;
            runtime.OnProviderError += HandleAgentProviderError;
            runtime.OnActivity += HandleAgentActivity;
            LatestValueThrottle<RuntimeActivity> activityBroadcast = BeginMissionActivity(mission);
            runtime.OnProcessExited += (processId, exitCode) =>
            {
                if (runtime is RemoteAgentRuntime stopped && stopped.StopRequested)
                {
                    StopProcessLivenessHeartbeat(processId);
                    lock (_ProcessToCaptain)
                    {
                        _ProcessToCaptain.Remove(processId);
                        _ProcessToMission.Remove(processId);
                    }
                    return;
                }
                HandleAgentProcessExited(processId, exitCode);
            };
            string exitedMissionId = mission.Id;
            runtime.OnProcessExited += (exitedPid, exitCode) => EndMissionActivity(exitedMissionId, activityBroadcast);
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

            missionTelemetry = telemetry;
            missionActivity = activityBroadcast;
        }

        /// <summary>
        /// Recover Harbor mission ownership before the REST listener accepts reconnects. Runtime callbacks, log artifacts,
        /// process maps and completion handling are restored; no launch request is sent to the Harbor.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of restored jobs.</returns>
        public async Task<int> RestoreHarborMissionsAsync(CancellationToken token = default)
        {
            if (_HarborConnections == null) return 0;
            int restored = 0;
            List<Captain> captains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Working, token).ConfigureAwait(false);
            foreach (Captain captain in captains)
            {
                if (!captain.ProcessId.HasValue || String.IsNullOrEmpty(captain.CurrentMissionId) || _RemoteMissionRuntimes.ContainsKey(captain.CurrentMissionId)) continue;
                Mission? mission = await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
                if (mission == null || (mission.Status != MissionStatusEnum.InProgress && mission.Status != MissionStatusEnum.Assigned)
                    || mission.CaptainId != captain.Id || (mission.ProcessId.HasValue && mission.ProcessId != captain.ProcessId) || String.IsNullOrEmpty(mission.DockId)) continue;
                Dock? dock = await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
                if (dock == null || !dock.Active || String.IsNullOrEmpty(dock.HarborId)) continue;
                List<HarborJobRecord> jobs = await _Database.HarborJobs.EnumerateAsync(dock.HarborId, mission.CreatedUtc, DateTime.MaxValue, token).ConfigureAwait(false);
                HarborJobRecord? job = jobs.Where(item => item.MissionId == mission.Id && item.CaptainId == captain.Id
                    && item.Kind == Armada.Core.Harbor.HarborJobKindEnum.Mission).OrderByDescending(item => item.LaunchedUtc).FirstOrDefault();
                if (job == null || (job.StopRequested && job.EndedUtc.HasValue) || job.Runtime != captain.Runtime.ToString()) continue;
                // The old callback may have claimed the exit (clearing ProcessId) before shutdown interrupted
                // completion. Re-arm that claim only for a recorded exit on this still-active assignment.
                if (!mission.ProcessId.HasValue)
                {
                    if (!job.EndedUtc.HasValue) continue;
                    MissionStatusEnum observed = mission.Status;
                    mission.ProcessId = captain.ProcessId;
                    if (!await _Database.Missions.TryUpdateIfStatusAsync(mission, new[] { observed }, token).ConfigureAwait(false)) continue;
                }

                RemoteAgentRuntime runtime = new RemoteAgentRuntime(_HarborConnections, dock.HarborId, captain.Runtime)
                {
                    MissionId = mission.Id,
                    JobKind = Armada.Core.Harbor.HarborJobKindEnum.Mission,
                    StructuredProgress = true
                };
                ObserveMissionRuntime(runtime, captain, mission, out _, out _);
                string finalMessage = Path.Combine(_Settings.LogDirectory, "final-messages", mission.Id + ".txt");
                _MissionFinalMessageFiles[mission.Id] = finalMessage;
                int restoredProcess = runtime.Reattach(job.JobId, captain.ProcessId.Value, Path.Combine(_Settings.LogDirectory, "missions", mission.Id + ".log"), finalMessage, alreadyExited: job.EndedUtc.HasValue, stopRequested: job.StopRequested);
                if (restoredProcess != captain.ProcessId.Value)
                {
                    captain.ProcessId = restoredProcess;
                    mission.ProcessId = restoredProcess;
                    await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                }
                lock (_ProcessToCaptain)
                {
                    _ProcessToCaptain[restoredProcess] = captain.Id;
                    _ProcessToMission[restoredProcess] = mission.Id;
                }
                StartProcessLivenessHeartbeat(captain.ProcessId.Value, captain.Id, mission.Id);
                restored++;
                // An exit may have been durably recorded immediately before the old Admiral stopped, but its
                // asynchronous mission completion was not committed. Replay it through the normal claimed-exit path.
                if (job.EndedUtc.HasValue)
                    _ = ReplayRecordedExitAsync(runtime, dock.HarborId!, mission.Id, job.ExitCode ?? -1);
            }
            return restored;
        }

        /// <summary>
        /// How long the replay of a Harbor job's recorded exit waits for that Harbor to reconnect after an Admiral restart.
        /// </summary>
        public TimeSpan RecordedExitReplayWait { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Replay a Harbor job's exit that was recorded before the Admiral stopped, once its Harbor is connected again.
        /// Completion checks the stage on the Harbor, commits there and hands off; run before the Harbor is back, it
        /// would find nothing to talk to and treat the work as unverified. Without the Harbor after the wait, the
        /// completion runs anyway so the mission is not left in limbo.
        /// </summary>
        private async Task ReplayRecordedExitAsync(RemoteAgentRuntime runtime, string harborId, string missionId, int exitCode)
        {
            try
            {
                DateTime deadline = DateTime.UtcNow + RecordedExitReplayWait;
                while (_HarborConnections != null && !_HarborConnections.IsConnected(harborId) && DateTime.UtcNow < deadline)
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                _Logging.Info(_Header + "replaying the recorded exit of mission " + missionId + " on Harbor " + harborId);
                runtime.OnExited(exitCode);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not replay the recorded exit of mission " + missionId + ": " + ex.ToString());
            }
        }

        /// <summary>
        /// Launch an agent process for the given captain, mission, and dock.
        /// </summary>
        public async Task<int> HandleLaunchAgentAsync(Captain captain, Mission mission, Dock dock)
        {
            _Logging.Info(_Header + "launching " + captain.Runtime + " agent for captain " + captain.Id);
            if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint)
            {
                CaptainModelValidationFailure? configuration = await ValidateApiEndpointCaptainAsync(captain, forMission: true).ConfigureAwait(false);
                if (configuration != null) throw new MissionLaunchConfigurationException(configuration.Message);
            }
            LaunchExecutorResolution resolution = await ResolveLaunchExecutorAsync(captain, mission, dock).ConfigureAwait(false);
            Armada.Runtimes.Interfaces.IAgentRuntime runtime = resolution.Executor.CreateRuntime(captain.Runtime);
            string launchKey = captain.Id + ":" + mission.Id;
            _PendingLaunches[launchKey] = new PendingLaunchInfo
            {
                CaptainId = captain.Id,
                MissionId = mission.Id
            };
            runtime.OnProcessStarted += processId => HandleProcessStarted(processId, launchKey);

            ObserveMissionRuntime(runtime, captain, mission, out ChatTurnTelemetryRecorder telemetry,
                out LatestValueThrottle<RuntimeActivity> activityBroadcast);

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
                // Claude Code's and Mux's permission prompts to Armada (it needs the mission token; a Harbor launch falls back
                // unless that Harbor advertises the prompt capability for the runtime).
                RemoteAgentRuntime? remoteLaunch = runtime as RemoteAgentRuntime;
                bool harborCanPrompt = remoteLaunch != null && _HarborConnections != null && _HarborConnections.CanPrompt(remoteLaunch.HarborId, captain.Runtime);
                Voyage? approvalVoyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await _Database.Voyages.ReadAsync(mission.VoyageId).ConfigureAwait(false);
                CliPermissionResolution permission = CliPermissionPolicyResolver.ResolveForMission(_Settings, captain, vessel, missionToken != null, remoteLaunch != null, harborCanPrompt, approvalVoyage?.ApprovalOptions);
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

                CliPermissionLaunch.Apply(runtime, permission.Effective, approvalVoyage?.ApprovalOptions?.TimeoutSeconds ?? _Settings.Permissions.PromptTimeoutSeconds, inProcessPrompt);

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
                    // Stream the captain's structured output (Claude Code, Codex) so its activity is visible while it runs;
                    // the runtime decodes it into the readable output text mode prints, so the final message is unchanged.
                    hostedRuntime.StructuredProgress = true;

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
                    remoteRuntime.StructuredProgress = true;
                    remoteRuntime.Display = await BuildMissionDisplayAsync(mission, captain, vessel, dock).ConfigureAwait(false);
                    if (missionToken != null)
                    {
                        // The Harbor binds the token against the MCP URL it was given in the handshake.
                        remoteRuntime.McpSessionToken = missionToken;
                        environment = null;
                    }

                    // CliPermissionLaunch.Apply already carries every resolved policy, including Bypass and Refuse,
                    // so the Harbor's API runtime enforces the policy reported in this mission's log.
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
                if (runtime is RemoteAgentRuntime failedRemote)
                {
                    _RemoteMissionRuntimes.TryRemove(new KeyValuePair<string, RemoteAgentRuntime>(mission.Id, failedRemote));
                    failedRemote.Detach();
                }
                _PendingLaunches.TryRemove(launchKey, out _);
                _MissionFinalMessageFiles.TryRemove(mission.Id, out _);
                _MissionTelemetry.TryRemove(new KeyValuePair<string, ChatTurnTelemetryRecorder>(mission.Id, telemetry));
                EndMissionActivity(mission.Id, activityBroadcast);

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
        /// Handle an activity a mission's captain reported (a tool call, text, or reasoning from its structured output):
        /// record it as the mission's current activity, count it as output for stall detection, and broadcast it.
        /// </summary>
        /// <param name="processId">Process ID.</param>
        /// <param name="activity">The activity.</param>
        public void HandleAgentActivity(int processId, RuntimeActivity activity)
        {
            if (activity == null) return;
            string? missionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToMission.TryGetValue(processId, out missionId);
            }

            if (String.IsNullOrEmpty(missionId)) return;
            MissionActivity.Update(missionId, activity);

            // A captain that streams its work is producing output even before it prints anything readable.
            HandleAgentHeartbeat(processId, activity.Summary);

            if (_ActivityBroadcasts.TryGetValue(missionId, out LatestValueThrottle<RuntimeActivity>? broadcast))
                broadcast.Submit(activity);
        }

        /// <summary>
        /// Start tracking a mission's activity for a launch: forget any left from an earlier run and set up its broadcast.
        /// </summary>
        private LatestValueThrottle<RuntimeActivity> BeginMissionActivity(Mission mission)
        {
            if (_ActivityBroadcasts.TryRemove(mission.Id, out LatestValueThrottle<RuntimeActivity>? earlier)) earlier.Dispose();
            MissionActivity.Clear(mission.Id);
            string missionId = mission.Id;
            string? tenantId = mission.TenantId;
            string? voyageId = mission.VoyageId;
            LatestValueThrottle<RuntimeActivity> broadcast = new LatestValueThrottle<RuntimeActivity>(_ActivityBroadcastInterval, _Time,
                activity => _WebSocketHub?.BroadcastMissionActivity(tenantId, missionId, voyageId, activity));
            _ActivityBroadcasts[missionId] = broadcast;
            return broadcast;
        }

        /// <summary>
        /// Stop tracking a mission's activity: the captain of this launch stopped. A later launch of the same mission
        /// (a relaunch) keeps its own tracking.
        /// </summary>
        private void EndMissionActivity(string missionId, LatestValueThrottle<RuntimeActivity> broadcast)
        {
            broadcast.Dispose();
            if (_ActivityBroadcasts.TryRemove(new KeyValuePair<string, LatestValueThrottle<RuntimeActivity>>(missionId, broadcast)))
                MissionActivity.Clear(missionId);
        }

        /// <summary>
        /// The display fields a Harbor shows for a mission launch: the mission, vessel, voyage and position, captain, stage,
        /// and branch. A lookup that fails leaves its fields out; it never stops the launch.
        /// </summary>
        private async Task<Armada.Core.Harbor.HarborLaunchDisplay> BuildMissionDisplayAsync(Mission mission, Captain captain, Vessel? vessel, Dock dock)
        {
            Voyage? voyage = null;
            List<MissionSummary>? voyageMissions = null;
            if (!String.IsNullOrEmpty(mission.VoyageId))
            {
                try
                {
                    voyage = await _Database.Voyages.ReadAsync(mission.VoyageId!).ConfigureAwait(false);
                    voyageMissions = await _Database.Missions.EnumerateSummariesByVoyageAsync(mission.VoyageId!).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Debug(_Header + "could not read voyage " + mission.VoyageId + " for the launch display of mission " + mission.Id + ": " + ex.Message);
                }
            }

            return HarborLaunchDisplayBuilder.ForMission(mission, captain, vessel, voyage, voyageMissions, dock.BranchName);
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

            // The captain heartbeat is throttled like the mission's: a chatty model prints many lines a second, and a
            // database write per line contends for the write lock under many concurrent captains.
            bool persistCaptainHeartbeat = ShouldPersistHeartbeat(_CaptainHeartbeatWrites, captainId);
            if (!persistCaptainHeartbeat && !persistMissionHeartbeat) return;

            string capturedCaptainId = captainId;
            _ = Task.Run(async () =>
            {
                if (persistCaptainHeartbeat)
                {
                    try { await _Database.Captains.UpdateHeartbeatAsync(captainId).ConfigureAwait(false); }
                    catch { _CaptainHeartbeatWrites.TryRemove(captainId, out _); }
                }

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
            _ProcessProviderErrors.AddOrUpdate(processId, error, (_, current) => RuntimeFailureClassifier.KeepMoreSignificant(current, error));
            _Logging.Debug(_Header + "process " + processId + " reported " + error.ToString());
        }

        /// <summary>
        /// Handle agent process exit event.
        /// </summary>
        public void HandleAgentProcessExited(int processId, int? exitCode)
        {
            if (_StoppingProcesses.ContainsKey(processId)) return;
            StopProcessLivenessHeartbeat(processId);

            string? captainId = null;
            string? missionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.TryGetValue(processId, out captainId);
                _ProcessToMission.TryGetValue(processId, out missionId);
            }

            // The process exit event can fire before HandleLaunchAgentAsync finishes registering the PID-to-captain/mission
            // mapping (race between process.Start() returning and the mapping being written). Wait for it briefly off this
            // thread (an exit event must never block a thread-pool thread), then continue the same way.
            if (String.IsNullOrEmpty(captainId) || String.IsNullOrEmpty(missionId))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string? lateCaptainId = null;
                        string? lateMissionId = null;
                        for (int attempt = 0; attempt < 20; attempt++)
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                            lock (_ProcessToCaptain)
                            {
                                _ProcessToCaptain.TryGetValue(processId, out lateCaptainId);
                                _ProcessToMission.TryGetValue(processId, out lateMissionId);
                            }
                            if (!String.IsNullOrEmpty(lateCaptainId) && !String.IsNullOrEmpty(lateMissionId))
                            {
                                _Logging.Debug(_Header + "process " + processId + " exit handler resolved mapping after " + (attempt + 1) + " retries");
                                CompleteProcessExit(processId, exitCode, lateCaptainId!, lateMissionId!);
                                return;
                            }
                        }

                        _Logging.Warn(_Header + "process " + processId + " exited (code " + (exitCode?.ToString() ?? "unknown") + ") but no captain/mission mapping found after retries -- exit may be lost");
                        _ProcessProviderErrors.TryRemove(processId, out _);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error resolving the exit of process " + processId + ": " + ex.ToString());
                    }
                });
                return;
            }

            CompleteProcessExit(processId, exitCode, captainId!, missionId!);
        }

        /// <summary>
        /// Async handler for agent process exit, delegating to the admiral service.
        /// </summary>
        public async Task HandleAgentProcessExitedAsync(int processId, int? exitCode, string captainId, string missionId)
        {
            _ProcessProviderErrors.TryRemove(processId, out RuntimeProviderError? providerError);
            RuntimeExitInfo exitInfo = RuntimeFailureClassifier.Decide(exitCode, providerError);

            // The completion flow takes this launch's telemetry; whatever it left (a failed or interrupted exit) is
            // dropped afterwards, unless the mission was relaunched meanwhile and the entry is the new launch's.
            _MissionTelemetry.TryGetValue(missionId, out ChatTurnTelemetryRecorder? launchTelemetry);
            try
            {
                await _Admiral.HandleProcessExitAsync(processId, exitInfo, captainId, missionId).ConfigureAwait(false);
            }
            finally
            {
                if (launchTelemetry != null)
                    _MissionTelemetry.TryRemove(new KeyValuePair<string, ChatTurnTelemetryRecorder>(missionId, launchTelemetry));
            }
        }

        /// <summary>
        /// Stop the agent process for the given captain.
        /// </summary>
        public async Task HandleStopAgentAsync(Captain captain)
        {
            if (!captain.ProcessId.HasValue) return;
            _Logging.Info(_Header + "stopping agent process " + captain.ProcessId.Value + " for captain " + captain.Id);
            int processId = captain.ProcessId.Value;
            _StoppingProcesses[processId] = 0;
            try
            {
                if (captain.CurrentMissionId != null && _RemoteMissionRuntimes.TryGetValue(captain.CurrentMissionId, out RemoteAgentRuntime? remote))
                    await remote.StopAndConfirmAsync().ConfigureAwait(false);
                else if (_HarborConnections != null && _HarborConnections.IsProcessTracked(processId))
                    await _HarborConnections.StopProcessAndConfirmAsync(processId).ConfigureAwait(false);
                else
                {
                    // A missing transient process map does not turn a Harbor PID into a local PID.
                    Mission? assignment = captain.CurrentMissionId == null ? null
                        : await _Database.Missions.ReadAsync(captain.CurrentMissionId).ConfigureAwait(false);
                    Dock? ownedDock = assignment?.DockId == null ? null
                        : await _Database.Docks.ReadAsync(assignment.DockId).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(ownedDock?.HarborId))
                    {
                        List<HarborJobRecord> records = await _Database.HarborJobs.EnumerateAsync(ownedDock.HarborId!, assignment!.CreatedUtc, DateTime.MaxValue).ConfigureAwait(false);
                        HarborJobRecord? last = records.Where(job => job.MissionId == assignment.Id && job.CaptainId == captain.Id)
                            .OrderByDescending(job => job.LaunchedUtc).FirstOrDefault();
                        if (last == null || !last.EndedUtc.HasValue)
                            throw new InvalidOperationException("Harbor mission is not tracked; restore its owning Harbor job before requesting a stop. Local PID fallback is prohibited.");
                        // An already-confirmed remote exit makes a repeated stop a no-op.
                    }
                    else
                    {
                        IHostProcessExecutor stopExecutor = ResolveStopExecutor(processId);
                        Armada.Runtimes.Interfaces.IAgentRuntime runtime = stopExecutor.CreateRuntime(captain.Runtime);
                        await runtime.StopAsync(processId).ConfigureAwait(false);
                    }
                }
                lock (_ProcessToCaptain)
                {
                    _ProcessToCaptain.Remove(processId);
                    _ProcessToMission.Remove(processId);
                }
                StopProcessLivenessHeartbeat(processId);
            }
            finally { _StoppingProcesses.TryRemove(processId, out _); }

        }

        #endregion

        #region Private-Methods

        private void CompleteProcessExit(int processId, int? exitCode, string captainId, string missionId)
        {
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
        /// Whether a mission heartbeat should be written now: the first one for a mission, then at most one per
        /// persist interval. The interval is measured on the monotonic clock: on the wall clock a backward step
        /// suppressed writes until the clock caught up, and the mission looked stalled.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <returns>True when the heartbeat should be persisted.</returns>
        internal bool ShouldPersistMissionHeartbeat(string missionId)
        {
            return ShouldPersistHeartbeat(_MissionHeartbeatWrites, missionId);
        }

        private bool ShouldPersistHeartbeat(System.Collections.Concurrent.ConcurrentDictionary<string, long> writes, string key)
        {
            bool persist = false;
            long now = _Time.GetTimestamp();
            writes.AddOrUpdate(
                key,
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
            Dock? latest = await FindLatestBranchDockAsync(mission, vessel).ConfigureAwait(false);
            return DockHostResolver.IsHarborDock(latest) ? latest!.HarborId : null;
        }

        private async Task<Dock?> FindLatestBranchDockAsync(Mission mission, Vessel vessel)
        {
            if (String.IsNullOrEmpty(mission.BranchName)) return null;
            List<Dock> docks = await _Database.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
            Dock? latest = null;
            foreach (Dock candidate in docks)
            {
                if (!String.Equals(candidate.BranchName, mission.BranchName, StringComparison.Ordinal)) continue;
                if (latest == null || candidate.CreatedUtc >= latest.CreatedUtc) latest = candidate;
            }

            return latest;
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

            // Mux runs next to the Harbor that will run this captain, so probe that mux, not one on the Admiral host.
            MuxHost host = await _MuxHosts.ResolveAsync(captain.TenantId, captain.UserId, null, captain, token).ConfigureAwait(false);
            if (host.Executor == null)
            {
                // Saving a captain must not require the developer's machine to be online; the launch checks again.
                _Logging.Warn(_Header + "skipping mux validation for captain " + captain.Id + ": " + host.Error);
                return null;
            }

            try
            {
                MuxProbeResult probe = await new MuxCliService(_Logging, host.Executor).ProbeAsync(captain, host.WorkingDirectory, token).ConfigureAwait(false);
                if (!MuxCliService.SupportsContractVersion(probe.ContractVersion))
                {
                    return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.UnsupportedContractVersion, "Mux returned structured output contract version " + probe.ContractVersion +
                        ", but Armada supports versions " + MuxCliService.MinimumContractVersion + " to " + MuxCliService.MaximumContractVersion + ".");
                }

                if (!probe.Success)
                {
                    string error = !String.IsNullOrWhiteSpace(probe.ErrorMessage)
                        ? probe.ErrorMessage
                        : "Mux probe failed with error code " + (String.IsNullOrWhiteSpace(probe.ErrorCode) ? "unknown" : probe.ErrorCode) + ".";
                    return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointProbeFailed, "Mux endpoint '" + options.Endpoint + "' failed validation" + MuxHostSuffix(host) + ": " + error);
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
                return CaptainModelValidationFailure.Create(CaptainModelValidationFailureEnum.EndpointProbeFailed, "Mux endpoint '" + options.Endpoint + "' failed validation" + MuxHostSuffix(host) + ": " + ex.Message);
            }
        }

        private static string MuxHostSuffix(MuxHost host)
        {
            return host.IsRemote ? " on Harbor " + host.HarborId : " on the Admiral host";
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
