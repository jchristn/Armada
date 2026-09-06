namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// The Harbor-side runner brain: dials the Admiral over a transport, sends its handshake, heartbeats,
    /// and carries out delegated host operations. Git/gh requests are executed through the supplied
    /// <see cref="IHostCommandExecutor"/>; captain-launch requests are acknowledged with an explanatory error
    /// until the process-launch executor is present. The protocol logic is transport-agnostic so it can be
    /// exercised without a live socket.
    /// </summary>
    public class HarborLinkClient
    {
        #region Public-Members

        /// <summary>
        /// MCP base URL advertised by the Admiral at the last handshake acknowledgement, or null.
        /// </summary>
        public string? McpBaseUrl { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[HarborLinkClient] ";
        private readonly string _HarborId;
        private readonly string _Name;
        private readonly List<HarborCapability> _Capabilities;
        private readonly int _MaxConcurrentJobs;
        private readonly IHostCommandExecutor _CommandExecutor;
        private readonly LoggingModule _Logging;
        private readonly int _HeartbeatIntervalMs;
        private readonly Action<HarborLogEntry>? _OnLog;
        private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1, 1);
        private Action? _OnConnected;

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
        public HarborLinkClient(
            string harborId,
            string name,
            List<HarborCapability> capabilities,
            int maxConcurrentJobs,
            IHostCommandExecutor commandExecutor,
            LoggingModule logging,
            int heartbeatIntervalMs,
            Action<HarborLogEntry>? onLog = null)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            _HarborId = harborId;
            _Name = name;
            _Capabilities = capabilities ?? new List<HarborCapability>();
            _MaxConcurrentJobs = maxConcurrentJobs < 1 ? 1 : maxConcurrentJobs;
            _CommandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _HeartbeatIntervalMs = heartbeatIntervalMs < 0 ? 0 : heartbeatIntervalMs;
            _OnLog = onLog;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one link session over the given transport: connect, handshake, then handle messages until the
        /// transport closes or cancellation is requested.
        /// </summary>
        /// <param name="transport">Transport to run over.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onConnected">Optional callback invoked once the transport is open and the handshake
        /// has been sent, so a caller can surface a "connected" state.</param>
        public async Task RunSessionAsync(IHarborTransport transport, CancellationToken token, Action? onConnected = null)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));

            _OnConnected = onConnected;
            await transport.ConnectAsync(token).ConfigureAwait(false);
            await SendAsync(transport, BuildHandshake(), token).ConfigureAwait(false);
            _Logging.Info(_Header + "harbor " + _HarborId + " sent handshake");
            Log(HarborLogDirection.Out, "Handshake sent (harbor " + _HarborId + ")");

            using (CancellationTokenSource sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task heartbeat = _HeartbeatIntervalMs > 0 ? HeartbeatLoopAsync(transport, sessionCts.Token) : Task.CompletedTask;
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
                            _Logging.Warn(_Header + "dropping malformed message: " + e.Message);
                            continue;
                        }

                        await HandleAsync(transport, message, token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    sessionCts.Cancel();
                    try { await heartbeat.ConfigureAwait(false); } catch { }
                    await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
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

        private async Task HandleAsync(IHarborTransport transport, HarborMessage message, CancellationToken token)
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
                    + (String.IsNullOrEmpty(git.WorkingDirectory) ? "" : " (in " + git.WorkingDirectory + ")")
                    + " [req " + git.RequestId + "]");

                HostCommandResult result = await _CommandExecutor.RunAsync(new HostCommandRequest
                {
                    Executable = git.Executable,
                    WorkingDirectory = git.WorkingDirectory,
                    Arguments = git.Arguments
                }, token).ConfigureAwait(false);

                await SendAsync(transport, new HarborGitResult
                {
                    CorrelationId = git.CorrelationId,
                    RequestId = git.RequestId,
                    ExitCode = result.ExitCode,
                    StandardOutput = result.StandardOutput,
                    StandardError = result.StandardError
                }, token).ConfigureAwait(false);

                Log(HarborLogDirection.Out, "Result: exit " + result.ExitCode + " [req " + git.RequestId + "]");
                return;
            }

            if (message is HarborLaunchRequest launch)
            {
                Log(HarborLogDirection.In, "Launch request for job " + launch.JobId + " (runtime " + launch.Runtime + ")");
                await SendAsync(transport, new HarborError
                {
                    CorrelationId = launch.CorrelationId,
                    JobId = launch.JobId,
                    Message = "Captain launch is not yet supported by this Harbor build."
                }, token).ConfigureAwait(false);
                Log(HarborLogDirection.Out, "Refused launch " + launch.JobId + " (captain delegation not yet enabled)");
                return;
            }

            _Logging.Debug(_Header + "ignoring message " + message.GetType().Name);
        }

        private async Task HeartbeatLoopAsync(IHarborTransport transport, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_HeartbeatIntervalMs, token).ConfigureAwait(false);
                    await SendAsync(transport, new HarborHeartbeat { LiveJobIds = new List<string>() }, token).ConfigureAwait(false);
                    Log(HarborLogDirection.Out, "Heartbeat");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "heartbeat failed: " + e.Message);
                    break;
                }
            }
        }

        private void Log(HarborLogDirection direction, string message)
        {
            try
            {
                _OnLog?.Invoke(new HarborLogEntry(direction, message));
            }
            catch
            {
            }
        }

        private async Task SendAsync(IHarborTransport transport, HarborMessage message, CancellationToken token)
        {
            string text = HarborProtocol.Serialize(message);
            await _SendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await transport.SendAsync(text, token).ConfigureAwait(false);
            }
            finally
            {
                _SendLock.Release();
            }
        }

        #endregion
    }
}
