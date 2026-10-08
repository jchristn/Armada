namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Server-side settings for the Harbor subsystem: the link endpoint, authentication requirement,
    /// heartbeat/liveness windows (reserved in 1.0), default per-Harbor job capacity, and the MCP base URL
    /// advertised to Harbors. The Harbor link endpoint is always registered, whatever the deployment mode; these
    /// settings are safe to leave populated in Local mode.
    /// </summary>
    public class HarborServerSettings
    {
        #region Public-Members

        /// <summary>
        /// WebSocket path Harbors connect to. Defaults to "/v1.0/harbor/connect". Applied at process
        /// start; changing it requires a restart.
        /// </summary>
        public string LinkPath
        {
            get => _LinkPath;
            set => _LinkPath = String.IsNullOrWhiteSpace(value) ? "/v1.0/harbor/connect" : value.Trim();
        }

        /// <summary>
        /// Whether every Harbor link must present a credential on the upgrade. A presented credential
        /// (x-access-key, or an Authorization header) is always validated as an Armada credential (bearer token,
        /// session token, or the local API key) and decides the Harbor's tenant and user, whatever this setting.
        /// When false (the default), a Harbor without a credential is still accepted, with no owning user, but only
        /// when the Admiral listens on a loopback hostname and the Harbor connects from loopback; a Harbor connecting
        /// from another host is always refused without a credential. When true, a Harbor without a credential is
        /// refused in every case.
        /// </summary>
        public bool RequireAuth { get; set; } = false;

        /// <summary>
        /// Reserved: expected heartbeat interval from a Harbor, in seconds. Clamped to [5, 3600]; defaults to 15.
        /// Accepted and validated but not read in 1.0: the Harbor chooses its own heartbeat interval
        /// (HeartbeatIntervalMs in the Harbor app settings, where 0 disables heartbeats).
        /// </summary>
        public int HeartbeatIntervalSeconds
        {
            get => _HeartbeatIntervalSeconds;
            set => _HeartbeatIntervalSeconds = value < 5 ? 5 : (value > 3600 ? 3600 : value);
        }

        /// <summary>
        /// How long a Harbor whose link closed counts as reconnecting before its link-health timeline (Harbor metrics)
        /// counts it as down, in seconds. Clamped to [5, 7200]; defaults to 45. Not a liveness check: a Harbor is marked
        /// Disconnected when its link closes, never on a missed heartbeat, and nothing sets Degraded (a Harbor may
        /// legitimately run with heartbeats disabled). Missions on a Harbor that stops responding are recovered by
        /// stall detection.
        /// </summary>
        public int HeartbeatTimeoutSeconds
        {
            get => _HeartbeatTimeoutSeconds;
            set => _HeartbeatTimeoutSeconds = value < 5 ? 5 : (value > 7200 ? 7200 : value);
        }

        /// <summary>
        /// Default maximum concurrent jobs for a Harbor that registers through its handshake without advertising a
        /// capacity (maxConcurrentJobs omitted or not positive). An existing registration keeps its capacity in that
        /// case. Clamped to a minimum of 1; defaults to 4.
        /// </summary>
        public int DefaultMaxJobsPerHarbor
        {
            get => _DefaultMaxJobsPerHarbor;
            set => _DefaultMaxJobsPerHarbor = value < 1 ? 1 : value;
        }

        /// <summary>
        /// MCP base URL advertised to Harbors so launched captains can reach the Admiral's MCP server. When
        /// null, the Admiral advertises its own loopback URL, which is correct in Local mode. In Split mode
        /// set this to a URL the Harbor host can actually reach (for example the published container host
        /// and port).
        /// </summary>
        public string? AdvertisedMcpBaseUrl { get; set; } = null;

        #endregion

        #region Private-Members

        private string _LinkPath = "/v1.0/harbor/connect";
        private int _HeartbeatIntervalSeconds = 15;
        private int _HeartbeatTimeoutSeconds = 45;
        private int _DefaultMaxJobsPerHarbor = 4;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborServerSettings()
        {
        }

        #endregion
    }
}
