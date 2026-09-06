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
