namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.InteropServices;
    using System.Threading.Channels;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Storage;
    using SyslogLogging;

    /// <summary>
    /// The Harbor-side runner brain: dials the Admiral over a transport, sends its handshake, heartbeats,
    /// and carries out delegated host operations. Git/gh requests run through the supplied
    /// <see cref="IHostCommandExecutor"/>; captain launches run through the supplied
    /// <see cref="IHarborJobRunner"/> (when present) with their lifecycle streamed back. Outbound messages
    /// are sent through a single ordered pump so streamed output cannot interleave or reorder. A job's lifecycle messages
    /// go through the <see cref="HarborJobOutbox"/> of the shared <see cref="JobState"/>, which outlives the session: a
    /// job that keeps running through a link drop reports its remaining output and its exit on the next link. The protocol
    /// logic is transport-agnostic so it can be exercised without a live socket.
    /// </summary>
    public class HarborLinkClient
    {
        #region Public-Members

        /// <summary>
        /// MCP base URL advertised by the Admiral at the last handshake acknowledgement, or null.
        /// </summary>
        public string? McpBaseUrl { get; private set; } = null;

        /// <summary>
        /// Time source. Waits (the deferred-launch health poll) and job durations are measured on its monotonic clock
        /// (<see cref="TimeProvider.GetTimestamp"/>), never on its wall clock, so a wall-clock jump (the host sleeping
        /// and waking, an NTP step) cannot end a wait early or inflate a duration. Defaults to
        /// <see cref="TimeProvider.System"/>; tests substitute a provider whose wall clock jumps.
        /// </summary>
        internal TimeProvider Time
        {
            get => _Time;
            set => _Time = value ?? throw new ArgumentNullException(nameof(Time));
        }

        /// <summary>
        /// Link counters kept across sessions (accepted sessions, reconnects), reported in every heartbeat. A Harbor runs
        /// one client per session, so the owner of the reconnect loop sets the same instance on every session's client;
        /// by default each client has its own, which reports no reconnects.
        /// </summary>
        public HarborLinkStatistics LinkStatistics
        {
            get => _LinkStatistics;
            set => _LinkStatistics = value ?? throw new ArgumentNullException(nameof(LinkStatistics));
        }

        /// <summary>
        /// Builds the link's log entries with their typed fields and remembers which directories are docks and checkouts.
        /// A Harbor runs one client per session, so the owner of the reconnect loop sets the same instance on every
        /// session's client (a dock made in one session can be removed in the next); by default each client has its own.
        /// </summary>
        public HarborLogClassifier LogClassifier
        {
            get => _LogClassifier;
            set => _LogClassifier = value ?? throw new ArgumentNullException(nameof(LogClassifier));
        }

        /// <summary>
        /// Round-trip time of the most recent acknowledged heartbeat in the current session, in milliseconds, or null.
        /// </summary>
        public long? LastRoundTripMs
        {
            get { lock (_HeartbeatLock) return _LastRoundTripMs; }
        }

        /// <summary>
        /// The Harbor's jobs: the ones running now and the outbox their lifecycle messages travel through. A Harbor runs
        /// one client per session, so the owner of the reconnect loop sets the same instance on every session's client
        /// (with the same job runner): a job started over one link then keeps reporting its output and exit after the link
        /// drops, and heartbeats on the next link still list it. By default each client has its own.
        /// </summary>
        public HarborJobState JobState
        {
            get => _JobState;
            set => _JobState = value ?? throw new ArgumentNullException(nameof(JobState));
        }

        /// <summary>
        /// Minimum time, in milliseconds, without a heartbeat acknowledgement after which the Harbor treats its link as
        /// dead and ends the session (so it reconnects). The timeout is the larger of this and three heartbeat intervals,
        /// and applies only once the Admiral has acknowledged a heartbeat on the link (an Admiral that never does is not
        /// judged). Minimum 1000; defaults to 45000.
        /// </summary>
        public int MinimumDeadLinkTimeoutMs
        {
            get => _MinimumDeadLinkTimeoutMs;
            set => _MinimumDeadLinkTimeoutMs = value < 1000 ? 1000 : value;
        }

        #endregion

        /// <summary>Optional bounded local storage collector. Runs off the socket and heartbeat paths.</summary>
        public Func<CancellationToken, Task<StorageHostReport>>? StorageCollector { get; set; }

        private StorageHostReport? _StorageReport;

        #region Private-Members

        private readonly string _Header = "[HarborLinkClient] ";
        private readonly string _HarborId;
        private readonly string _Name;
        private readonly List<HarborCapability> _Capabilities;
        private readonly int _MaxConcurrentJobs;
        private readonly IHostCommandExecutor _CommandExecutor;
        private readonly IHarborJobRunner? _JobRunner;
        private readonly IUsageProbe? _UsageProbe;
        private readonly HarborDockManager? _Docks;
        private readonly LoggingModule _Logging;
        private readonly int _HeartbeatIntervalMs;
        private readonly Action<HarborLogEntry>? _OnLog;
        private HarborJobState _JobState = new HarborJobState();
        private static readonly TimeSpan _ActivityInterval = TimeSpan.FromSeconds(1);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _ProbeCancellations = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _CommandLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Task, byte> _CommandsInFlight = new ConcurrentDictionary<Task, byte>();
        private CancellationTokenSource? _SessionCommands = null;
        private Action? _OnConnected;
        private Channel<HarborMessage>? _Outbound;
        private SemaphoreSlim? _Wake = null;
        private CancellationTokenSource? _SessionLink = null;
        private volatile bool _HandshakeSent = false;
        private List<string> _HandshakeJobIds = new List<string>();
        private long _LastAckTimestamp = 0;
        private bool _AckSeen = false;
        private int _MinimumDeadLinkTimeoutMs = 45000;
        private TimeProvider _Time = TimeProvider.System;
        private HarborLinkStatistics _LinkStatistics = new HarborLinkStatistics();
        private HarborLogClassifier _LogClassifier = new HarborLogClassifier();
        private readonly object _HeartbeatLock = new object();
        private readonly Dictionary<long, long> _HeartbeatSentTimestamps = new Dictionary<long, long>();
        private long _HeartbeatSequence = 0;
        private long? _LastRoundTripMs = null;
        private const int _MaxPendingHeartbeats = 8;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harborId">Harbor identifier (hbr_ prefix).</param>
        /// <param name="name">Harbor name.</param>
        /// <param name="capabilities">Advertised capabilities.</param>
        /// <param name="maxConcurrentJobs">Advertised capacity.</param>
        /// <param name="commandExecutor">Executor for delegated git/gh commands (typically local).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="heartbeatIntervalMs">Heartbeat interval in milliseconds; 0 disables heartbeats.</param>
        /// <param name="onLog">Optional sink for link log entries (work in, status out) for a UI log view.</param>
        /// <param name="jobRunner">Optional captain launcher; when null, launch requests are refused. When set the Harbor
        /// advertises <see cref="HarborProtocol.CliPermissionPromptCapability"/> for Claude Code, and for Mux when the
        /// installed mux is 1.5.0 or later.</param>
        /// <param name="dockManager">Optional Harbor-side dock manager; when set the Harbor advertises
        /// <see cref="HarborProtocol.DockCapability"/> and <see cref="HarborProtocol.CheckoutCapability"/>, creates mission docks
        /// on this host, and serves file operations in vessel checkouts. When null, dock and file requests are refused. When
        /// its <see cref="HarborDockManager.TerminalLauncher"/> is set, the Harbor also advertises
        /// <see cref="HarborProtocol.OpenTerminalCapability"/>.</param>
        /// <param name="usageProbe">Optional host-side usage helper; when set, the Harbor advertises
        /// <see cref="HarborProtocol.UsageProbeCapability"/> and runs account usage capture probes on this
        /// host, performing credential access and sanitization locally. When null, a usage probe request is
        /// answered Unsupported rather than run.</param>
        public HarborLinkClient(
            string harborId,
            string name,
            List<HarborCapability> capabilities,
            int maxConcurrentJobs,
            IHostCommandExecutor commandExecutor,
            LoggingModule logging,
            int heartbeatIntervalMs,
            Action<HarborLogEntry>? onLog = null,
            IHarborJobRunner? jobRunner = null,
            HarborDockManager? dockManager = null,
            IUsageProbe? usageProbe = null)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            _HarborId = harborId;
            _Name = name;
            _Capabilities = new List<HarborCapability>(capabilities ?? new List<HarborCapability>());
            _Docks = dockManager;
            if (_Docks != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.DockCapability, StringComparison.OrdinalIgnoreCase)))
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.DockCapability, Available = true });
            if (_Docks != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.CheckoutCapability, StringComparison.OrdinalIgnoreCase)))
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.CheckoutCapability, Available = true });
            if (_Docks != null && _Docks.TerminalLauncher != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.OpenTerminalCapability, StringComparison.OrdinalIgnoreCase)))
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.OpenTerminalCapability, Available = true });
            if (jobRunner != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.InferencePolicyCapability, StringComparison.OrdinalIgnoreCase)))
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.InferencePolicyCapability, Available = true });
            if (jobRunner != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.InferenceContextCapability, StringComparison.OrdinalIgnoreCase)))
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.InferenceContextCapability, Available = true });
            if (jobRunner != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.InferenceStreamingCapability, StringComparison.OrdinalIgnoreCase)))
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.InferenceStreamingCapability, Available = true });
            if (jobRunner != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.CliPermissionPromptCapability, StringComparison.OrdinalIgnoreCase)))
            {
                // Claude Code always can; Mux only from 1.5.0 (mux print --permission-prompt-tool).
                string runtimes = MuxCapabilities.SupportsPermissionPromptTool()
                    ? Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode + "," + Armada.Core.Enums.AgentRuntimeEnum.Mux
                    : Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode.ToString();
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.CliPermissionPromptCapability, Available = true, Detail = runtimes });
            }
            if (usageProbe != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.UsageProbeCapability, StringComparison.OrdinalIgnoreCase)))
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.UsageProbeCapability, Available = true });

            _MaxConcurrentJobs = maxConcurrentJobs < 1 ? 1 : maxConcurrentJobs;
            _CommandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _HeartbeatIntervalMs = heartbeatIntervalMs < 0 ? 0 : heartbeatIntervalMs;
            _OnLog = onLog;
            _JobRunner = jobRunner;
            _UsageProbe = usageProbe;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Identifiers of the jobs this Harbor is running now.
        /// </summary>
        /// <returns>Snapshot of the live job identifiers.</returns>
        public List<string> LiveJobIds()
        {
            return _JobState.LiveJobIds();
        }

        /// <summary>
        /// The jobs this Harbor is running now (what each is, its runtime, and when it started), oldest first.
        /// </summary>
        /// <returns>Copies of the live jobs.</returns>
        public List<HarborJobInfo> LiveJobs()
        {
            return _JobState.LiveJobs();
        }

        /// <summary>
        /// Run one link session over the given transport: connect, handshake, then handle messages until the
        /// transport closes or cancellation is requested.
        /// </summary>
        /// <param name="transport">Transport to run over.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onConnected">Optional callback invoked once the Admiral accepts the handshake.</param>
        public async Task RunSessionAsync(IHarborTransport transport, CancellationToken token, Action? onConnected = null)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));

            _OnConnected = onConnected;
            ResetHeartbeatTiming();
            Channel<HarborMessage> outbound = Channel.CreateUnbounded<HarborMessage>();
            SemaphoreSlim wake = new SemaphoreSlim(0, Int32.MaxValue);
            Action onJobMessage = () => Wake(wake);
            _Outbound = outbound;
            _Wake = wake;
            _HandshakeSent = false;
            _JobState.Attach(_JobRunner);

            try
            {
                await transport.ConnectAsync(token).ConfigureAwait(false);
            }
            catch
            {
                _Outbound = null;
                _Wake = null;
                throw;
            }

            using (CancellationTokenSource sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (CancellationTokenSource commandCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (CancellationTokenSource linkCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                _SessionCommands = commandCts;
                _SessionLink = linkCts;

                // Job messages held from earlier links are replayed on this one, in order, right after the handshake.
                _JobState.Outbox.BeginSession();
                _JobState.Outbox.MessageAdded += onJobMessage;
                Task pump = SendPumpAsync(transport, outbound, wake, sessionCts.Token);
                Task storage = StorageCollector != null ? Task.Run(() => StorageLoopAsync(sessionCts.Token), sessionCts.Token) : Task.CompletedTask;
                Task heartbeat = _HeartbeatIntervalMs > 0 ? HeartbeatLoopAsync(sessionCts.Token) : Task.CompletedTask;

                HarborHandshake handshake = BuildHandshake();
                _HandshakeJobIds = handshake.LiveJobIds ?? new List<string>();
                Enqueue(handshake);
                _Logging.Debug(_Header + "harbor " + _HarborId + " sent handshake");
                LogEntry(_LogClassifier.Link(HarborLogDirection.Out, "Handshake sent (harbor " + _HarborId + ")"));

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        string? raw;
                        try
                        {
                            raw = await transport.ReceiveAsync(linkCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested && linkCts.IsCancellationRequested)
                        {
                            // The heartbeat loop found the link dead (no acknowledgement for too long): end the session so
                            // the owner reconnects.
                            break;
                        }

                        if (raw == null) break;

                        HarborMessage message;
                        try
                        {
                            message = HarborProtocol.Deserialize(raw);
                        }
                        catch (FormatException e)
                        {
                            _Logging.Warn(_Header + "dropping malformed message: " + e.ToString());
                            continue;
                        }

                        await HandleAsync(message, token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    // Commands still running belong to this link: nobody can read their results once it is gone. Stop
                    // them, and let the ones that finish send their results before the outbound queue closes. Job
                    // messages are not lost with the link: what the pump cannot write stays in the outbox for the next one.
                    commandCts.Cancel();
                    try { await Task.WhenAll(_CommandsInFlight.Keys).ConfigureAwait(false); } catch { }
                    _SessionCommands = null;
                    outbound.Writer.TryComplete();
                    Wake(wake);
                    try { await pump.ConfigureAwait(false); } catch { }
                    _JobState.Outbox.MessageAdded -= onJobMessage;
                    sessionCts.Cancel();
                    try { await heartbeat.ConfigureAwait(false); } catch { }
                    try { await storage.ConfigureAwait(false); } catch (OperationCanceledException) { }
                    await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    _SessionLink = null;
                    _Outbound = null;
                    _Wake = null;
                }
            }
        }

        #endregion

        #region Private-Methods

        private HarborHandshake BuildHandshake()
        {
            return new HarborHandshake
            {
                HarborId = _HarborId,
                Name = _Name,
                ProtocolVersion = HarborProtocol.Version,
                OsPlatform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                MaxConcurrentJobs = _MaxConcurrentJobs,
                Capabilities = _Capabilities,
                LiveJobIds = _JobState.LiveJobIds(),
                HeartbeatIntervalMs = _HeartbeatIntervalMs,
                JobOutboxEpoch = _JobState.Outbox.Epoch
            };
        }

        private async Task HandleAsync(HarborMessage message, CancellationToken token)
        {
            if (message is HarborHandshakeAck ack)
            {
                McpBaseUrl = ack.McpBaseUrl;
                if (!ack.Accepted)
                {
                    _Logging.Warn(_Header + "handshake rejected: " + (ack.Reason ?? "unspecified"));
                    LogEntry(_LogClassifier.Link(HarborLogDirection.In, "Handshake rejected: " + (ack.Reason ?? "unspecified"), HarborLogOutcomeEnum.Failed));
                }
                else
                {
                    _Logging.Info(_Header + "handshake accepted; mcp=" + (ack.McpBaseUrl ?? "(none)"));
                    LogEntry(_LogClassifier.Link(HarborLogDirection.In, "Handshake accepted by Admiral. MCP=" + (ack.McpBaseUrl ?? "(none)"), HarborLogOutcomeEnum.Ok));
                    _LinkStatistics.RecordAccepted(_Time.GetUtcNow().UtcDateTime);
                    // Job messages are acknowledged in heartbeat acknowledgements; without heartbeats nothing would ever be
                    // acknowledged, so drop what the Admiral already has and then drop each message once written.
                    if (_HeartbeatIntervalMs <= 0 && ack.LastJobSequence.HasValue) _JobState.Outbox.Acknowledge(ack.LastJobSequence.Value);
                    _JobState.Outbox.SetAcknowledgementMode(_HeartbeatIntervalMs > 0 ? ack.LastJobSequence : null);
                    if (ack.OwnedJobIds != null) StopUnownedJobs(ack.OwnedJobIds);
                    Wake(_Wake);
                    _OnConnected?.Invoke();
                }
                return;
            }

            if (message is HarborHeartbeatAck heartbeatAck)
            {
                RecordHeartbeatAck(heartbeatAck.Sequence);
                if (heartbeatAck.LastJobSequence.HasValue) _JobState.Outbox.Acknowledge(heartbeatAck.LastJobSequence.Value);
                return;
            }

            if (message is HarborGitRequest git)
            {
                // A command can run for a long time (a check run, a build): run it off the receive loop so launches,
                // dock and file requests, and heartbeat acknowledgements keep flowing, one at a time per working
                // directory so commands in the same folder keep their order. A usage probe is its own path: it never
                // shares the per-directory command gate and never runs an executable.
                CancellationToken commandToken = _SessionCommands?.Token ?? token;
                if (git.Kind == HostCommandKindEnum.UsageProbe && !String.IsNullOrWhiteSpace(git.CancelsRequestId))
                {
                    // Request-specific cancellation: only the named probe is cancelled; its helper stops the
                    // probe's process tree and the probe's own reply reports ProbeCancelled.
                    if (_ProbeCancellations.TryGetValue(git.CancelsRequestId, out CancellationTokenSource? target))
                    {
                        try { target.Cancel(); }
                        catch (ObjectDisposedException) { }
                    }
                    return;
                }

                Task work;
                if (git.Kind == HostCommandKindEnum.UsageProbe)
                {
                    // Registered here, on the receive loop, before the probe starts: a cancel message is read
                    // after its probe request, so it always finds the probe's source while the probe runs.
                    CancellationTokenSource probeCts = CancellationTokenSource.CreateLinkedTokenSource(commandToken);
                    string probeRequestId = git.RequestId ?? String.Empty;
                    _ProbeCancellations[probeRequestId] = probeCts;
                    work = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleUsageProbeAsync(git, probeCts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _ProbeCancellations.TryRemove(new KeyValuePair<string, CancellationTokenSource>(probeRequestId, probeCts));
                            probeCts.Dispose();
                        }
                    });
                }
                else
                {
                    work = Task.Run(() => HandleGitAsync(git, commandToken));
                }
                _CommandsInFlight.TryAdd(work, 0);
                _ = work.ContinueWith(done => _CommandsInFlight.TryRemove(done, out byte _), TaskScheduler.Default);
                return;
            }

            if (message is HarborLaunchRequest launch)
            {
                await HandleLaunchAsync(launch, token).ConfigureAwait(false);
                return;
            }

            if (message is HarborKillRequest kill)
            {
                if (_JobRunner != null)
                {
                    // A stop waits up to its graceful timeout for the process to exit: run it off the receive loop so a
                    // stop-all of many jobs does not hold up heartbeats, acknowledgements and other requests.
                    LogEntry(_LogClassifier.Stop(kill.JobId));
                    IHarborJobRunner runner = _JobRunner;
                    Task work = Task.Run(async () =>
                    {
                        try
                        {
                            await runner.StopAsync(kill.JobId, kill.GracefulTimeoutMs, token).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            _Logging.Warn(_Header + "stop of job " + kill.JobId + " failed: " + e.ToString());
                        }
                    });
                    _CommandsInFlight.TryAdd(work, 0);
                    _ = work.ContinueWith(done => _CommandsInFlight.TryRemove(done, out byte _), TaskScheduler.Default);
                }
                return;
            }

            if (message is HarborDeferredLaunchRequest deferred)
            {
                HandleDeferredLaunch(deferred);
                return;
            }

            if (message is HarborDockRequest dock)
            {
                // Clones and fetches can take a while: run off the receive loop so other work keeps flowing.
                _ = Task.Run(() => HandleDockAsync(dock, token));
                return;
            }

            if (message is HarborFileRequest file)
            {
                _ = Task.Run(() => HandleFileAsync(file, token));
                return;
            }

            _Logging.Debug(_Header + "ignoring message " + message.GetType().Name);
        }

        private async Task HandleGitAsync(HarborGitRequest git, CancellationToken token)
        {
            string key = String.IsNullOrWhiteSpace(git.WorkingDirectory) ? String.Empty : git.WorkingDirectory.Trim().TrimEnd('/', '\\');
            SemaphoreSlim gate = _CommandLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            // Not cancellable: a command already received is always handed to the executor, which stops it when the link
            // closes (and a command that finishes anyway still reports its result while the link can send).
            await gate.WaitAsync().ConfigureAwait(false);

            try
            {
                LogEntry(_LogClassifier.GitRequest(git));

                HostCommandResult result;
                try
                {
                    HostCommandRequest command = new HostCommandRequest
                    {
                        Executable = git.Executable,
                        WorkingDirectory = git.WorkingDirectory,
                        Arguments = git.Arguments
                    };
                    if (git.TimeoutMs > 0) command.TimeoutMs = git.TimeoutMs;
                    result = await _CommandExecutor.RunAsync(command, token).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    // A command that cannot start (a working directory that does not exist here, a missing executable)
                    // is a failed result, not the end of the link.
                    result = new HostCommandResult { ExitCode = -1, StandardError = e.Message };
                }

                Enqueue(new HarborGitResult
                {
                    CorrelationId = git.CorrelationId,
                    RequestId = git.RequestId,
                    ExitCode = result.ExitCode,
                    StandardOutput = result.StandardOutput,
                    StandardError = result.StandardError,
                    TimedOut = result.TimedOut
                });

                LogEntry(_LogClassifier.GitResult(git, result));
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Run one account usage capture probe on this host: deserialize the safe probe context, let the
        /// host-side usage helper read the account limits with this host's own credentials, and reply with
        /// the sanitized result only. Never runs an executable, never shares the per-directory command
        /// gate, and never puts probe output into an unrestricted jobs log: the reply is the typed
        /// result, and failures become redacted error codes.
        /// </summary>
        /// <param name="probe">Usage probe request from the Admiral.</param>
        /// <param name="token">Cancellation token (the session command token).</param>
        private async Task HandleUsageProbeAsync(HarborGitRequest probe, CancellationToken token)
        {
            LogEntry(_LogClassifier.GitRequest(probe));

            UsageProbeResult result;
            IUsageProbe? helper = _UsageProbe;
            if (helper == null)
            {
                result = UsageProbeResult.Unsupported("HostUpgradeRequired", "this Harbor build cannot run usage probes; update it");
            }
            else
            {
                UsageProbeHost host = UsageProbeHost.Deserialize(probe.UsageProbeJson);
                if (String.IsNullOrWhiteSpace(host.TenantId) || String.IsNullOrWhiteSpace(host.HostId))
                {
                    result = UsageProbeResult.Failed("InvalidProbeContext", "the usage probe request has no tenant or host identity");
                }
                else
                {
                    if (probe.TimeoutMs > 0) host.TimeoutSeconds = (probe.TimeoutMs + 999) / 1000;
                    result = await RunUsageProbeAsync(helper, host, token).ConfigureAwait(false);
                }
            }

            Enqueue(new HarborGitResult
            {
                CorrelationId = probe.CorrelationId,
                RequestId = probe.RequestId,
                ExitCode = 0,
                StandardOutput = result.Serialize(),
                StandardError = String.IsNullOrWhiteSpace(result.ErrorCode) ? String.Empty : result.ErrorCode,
                TimedOut = String.Equals(result.ErrorCode, "ProbeTimeout", StringComparison.Ordinal)
            });

            LogEntry(_LogClassifier.GitResult(probe, new HostCommandResult
            {
                ExitCode = 0,
                StandardError = result.Status.ToString() + (String.IsNullOrWhiteSpace(result.ErrorCode) ? String.Empty : " " + result.ErrorCode)
            }));
        }

        private async Task<UsageProbeResult> RunUsageProbeAsync(IUsageProbe helper, UsageProbeHost host, CancellationToken token)
        {
            try
            {
                return await helper.CaptureAsync(host, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return UsageProbeResult.Failed("ProbeCancelled", "the usage probe was cancelled");
            }
            catch (Exception e)
            {
                // A probe that cannot run is a failed attempt with a redacted diagnostic, never an
                // unrestricted stderr channel and never the end of the link.
                return UsageProbeResult.Failed("ProbeFailed", SecretRedactor.Redact(e.Message));
            }
        }

        private static bool UsesWorktree(string? workingDirectory, string worktreePath)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory)) return false;
            try { return HarborDockSettings.IsSameOrUnder(workingDirectory, worktreePath); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private async Task HandleDockAsync(HarborDockRequest dock, CancellationToken token)
        {
            LogEntry(_LogClassifier.DockRequest(dock));

            HarborDockResult result;
            HarborJobInfo? owner = dock.Operation == HarborDockOperationEnum.Reclaim && !String.IsNullOrWhiteSpace(dock.WorktreePath)
                ? _JobState.LiveJobs().Find(job => UsesWorktree(job.WorkingDirectory, dock.WorktreePath!)) : null;
            if (owner != null)
            {
                result = new HarborDockResult
                {
                    CorrelationId = dock.CorrelationId, RequestId = dock.RequestId,
                    Message = "Cannot reclaim this worktree while Harbor job " + owner.JobId + " is running in it. Stop the job before reclaiming its dock."
                };
            }
            else if (_Docks == null)
            {
                result = new HarborDockResult
                {
                    CorrelationId = dock.CorrelationId,
                    RequestId = dock.RequestId,
                    Message = "this Harbor build does not create mission docks"
                };
            }
            else
            {
                try
                {
                    result = await _Docks.HandleAsync(dock, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            Enqueue(result);
            LogEntry(_LogClassifier.DockResult(dock, result));
        }

        private async Task HandleFileAsync(HarborFileRequest file, CancellationToken token)
        {
            HarborFileResult result;
            if (_Docks == null)
            {
                result = new HarborFileResult
                {
                    CorrelationId = file.CorrelationId,
                    RequestId = file.RequestId,
                    Message = "this Harbor build does not create mission docks"
                };
            }
            else
            {
                try
                {
                    result = await _Docks.HandleFileAsync(file, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            Enqueue(result);

            // Reads and stats are frequent and uninteresting; writes and refusals are worth a line.
            if (file.Operation == HarborFileOperationEnum.Write || file.Operation == HarborFileOperationEnum.AddGitExclude || !result.Success)
                LogEntry(_LogClassifier.FileResult(file, result));
        }

        private void HandleDeferredLaunch(HarborDeferredLaunchRequest deferred)
        {
            bool armed = !String.IsNullOrWhiteSpace(deferred.LaunchExePath) && File.Exists(deferred.LaunchExePath);
            Enqueue(new HarborDeferredLaunchAck
            {
                CorrelationId = deferred.CorrelationId,
                RequestId = deferred.RequestId,
                Armed = armed,
                Message = armed ? null : "Launch executable not found: " + deferred.LaunchExePath
            });
            LogEntry(_LogClassifier.Link(HarborLogDirection.Out, "Deferred launch " + (armed ? "armed" : "declined (launch executable not found: " + deferred.LaunchExePath + ")") + " [req " + deferred.RequestId + "]",
                armed ? HarborLogOutcomeEnum.Ok : HarborLogOutcomeEnum.Failed));

            if (!armed) return;

            // Perform the cutover on a detached task: the Admiral exits after this ack, so the health poll and
            // any rollback run without the link. The launched process self-gates on the predecessor pid.
            _ = Task.Run(async () => await RunDeferredLaunchAsync(deferred).ConfigureAwait(false));
        }

        private async Task RunDeferredLaunchAsync(HarborDeferredLaunchRequest deferred)
        {
            try
            {
                LaunchSlotProcess(deferred.LaunchExePath, deferred.WorkingDirectory, deferred.WaitForPid);

                bool healthy = await PollHealthAsync(deferred.HealthUrl, deferred.HealthTimeoutSeconds).ConfigureAwait(false);
                if (healthy)
                {
                    _Logging.Info(_Header + "deferred launch: new slot reported healthy at " + deferred.HealthUrl);
                    return;
                }

                _Logging.Warn(_Header + "deferred launch: new slot did not become healthy within " + deferred.HealthTimeoutSeconds + "s; rolling back to " + deferred.FallbackSlot);

                if (!String.IsNullOrWhiteSpace(deferred.CurrentPointerPath) && !String.IsNullOrWhiteSpace(deferred.FallbackSlot))
                {
                    try { File.WriteAllText(deferred.CurrentPointerPath, deferred.FallbackSlot); }
                    catch (Exception e) { _Logging.Warn(_Header + "deferred launch: could not rewrite pointer " + deferred.CurrentPointerPath + ": " + e.Message); }
                }

                if (!String.IsNullOrWhiteSpace(deferred.FallbackExePath) && File.Exists(deferred.FallbackExePath))
                    LaunchSlotProcess(deferred.FallbackExePath, deferred.WorkingDirectory, 0);
                else
                    _Logging.Warn(_Header + "deferred launch: fallback executable missing at " + deferred.FallbackExePath + "; cannot roll back automatically");
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "deferred launch failed: " + e.ToString());
            }
        }

        private void LaunchSlotProcess(string executablePath, string workingDirectory, int waitForPid)
        {
            string workDir = String.IsNullOrWhiteSpace(workingDirectory)
                ? (Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory)
                : workingDirectory;

            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                startInfo = new ProcessStartInfo { FileName = executablePath, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized };
            else
                startInfo = new ProcessStartInfo { FileName = executablePath, UseShellExecute = false, CreateNoWindow = true };

            startInfo.WorkingDirectory = workDir;
            if (waitForPid > 0) startInfo.Environment[Armada.Core.Constants.RestartWaitPidEnvVar] = waitForPid.ToString();

            Process? started = Process.Start(startInfo);
            _Logging.Debug(_Header + "deferred launch: started " + executablePath + (started != null ? " (pid " + started.Id + ")" : ""));
        }

        private async Task<bool> PollHealthAsync(string healthUrl, int timeoutSeconds)
        {
            if (String.IsNullOrWhiteSpace(healthUrl)) return true;

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                return await PollUntilHealthyAsync(
                    async () =>
                    {
                        try
                        {
                            HttpResponseMessage response = await client.GetAsync(healthUrl).ConfigureAwait(false);
                            return response.IsSuccessStatusCode;
                        }
                        catch
                        {
                            // Not up yet; keep polling until the timeout.
                            return false;
                        }
                    },
                    TimeSpan.FromSeconds(timeoutSeconds < 1 ? 1 : timeoutSeconds),
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Probe until the probe reports healthy or the timeout elapses. The timeout is measured on the monotonic clock
        /// of <see cref="Time"/>: a wall-clock deadline expired as soon as the host slept and woke, rolling back a
        /// healthy new slot.
        /// </summary>
        /// <param name="probe">Health probe; true when healthy.</param>
        /// <param name="timeout">How long to keep probing.</param>
        /// <param name="interval">Delay between probes.</param>
        /// <returns>True when the probe reported healthy in time.</returns>
        internal async Task<bool> PollUntilHealthyAsync(Func<Task<bool>> probe, TimeSpan timeout, TimeSpan interval)
        {
            if (probe == null) throw new ArgumentNullException(nameof(probe));

            long started = _Time.GetTimestamp();
            while (_Time.GetElapsedTime(started) < timeout)
            {
                if (await probe().ConfigureAwait(false)) return true;
                await Task.Delay(interval).ConfigureAwait(false);
            }

            return false;
        }

        private async Task HandleLaunchAsync(HarborLaunchRequest launch, CancellationToken token)
        {
            string? resolvedDirectory = _JobRunner != null
                ? _JobRunner.ResolveWorkingDirectory(launch)
                : (String.IsNullOrWhiteSpace(launch.WorkingDirectory) ? null : launch.WorkingDirectory);
            LogEntry(_LogClassifier.Launch(launch, resolvedDirectory, _Time.GetUtcNow().UtcDateTime));

            if (_JobRunner == null)
            {
                EnqueueJob(new HarborError
                {
                    CorrelationId = launch.CorrelationId,
                    JobId = launch.JobId,
                    Message = "Captain launch is not enabled on this Harbor build."
                });
                LogEntry(_LogClassifier.LaunchRefused(launch));
                return;
            }

            // Durations are measured on the monotonic clock: a wall-clock difference grows by however long the host
            // slept while the job ran.
            long started = _Time.GetTimestamp();
            long firstOutputElapsedTicks = -1;
            string jobId = launch.JobId;

            // An interactive launch that streams JSON (an Ask or chat turn) prints protocol events on stdout, which the
            // Admiral reads; Running now shows their activity instead of the raw events.
            AgentRuntimeEnum? launchRuntime = launch.RuntimeType;
            bool stdoutIsProtocol = launch.StreamJsonOutput && launchRuntime.HasValue && MissionStreamDecoder.Supports(launchRuntime.Value);

            // The job's activity (from its runtime's structured output) updates the Running now list at once and, when
            // the Admiral asked for structured progress, goes to the Admiral at most about once a second (newest wins).
            LatestValueThrottle<RuntimeActivity>? activityToAdmiral = launch.StructuredProgress
                ? new LatestValueThrottle<RuntimeActivity>(_ActivityInterval, _Time, activity => EnqueueJob(new HarborActivity { JobId = jobId, Activity = activity }))
                : null;

            // Every callback below outlives this session: they report through the shared job state and outbox, never
            // through this session's link, so a job that keeps running through a link drop still reports to the Admiral.
            HarborJobState jobs = _JobState;
            jobs.SetActivityHandler(jobId, activity =>
            {
                // Structured output counts as output for the time to first output: a captain that streams its work has
                // started answering even though it prints nothing readable until it finishes.
                System.Threading.Interlocked.CompareExchange(ref firstOutputElapsedTicks, _Time.GetElapsedTime(started).Ticks, -1);
                jobs.UpdateLiveJob(jobId, job => job.SetActivity(activity));
                activityToAdmiral?.Submit(activity);
            });

            try
            {
                await _JobRunner.StartAsync(
                    launch,
                    McpBaseUrl,
                    processId =>
                    {
                        AddLiveJob(jobs, launch);
                        jobs.Outbox.Add(new HarborStarted { CorrelationId = launch.CorrelationId, JobId = launch.JobId, ProcessId = processId });
                        LogEntry(_LogClassifier.Started(launch, processId));
                    },
                    (stream, data) =>
                    {
                        // Record the time to first output once (time-to-first-token proxy).
                        System.Threading.Interlocked.CompareExchange(ref firstOutputElapsedTicks, _Time.GetElapsedTime(started).Ticks, -1);
                        bool readable = stream == HarborOutputStreamEnum.Stderr || (stream == HarborOutputStreamEnum.Stdout && !stdoutIsProtocol);
                        if (readable) jobs.UpdateLiveJob(jobId, job => job.AddRecentLine(data));
                        jobs.Outbox.Add(new HarborOutput { JobId = launch.JobId, Stream = stream, Data = data });
                    },
                    exitCode =>
                    {
                        EndActivity(jobs, jobId, activityToAdmiral);
                        long durationMs = (long)_Time.GetElapsedTime(started).TotalMilliseconds;
                        long firstOutput = System.Threading.Interlocked.Read(ref firstOutputElapsedTicks);
                        long? ttftMs = firstOutput < 0
                            ? (long?)null
                            : (long)TimeSpan.FromTicks(firstOutput).TotalMilliseconds;
                        // Queue the exit before the job leaves the live set, so a heartbeat that no longer lists the job
                        // is built after its exit is queued.
                        jobs.Outbox.Add(new HarborExited { JobId = launch.JobId, ExitCode = exitCode, DurationMs = durationMs, TimeToFirstTokenMs = ttftMs });
                        jobs.RemoveLiveJob(launch.JobId);
                        LogEntry(_LogClassifier.Exited(launch, exitCode, durationMs, ttftMs));
                    },
                    token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                EndActivity(jobs, jobId, activityToAdmiral);
                jobs.RemoveLiveJob(launch.JobId);
                jobs.Outbox.Add(new HarborError { CorrelationId = launch.CorrelationId, JobId = launch.JobId, Message = e.Message });
                LogEntry(_LogClassifier.LaunchFailed(launch, e.Message));
            }
        }

        private static void EndActivity(HarborJobState jobs, string jobId, LatestValueThrottle<RuntimeActivity>? activityToAdmiral)
        {
            jobs.RemoveActivityHandler(jobId);
            activityToAdmiral?.Dispose();
        }

        private void StopUnownedJobs(List<string> ownedJobIds)
        {
            if (_JobRunner == null) return;
            HashSet<string> owned = new HashSet<string>(ownedJobIds, StringComparer.Ordinal);
            foreach (string jobId in _HandshakeJobIds)
            {
                if (owned.Contains(jobId) || !_JobState.IsLive(jobId)) continue;

                // The Admiral gave up on this job while the link was down (its mission was recovered elsewhere, or the
                // Admiral restarted): stop it, so a second agent does not work in the same dock.
                _Logging.Warn(_Header + "the Admiral no longer owns job " + jobId + "; stopping it");
                LogEntry(_LogClassifier.Link(HarborLogDirection.In, "The Admiral no longer owns job " + jobId + "; stopping it", HarborLogOutcomeEnum.Failed));
                IHarborJobRunner runner = _JobRunner;
                string target = jobId;
                _ = Task.Run(async () =>
                {
                    try { await runner.StopAsync(target, 10000, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception e) { _Logging.Warn(_Header + "stop of unowned job " + target + " failed: " + e.Message); }
                });
            }
        }

        private async Task SendPumpAsync(IHarborTransport transport, Channel<HarborMessage> outbound, SemaphoreSlim wake, CancellationToken token)
        {
            try
            {
                while (true)
                {
                    bool sent = false;

                    // Link messages first (the handshake is always the first), then one job message, in turn, so heavy
                    // output cannot hold back heartbeats and command results.
                    if (outbound.Reader.TryRead(out HarborMessage? message))
                    {
                        await SendMessageAsync(transport, message, token).ConfigureAwait(false);
                        if (message is HarborHandshake) _HandshakeSent = true;
                        sent = true;
                    }

                    if (_HandshakeSent && _JobState.Outbox.TryPeekNext(out HarborMessage? job) && job != null)
                    {
                        await SendMessageAsync(transport, job, token).ConfigureAwait(false);
                        _JobState.Outbox.MarkSent(job);
                        sent = true;
                    }

                    if (sent) continue;
                    if (outbound.Reader.Completion.IsCompleted) break;
                    await wake.WaitAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "send pump stopped: " + e.ToString());
            }
        }

        private async Task SendMessageAsync(IHarborTransport transport, HarborMessage message, CancellationToken token)
        {
            string serialized = HarborProtocol.Serialize(message);

            // Time a heartbeat from when it goes to the socket, so output queued ahead of it does not count as link
            // latency.
            if (message is HarborHeartbeat heartbeat && heartbeat.Sequence.HasValue)
                RecordHeartbeatSent(heartbeat.Sequence.Value);

            await transport.SendAsync(serialized, token).ConfigureAwait(false);
        }

        private static void Wake(SemaphoreSlim? wake)
        {
            if (wake == null) return;
            try
            {
                if (wake.CurrentCount == 0) wake.Release();
            }
            catch (SemaphoreFullException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_HeartbeatIntervalMs, token).ConfigureAwait(false);
                    if (IsLinkDead())
                    {
                        _Logging.Warn(_Header + "no heartbeat acknowledgement from the Admiral for " + DeadLinkTimeoutMs() + " ms; treating the link as dead");
                        LogEntry(_LogClassifier.Link(HarborLogDirection.Info, "No answer from the Admiral for " + (DeadLinkTimeoutMs() / 1000) + "s; reconnecting", HarborLogOutcomeEnum.Failed));
                        try { _SessionLink?.Cancel(); }
                        catch (ObjectDisposedException) { }
                        break;
                    }

                    Enqueue(BuildHeartbeat());
                    LogEntry(_LogClassifier.Heartbeat());
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "heartbeat failed: " + e.ToString());
                    break;
                }
            }
        }

        /// <summary>
        /// The next heartbeat of this session: the live jobs, a new sequence number for the Admiral to acknowledge, the
        /// round trip of the last acknowledged heartbeat, and the reconnect counters.
        /// </summary>
        /// <returns>The heartbeat.</returns>
        internal HarborHeartbeat BuildHeartbeat()
        {
            long sequence;
            long? lastRoundTrip;
            lock (_HeartbeatLock)
            {
                _HeartbeatSequence++;
                sequence = _HeartbeatSequence;
                lastRoundTrip = _LastRoundTripMs;
            }

            return new HarborHeartbeat
            {
                StorageReport = Volatile.Read(ref _StorageReport),
                LiveJobIds = _JobState.LiveJobIds(),
                Sequence = sequence,
                LastRoundTripMs = lastRoundTrip,
                ReconnectCount = _LinkStatistics.ReconnectCount,
                LastReconnectUtc = _LinkStatistics.LastReconnectUtc
            };
        }

        private async Task StorageLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    StorageHostReport report = await StorageCollector!(token).ConfigureAwait(false);
                    Volatile.Write(ref _StorageReport, report);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception) { _Logging.Warn(_Header + "storage measurement unavailable; keeping last report"); }
                try { await Task.Delay(TimeSpan.FromMinutes(5), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void ResetHeartbeatTiming()
        {
            lock (_HeartbeatLock)
            {
                _HeartbeatSequence = 0;
                _LastRoundTripMs = null;
                _HeartbeatSentTimestamps.Clear();
                _AckSeen = false;
                _LastAckTimestamp = 0;
            }
        }

        private void RecordHeartbeatSent(long sequence)
        {
            lock (_HeartbeatLock)
            {
                _HeartbeatSentTimestamps[sequence] = _Time.GetTimestamp();

                // Heartbeats the Admiral never acknowledged (an older Admiral, or a lost ack) must not pile up.
                if (_HeartbeatSentTimestamps.Count > _MaxPendingHeartbeats)
                {
                    List<long> stale = new List<long>();
                    foreach (long pending in _HeartbeatSentTimestamps.Keys)
                        if (pending <= sequence - _MaxPendingHeartbeats) stale.Add(pending);
                    foreach (long pending in stale) _HeartbeatSentTimestamps.Remove(pending);
                }
            }
        }

        private void RecordHeartbeatAck(long sequence)
        {
            lock (_HeartbeatLock)
            {
                if (!_HeartbeatSentTimestamps.TryGetValue(sequence, out long sent)) return;
                _HeartbeatSentTimestamps.Remove(sequence);
                _LastRoundTripMs = (long)Math.Round(_Time.GetElapsedTime(sent).TotalMilliseconds, MidpointRounding.AwayFromZero);
                _AckSeen = true;
                _LastAckTimestamp = _Time.GetTimestamp();
            }
        }

        private long DeadLinkTimeoutMs()
        {
            long threeBeats = (long)_HeartbeatIntervalMs * 3;
            return threeBeats > _MinimumDeadLinkTimeoutMs ? threeBeats : _MinimumDeadLinkTimeoutMs;
        }

        /// <summary>
        /// Whether the link is dead: the Admiral acknowledged a heartbeat on this session, and has acknowledged none for
        /// longer than the dead-link timeout (the larger of <see cref="MinimumDeadLinkTimeoutMs"/> and three heartbeat
        /// intervals), measured on the monotonic clock.
        /// </summary>
        /// <returns>True when the link should be dropped.</returns>
        internal bool IsLinkDead()
        {
            lock (_HeartbeatLock)
            {
                if (!_AckSeen) return false;
                return _Time.GetElapsedTime(_LastAckTimestamp).TotalMilliseconds > DeadLinkTimeoutMs();
            }
        }

        private void Enqueue(HarborMessage message)
        {
            Channel<HarborMessage>? outbound = _Outbound;
            if (outbound == null) return;
            outbound.Writer.TryWrite(message);
            Wake(_Wake);
        }

        private void EnqueueJob(HarborMessage message)
        {
            _JobState.Outbox.Add(message);
        }

        private void AddLiveJob(HarborJobState jobs, HarborLaunchRequest launch)
        {
            HarborJobInfo job = HarborJobInfo.FromLaunch(launch, _Time.GetUtcNow().UtcDateTime);
            if (_JobRunner is IHarborJobProgressSource progress) job.LogPath = progress.LogPathOf(launch.JobId);
            jobs.AddLiveJob(job);
        }

        private void LogEntry(HarborLogEntry entry)
        {
            try
            {
                _OnLog?.Invoke(entry);
            }
            catch
            {
            }
        }

        #endregion
    }
}
