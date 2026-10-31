namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;

    /// <summary>
    /// A live Harbor link tracked by the connection manager: its identity, the send channel back to it, and
    /// the set of jobs it currently reports running (used for load-aware routing and reconnect rebind).
    /// </summary>
    public class HarborConnection
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier.
        /// </summary>
        public string HarborId { get; }

        /// <summary>
        /// Owning tenant identifier, resolved from the Harbor's credential.
        /// </summary>
        public string? TenantId { get; }

        /// <summary>
        /// Owning user identifier, resolved from the Harbor's credential.
        /// </summary>
        public string? UserId { get; }

        /// <summary>
        /// When the Harbor was last heard from on this link (UTC): any message counts. The connection manager's sweep
        /// treats a link that has been silent for longer than <see cref="StaleAfter"/> as dead.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When this link was established by its handshake (UTC). A captain on this Harbor that was unreachable while the
        /// link was down has its stall clock reset to this time when the link returns.
        /// </summary>
        public DateTime LinkedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The heartbeat interval the Harbor advertised in its handshake, in milliseconds: null when it did not say (an
        /// older Harbor), zero when it sends no heartbeats.
        /// </summary>
        public int? HeartbeatIntervalMs { get; set; } = null;

        /// <summary>
        /// When the last heartbeat arrived on this link (UTC), or null before the first.
        /// </summary>
        public DateTime? LastHeartbeatUtc { get; private set; } = null;

        /// <summary>
        /// Time between the last two heartbeats on this link, in milliseconds, or null before the second.
        /// </summary>
        public long? ObservedHeartbeatIntervalMs { get; private set; } = null;

        /// <summary>
        /// Closes the transport behind this link (the WebSocket), or null when the transport did not supply one. Called
        /// when the connection manager drops a link it found dead, so a half-open socket does not linger.
        /// </summary>
        public Action? CloseLink { get; set; } = null;

        /// <summary>
        /// Whether this Harbor creates mission docks on its own host (it advertised
        /// <see cref="HarborProtocol.DockCapability"/> in its handshake on this link).
        /// </summary>
        public bool HostsDocks { get; set; } = false;

        /// <summary>
        /// Whether the Harbor serves operations in vessel checkouts on its host (it advertised
        /// <see cref="Armada.Core.Harbor.HarborProtocol.CheckoutCapability"/>).
        /// </summary>
        public bool HostsCheckouts { get; set; } = false;

        /// <summary>
        /// Whether the Harbor can open a terminal on its desktop in a vessel checkout (it advertised
        /// <see cref="Armada.Core.Harbor.HarborProtocol.OpenTerminalCapability"/>).
        /// </summary>
        public bool OpensTerminals { get; set; } = false;

        /// <summary>
        /// Runtimes whose permission prompts the Harbor can send to Armada (the detail of
        /// <see cref="Armada.Core.Harbor.HarborProtocol.CliPermissionPromptCapability"/> in its handshake); empty for a
        /// Harbor that predates it.
        /// </summary>
        public HashSet<Armada.Core.Enums.AgentRuntimeEnum> PromptRuntimes { get; set; } = new HashSet<Armada.Core.Enums.AgentRuntimeEnum>();

        /// <summary>
        /// The Harbor's name from its handshake on this link.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Number of jobs the Harbor currently reports running.
        /// </summary>
        public int InFlightJobs
        {
            get
            {
                lock (_JobLock) { return _LiveJobIds.Count; }
            }
        }

        #endregion

        #region Private-Members

        private readonly HarborSendDelegate _Send;
        private readonly HashSet<string> _LiveJobIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _JobLock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="tenantId">Owning tenant identifier.</param>
        /// <param name="userId">Owning user identifier.</param>
        /// <param name="send">Channel used to send messages to the Harbor.</param>
        public HarborConnection(string harborId, string? tenantId, string? userId, HarborSendDelegate send)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            HarborId = harborId;
            TenantId = tenantId;
            UserId = userId;
            _Send = send ?? throw new ArgumentNullException(nameof(send));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send a message to the Harbor.
        /// </summary>
        /// <param name="message">Message to send.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendAsync(HarborMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return _Send(message, token);
        }

        /// <summary>
        /// Whether this connection sends through the given link delegate, i.e. belongs to that socket.
        /// </summary>
        /// <param name="send">Send delegate the transport registered at handshake.</param>
        /// <returns>True when it is the same link.</returns>
        public bool IsSameLink(HarborSendDelegate? send)
        {
            return send != null && ReferenceEquals(_Send, send);
        }

        /// <summary>
        /// Replace the set of live job ids with the authoritative list reported in a heartbeat.
        /// </summary>
        /// <param name="jobIds">Live job identifiers.</param>
        public void SetLiveJobs(IEnumerable<string>? jobIds)
        {
            lock (_JobLock)
            {
                _LiveJobIds.Clear();
                if (jobIds == null) return;
                foreach (string id in jobIds)
                    if (!String.IsNullOrWhiteSpace(id)) _LiveJobIds.Add(id);
            }
        }

        /// <summary>
        /// Record a heartbeat's arrival: it refreshes <see cref="LastSeenUtc"/> and measures the interval between heartbeats.
        /// </summary>
        /// <param name="receivedUtc">When it arrived (UTC).</param>
        public void RecordHeartbeat(DateTime receivedUtc)
        {
            if (LastHeartbeatUtc.HasValue && receivedUtc > LastHeartbeatUtc.Value)
                ObservedHeartbeatIntervalMs = (long)(receivedUtc - LastHeartbeatUtc.Value).TotalMilliseconds;
            LastHeartbeatUtc = receivedUtc;
            LastSeenUtc = receivedUtc;
        }

        /// <summary>
        /// How long this link may stay silent before it counts as dead: three heartbeat intervals, and never less than
        /// <paramref name="minimum"/>. The interval is the one the Harbor advertised, else the one observed between its
        /// heartbeats. Null when no heartbeats are expected (the Harbor turned them off, or an older Harbor has not sent two
        /// yet), so a Harbor that legitimately runs without heartbeats is never judged.
        /// </summary>
        /// <param name="minimum">Lower bound of the timeout.</param>
        /// <returns>The timeout, or null.</returns>
        public TimeSpan? StaleAfter(TimeSpan minimum)
        {
            long? intervalMs = null;
            if (HeartbeatIntervalMs.HasValue)
            {
                if (HeartbeatIntervalMs.Value <= 0) return null;
                intervalMs = HeartbeatIntervalMs.Value;
            }
            else if (ObservedHeartbeatIntervalMs.HasValue && ObservedHeartbeatIntervalMs.Value > 0)
            {
                intervalMs = ObservedHeartbeatIntervalMs.Value;
            }

            if (!intervalMs.HasValue) return null;
            TimeSpan threeBeats = TimeSpan.FromMilliseconds(intervalMs.Value * 3.0);
            return threeBeats > minimum ? threeBeats : minimum;
        }

        /// <summary>
        /// Snapshot of the job identifiers the Harbor last reported running.
        /// </summary>
        /// <returns>The identifiers.</returns>
        public List<string> LiveJobIds()
        {
            lock (_JobLock) return new List<string>(_LiveJobIds);
        }

        /// <summary>
        /// Whether the Harbor currently reports the given job as running.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <returns>True when the job is live.</returns>
        public bool HasJob(string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return false;
            lock (_JobLock) { return _LiveJobIds.Contains(jobId); }
        }

        #endregion
    }
}
