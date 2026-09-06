namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Tracks the set of live Harbor links and applies the runtime state they report. Handshakes register
    /// or refresh a Harbor and are acknowledged with the advertised MCP base URL; heartbeats advance
    /// liveness and the per-Harbor live-job set; disconnects mark the Harbor offline. The manager is
    /// deliberately independent of the web-server transport, so the WebSocket handler simply feeds it typed
    /// messages and supplies a send channel.
    /// </summary>
    public class HarborConnectionManager
    {
        #region Public-Members

        /// <summary>
        /// Snapshot of the identifiers of Harbors with a live link.
        /// </summary>
        public IReadOnlyCollection<string> ConnectedHarborIds => new List<string>(_Connections.Keys);

        #endregion

        #region Private-Members

        private readonly string _Header = "[HarborConnectionManager] ";
        private readonly HarborService _Harbors;
        private readonly LoggingModule _Logging;
        private readonly string? _AdvertisedMcpBaseUrl;
        private readonly ConcurrentDictionary<string, HarborConnection> _Connections = new ConcurrentDictionary<string, HarborConnection>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<HarborGitResult>> _PendingGit = new ConcurrentDictionary<string, TaskCompletionSource<HarborGitResult>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, IHarborJobListener> _JobListeners = new ConcurrentDictionary<string, IHarborJobListener>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<int, HarborJobHandle> _JobsByProcessId = new ConcurrentDictionary<int, HarborJobHandle>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harbors">Harbor service.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="advertisedMcpBaseUrl">MCP base URL advertised to Harbors so launched captains can
        /// reach the Admiral, or null to omit it.</param>
        public HarborConnectionManager(HarborService harbors, LoggingModule logging, string? advertisedMcpBaseUrl)
        {
            _Harbors = harbors ?? throw new ArgumentNullException(nameof(harbors));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _AdvertisedMcpBaseUrl = advertisedMcpBaseUrl;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register or refresh a Harbor from its handshake and return the acknowledgement to send back. The
        /// tenant and user are resolved from the Harbor's credential by the transport and passed in.
        /// </summary>
        /// <param name="handshake">Handshake message.</param>
        /// <param name="tenantId">Owning tenant identifier.</param>
        /// <param name="userId">Owning user identifier.</param>
        /// <param name="send">Channel used to send messages to the Harbor.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The acknowledgement to send to the Harbor.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required arguments are missing.</exception>
        public async Task<HarborHandshakeAck> OnHandshakeAsync(
            HarborHandshake handshake,
            string? tenantId,
            string? userId,
            HarborSendDelegate send,
            CancellationToken token = default)
        {
            if (handshake == null) throw new ArgumentNullException(nameof(handshake));
            if (send == null) throw new ArgumentNullException(nameof(send));
            if (String.IsNullOrWhiteSpace(handshake.HarborId)) throw new ArgumentException("Handshake is missing a harbor id.");
            if (String.IsNullOrWhiteSpace(handshake.Name)) throw new ArgumentException("Handshake is missing a harbor name.");

            HarborConnection connection = new HarborConnection(handshake.HarborId, tenantId, userId, send);
            connection.SetLiveJobs(null);
            _Connections[handshake.HarborId] = connection;

            await _Harbors.UpsertFromHandshakeAsync(
                handshake.HarborId,
                tenantId,
                userId,
                handshake.Name,
                handshake.ProtocolVersion,
                handshake.OsPlatform,
                handshake.Architecture,
                handshake.MaxConcurrentJobs,
                handshake.Capabilities,
                token).ConfigureAwait(false);

            _Logging.Info(_Header + "harbor " + handshake.HarborId + " linked (" + handshake.Name + ")");
            return new HarborHandshakeAck
            {
                CorrelationId = handshake.CorrelationId,
                Accepted = true,
                McpBaseUrl = _AdvertisedMcpBaseUrl
            };
        }

        /// <summary>
        /// Apply an inbound message from a linked Harbor. Heartbeats advance liveness and the live-job set;
        /// errors are logged. Job lifecycle events (started/output/exited/gitResult) are routed to pending
        /// job handlers once the remote executor is attached; until then they are recorded and ignored.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="message">Inbound message.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnMessageAsync(string harborId, HarborMessage message, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (message == null) throw new ArgumentNullException(nameof(message));

            if (message is HarborHeartbeat heartbeat)
            {
                if (_Connections.TryGetValue(harborId, out HarborConnection? connection))
                {
                    connection.LastSeenUtc = DateTime.UtcNow;
                    connection.SetLiveJobs(heartbeat.LiveJobIds);
                }

                await _Harbors.MarkConnectionAsync(harborId, HarborConnectionStatusEnum.Connected, true, token).ConfigureAwait(false);
                return;
            }

            if (message is HarborGitResult gitResult)
            {
                if (_PendingGit.TryRemove(gitResult.RequestId, out TaskCompletionSource<HarborGitResult>? pending))
                    pending.TrySetResult(gitResult);
                return;
            }

            if (message is HarborStarted started)
            {
                if (_JobListeners.TryGetValue(started.JobId, out IHarborJobListener? startListener))
                    startListener.OnStarted(started.ProcessId);
                return;
            }

            if (message is HarborOutput output)
            {
                if (_JobListeners.TryGetValue(output.JobId, out IHarborJobListener? outputListener))
                    outputListener.OnOutput(output.Stream, output.Data);
                return;
            }

            if (message is HarborExited exited)
            {
                if (_JobListeners.TryRemove(exited.JobId, out IHarborJobListener? exitListener))
                    exitListener.OnExited(exited.ExitCode);
                return;
            }

            if (message is HarborError error)
            {
                _Logging.Warn(_Header + "harbor " + harborId + " reported error"
                    + (String.IsNullOrEmpty(error.JobId) ? "" : " (job " + error.JobId + ")") + ": " + error.Message);
                return;
            }

            // started/output/exited are consumed by the remote process executor's pending-job handlers.
            _Logging.Debug(_Header + "harbor " + harborId + " message " + message.GetType().Name);
        }

        /// <summary>
        /// Send a git/gh request to a connected Harbor and await its result. Returns null when the Harbor
        /// does not reply within the timeout.
        /// </summary>
        /// <param name="harborId">Target Harbor identifier.</param>
        /// <param name="request">Git request (its RequestId correlates the reply).</param>
        /// <param name="timeoutMs">Timeout in milliseconds; values below 1 mean wait indefinitely.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The git result, or null on timeout.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected.</exception>
        public async Task<HarborGitResult?> SendGitAsync(string harborId, HarborGitRequest request, int timeoutMs, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.RequestId)) throw new ArgumentException("Git request is missing a request id.");
            if (!_Connections.TryGetValue(harborId, out HarborConnection? connection))
                throw new InvalidOperationException("Harbor " + harborId + " is not connected.");

            TaskCompletionSource<HarborGitResult> completion = new TaskCompletionSource<HarborGitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _PendingGit[request.RequestId] = completion;

            try
            {
                await connection!.SendAsync(request, token).ConfigureAwait(false);

                if (timeoutMs < 1)
                    return await completion.Task.ConfigureAwait(false);

                Task delay = Task.Delay(timeoutMs, token);
                Task finished = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);
                if (finished == completion.Task)
                    return await completion.Task.ConfigureAwait(false);

                return null;
            }
            finally
            {
                _PendingGit.TryRemove(request.RequestId, out TaskCompletionSource<HarborGitResult>? _);
            }
        }

        /// <summary>
        /// Register a listener for a delegated job's lifecycle, then send the launch request to the Harbor.
        /// The listener receives started/output/exited as the Harbor reports them. Throws when the Harbor is
        /// not connected.
        /// </summary>
        /// <param name="harborId">Target Harbor identifier.</param>
        /// <param name="request">Launch request (its JobId keys the lifecycle).</param>
        /// <param name="listener">Listener for the job's lifecycle.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected.</exception>
        public async Task LaunchAsync(string harborId, HarborLaunchRequest request, IHarborJobListener listener, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (String.IsNullOrWhiteSpace(request.JobId)) throw new ArgumentException("Launch request is missing a job id.");
            if (!_Connections.TryGetValue(harborId, out HarborConnection? connection))
                throw new InvalidOperationException("Harbor " + harborId + " is not connected.");

            _JobListeners[request.JobId] = listener;
            try
            {
                await connection!.SendAsync(request, token).ConfigureAwait(false);
            }
            catch
            {
                _JobListeners.TryRemove(request.JobId, out IHarborJobListener? _);
                throw;
            }
        }

        /// <summary>
        /// Send a stop request for a delegated job to a connected Harbor. No-op when the Harbor is not
        /// connected (its jobs are gone with it).
        /// </summary>
        /// <param name="harborId">Target Harbor identifier.</param>
        /// <param name="jobId">Job identifier to stop.</param>
        /// <param name="gracefulTimeoutMs">Graceful window before a forced kill.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task KillJobAsync(string harborId, string jobId, int gracefulTimeoutMs, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));
            _JobListeners.TryRemove(jobId, out IHarborJobListener? _);
            if (!_Connections.TryGetValue(harborId, out HarborConnection? connection)) return;
            await connection!.SendAsync(new HarborKillRequest { JobId = jobId, GracefulTimeoutMs = gracefulTimeoutMs }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether a delegated job is currently tracked (its Harbor is expected to be running it).
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <returns>True when the job is tracked.</returns>
        public bool IsJobTracked(string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return false;
            return _JobListeners.ContainsKey(jobId);
        }

        /// <summary>
        /// Associate a reported host process id with the Harbor and job running it, so a later stop or
        /// liveness check by process id can be routed correctly.
        /// </summary>
        /// <param name="processId">Host process id reported by the Harbor.</param>
        /// <param name="harborId">Harbor running the job.</param>
        /// <param name="jobId">Job identifier.</param>
        public void RegisterProcessId(int processId, string harborId, string jobId)
        {
            if (processId <= 0) return;
            _JobsByProcessId[processId] = new HarborJobHandle(harborId, jobId);
        }

        /// <summary>
        /// Forget a host process id (its job exited).
        /// </summary>
        /// <param name="processId">Host process id.</param>
        public void UnregisterProcessId(int processId)
        {
            _JobsByProcessId.TryRemove(processId, out HarborJobHandle? _);
        }

        /// <summary>
        /// Whether a delegated job is still tracked by its reported process id.
        /// </summary>
        /// <param name="processId">Host process id.</param>
        /// <returns>True when tracked.</returns>
        public bool IsProcessTracked(int processId)
        {
            return _JobsByProcessId.ContainsKey(processId);
        }

        /// <summary>
        /// Resolve the Harbor running a delegated job by its reported process id.
        /// </summary>
        /// <param name="processId">Host process id.</param>
        /// <param name="harborId">The owning Harbor identifier when found.</param>
        /// <returns>True when the process id maps to a delegated job.</returns>
        public bool TryGetHarborForProcess(int processId, out string? harborId)
        {
            harborId = null;
            if (!_JobsByProcessId.TryGetValue(processId, out HarborJobHandle? handle)) return false;
            harborId = handle!.HarborId;
            return true;
        }

        /// <summary>
        /// Whether any Harbor currently has a live link.
        /// </summary>
        /// <returns>True when at least one Harbor is connected.</returns>
        public bool HasConnectedHarbor()
        {
            return !_Connections.IsEmpty;
        }

        /// <summary>
        /// Choose a Harbor for a launch by dock affinity, vessel preference, capability match, and load. The
        /// caller supplies the owning tenant so only that tenant's Harbors are considered (an empty tenant
        /// enumerates all Harbors as an admin). Returns a decision whose <see cref="HarborRoutingDecision.Success"/>
        /// is false when no eligible Harbor is available, in which case the caller should run locally.
        /// </summary>
        /// <param name="tenantId">Owning tenant identifier, or null/empty to enumerate all Harbors.</param>
        /// <param name="request">Routing inputs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The routing decision.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the request is null.</exception>
        public async Task<HarborRoutingDecision> SelectHarborAsync(string? tenantId, HarborRoutingRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            AuthContext auth = new AuthContext
            {
                IsAuthenticated = true,
                TenantId = tenantId,
                IsAdmin = String.IsNullOrEmpty(tenantId)
            };

            List<Harbor> candidates = await _Harbors.EnumerateAsync(auth, token).ConfigureAwait(false);
            HarborRouter router = new HarborRouter();
            return router.Select(candidates, IsConnected, InFlightJobs, request);
        }

        /// <summary>
        /// Stop a delegated job by the process id the Harbor reported. No-op when the process id is unknown.
        /// </summary>
        /// <param name="processId">Host process id.</param>
        /// <param name="gracefulTimeoutMs">Graceful window before a forced kill.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task KillByProcessIdAsync(int processId, int gracefulTimeoutMs, CancellationToken token = default)
        {
            if (!_JobsByProcessId.TryRemove(processId, out HarborJobHandle? handle)) return;
            await KillJobAsync(handle!.HarborId, handle.JobId, gracefulTimeoutMs, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record that a Harbor's link has closed. Its running jobs remain on the host; the Harbor is marked
        /// disconnected until it reconnects.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnDisconnectedAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            _Connections.TryRemove(harborId, out HarborConnection? _);
            _Logging.Info(_Header + "harbor " + harborId + " link closed");
            await _Harbors.MarkConnectionAsync(harborId, HarborConnectionStatusEnum.Disconnected, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Try to get the live connection for a Harbor.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="connection">The connection, when linked.</param>
        /// <returns>True when the Harbor has a live link.</returns>
        public bool TryGetConnection(string harborId, out HarborConnection? connection)
        {
            connection = null;
            if (String.IsNullOrWhiteSpace(harborId)) return false;
            return _Connections.TryGetValue(harborId, out connection);
        }

        /// <summary>
        /// Whether a Harbor currently has a live link.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>True when linked.</returns>
        public bool IsConnected(string harborId)
        {
            if (String.IsNullOrWhiteSpace(harborId)) return false;
            return _Connections.ContainsKey(harborId);
        }

        /// <summary>
        /// Number of jobs a linked Harbor currently reports running, or 0 when it is not linked.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>In-flight job count.</returns>
        public int InFlightJobs(string harborId)
        {
            if (!String.IsNullOrWhiteSpace(harborId) && _Connections.TryGetValue(harborId, out HarborConnection? connection))
                return connection!.InFlightJobs;
            return 0;
        }

        #endregion
    }
}
