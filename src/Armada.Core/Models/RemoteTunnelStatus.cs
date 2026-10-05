namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Live status for Armada's outbound remote-control tunnel.
    /// </summary>
    public class RemoteTunnelStatus
    {
        #region Public-Members

        /// <summary>
        /// Whether remote control is enabled in settings.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Current tunnel state.
        /// </summary>
        public RemoteTunnelStateEnum State { get; set; } = RemoteTunnelStateEnum.Disabled;

        /// <summary>
        /// Configured tunnel endpoint URL, if any.
        /// </summary>
        public string? TunnelUrl { get; set; } = null;

        /// <summary>
        /// Effective instance identifier sent to the proxy.
        /// </summary>
        public string? InstanceId { get; set; } = null;

        /// <summary>
        /// Timestamp of the last connection attempt.
        /// </summary>
        public DateTime? LastConnectAttemptUtc { get; set; } = null;

        /// <summary>
        /// Timestamp when the current connection was established.
        /// </summary>
        public DateTime? ConnectedUtc { get; set; } = null;

        /// <summary>
        /// Timestamp of the most recent successful heartbeat/pong.
        /// </summary>
        public DateTime? LastHeartbeatUtc { get; set; } = null;

        /// <summary>
        /// Timestamp when the last disconnect was observed.
        /// </summary>
        public DateTime? LastDisconnectUtc { get; set; } = null;

        /// <summary>
        /// Most recent connection or protocol error.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Machine-readable code for <see cref="LastError"/>: a <see cref="RemoteTunnelErrorCodes"/> value for errors the
        /// Admiral detects, or the proxy's envelope ErrorCode (for example invalid_handshake). Null when there is no error.
        /// </summary>
        public string? LastErrorCode { get; set; } = null;

        /// <summary>
        /// Number of consecutive reconnect attempts in the current cycle.
        /// </summary>
        public int ReconnectAttempts { get; set; } = 0;

        /// <summary>
        /// Most recent measured latency in milliseconds, if available.
        /// </summary>
        public long? LatencyMs { get; set; } = null;

        /// <summary>
        /// Capability manifest advertised to the proxy.
        /// </summary>
        public RemoteTunnelCapabilityManifest CapabilityManifest
        {
            get => _CapabilityManifest;
            set => _CapabilityManifest = value ?? new RemoteTunnelCapabilityManifest();
        }

        #endregion

        #region Private-Members

        private RemoteTunnelCapabilityManifest _CapabilityManifest = new RemoteTunnelCapabilityManifest();

        #endregion
    }
}
