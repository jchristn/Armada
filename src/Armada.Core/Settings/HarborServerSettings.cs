namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Server-side settings for the Harbor subsystem: the link endpoint, authentication requirement,
    /// heartbeat/liveness windows, default per-Harbor job capacity, and the MCP base URL advertised to
    /// Harbors. Only relevant when the deployment mode is Split, but safe to leave populated in Local mode.
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
        /// Whether a Harbor link must present a credential on the upgrade. Defaults to false so a local,
        /// loopback Harbor connects with no setup; enable it for any non-loopback or multi-tenant deployment.
        /// Full signed-request credential validation is a follow-up; when enabled today the endpoint requires
        /// the presence of an access-key header.
        /// </summary>
        public bool RequireAuth { get; set; } = false;

        /// <summary>
        /// Expected heartbeat interval from a Harbor, in seconds. Clamped to [5, 3600]; defaults to 15.
        /// </summary>
        public int HeartbeatIntervalSeconds
        {
            get => _HeartbeatIntervalSeconds;
            set => _HeartbeatIntervalSeconds = value < 5 ? 5 : (value > 3600 ? 3600 : value);
        }

        /// <summary>
        /// How long without a heartbeat before a Harbor is marked Degraded then Disconnected, in seconds.
        /// Clamped to a minimum of the heartbeat interval; defaults to 45.
        /// </summary>
        public int HeartbeatTimeoutSeconds
        {
            get => _HeartbeatTimeoutSeconds;
            set => _HeartbeatTimeoutSeconds = value < 5 ? 5 : (value > 7200 ? 7200 : value);
        }

        /// <summary>
        /// Default maximum concurrent jobs applied to a Harbor that does not advertise its own capacity.
        /// Clamped to a minimum of 1; defaults to 4.
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
