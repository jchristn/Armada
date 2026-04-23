namespace Armada.Core
{
    /// <summary>
    /// Application-wide constants.
    /// </summary>
    public static class Constants
    {
        #region Public-Members

        /// <summary>
        /// Shared ID generator instance.
        /// </summary>
        public static readonly PrettyId.IdGenerator IdGenerator = new PrettyId.IdGenerator();

        /// <summary>
        /// Product name.
        /// </summary>
        public static readonly string ProductName = "Armada";

        /// <summary>
        /// Product version.
        /// </summary>
        public static readonly string ProductVersion = "0.7.0";

        /// <summary>
        /// Default data directory.
        /// </summary>
        public static readonly string DefaultDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".armada");

        /// <summary>
        /// Default database filename.
        /// </summary>
        public static readonly string DefaultDatabaseFilename = "armada.db";

        /// <summary>
        /// Default Admiral port.
        /// </summary>
        public static readonly int DefaultAdmiralPort = 7890;

        /// <summary>
        /// Default MCP port.
        /// </summary>
        public static readonly int DefaultMcpPort = 7891;

        /// <summary>
        /// Default proxy port.
        /// </summary>
        public static readonly int DefaultProxyPort = 7893;

        /// <summary>
        /// Default remote tunnel URL.
        /// </summary>
        public static readonly string DefaultRemoteTunnelUrl = "http://proxy.armadago.ai:7893/tunnel";

        /// <summary>
        /// Default heartbeat interval in seconds.
        /// </summary>
        public static readonly int DefaultHeartbeatIntervalSeconds = 30;

        /// <summary>
        /// Default stall detection threshold in minutes.
        /// </summary>
        public static readonly int DefaultStallThresholdMinutes = 10;

        /// <summary>
        /// Default maximum number of auto-recovery attempts per captain.
        /// </summary>
        public static readonly int DefaultMaxRecoveryAttempts = 3;

        /// <summary>
        /// Default maximum log file size in bytes (10 MB).
        /// </summary>
        public static readonly long DefaultMaxLogFileSizeBytes = 10 * 1024 * 1024;

        /// <summary>
        /// Default maximum number of rotated log files to keep.
        /// </summary>
        public static readonly int DefaultMaxLogFileCount = 5;

        /// <summary>
        /// Fleet ID prefix.
        /// </summary>
        public static readonly string FleetIdPrefix = "flt_";

        /// <summary>
        /// Vessel ID prefix.
        /// </summary>
        public static readonly string VesselIdPrefix = "vsl_";

        /// <summary>
        /// Captain ID prefix.
        /// </summary>
        public static readonly string CaptainIdPrefix = "cpt_";

        /// <summary>
        /// Mission ID prefix.
        /// </summary>
        public static readonly string MissionIdPrefix = "msn_";

        /// <summary>
        /// Voyage ID prefix.
        /// </summary>
        public static readonly string VoyageIdPrefix = "vyg_";

        /// <summary>
        /// Playbook ID prefix.
        /// </summary>
        public static readonly string PlaybookIdPrefix = "pbk_";

        /// <summary>
        /// Dock ID prefix.
        /// </summary>
        public static readonly string DockIdPrefix = "dck_";

        /// <summary>
        /// Signal ID prefix.
        /// </summary>
        public static readonly string SignalIdPrefix = "sig_";

        /// <summary>
        /// Agent runtime ID prefix.
        /// </summary>
        public static readonly string AgentRuntimeIdPrefix = "art_";

        /// <summary>
        /// Default data retention period in days for completed records.
        /// </summary>
        public static readonly int DefaultDataRetentionDays = 30;

        /// <summary>
        /// Branch prefix for Armada-managed branches.
        /// </summary>
        public static readonly string BranchPrefix = "armada/";

        /// <summary>
        /// Default fleet name created automatically on first use.
        /// </summary>
        public static readonly string DefaultFleetName = "default";

        /// <summary>
        /// Default maximum captains for auto-scaling. 0 = unlimited.
        /// </summary>
        public static readonly int DefaultMaxCaptains = 5;

        /// <summary>
        /// Default idle captain timeout in seconds before auto-removal.
        /// 0 = disabled.
        /// </summary>
        public static readonly int DefaultIdleCaptainTimeoutSeconds = 0;

        /// <summary>
        /// Default remote tunnel connect timeout in seconds.
        /// </summary>
        public static readonly int DefaultRemoteConnectTimeoutSeconds = 15;

        /// <summary>
        /// Default remote tunnel heartbeat interval in seconds.
        /// </summary>
        public static readonly int DefaultRemoteHeartbeatIntervalSeconds = 30;

        /// <summary>
        /// Default base reconnect delay in seconds for the remote tunnel.
        /// </summary>
        public static readonly int DefaultRemoteReconnectBaseDelaySeconds = 5;

        /// <summary>
        /// Default maximum reconnect delay in seconds for the remote tunnel.
        /// </summary>
        public static readonly int DefaultRemoteReconnectMaxDelaySeconds = 60;

        /// <summary>
        /// Current remote tunnel protocol version.
        /// </summary>
        public static readonly string RemoteTunnelProtocolVersion = "2026-04-04";

        /// <summary>
        /// Default shared password for proxy/tunnel authentication.
        /// </summary>
        public static readonly string DefaultRemoteTunnelPassword = "armadaadmin";

        /// <summary>
        /// Default proxy handshake timeout in seconds.
        /// </summary>
        public static readonly int DefaultProxyHandshakeTimeoutSeconds = 15;

        /// <summary>
        /// Default proxy stale-instance threshold in seconds.
        /// </summary>
        public static readonly int DefaultProxyStaleAfterSeconds = 90;

        /// <summary>
        /// Default proxy tunnel request timeout in seconds.
        /// </summary>
        public static readonly int DefaultProxyRequestTimeoutSeconds = 20;

        /// <summary>
        /// Tenant ID prefix.
        /// </summary>
        public static readonly string TenantIdPrefix = "ten_";

        /// <summary>
        /// User ID prefix.
        /// </summary>
        public static readonly string UserIdPrefix = "usr_";

        /// <summary>
        /// Credential ID prefix.
        /// </summary>
        public static readonly string CredentialIdPrefix = "crd_";

        /// <summary>
        /// Header name for session tokens.
        /// </summary>
        public static readonly string SessionTokenHeader = "X-Token";

        /// <summary>
        /// Header name for authenticated Armada.Proxy browser sessions.
        /// </summary>
        public static readonly string ProxySessionTokenHeader = "X-Armada-Proxy-Session";

        /// <summary>
        /// Default tenant identifier.
        /// </summary>
        public static readonly string DefaultTenantId = "default";

        /// <summary>
        /// Default tenant name.
        /// </summary>
        public static readonly string DefaultTenantName = "Default Tenant";

        /// <summary>
        /// Default user email address.
        /// </summary>
        public static readonly string DefaultUserEmail = "admin@armada";

        /// <summary>
        /// Default user password.
        /// </summary>
        public static readonly string DefaultUserPassword = "password";

        /// <summary>
        /// Default user identifier.
        /// </summary>
        public static readonly string DefaultUserId = "default";

        /// <summary>
        /// Default credential identifier.
        /// </summary>
        public static readonly string DefaultCredentialId = "default";

        /// <summary>
        /// Default bearer token.
        /// </summary>
        public static readonly string DefaultBearerToken = "default";

        /// <summary>
        /// Session token lifetime in hours.
        /// </summary>
        public static readonly int SessionTokenLifetimeHours = 24;

        /// <summary>
        /// System tenant identifier for synthetic admin identity.
        /// </summary>
        public static readonly string SystemTenantId = "ten_system";

        /// <summary>
        /// System tenant name.
        /// </summary>
        public static readonly string SystemTenantName = "System";

        /// <summary>
        /// System user identifier for synthetic admin identity.
        /// </summary>
        public static readonly string SystemUserId = "usr_system";

        /// <summary>
        /// System user email.
        /// </summary>
        public static readonly string SystemUserEmail = "system@armada";

        #endregion
    }
}
