namespace Armada.Proxy.Models
{
    using Armada.Core.Models;
    using Armada.Proxy.Enums;
    using Armada.Proxy.Services;

    /// <summary>
    /// In-memory representation of an Armada instance tracked by the proxy.
    /// </summary>
    public class RemoteInstanceRecord
    {
        #region Public-Members

        /// <summary>
        /// Stable instance identifier.
        /// </summary>
        public string InstanceId { get; set; } = String.Empty;

        /// <summary>
        /// Armada release version reported during handshake.
        /// </summary>
        public string? ArmadaVersion { get; set; } = null;

        /// <summary>
        /// Tunnel protocol version reported during handshake.
        /// </summary>
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// Capability list advertised by the instance.
        /// </summary>
        public List<string> Capabilities { get; set; } = new List<string>();

        /// <summary>
        /// Remote address for the active connection, if any.
        /// </summary>
        public string? RemoteAddress { get; set; } = null;

        /// <summary>
        /// First time this instance was seen by the proxy process.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Current connection start time, if connected.
        /// </summary>
        public DateTime? ConnectedUtc { get; set; } = null;

        /// <summary>
        /// Last tunnel activity seen from the instance.
        /// </summary>
        public DateTime? LastSeenUtc { get; set; } = null;

        /// <summary>
        /// Last recorded inbound event timestamp.
        /// </summary>
        public DateTime? LastEventUtc { get; set; } = null;

        /// <summary>
        /// Last disconnect timestamp.
        /// </summary>
        public DateTime? LastDisconnectUtc { get; set; } = null;

        /// <summary>
        /// Most recent error associated with the instance.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Active session transport, if connected.
        /// </summary>
        public RemoteInstanceSession? Session { get; private set; } = null;

        /// <summary>
        /// Attach or replace the active session after a successful handshake.
        /// </summary>
        public RemoteInstanceSession? AttachSession(RemoteInstanceSession session, RemoteTunnelHandshakePayload handshake, string? remoteAddress, DateTime nowUtc)
        {
            lock (_SyncRoot)
            {
                RemoteInstanceSession? previous = Session;

                Session = session;
                InstanceId = handshake.InstanceId?.Trim() ?? InstanceId;
                ArmadaVersion = handshake.ArmadaVersion;
                ProtocolVersion = handshake.ProtocolVersion;
                Capabilities = handshake.Capabilities?.Where(capability => !String.IsNullOrWhiteSpace(capability)).Select(capability => capability.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    ?? new List<string>();
                RemoteAddress = remoteAddress;
                FirstSeenUtc = FirstSeenUtc == default ? nowUtc : FirstSeenUtc;
                ConnectedUtc = nowUtc;
                LastSeenUtc = nowUtc;
                LastError = null;
                return previous;
            }
        }

        /// <summary>
        /// Mark the instance disconnected.
        /// </summary>
        public void MarkDisconnected(DateTime nowUtc, string? error = null)
        {
            lock (_SyncRoot)
            {
                Session = null;
                LastDisconnectUtc = nowUtc;
                LastSeenUtc = nowUtc;

                if (!String.IsNullOrWhiteSpace(error))
                {
                    LastError = error;
                }
            }
        }

        /// <summary>
        /// Update the last-seen timestamp.
        /// </summary>
        public void MarkSeen(DateTime nowUtc)
        {
            lock (_SyncRoot)
            {
                LastSeenUtc = nowUtc;
            }
        }

        /// <summary>
        /// Record an inbound event for later inspection.
        /// </summary>
        public void RecordEvent(RemoteTunnelEnvelope envelope, DateTime nowUtc, int maxRecentEvents)
        {
            lock (_SyncRoot)
            {
                LastSeenUtc = nowUtc;
                LastEventUtc = nowUtc;

                _RecentEvents.Add(new RemoteInstanceEventRecord
                {
                    Method = envelope.Method ?? String.Empty,
                    CorrelationId = envelope.CorrelationId,
                    Message = envelope.Message,
                    TimestampUtc = envelope.TimestampUtc ?? nowUtc,
                    Payload = envelope.Payload
                });

                while (_RecentEvents.Count > maxRecentEvents)
                {
                    _RecentEvents.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Return a copy of recent events.
        /// </summary>
        public IReadOnlyList<RemoteInstanceEventRecord> GetRecentEvents()
        {
            lock (_SyncRoot)
            {
                return _RecentEvents
                    .Select(evt => new RemoteInstanceEventRecord
                    {
                        Method = evt.Method,
                        CorrelationId = evt.CorrelationId,
                        Message = evt.Message,
                        TimestampUtc = evt.TimestampUtc,
                        Payload = evt.Payload
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// Convert the record into a summary payload.
        /// </summary>
        public RemoteInstanceSummary ToSummary(DateTime nowUtc, int staleAfterSeconds)
        {
            lock (_SyncRoot)
            {
                RemoteInstanceStateEnum state;
                if (Session != null)
                {
                    if (LastSeenUtc.HasValue && (nowUtc - LastSeenUtc.Value).TotalSeconds > staleAfterSeconds)
                    {
                        state = RemoteInstanceStateEnum.Stale;
                    }
                    else
                    {
                        state = RemoteInstanceStateEnum.Connected;
                    }
                }
                else
                {
                    state = RemoteInstanceStateEnum.Offline;
                }

                return new RemoteInstanceSummary
                {
                    InstanceId = InstanceId,
                    State = state,
                    ArmadaVersion = ArmadaVersion,
                    ProtocolVersion = ProtocolVersion,
                    Capabilities = new List<string>(Capabilities),
                    RemoteAddress = RemoteAddress,
                    FirstSeenUtc = FirstSeenUtc,
                    ConnectedUtc = ConnectedUtc,
                    LastSeenUtc = LastSeenUtc,
                    LastEventUtc = LastEventUtc,
                    LastDisconnectUtc = LastDisconnectUtc,
                    LastError = LastError,
                    RecentEventCount = _RecentEvents.Count,
                    PendingRequestCount = Session?.PendingRequestCount ?? 0
                };
            }
        }

        #endregion

        #region Private-Members

        private readonly object _SyncRoot = new object();
        private readonly List<RemoteInstanceEventRecord> _RecentEvents = new List<RemoteInstanceEventRecord>();

        #endregion
    }
}
