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

        /// <summary>
        /// Recorder for the Harbor metrics (job lifecycle, link transitions, heartbeat round trips), or null to record
        /// nothing.
        /// </summary>
        public HarborMetricsRecorder? Metrics { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[HarborConnectionManager] ";
        private readonly HarborService _Harbors;
        private readonly LoggingModule _Logging;
        private readonly string? _AdvertisedMcpBaseUrl;
        private readonly ConcurrentDictionary<string, HarborConnection> _Connections = new ConcurrentDictionary<string, HarborConnection>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<HarborGitResult>> _PendingGit = new ConcurrentDictionary<string, TaskCompletionSource<HarborGitResult>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<HarborDeferredLaunchAck>> _PendingDeferredLaunch = new ConcurrentDictionary<string, TaskCompletionSource<HarborDeferredLaunchAck>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<HarborDockResult>> _PendingDock = new ConcurrentDictionary<string, TaskCompletionSource<HarborDockResult>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<HarborFileResult>> _PendingFile = new ConcurrentDictionary<string, TaskCompletionSource<HarborFileResult>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _PendingOwners = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
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

            // Register (or re-link) first: a Harbor id registered to another identity is refused before the live
            // connection is replaced, so another credential cannot take over the link.
            try
            {
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
            }
            catch (UnauthorizedAccessException ex)
            {
                _Logging.Warn(_Header + "refused harbor " + handshake.HarborId + ": " + ex.Message);
                return new HarborHandshakeAck
                {
                    CorrelationId = handshake.CorrelationId,
                    Accepted = false,
                    Reason = "Harbor id is registered to a different identity."
                };
            }

            HarborConnection connection = new HarborConnection(handshake.HarborId, tenantId, userId, send);
            connection.SetLiveJobs(null);
            connection.HostsDocks = HasAvailableCapability(handshake.Capabilities, HarborProtocol.DockCapability);
            connection.HostsCheckouts = connection.HostsDocks && HasAvailableCapability(handshake.Capabilities, HarborProtocol.CheckoutCapability);
            connection.Name = handshake.Name;
            _Connections[handshake.HarborId] = connection;

            _Logging.Info(_Header + "harbor " + handshake.HarborId + " linked (" + handshake.Name + ")");
            if (Metrics != null) await Metrics.OnLinkConnectedAsync(handshake.HarborId, token).ConfigureAwait(false);
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
                    // Acknowledge first, before any database work, so the Harbor's round-trip time measures the link.
                    if (heartbeat.Sequence.HasValue)
                    {
                        try
                        {
                            await connection!.SendAsync(new HarborHeartbeatAck { Sequence = heartbeat.Sequence.Value }, token).ConfigureAwait(false);
                        }
                        catch (Exception e) when (!(e is OperationCanceledException))
                        {
                            _Logging.Debug(_Header + "could not acknowledge heartbeat of harbor " + harborId + ": " + e.Message);
                        }
                    }

                    connection!.LastSeenUtc = DateTime.UtcNow;
                    connection.SetLiveJobs(heartbeat.LiveJobIds);
                }

                if (Metrics != null) await Metrics.OnHeartbeatAsync(harborId, heartbeat, token).ConfigureAwait(false);

                await _Harbors.MarkConnectionAsync(harborId, HarborConnectionStatusEnum.Connected, true, token).ConfigureAwait(false);
                return;
            }

            if (message is HarborGitResult gitResult)
            {
                if (_PendingGit.TryRemove(gitResult.RequestId, out TaskCompletionSource<HarborGitResult>? pending))
                    pending.TrySetResult(gitResult);
                return;
            }

            if (message is HarborDockResult dockResult)
            {
                if (_PendingDock.TryRemove(dockResult.RequestId, out TaskCompletionSource<HarborDockResult>? pendingDock))
                    pendingDock.TrySetResult(dockResult);
                return;
            }

            if (message is HarborFileResult fileResult)
            {
                if (_PendingFile.TryRemove(fileResult.RequestId, out TaskCompletionSource<HarborFileResult>? pendingFile))
                    pendingFile.TrySetResult(fileResult);
                return;
            }

            if (message is HarborDeferredLaunchAck deferredAck)
            {
                if (_PendingDeferredLaunch.TryRemove(deferredAck.RequestId, out TaskCompletionSource<HarborDeferredLaunchAck>? pendingDeferred))
                    pendingDeferred.TrySetResult(deferredAck);
                return;
            }

            if (message is HarborStarted started)
            {
                if (Metrics != null) await Metrics.OnStartedAsync(started.JobId, token).ConfigureAwait(false);
                if (_JobListeners.TryGetValue(started.JobId, out IHarborJobListener? startListener))
                    startListener.OnStarted(started.ProcessId);
                return;
            }

            if (message is HarborOutput output)
            {
                Metrics?.OnOutput(output.JobId);
                if (_JobListeners.TryGetValue(output.JobId, out IHarborJobListener? outputListener))
                    outputListener.OnOutput(output.Stream, output.Data);
                return;
            }

            if (message is HarborActivity activity)
            {
                // Structured activity is output too: it counts toward the job's time to first output.
                Metrics?.OnOutput(activity.JobId);
                if (activity.Activity != null && _JobListeners.TryGetValue(activity.JobId, out IHarborJobListener? activityListener))
                    activityListener.OnActivity(activity.Activity);
                return;
            }

            if (message is HarborExited exited)
            {
                if (Metrics != null) await Metrics.OnExitedAsync(exited, token).ConfigureAwait(false);
                if (_JobListeners.TryRemove(exited.JobId, out IHarborJobListener? exitListener))
                    exitListener.OnExited(exited.ExitCode);
                return;
            }

            if (message is HarborError error)
            {
                _Logging.Warn(_Header + "harbor " + harborId + " reported error"
                    + (String.IsNullOrEmpty(error.JobId) ? "" : " (job " + error.JobId + ")") + ": " + error.Message);

                // A job-scoped error ends the job (the Harbor could not launch it): tell its listener at once so the
                // launch fails with the Harbor's reason instead of waiting out the start timeout.
                if (!String.IsNullOrEmpty(error.JobId) && Metrics != null)
                    await Metrics.OnLaunchFailedAsync(error.JobId!, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(error.JobId) && _JobListeners.TryRemove(error.JobId!, out IHarborJobListener? failedListener))
                    failedListener.OnFailed(String.IsNullOrEmpty(error.Message) ? "The Harbor could not run the job." : error.Message);
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
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected, or its link closes before it
        /// replies (the request fails at once instead of waiting out the timeout).</exception>
        public async Task<HarborGitResult?> SendGitAsync(string harborId, HarborGitRequest request, int timeoutMs, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.RequestId)) throw new ArgumentException("Git request is missing a request id.");
            if (!_Connections.TryGetValue(harborId, out HarborConnection? connection))
                throw new InvalidOperationException("Harbor " + harborId + " is not connected.");

            TaskCompletionSource<HarborGitResult> completion = new TaskCompletionSource<HarborGitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _PendingGit[request.RequestId] = completion;
            _PendingOwners[request.RequestId] = harborId;

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
                _PendingOwners.TryRemove(request.RequestId, out string? _);
            }
        }

        /// <summary>
        /// Send a dock request (resolve a vessel's repository, create or remove a dock) to a connected Harbor and await
        /// its result. Returns null when the Harbor does not reply within the timeout.
        /// </summary>
        /// <param name="harborId">Target Harbor identifier.</param>
        /// <param name="request">Dock request (its RequestId correlates the reply).</param>
        /// <param name="timeoutMs">Timeout in milliseconds; values below 1 mean wait indefinitely.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result, or null on timeout.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected, does not create docks, or its
        /// link closes before it replies.</exception>
        public async Task<HarborDockResult?> SendDockAsync(string harborId, HarborDockRequest request, int timeoutMs, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.RequestId)) throw new ArgumentException("Dock request is missing a request id.");
            HarborConnection connection = RequireDockHost(harborId);
            return await SendAndWaitAsync(harborId, connection, request, request.RequestId, _PendingDock, timeoutMs, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a file request (stat, read, or write a file in one of the Harbor's docks, or add a git exclude entry) to a
        /// connected Harbor and await its result. Returns null when the Harbor does not reply within the timeout.
        /// </summary>
        /// <param name="harborId">Target Harbor identifier.</param>
        /// <param name="request">File request (its RequestId correlates the reply).</param>
        /// <param name="timeoutMs">Timeout in milliseconds; values below 1 mean wait indefinitely.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result, or null on timeout.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected, does not create docks, or its
        /// link closes before it replies.</exception>
        public async Task<HarborFileResult?> SendFileAsync(string harborId, HarborFileRequest request, int timeoutMs, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.RequestId)) throw new ArgumentException("File request is missing a request id.");
            HarborConnection connection = RequireDockHost(harborId);
            return await SendAndWaitAsync(harborId, connection, request, request.RequestId, _PendingFile, timeoutMs, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A Harbor as people know it, for messages: "Name (id)" while it is connected, else its identifier.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>The description.</returns>
        public string Describe(string harborId)
        {
            if (!String.IsNullOrWhiteSpace(harborId) && _Connections.TryGetValue(harborId, out HarborConnection? connection)
                && !String.IsNullOrWhiteSpace(connection!.Name) && !String.Equals(connection.Name, harborId, StringComparison.Ordinal))
                return connection.Name + " (" + harborId + ")";
            return harborId ?? String.Empty;
        }

        /// <summary>
        /// Whether a connected Harbor creates mission docks on its own host (it advertised
        /// <see cref="HarborProtocol.DockCapability"/>).
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>True when connected and hosting docks.</returns>
        public bool HostsDocks(string harborId)
        {
            return !String.IsNullOrWhiteSpace(harborId) && _Connections.TryGetValue(harborId, out HarborConnection? connection) && connection!.HostsDocks;
        }

        /// <summary>
        /// Whether a connected Harbor serves operations in vessel checkouts on its host (it advertised
        /// <see cref="HarborProtocol.CheckoutCapability"/>).
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>True when connected and serving checkout operations.</returns>
        public bool HostsCheckouts(string harborId)
        {
            return !String.IsNullOrWhiteSpace(harborId) && _Connections.TryGetValue(harborId, out HarborConnection? connection) && connection!.HostsCheckouts;
        }

        /// <summary>
        /// The registered Harbors a request of a tenant may use, by the same rules as launch routing: one owned by the
        /// tenant or a shared (unassigned) Harbor, and, when <paramref name="restrictToOwner"/> is set, only Harbors owned by
        /// <paramref name="ownerUserId"/>. Connection state is not considered.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null/empty for every Harbor.</param>
        /// <param name="restrictToOwner">Whether only the owner's Harbors count.</param>
        /// <param name="ownerUserId">Owner user identifier (with <paramref name="restrictToOwner"/>).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The candidates.</returns>
        public async Task<List<Harbor>> ListCandidatesAsync(string? tenantId, bool restrictToOwner, string? ownerUserId, CancellationToken token = default)
        {
            AuthContext adminAuth = new AuthContext { IsAuthenticated = true, IsAdmin = true };
            List<Harbor> all = await _Harbors.EnumerateAsync(adminAuth, token).ConfigureAwait(false);
            return FilterCandidates(all, tenantId, restrictToOwner, ownerUserId);
        }

        /// <summary>
        /// Send a deferred-launch instruction to a Harbor and await its acknowledgement. The Harbor arms the
        /// instruction (to launch a new slot after the Admiral exits, with health-gated rollback) and replies;
        /// the reply confirms it is safe for the Admiral to exit. Returns null when the Harbor does not reply
        /// within the timeout.
        /// </summary>
        /// <param name="harborId">Target Harbor identifier.</param>
        /// <param name="request">Deferred-launch request (its RequestId correlates the reply).</param>
        /// <param name="timeoutMs">Timeout in milliseconds; values below 1 mean wait indefinitely.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The acknowledgement, or null on timeout.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected.</exception>
        public async Task<HarborDeferredLaunchAck?> SendDeferredLaunchAsync(string harborId, HarborDeferredLaunchRequest request, int timeoutMs, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.RequestId)) throw new ArgumentException("Deferred-launch request is missing a request id.");
            if (!_Connections.TryGetValue(harborId, out HarborConnection? connection))
                throw new InvalidOperationException("Harbor " + harborId + " is not connected.");

            TaskCompletionSource<HarborDeferredLaunchAck> completion = new TaskCompletionSource<HarborDeferredLaunchAck>(TaskCreationOptions.RunContinuationsAsynchronously);
            _PendingDeferredLaunch[request.RequestId] = completion;
            _PendingOwners[request.RequestId] = harborId;

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
                _PendingDeferredLaunch.TryRemove(request.RequestId, out TaskCompletionSource<HarborDeferredLaunchAck>? _);
                _PendingOwners.TryRemove(request.RequestId, out string? _);
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

            // Record before sending, so a Harbor that starts the job at once finds the record.
            if (Metrics != null) await Metrics.OnLaunchAsync(harborId, connection!.TenantId, request, token).ConfigureAwait(false);
            try
            {
                await connection!.SendAsync(request, token).ConfigureAwait(false);
            }
            catch
            {
                _JobListeners.TryRemove(request.JobId, out IHarborJobListener? _);
                if (Metrics != null) await Metrics.OnLaunchFailedAsync(request.JobId, CancellationToken.None).ConfigureAwait(false);
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
            Metrics?.OnStopRequested(jobId);
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
        /// enumerates all Harbors as an admin); <see cref="HarborRoutingRequest.RestrictToOwner"/> further limits the
        /// candidates to one user's Harbors. Returns a decision whose <see cref="HarborRoutingDecision.Success"/> is
        /// false when no eligible Harbor is available; the caller may then run locally only when the dock is not
        /// pinned to a Harbor and no Harbor is required by policy.
        /// </summary>
        /// <param name="tenantId">Owning tenant identifier, or null/empty to enumerate all Harbors.</param>
        /// <param name="request">Routing inputs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The routing decision.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the request is null.</exception>
        public async Task<HarborRoutingDecision> SelectHarborAsync(string? tenantId, HarborRoutingRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Routing is a system operation: enumerate every Harbor, then keep those a mission of this tenant
            // may use -- one owned by the tenant, or a globally shared (unassigned) Harbor. This preserves
            // tenant isolation while letting a single shared Harbor serve a local, single-tenant deployment
            // where the mission carries a tenant but the Harbor registered without one.
            AuthContext adminAuth = new AuthContext { IsAuthenticated = true, IsAdmin = true };
            List<Harbor> all = await _Harbors.EnumerateAsync(adminAuth, token).ConfigureAwait(false);

            List<Harbor> candidates = FilterCandidates(all, tenantId, request.RestrictToOwner, request.OwnerUserId);

            HarborRouter router = new HarborRouter();
            return router.Select(candidates, IsConnected, InFlightJobs, request);
        }

        /// <summary>
        /// Whether an eligible Harbor owned by the given user is currently connected and able to serve the
        /// request. Used to enforce the "require the requesting user's Harbor" launch policy: only a Harbor
        /// whose owner matches the user counts (shared or other users' Harbors do not).
        /// </summary>
        /// <param name="userId">Owning user identifier the Harbor must belong to (null matches Harbors with no
        /// owner, e.g. a local single-user install).</param>
        /// <param name="request">Routing inputs (runtime/capabilities/capacity).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when at least one Harbor owned by the user is eligible right now.</returns>
        public async Task<bool> HasEligibleHarborForUserAsync(string? userId, HarborRoutingRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            AuthContext adminAuth = new AuthContext { IsAuthenticated = true, IsAdmin = true };
            List<Harbor> all = await _Harbors.EnumerateAsync(adminAuth, token).ConfigureAwait(false);

            List<Harbor> candidates = new List<Harbor>();
            foreach (Harbor harbor in all)
            {
                if (String.Equals(harbor.UserId, userId, StringComparison.Ordinal)) candidates.Add(harbor);
            }

            HarborRouter router = new HarborRouter();
            HarborRoutingDecision decision = router.Select(candidates, IsConnected, InFlightJobs, request);
            return decision.Success;
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
        public Task OnDisconnectedAsync(string harborId, CancellationToken token = default)
        {
            return OnDisconnectedAsync(harborId, null, token);
        }

        /// <summary>
        /// Record that one link of a Harbor has closed. When <paramref name="link"/> is given and the Harbor has since
        /// reconnected on a different link, the close is stale and the live connection is left alone. Otherwise the
        /// connection is removed, the Harbor is marked disconnected, and requests still waiting for its reply (git
        /// commands, deferred-launch acknowledgements) fail immediately instead of waiting out their timeouts. Running
        /// jobs remain on the host.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="link">Send delegate of the link that closed, or null to close whatever link is registered.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnDisconnectedAsync(string harborId, HarborSendDelegate? link, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));

            if (_Connections.TryGetValue(harborId, out HarborConnection? current) && link != null && !current!.IsSameLink(link))
            {
                _Logging.Info(_Header + "harbor " + harborId + " superseded link closed; the newer link stays connected");
                return;
            }

            bool removed;
            if (current != null && link != null)
            {
                removed = _Connections.TryRemove(new KeyValuePair<string, HarborConnection>(harborId, current));
            }
            else
            {
                removed = _Connections.TryRemove(harborId, out HarborConnection? _);
            }

            int failed = FailPendingRequests(harborId);
            _Logging.Info(_Header + "harbor " + harborId + " link closed" + (failed > 0 ? "; failed " + failed + " pending request(s)" : ""));
            await _Harbors.MarkConnectionAsync(harborId, HarborConnectionStatusEnum.Disconnected, false, token).ConfigureAwait(false);
            // Only a link that was open closes: a repeated close of a Harbor that is already gone is not another drop.
            if (removed && Metrics != null) await Metrics.OnLinkClosedAsync(harborId, token).ConfigureAwait(false);
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

        #region Private-Methods

        private static List<Harbor> FilterCandidates(List<Harbor> all, string? tenantId, bool restrictToOwner, string? ownerUserId)
        {
            List<Harbor> candidates = new List<Harbor>();
            foreach (Harbor harbor in all)
            {
                bool shared = String.IsNullOrEmpty(harbor.TenantId);
                bool sameTenant = !String.IsNullOrEmpty(tenantId) && String.Equals(harbor.TenantId, tenantId, StringComparison.Ordinal);
                if (!(String.IsNullOrEmpty(tenantId) || shared || sameTenant)) continue;
                if (restrictToOwner && !String.Equals(harbor.UserId, ownerUserId, StringComparison.Ordinal)) continue;
                candidates.Add(harbor);
            }

            return candidates;
        }

        private static bool HasAvailableCapability(List<HarborCapability>? capabilities, string name)
        {
            if (capabilities == null) return false;
            foreach (HarborCapability capability in capabilities)
                if (capability != null && capability.Available && String.Equals(capability.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private HarborConnection RequireDockHost(string harborId)
        {
            if (!_Connections.TryGetValue(harborId, out HarborConnection? connection))
                throw new InvalidOperationException("Harbor " + harborId + " is not connected.");
            if (!connection!.HostsDocks)
                throw new InvalidOperationException("Harbor " + harborId + " does not create mission docks on its host (it predates Harbor-side docks); update it.");
            return connection;
        }

        private async Task<TResult?> SendAndWaitAsync<TResult>(
            string harborId,
            HarborConnection connection,
            HarborMessage request,
            string requestId,
            ConcurrentDictionary<string, TaskCompletionSource<TResult>> pending,
            int timeoutMs,
            CancellationToken token) where TResult : class
        {
            TaskCompletionSource<TResult> completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[requestId] = completion;
            _PendingOwners[requestId] = harborId;

            try
            {
                await connection.SendAsync(request, token).ConfigureAwait(false);

                if (timeoutMs < 1)
                    return await completion.Task.WaitAsync(token).ConfigureAwait(false);

                Task delay = Task.Delay(timeoutMs, token);
                Task finished = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);
                if (finished == completion.Task)
                    return await completion.Task.ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                return null;
            }
            finally
            {
                pending.TryRemove(requestId, out TaskCompletionSource<TResult>? _);
                _PendingOwners.TryRemove(requestId, out string? _);
            }
        }

        private int FailPendingRequests(string harborId)
        {
            int failed = 0;
            foreach (KeyValuePair<string, string> owner in _PendingOwners)
            {
                if (!String.Equals(owner.Value, harborId, StringComparison.Ordinal)) continue;
                InvalidOperationException reason = new InvalidOperationException("Harbor " + harborId + " disconnected before replying to request " + owner.Key + ".");
                if (_PendingGit.TryGetValue(owner.Key, out TaskCompletionSource<HarborGitResult>? git) && git.TrySetException(reason)) failed++;
                if (_PendingDeferredLaunch.TryGetValue(owner.Key, out TaskCompletionSource<HarborDeferredLaunchAck>? deferred) && deferred.TrySetException(reason)) failed++;
                if (_PendingDock.TryGetValue(owner.Key, out TaskCompletionSource<HarborDockResult>? dock) && dock.TrySetException(reason)) failed++;
                if (_PendingFile.TryGetValue(owner.Key, out TaskCompletionSource<HarborFileResult>? file) && file.TrySetException(reason)) failed++;
            }
            return failed;
        }

        #endregion
    }
}
