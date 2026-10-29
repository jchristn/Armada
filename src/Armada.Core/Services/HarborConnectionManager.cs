namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Storage;
    using SyslogLogging;

    /// <summary>
    /// Tracks the set of live Harbor links and applies the runtime state they report. Handshakes register
    /// or refresh a Harbor and are acknowledged with the advertised MCP base URL; heartbeats advance
    /// liveness and the per-Harbor live-job set; disconnects mark the Harbor offline. A Harbor's jobs survive a link drop:
    /// on reconnect the handshake lists the jobs still running, the acknowledgement names the jobs the Admiral still owns
    /// (the Harbor stops the rest), and the Harbor replays the job messages it could not deliver, which are deduplicated
    /// by their outbox sequence. A job the Harbor no longer lists (in its handshake or a heartbeat) and whose exit does
    /// not arrive within <see cref="VanishedJobGrace"/> is reported to its listener as failed, so its mission is recovered
    /// at once. A periodic sweep (<see cref="SweepAsync"/>) also drops links that have gone silent for about three
    /// heartbeat intervals. The manager is deliberately independent of the web-server transport, so the WebSocket handler
    /// simply feeds it typed messages and supplies a send channel.
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

        /// <summary>
        /// UTC clock for link liveness and the vanished-job grace; defaults to <see cref="DateTime.UtcNow"/>.
        /// </summary>
        public Func<DateTime> Clock
        {
            get => _Clock;
            set => _Clock = value ?? throw new ArgumentNullException(nameof(Clock));
        }

        /// <summary>
        /// How often the manager runs <see cref="SweepAsync"/> on its own while any Harbor is linked or a vanished job is
        /// pending. <see cref="TimeSpan.Zero"/> (or less) turns the automatic sweep off; the caller then runs
        /// <see cref="SweepAsync"/> itself. Defaults to 5 seconds.
        /// </summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Lower bound of the time a link may stay silent before the sweep treats it as dead; the timeout is otherwise
        /// three of the Harbor's heartbeat intervals. Minimum 1 second; defaults to 45 seconds.
        /// </summary>
        public TimeSpan MinimumStaleLinkTimeout
        {
            get => _MinimumStaleLinkTimeout;
            set => _MinimumStaleLinkTimeout = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
        }

        /// <summary>
        /// How long after a Harbor stops listing a started job (in its handshake or a heartbeat) the manager waits for the
        /// job's exit (which the Harbor may still be replaying from its outbox) before it reports the job to its listener as
        /// failed. Minimum zero; defaults to 20 seconds.
        /// </summary>
        public TimeSpan VanishedJobGrace
        {
            get => _VanishedJobGrace;
            set => _VanishedJobGrace = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        /// <summary>Startup grace for a restored job to reconnect before normal stall recovery resumes.</summary>
        public TimeSpan RestoredJobReconnectGrace { get; set; } = TimeSpan.FromSeconds(45);

        #endregion

        /// <summary>Admin inventory sink for reports bound to an authenticated Harbor link.</summary>
        public Action<string, StorageHostReport>? StorageReportReceived { get; set; }

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
        private readonly ConcurrentDictionary<string, string> _JobOwners = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _StartedJobs = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _RestoredSince = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _VanishedSince = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, HarborJobSequenceState> _JobSequences = new ConcurrentDictionary<string, HarborJobSequenceState>(StringComparer.Ordinal);
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private TimeSpan _MinimumStaleLinkTimeout = TimeSpan.FromSeconds(45);
        private TimeSpan _VanishedJobGrace = TimeSpan.FromSeconds(20);
        private int _SweepRunning = 0;
        private int _NextProcessHandle = 1000000000;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _PendingStops = new ConcurrentDictionary<string, TaskCompletionSource<bool>>(StringComparer.Ordinal);

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
        /// <param name="closeLink">Closes the transport behind the link, used when the sweep finds the link dead; null when
        /// the transport cannot be closed from here.</param>
        /// <returns>The acknowledgement to send to the Harbor. Besides acceptance it names the jobs the Admiral still owns on
        /// the Harbor and the last job message received from it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required arguments are missing.</exception>
        /// <exception cref="ArgumentException">Thrown when the handshake has no Harbor id or name.</exception>
        public async Task<HarborHandshakeAck> OnHandshakeAsync(
            HarborHandshake handshake,
            string? tenantId,
            string? userId,
            HarborSendDelegate send,
            CancellationToken token = default,
            Action? closeLink = null)
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
            catch (CollectionLimitExceededException ex)
            {
                // Reject, never truncate: the Harbor must advertise a bounded capability list.
                _Logging.Warn(_Header + "refused harbor " + handshake.HarborId + ": " + ex.Message);
                return new HarborHandshakeAck
                {
                    CorrelationId = handshake.CorrelationId,
                    Accepted = false,
                    Reason = ex.Message
                };
            }

            DateTime now = _Clock();
            HarborConnection connection = new HarborConnection(handshake.HarborId, tenantId, userId, send);
            connection.SetLiveJobs(handshake.LiveJobIds);
            connection.HostsDocks = HasAvailableCapability(handshake.Capabilities, HarborProtocol.DockCapability);
            connection.HostsCheckouts = connection.HostsDocks && HasAvailableCapability(handshake.Capabilities, HarborProtocol.CheckoutCapability);
            connection.OpensTerminals = connection.HostsCheckouts && HasAvailableCapability(handshake.Capabilities, HarborProtocol.OpenTerminalCapability);
            connection.PromptRuntimes = ReadPromptRuntimes(handshake.Capabilities);
            connection.Name = handshake.Name;
            connection.HeartbeatIntervalMs = handshake.HeartbeatIntervalMs;
            connection.LastSeenUtc = now;
            connection.CloseLink = closeLink;

            // Requests sent over the link this handshake replaces can never be answered (the Harbor does not replay RPC
            // replies, and the old link's own disconnect is ignored once it is no longer current): fail them now so their
            // callers retry or report at once instead of waiting out their full timeout.
            if (_Connections.ContainsKey(handshake.HarborId))
            {
                int superseded = FailPendingRequests(handshake.HarborId);
                if (superseded > 0) _Logging.Info(_Header + "failed " + superseded + " pending request(s) of Harbor " + handshake.HarborId + " whose link was replaced");
            }

            _Connections[handshake.HarborId] = connection;

            // A job marked missing on the earlier link starts its grace again: the new link replays the outbox (which
            // may carry the job's exit) before its first heartbeat says what still runs.
            foreach (KeyValuePair<string, string> owner in _JobOwners)
            {
                if (String.Equals(owner.Value, handshake.HarborId, StringComparison.Ordinal))
                    _VanishedSince.TryRemove(owner.Key, out DateTime _);
            }

            long? lastJobSequence = null;
            if (!String.IsNullOrWhiteSpace(handshake.JobOutboxEpoch))
            {
                HarborJobSequenceState state = _JobSequences.GetOrAdd(handshake.HarborId, _ => new HarborJobSequenceState());
                lock (state)
                {
                    // A new epoch is a new Harbor process: its outbox counts from 1 again.
                    if (!String.Equals(state.Epoch, handshake.JobOutboxEpoch, StringComparison.Ordinal))
                    {
                        state.Epoch = handshake.JobOutboxEpoch;
                        state.LastSequence = 0;
                    }

                    lastJobSequence = state.LastSequence;
                }
            }
            else
            {
                _JobSequences.TryRemove(handshake.HarborId, out HarborJobSequenceState? _);
            }

            // Jobs started over an earlier link that the Harbor no longer runs ended while the link was down; their exit
            // may still arrive from the Harbor's outbox, so they are settled after the grace (see SweepAsync).
            if (handshake.LiveJobIds != null) TrackReportedJobs(handshake.HarborId, handshake.LiveJobIds, now);
            List<string> owned = OwnedJobIds(handshake.HarborId);

            _Logging.Info(_Header + "harbor " + handshake.HarborId + " linked (" + handshake.Name + ")"
                + (handshake.LiveJobIds != null ? "; running " + handshake.LiveJobIds.Count + " job(s), " + owned.Count + " owned by this Admiral" : ""));
            if (Metrics != null) await Metrics.OnLinkConnectedAsync(handshake.HarborId, token).ConfigureAwait(false);
            EnsureSweepLoop();
            NotifyLinked(handshake.HarborId);
            return new HarborHandshakeAck
            {
                CorrelationId = handshake.CorrelationId,
                Accepted = true,
                McpBaseUrl = _AdvertisedMcpBaseUrl,
                OwnedJobIds = owned,
                LastJobSequence = lastJobSequence
            };
        }

        /// <summary>
        /// The jobs this Admiral still tracks on a Harbor: launched there and waiting on their lifecycle (not exited, failed,
        /// stopped, or given up on).
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>Job identifiers.</returns>
        public List<string> OwnedJobIds(string harborId)
        {
            List<string> owned = new List<string>();
            if (String.IsNullOrWhiteSpace(harborId)) return owned;
            foreach (KeyValuePair<string, string> owner in _JobOwners)
            {
                if (String.Equals(owner.Value, harborId, StringComparison.Ordinal) && _JobListeners.ContainsKey(owner.Key)) owned.Add(owner.Key);
            }

            return owned;
        }

        /// <summary>
        /// Periodic upkeep, run on its own every <see cref="SweepInterval"/> while a Harbor is linked (and callable
        /// directly). A link not heard from for longer than its stale timeout (three heartbeat intervals, at least
        /// <see cref="MinimumStaleLinkTimeout"/>; links without heartbeats are never judged) is treated as closed: it is
        /// removed, its transport is closed, and waiting requests fail. A started job its Harbor stopped listing more than
        /// <see cref="VanishedJobGrace"/> ago, and whose exit never arrived, is reported to its listener as failed.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of links dropped plus jobs reported.</returns>
        public async Task<int> SweepAsync(CancellationToken token = default)
        {
            DateTime now = _Clock();
            int acted = 0;

            foreach (KeyValuePair<string, HarborConnection> pair in _Connections)
            {
                TimeSpan? staleAfter = pair.Value.StaleAfter(_MinimumStaleLinkTimeout);
                if (!staleAfter.HasValue || now - pair.Value.LastSeenUtc <= staleAfter.Value) continue;
                _Logging.Warn(_Header + "harbor " + pair.Key + " not heard from for " + (int)(now - pair.Value.LastSeenUtc).TotalSeconds
                    + "s (timeout " + (int)staleAfter.Value.TotalSeconds + "s); treating its link as closed");
                if (await DropLinkAsync(pair.Key, pair.Value, token).ConfigureAwait(false)) acted++;
            }

            acted += await ReportVanishedJobsAsync(now, token).ConfigureAwait(false);
            return acted;
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

            // Any message shows the link is alive.
            if (_Connections.TryGetValue(harborId, out HarborConnection? seen)) seen!.LastSeenUtc = _Clock();

            // A job identifier is not authority. Check the authenticated sender before lifecycle callbacks or
            // durable metrics, including replayed exits that arrive before their runtime is restored in memory.
            string? referencedJob = message switch
            {
                HarborStarted startedEvent => startedEvent.JobId,
                HarborOutput outputEvent => outputEvent.JobId,
                HarborActivity activityEvent => activityEvent.JobId,
                HarborExited exitedEvent => exitedEvent.JobId,
                HarborError errorEvent => errorEvent.JobId,
                _ => null
            };
            if (referencedJob != null)
            {
                bool ownsJob = !String.IsNullOrWhiteSpace(referencedJob)
                    && (_JobOwners.TryGetValue(referencedJob, out string? owner)
                        ? String.Equals(owner, harborId, StringComparison.Ordinal)
                        : Metrics != null && await Metrics.IsJobOwnedByHarborAsync(harborId, referencedJob, token).ConfigureAwait(false));
                if (!ownsJob)
                {
                    // Deliberately discarded obsolete/unauthorized frames may drain from this sender's outbox.
                    // A failed ownership lookup throws before acknowledgement, so a valid exit is not lost on a DB fault.
                    if (message.OutboxSequence.HasValue) AcceptJobSequence(harborId, message.OutboxSequence.Value);
                    _Logging.Warn(_Header + "ignored " + message.GetType().Name + " from Harbor " + harborId + " because it does not own the referenced job");
                    return;
                }
            }

            // Pending host operations are authorized by the requested Harbor, not by knowing a correlation ID.
            string? referencedRequest = message switch
            {
                HarborGitResult gitReply => gitReply.RequestId ?? String.Empty,
                HarborDockResult dockReply => dockReply.RequestId ?? String.Empty,
                HarborFileResult fileReply => fileReply.RequestId ?? String.Empty,
                HarborDeferredLaunchAck deferredReply => deferredReply.RequestId ?? String.Empty,
                _ => null
            };
            if (referencedRequest != null && (!_PendingOwners.TryGetValue(referencedRequest, out string? requestedHarbor)
                || !String.Equals(requestedHarbor, harborId, StringComparison.Ordinal)))
            {
                if (message.OutboxSequence.HasValue) AcceptJobSequence(harborId, message.OutboxSequence.Value);
                _Logging.Warn(_Header + "ignored " + message.GetType().Name + " from Harbor " + harborId + " because it does not own the pending request");
                return;
            }

            // A job message the Harbor replays after a reconnect may have arrived over the earlier link already.
            if (message.OutboxSequence.HasValue && IsReplayedJobSequence(harborId, message.OutboxSequence.Value))
            {
                _Logging.Debug(_Header + "harbor " + harborId + " replayed job message " + message.OutboxSequence.Value + " already received; dropped");
                return;
            }

            // The sequence is acknowledged (through the next heartbeat's LastJobSequence) only after its handler finished.
            // A handler that throws leaves it unacknowledged and ends the link, so the Harbor replays it on reconnect
            // instead of dropping it from its outbox: an exit is never lost to a database fault.
            await ApplyMessageAsync(harborId, message, token).ConfigureAwait(false);
            if (message.OutboxSequence.HasValue) AcceptJobSequence(harborId, message.OutboxSequence.Value);
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
        /// Whether a connected Harbor can open a terminal on its desktop in a vessel checkout (it advertised
        /// <see cref="HarborProtocol.OpenTerminalCapability"/>).
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>True when connected and able to open terminals.</returns>
        public bool OpensTerminals(string harborId)
        {
            return !String.IsNullOrWhiteSpace(harborId) && _Connections.TryGetValue(harborId, out HarborConnection? connection) && connection!.OpensTerminals;
        }

        /// <summary>
        /// Called (on a background task, after the handshake is acknowledged) each time a Harbor links, with its id. An
        /// exception it throws is logged and ignored.
        /// </summary>
        public Action<string>? OnLinked { get; set; } = null;

        /// <summary>
        /// Whether a connected Harbor can send a captain's permission prompts to Armada for a runtime (it advertised
        /// <see cref="HarborProtocol.CliPermissionPromptCapability"/> listing the runtime).
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="runtime">Captain runtime.</param>
        /// <returns>True when connected and able to prompt for the runtime.</returns>
        public bool CanPrompt(string harborId, AgentRuntimeEnum runtime)
        {
            return !String.IsNullOrWhiteSpace(harborId) && _Connections.TryGetValue(harborId, out HarborConnection? connection) && connection!.PromptRuntimes.Contains(runtime);
        }

        /// <summary>
        /// Whether any connected Harbor a tenant may use (one owned by the tenant, or a shared Harbor) can send permission
        /// prompts to Armada for a runtime. Used where the launch host is not chosen yet, such as an Ask turn's policy.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null/empty for every Harbor.</param>
        /// <param name="runtime">Captain runtime.</param>
        /// <returns>True when such a Harbor is connected.</returns>
        public bool AnyHarborCanPrompt(string? tenantId, AgentRuntimeEnum runtime)
        {
            foreach (HarborConnection connection in _Connections.Values)
            {
                if (!connection.PromptRuntimes.Contains(runtime)) continue;
                if (String.IsNullOrEmpty(tenantId) || String.IsNullOrEmpty(connection.TenantId) || String.Equals(connection.TenantId, tenantId, StringComparison.Ordinal)) return true;
            }

            return false;
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
            _JobOwners[request.JobId] = harborId;

            // Record before sending, so a Harbor that starts the job at once finds the record.
            if (Metrics != null) await Metrics.OnLaunchAsync(harborId, connection!.TenantId, request, token).ConfigureAwait(false);
            try
            {
                await connection!.SendAsync(request, token).ConfigureAwait(false);
            }
            catch
            {
                _JobListeners.TryRemove(request.JobId, out IHarborJobListener? _);
                ForgetJob(request.JobId);
                if (Metrics != null) await Metrics.OnLaunchFailedAsync(request.JobId, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Restore a durable mission job before accepting Harbor reconnects. No launch is sent.
        /// </summary>
        /// <param name="harborId">Owning Harbor.</param>
        /// <param name="jobId">Persisted launch identifier.</param>
        /// <param name="processId">Persisted Harbor process identifier.</param>
        /// <param name="listener">Reconstructed lifecycle listener.</param>
        /// <returns>Unique Admiral process handle, remapped if a different restored job already owns this id.</returns>
        public int RestoreJob(string harborId, string jobId, int processId, IHarborJobListener listener)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));
            if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (!_JobListeners.TryAdd(jobId, listener)) throw new InvalidOperationException("Job already tracked: " + jobId);
            _JobOwners[jobId] = harborId;
            _StartedJobs[jobId] = 0;
            _RestoredSince[jobId] = _Clock();
            return RegisterProcessId(processId, harborId, jobId);
        }

        /// <summary>
        /// Stop a delegated job: the Admiral stops tracking it at once, and a connected Harbor is sent a stop request. When
        /// the Harbor is not connected the request cannot be delivered, but the job is not gone: a Harbor's jobs keep
        /// running through a link drop. The Harbor stops it when it reconnects, because the handshake acknowledgement no
        /// longer lists it among the jobs the Admiral owns (<see cref="HarborHandshakeAck.OwnedJobIds"/>).
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
            ForgetJob(jobId);
            if (Metrics != null) await Metrics.OnStopRequestedAsync(jobId, token).ConfigureAwait(false);
            if (!_Connections.TryGetValue(harborId, out HarborConnection? connection)) return;
            await connection!.SendAsync(new HarborKillRequest { JobId = jobId, GracefulTimeoutMs = gracefulTimeoutMs }, token).ConfigureAwait(false);
        }

        /// <summary>Request a stop and retain ownership until the owning Harbor confirms actual process exit.</summary>
        /// <param name="harborId">Owning Harbor.</param>
        /// <param name="jobId">Job to stop.</param>
        /// <param name="timeout">Maximum wait for exit confirmation.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task StopJobAndConfirmAsync(string harborId, string jobId, TimeSpan timeout, CancellationToken token = default)
        {
            if (!_JobOwners.TryGetValue(jobId, out string? owner) || owner != harborId)
                throw new InvalidOperationException("Cannot confirm stop: the Harbor does not own this tracked job.");
            TaskCompletionSource<bool> completion = _PendingStops.GetOrAdd(jobId,
                _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            try
            {
                if (Metrics != null) await Metrics.OnStopRequestedAsync(jobId, deadline.Token).ConfigureAwait(false);
                if (!_Connections.TryGetValue(harborId, out HarborConnection? connection))
                    throw new InvalidOperationException("Cannot confirm stop: Harbor is disconnected. Reconnect the Harbor before recovery.");
                await connection.SendAsync(new HarborKillRequest { JobId = jobId, GracefulTimeoutMs = 10000 }, deadline.Token).ConfigureAwait(false);
                await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("The owning Harbor did not confirm process exit before the stop deadline. Ownership is retained; reconnect the Harbor before recovery.");
            }
            // Caller cancellation/timeout does not cancel the job's shared exit notification. Keep it until
            // the actual exit so overlapping/repeated stop callers cannot orphan one another's wait.
        }

        /// <summary>Stop a tracked remote process handle and require an owning Harbor exit report.</summary>
        /// <param name="processId">Admiral process handle.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task StopProcessAndConfirmAsync(int processId, CancellationToken token = default)
        {
            if (!_JobsByProcessId.TryGetValue(processId, out HarborJobHandle? job))
                throw new InvalidOperationException("Remote process is no longer tracked; reconcile its lifecycle before recovery.");
            await StopJobAndConfirmAsync(job.HarborId, job.JobId, TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
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
        /// <returns>Admiral process handle; differs from the native id when another Harbor already uses it.</returns>
        public int RegisterProcessId(int processId, string harborId, string jobId)
        {
            if (processId <= 0) return processId;
            lock (_JobsByProcessId)
            {
                foreach (KeyValuePair<int, HarborJobHandle> existing in _JobsByProcessId)
                    if (existing.Value.HarborId == harborId && existing.Value.JobId == jobId) return existing.Key;
                int handle = processId;
                while (_JobsByProcessId.ContainsKey(handle))
                {
                    handle = Interlocked.Increment(ref _NextProcessHandle);
                    if (handle <= 0) throw new InvalidOperationException("Remote process handle space exhausted.");
                }
                _JobsByProcessId[handle] = new HarborJobHandle(harborId, jobId);
                return handle;
            }
        }

        /// <summary>
        /// Allocate the Admiral's process handle for a newly started Harbor job. The handle comes from a range of its own
        /// (from 1,000,000,000), so it can never equal an operating-system process id on the Admiral host or an API
        /// captain's in-process id (from 2,000,000,000), and the Admiral's process maps cannot confuse a Harbor job with a
        /// local process (L6). The Harbor's own process id is not used. A job already registered keeps its handle.
        /// </summary>
        /// <param name="harborId">Harbor running the job.</param>
        /// <param name="jobId">Job identifier.</param>
        /// <returns>The Admiral process handle.</returns>
        /// <exception cref="ArgumentNullException">Thrown when harborId or jobId is null or empty.</exception>
        public int RegisterLaunchedJob(string harborId, string jobId)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));
            lock (_JobsByProcessId)
            {
                foreach (KeyValuePair<int, HarborJobHandle> existing in _JobsByProcessId)
                    if (existing.Value.HarborId == harborId && existing.Value.JobId == jobId) return existing.Key;
                int handle;
                do
                {
                    handle = Interlocked.Increment(ref _NextProcessHandle);
                    if (handle <= 0 || handle >= 2000000000) throw new InvalidOperationException("Remote process handle space exhausted.");
                } while (_JobsByProcessId.ContainsKey(handle));
                _JobsByProcessId[handle] = new HarborJobHandle(harborId, jobId);
                return handle;
            }
        }

        /// <summary>
        /// Forget a host process id (its job exited).
        /// </summary>
        /// <param name="processId">Host process id.</param>
        public void UnregisterProcessId(int processId)
        {
            _JobsByProcessId.TryRemove(processId, out HarborJobHandle? _);
        }

        /// <summary>Whether a restored process is within its bounded wait for the Harbor's first report.</summary>
        /// <param name="processId">Persisted process identifier.</param>
        /// <returns>True only during startup reconnect grace.</returns>
        public bool IsProcessAwaitingReconnect(int processId)
        {
            return _JobsByProcessId.TryGetValue(processId, out HarborJobHandle? job)
                && _RestoredSince.TryGetValue(job.JobId, out DateTime restored)
                && _Clock() - restored < RestoredJobReconnectGrace;
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
        /// The state of a Harbor's link right now: Linked when it was heard from recently, Silent when it has been quiet
        /// for more than half of its stale-link timeout (the sweep drops it at the full timeout), NotLinked otherwise.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>The link state.</returns>
        public HarborLinkStateEnum GetLinkState(string harborId)
        {
            if (String.IsNullOrWhiteSpace(harborId)) return HarborLinkStateEnum.Unknown;
            if (!_Connections.TryGetValue(harborId, out HarborConnection? connection) || connection == null) return HarborLinkStateEnum.NotLinked;
            TimeSpan? staleAfter = connection.StaleAfter(_MinimumStaleLinkTimeout);
            if (!staleAfter.HasValue) return HarborLinkStateEnum.Linked;
            TimeSpan silence = _Clock() - connection.LastSeenUtc;
            return silence > TimeSpan.FromTicks(staleAfter.Value.Ticks / 2) ? HarborLinkStateEnum.Silent : HarborLinkStateEnum.Linked;
        }

        /// <summary>
        /// When a linked Harbor was last heard from, or null when it is not linked.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>The last time the link carried a message (UTC), or null.</returns>
        public DateTime? GetLinkLastSeenUtc(string harborId)
        {
            if (String.IsNullOrWhiteSpace(harborId)) return null;
            if (_Connections.TryGetValue(harborId, out HarborConnection? connection) && connection != null) return connection.LastSeenUtc;
            return null;
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

        private async Task ApplyMessageAsync(string harborId, HarborMessage message, CancellationToken token)
        {
            if (message is HarborHeartbeat heartbeat)
            {
                bool linked = false;
                if (_Connections.TryGetValue(harborId, out HarborConnection? connection))
                {
                    linked = true;
                    // Acknowledge first, before any database work, so the Harbor's round-trip time measures the link.
                    if (heartbeat.Sequence.HasValue)
                    {
                        try
                        {
                            await connection!.SendAsync(new HarborHeartbeatAck { Sequence = heartbeat.Sequence.Value, LastJobSequence = LastJobSequence(harborId) }, token).ConfigureAwait(false);
                        }
                        catch (Exception e) when (!(e is OperationCanceledException))
                        {
                            _Logging.Debug(_Header + "could not acknowledge heartbeat of harbor " + harborId + ": " + e.Message);
                        }
                    }

                    DateTime now = _Clock();
                    connection!.RecordHeartbeat(now);
                    connection.SetLiveJobs(heartbeat.LiveJobIds);
                    TrackReportedJobs(harborId, heartbeat.LiveJobIds ?? new List<string>(), now);
                }

                if (linked && heartbeat.StorageReport != null) StorageReportReceived?.Invoke(harborId, heartbeat.StorageReport);
                if (Metrics != null) await Metrics.OnHeartbeatAsync(harborId, heartbeat, token).ConfigureAwait(false);

                // A heartbeat that arrives on a link already dropped (the sweep found it dead) must not mark the Harbor
                // connected again; its next handshake does.
                if (linked) await _Harbors.MarkConnectionAsync(harborId, HarborConnectionStatusEnum.Connected, true, token).ConfigureAwait(false);
                await ReportVanishedJobsAsync(_Clock(), token).ConfigureAwait(false);
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
                {
                    _StartedJobs[started.JobId] = 0;
                    startListener.OnStarted(started.ProcessId);
                }
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
                ForgetJob(exited.JobId);
                if (_JobListeners.TryRemove(exited.JobId, out IHarborJobListener? exitListener))
                    exitListener.OnExited(exited.ExitCode);
                if (_PendingStops.TryRemove(exited.JobId, out TaskCompletionSource<bool>? stopped)) stopped.TrySetResult(true);
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
                if (!String.IsNullOrEmpty(error.JobId)) ForgetJob(error.JobId!);
                if (!String.IsNullOrEmpty(error.JobId) && _JobListeners.TryRemove(error.JobId!, out IHarborJobListener? failedListener))
                    failedListener.OnFailed(String.IsNullOrEmpty(error.Message) ? "The Harbor could not run the job." : error.Message);
                return;
            }

            // started/output/exited are consumed by the remote process executor's pending-job handlers.
            _Logging.Debug(_Header + "harbor " + harborId + " message " + message.GetType().Name);
        }

        private bool IsReplayedJobSequence(string harborId, long sequence)
        {
            if (!_JobSequences.TryGetValue(harborId, out HarborJobSequenceState? state)) return false;
            lock (state!) return sequence <= state.LastSequence;
        }

        private bool AcceptJobSequence(string harborId, long sequence)
        {
            if (!_JobSequences.TryGetValue(harborId, out HarborJobSequenceState? state)) return true;
            lock (state!)
            {
                if (sequence <= state.LastSequence) return false;
                state.LastSequence = sequence;
                return true;
            }
        }

        private long? LastJobSequence(string harborId)
        {
            if (!_JobSequences.TryGetValue(harborId, out HarborJobSequenceState? state)) return null;
            lock (state!) return state.LastSequence;
        }

        private void ForgetJob(string jobId)
        {
            if (String.IsNullOrEmpty(jobId)) return;
            _JobOwners.TryRemove(jobId, out string? _);
            _StartedJobs.TryRemove(jobId, out byte _);
            _VanishedSince.TryRemove(jobId, out DateTime _);
            _RestoredSince.TryRemove(jobId, out DateTime _);
        }

        private void TrackReportedJobs(string harborId, List<string> liveJobIds, DateTime now)
        {
            HashSet<string> live = new HashSet<string>(liveJobIds ?? new List<string>(), StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> owner in _JobOwners)
            {
                if (!String.Equals(owner.Value, harborId, StringComparison.Ordinal)) continue;

                // A job that has not reported its start may not be in the Harbor's live set yet; the launch's start timeout
                // covers one that never starts.
                if (!_StartedJobs.ContainsKey(owner.Key)) continue;
                if (live.Contains(owner.Key))
                {
                    _VanishedSince.TryRemove(owner.Key, out DateTime _);
                    _RestoredSince.TryRemove(owner.Key, out DateTime _);
                }
                else _VanishedSince.TryAdd(owner.Key, now);
            }
        }

        private async Task<int> ReportVanishedJobsAsync(DateTime now, CancellationToken token)
        {
            int reported = 0;
            foreach (KeyValuePair<string, DateTime> vanished in _VanishedSince)
            {
                if (now - vanished.Value < _VanishedJobGrace) continue;
                if (!_VanishedSince.TryRemove(new KeyValuePair<string, DateTime>(vanished.Key, vanished.Value))) continue;

                string jobId = vanished.Key;
                _JobOwners.TryGetValue(jobId, out string? harborId);
                ForgetJob(jobId);
                if (!_JobListeners.TryRemove(jobId, out IHarborJobListener? listener)) continue;

                _Logging.Warn(_Header + "harbor " + (harborId ?? "(unknown)") + " no longer runs job " + jobId
                    + " and its exit did not arrive within " + (int)_VanishedJobGrace.TotalSeconds + "s; reporting it as failed");
                if (Metrics != null) await Metrics.OnJobLostAsync(jobId, token).ConfigureAwait(false);
                try
                {
                    listener!.OnFailed("Harbor " + Describe(harborId ?? String.Empty) + " no longer runs job " + jobId
                        + " (it ended while the link to the Harbor was down, and its exit was not reported).");
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "listener of vanished job " + jobId + " failed: " + e.Message);
                }

                reported++;
            }

            return reported;
        }

        private async Task<bool> DropLinkAsync(string harborId, HarborConnection connection, CancellationToken token)
        {
            if (!_Connections.TryRemove(new KeyValuePair<string, HarborConnection>(harborId, connection))) return false;
            int failed = FailPendingRequests(harborId);
            _Logging.Info(_Header + "harbor " + harborId + " link dropped as dead" + (failed > 0 ? "; failed " + failed + " pending request(s)" : ""));
            try { connection.CloseLink?.Invoke(); }
            catch (Exception e) { _Logging.Debug(_Header + "closing the dead link of harbor " + harborId + " failed: " + e.Message); }
            await _Harbors.MarkConnectionAsync(harborId, HarborConnectionStatusEnum.Disconnected, false, token).ConfigureAwait(false);
            if (Metrics != null) await Metrics.OnLinkClosedAsync(harborId, token).ConfigureAwait(false);
            return true;
        }

        private void EnsureSweepLoop()
        {
            if (SweepInterval <= TimeSpan.Zero) return;
            if (Interlocked.CompareExchange(ref _SweepRunning, 1, 0) != 0) return;
            _ = Task.Run(SweepLoopAsync);
        }

        private async Task SweepLoopAsync()
        {
            while (true)
            {
                TimeSpan interval = SweepInterval;
                if (interval <= TimeSpan.Zero)
                {
                    Interlocked.Exchange(ref _SweepRunning, 0);
                    return;
                }

                await Task.Delay(interval).ConfigureAwait(false);
                try
                {
                    await SweepAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "harbor link sweep failed: " + e.Message);
                }

                // The loop runs only while there is something to watch, and starts again with the next link.
                if (_Connections.IsEmpty && _VanishedSince.IsEmpty)
                {
                    Interlocked.Exchange(ref _SweepRunning, 0);
                    if ((_Connections.IsEmpty && _VanishedSince.IsEmpty) || Interlocked.CompareExchange(ref _SweepRunning, 1, 0) != 0) return;
                }
            }
        }

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

        private void NotifyLinked(string harborId)
        {
            Action<string>? linked = OnLinked;
            if (linked == null) return;
            _ = Task.Run(async () =>
            {
                // Let the acknowledgement reach the Harbor before anything is asked of it.
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                try
                {
                    linked(harborId);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "the link handler for harbor " + harborId + " failed: " + ex.Message);
                }
            });
        }

        private static HashSet<AgentRuntimeEnum> ReadPromptRuntimes(List<HarborCapability>? capabilities)
        {
            HashSet<AgentRuntimeEnum> runtimes = new HashSet<AgentRuntimeEnum>();
            if (capabilities == null) return runtimes;
            foreach (HarborCapability capability in capabilities)
            {
                if (capability == null || !capability.Available || !String.Equals(capability.Name, HarborProtocol.CliPermissionPromptCapability, StringComparison.OrdinalIgnoreCase)) continue;
                if (String.IsNullOrWhiteSpace(capability.Detail)) continue;
                foreach (string name in capability.Detail!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Enum.TryParse<AgentRuntimeEnum>(name, true, out AgentRuntimeEnum runtime) && runtime != AgentRuntimeEnum.ApiEndpoint) runtimes.Add(runtime);
                }
            }

            return runtimes;
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
