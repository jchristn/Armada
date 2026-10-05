namespace Armada.Proxy.Models
{
    using Armada.Proxy.Enums;

    /// <summary>
    /// API-friendly summary of a connected or recently seen Armada instance.
    /// </summary>
    public class RemoteInstanceSummary
    {
        #region Public-Members

        /// <summary>
        /// Stable instance identifier.
        /// </summary>
        public string InstanceId { get; set; } = String.Empty;

        /// <summary>
        /// Current instance state.
        /// </summary>
        public RemoteInstanceStateEnum State { get; set; } = RemoteInstanceStateEnum.Offline;

        /// <summary>
        /// Armada release version reported by the instance.
        /// </summary>
        public string? ArmadaVersion { get; set; } = null;

        /// <summary>
        /// Tunnel protocol version reported by the instance.
        /// </summary>
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// Capability names advertised by the instance.
        /// </summary>
        public List<string> Capabilities { get; set; } = new List<string>();

        /// <summary>
        /// Remote address associated with the active session.
        /// </summary>
        public string? RemoteAddress { get; set; } = null;

        /// <summary>
        /// First time the instance was seen by this proxy process.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Current connection start time, if connected.
        /// </summary>
        public DateTime? ConnectedUtc { get; set; } = null;

        /// <summary>
        /// Last observed inbound activity.
        /// </summary>
        public DateTime? LastSeenUtc { get; set; } = null;

        /// <summary>
        /// Last recorded event timestamp.
        /// </summary>
        public DateTime? LastEventUtc { get; set; } = null;

        /// <summary>
        /// Last disconnect time.
        /// </summary>
        public DateTime? LastDisconnectUtc { get; set; } = null;

        /// <summary>
        /// Most recent tunnel-related error.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Number of retained recent events.
        /// </summary>
        public int RecentEventCount { get; set; } = 0;

        /// <summary>
        /// Number of currently pending routed requests.
        /// </summary>
        public int PendingRequestCount { get; set; } = 0;

        #endregion
    }
}
