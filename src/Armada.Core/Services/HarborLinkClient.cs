namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.InteropServices;
    using System.Threading.Channels;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// The Harbor-side runner brain: dials the Admiral over a transport, sends its handshake, heartbeats,
    /// and carries out delegated host operations. Git/gh requests run through the supplied
    /// <see cref="IHostCommandExecutor"/>; captain launches run through the supplied
    /// <see cref="IHarborJobRunner"/> (when present) with their lifecycle streamed back. Outbound messages
    /// are sent through a single ordered pump so streamed output cannot interleave or reorder. The protocol
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

        #endregion

        #region Private-Members

        private readonly string _Header = "[HarborLinkClient] ";
        private readonly string _HarborId;
        private readonly string _Name;
        private readonly List<HarborCapability> _Capabilities;
        private readonly int _MaxConcurrentJobs;
        private readonly IHostCommandExecutor _CommandExecutor;
        private readonly IHarborJobRunner? _JobRunner;
        private readonly HarborDockManager? _Docks;
        private readonly LoggingModule _Logging;
        private readonly int _HeartbeatIntervalMs;
        private readonly Action<HarborLogEntry>? _OnLog;
        private readonly Dictionary<string, HarborJobInfo> _LiveJobs = new Dictionary<string, HarborJobInfo>(StringComparer.Ordinal);
        private readonly object _JobLock = new object();
        private Action? _OnConnected;
        private Channel<HarborMessage>? _Outbound;
        private TimeProvider _Time = TimeProvider.System;

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
        /// <param name="jobRunner">Optional captain launcher; when null, launch requests are refused.</param>
        /// <param name="dockManager">Optional Harbor-side dock manager; when set the Harbor advertises
        /// <see cref="HarborProtocol.DockCapability"/> and creates mission docks on this host. When null, dock and file
        /// requests are refused.</param>
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
            HarborDockManager? dockManager = null)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            _HarborId = harborId;
            _Name = name;
            _Capabilities = new List<HarborCapability>(capabilities ?? new List<HarborCapability>());
            _Docks = dockManager;
            if (_Docks != null && !_Capabilities.Exists(c => String.Equals(c.Name, HarborProtocol.DockCapability, StringComparison.OrdinalIgnoreCase)))
                _Capabilities.Add(new HarborCapability { Name = HarborProtocol.DockCapability, Available = true });
            _MaxConcurrentJobs = maxConcurrentJobs < 1 ? 1 : maxConcurrentJobs;
            _CommandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _HeartbeatIntervalMs = heartbeatIntervalMs < 0 ? 0 : heartbeatIntervalMs;
            _OnLog = onLog;
            _JobRunner = jobRunner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Identifiers of the jobs this Harbor is running now.
        /// </summary>
        /// <returns>Snapshot of the live job identifiers.</returns>
        public List<string> LiveJobIds()
        {
            return SnapshotLiveJobs();
        }

        /// <summary>
        /// The jobs this Harbor is running now (what each is, its runtime, and when it started), oldest first.
        /// </summary>
        /// <returns>Copies of the live jobs.</returns>
        public List<HarborJobInfo> LiveJobs()
        {
            List<HarborJobInfo> jobs = new List<HarborJobInfo>();
            lock (_JobLock)
            {
                foreach (HarborJobInfo job in _LiveJobs.Values) jobs.Add(job.Clone());
            }

            jobs.Sort((a, b) => a.StartedUtc.CompareTo(b.StartedUtc));
            return jobs;
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
            Channel<HarborMessage> outbound = Channel.CreateUnbounded<HarborMessage>();
            _Outbound = outbound;

            await transport.ConnectAsync(token).ConfigureAwait(false);

            using (CancellationTokenSource sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task pump = SendPumpAsync(transport, outbound, sessionCts.Token);
                Task heartbeat = _HeartbeatIntervalMs > 0 ? HeartbeatLoopAsync(sessionCts.Token) : Task.CompletedTask;

                Enqueue(BuildHandshake());
                _Logging.Debug(_Header + "harbor " + _HarborId + " sent handshake");
                Log(HarborLogDirection.Out, "Handshake sent (harbor " + _HarborId + ")");

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        string? raw = await transport.ReceiveAsync(token).ConfigureAwait(false);
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
                    outbound.Writer.TryComplete();
                    try { await pump.ConfigureAwait(false); } catch { }
                    sessionCts.Cancel();
                    try { await heartbeat.ConfigureAwait(false); } catch { }
                    await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    _Outbound = null;
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
                Capabilities = _Capabilities
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
                    Log(HarborLogDirection.In, "Handshake rejected: " + (ack.Reason ?? "unspecified"));
                }
                else
                {
                    _Logging.Info(_Header + "handshake accepted; mcp=" + (ack.McpBaseUrl ?? "(none)"));
                    Log(HarborLogDirection.In, "Handshake accepted by Admiral. MCP=" + (ack.McpBaseUrl ?? "(none)"));
                    _OnConnected?.Invoke();
                }
                return;
            }

            if (message is HarborGitRequest git)
            {
                Log(HarborLogDirection.In, "Work: " + git.Executable + " " + String.Join(" ", git.Arguments)
                    + (String.IsNullOrEmpty(git.WorkingDirectory) ? " (in the Harbor's current directory)" : " (in " + git.WorkingDirectory + ")")
                    + " [req " + git.RequestId + "]");

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

                Log(HarborLogDirection.Out, "Result: exit " + result.ExitCode + (result.TimedOut ? " (timed out)" : "") + " [req " + git.RequestId + "]");
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
                    Log(HarborLogDirection.In, "Stop job " + kill.JobId);
                    try
                    {
                        await _JobRunner.StopAsync(kill.JobId, kill.GracefulTimeoutMs, token).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        _Logging.Warn(_Header + "stop of job " + kill.JobId + " failed: " + e.ToString());
                    }
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

        private async Task HandleDockAsync(HarborDockRequest dock, CancellationToken token)
        {
            Log(HarborLogDirection.In, "Dock " + dock.Operation + " for vessel " + dock.VesselName
                + (String.IsNullOrEmpty(dock.BranchName) ? "" : " branch " + dock.BranchName)
                + (String.IsNullOrEmpty(dock.WorktreePath) ? "" : " at " + dock.WorktreePath)
                + " [req " + dock.RequestId + "]");

            HarborDockResult result;
            if (_Docks == null)
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
            Log(HarborLogDirection.Out, DescribeDockResult(dock, result) + " [req " + dock.RequestId + "]");
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
                Log(HarborLogDirection.In, "File " + file.Operation + " " + file.Path + (result.Success ? "" : " failed: " + result.Message) + " [req " + file.RequestId + "]");
        }

        private static string DescribeDockResult(HarborDockRequest request, HarborDockResult result)
        {
            if (!result.Success) return "Dock " + request.Operation + " failed: " + (result.Message ?? "no reason given");
            switch (request.Operation)
            {
                case HarborDockOperationEnum.Provision:
                    return "Dock ready at " + result.WorktreePath + " (" + DescribeSource(result) + ")";
                case HarborDockOperationEnum.Reclaim:
                    return "Dock removed at " + result.WorktreePath;
                default:
                    return "Vessel " + request.VesselName + " served from " + DescribeSource(result);
            }
        }

        private static string DescribeSource(HarborDockResult result)
        {
            switch (result.Source)
            {
                case HarborRepositorySourceEnum.Mapped:
                    return "mapped checkout " + result.CheckoutPath;
                case HarborRepositorySourceEnum.Discovered:
                    return "discovered checkout " + result.CheckoutPath;
                case HarborRepositorySourceEnum.Clone:
                    return "Harbor clone " + result.RepositoryPath;
                default:
                    return "no repository";
            }
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
            Log(HarborLogDirection.Out, "Deferred launch " + (armed ? "armed" : "declined (launch executable not found: " + deferred.LaunchExePath + ")") + " [req " + deferred.RequestId + "]");

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
            Log(HarborLogDirection.In, "Launch job " + launch.JobId + " (runtime " + launch.Runtime + ") " + DescribeLaunchDirectory(launch));

            if (_JobRunner == null)
            {
                Enqueue(new HarborError
                {
                    CorrelationId = launch.CorrelationId,
                    JobId = launch.JobId,
                    Message = "Captain launch is not enabled on this Harbor build."
                });
                Log(HarborLogDirection.Out, "Refused launch " + launch.JobId + " (captain delegation not enabled)");
                return;
            }

            // Durations are measured on the monotonic clock: a wall-clock difference grows by however long the host
            // slept while the job ran.
            long started = _Time.GetTimestamp();
            long firstOutputElapsedTicks = -1;

            try
            {
                await _JobRunner.StartAsync(
                    launch,
                    McpBaseUrl,
                    processId =>
                    {
                        AddLiveJob(launch);
                        Enqueue(new HarborStarted { CorrelationId = launch.CorrelationId, JobId = launch.JobId, ProcessId = processId });
                        Log(HarborLogDirection.Out, "Started job " + launch.JobId + " (pid " + processId + ")");
                    },
                    (stream, data) =>
                    {
                        // Record the time to first output once (time-to-first-token proxy).
                        System.Threading.Interlocked.CompareExchange(ref firstOutputElapsedTicks, _Time.GetElapsedTime(started).Ticks, -1);
                        Enqueue(new HarborOutput { JobId = launch.JobId, Stream = stream, Data = data });
                    },
                    exitCode =>
                    {
                        RemoveLiveJob(launch.JobId);
                        long durationMs = (long)_Time.GetElapsedTime(started).TotalMilliseconds;
                        long firstOutput = System.Threading.Interlocked.Read(ref firstOutputElapsedTicks);
                        long? ttftMs = firstOutput < 0
                            ? (long?)null
                            : (long)TimeSpan.FromTicks(firstOutput).TotalMilliseconds;
                        Enqueue(new HarborExited { JobId = launch.JobId, ExitCode = exitCode, DurationMs = durationMs, TimeToFirstTokenMs = ttftMs });
                        Log(HarborLogDirection.Out, "Exited job " + launch.JobId + " (code " + exitCode
                            + ", runtime " + FormatDuration(durationMs)
                            + (ttftMs.HasValue ? ", first output " + FormatDuration(ttftMs.Value) : "") + ")");
                    },
                    token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                RemoveLiveJob(launch.JobId);
                Enqueue(new HarborError { CorrelationId = launch.CorrelationId, JobId = launch.JobId, Message = e.Message });
                Log(HarborLogDirection.Out, "Launch " + launch.JobId + " failed: " + e.Message);
            }
        }

        private async Task SendPumpAsync(IHarborTransport transport, Channel<HarborMessage> outbound, CancellationToken token)
        {
            try
            {
                await foreach (HarborMessage message in outbound.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    await transport.SendAsync(HarborProtocol.Serialize(message), token).ConfigureAwait(false);
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

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_HeartbeatIntervalMs, token).ConfigureAwait(false);
                    Enqueue(new HarborHeartbeat { LiveJobIds = SnapshotLiveJobs() });
                    LogEntry(new HarborLogEntry(HarborLogDirection.Out, "Heartbeat") { IsHeartbeat = true });
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

        private void Enqueue(HarborMessage message)
        {
            _Outbound?.Writer.TryWrite(message);
        }

        private static string FormatDuration(long milliseconds)
        {
            if (milliseconds < 1000) return milliseconds + "ms";
            double seconds = milliseconds / 1000.0;
            if (seconds < 60) return seconds.ToString("0.0") + "s";
            long totalSeconds = milliseconds / 1000;
            long minutes = totalSeconds / 60;
            long remainderSeconds = totalSeconds % 60;
            return minutes + "m" + remainderSeconds + "s";
        }

        private void AddLiveJob(HarborLaunchRequest launch)
        {
            HarborJobInfo job = HarborJobInfo.FromLaunch(launch, _Time.GetUtcNow().UtcDateTime);
            lock (_JobLock) { _LiveJobs[launch.JobId] = job; }
        }

        private void RemoveLiveJob(string jobId)
        {
            lock (_JobLock) { _LiveJobs.Remove(jobId); }
        }

        private List<string> SnapshotLiveJobs()
        {
            lock (_JobLock) { return new List<string>(_LiveJobs.Keys); }
        }

        /// <summary>
        /// Describe where a launch runs on this host, for the activity log: the requested working directory, the per-job
        /// scratch directory the job runner creates (naming the requested path when it does not exist here), or that the
        /// request names no working directory.
        /// </summary>
        private string DescribeLaunchDirectory(HarborLaunchRequest launch)
        {
            string requested = launch.WorkingDirectory ?? String.Empty;
            string? resolved = _JobRunner != null ? _JobRunner.ResolveWorkingDirectory(launch) : (String.IsNullOrWhiteSpace(requested) ? null : requested);
            if (String.IsNullOrWhiteSpace(resolved))
                return "with no working directory";
            if (String.Equals(resolved, requested, StringComparison.Ordinal))
                return "in " + resolved;
            if (String.IsNullOrWhiteSpace(requested))
                return "in scratch directory " + resolved;
            return "in scratch directory " + resolved + " (requested " + requested + " does not exist on this host)";
        }

        private void Log(HarborLogDirection direction, string message)
        {
            LogEntry(new HarborLogEntry(direction, message));
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
