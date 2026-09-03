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
        public static readonly string ProductVersion = "0.9.0";

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
        /// Environment variable set on a replacement Admiral process during an in-place restart. Its value
        /// is the process id of the outgoing Admiral; the replacement waits for that process to exit (which
        /// frees the listening port) before it binds, so a self-triggered restart cannot race the old
        /// instance for the port.
        /// </summary>
        public static readonly string RestartWaitPidEnvVar = "ARMADA_RESTART_WAIT_PID";

        /// <summary>
        /// Default heartbeat interval in seconds. Drives the Admiral health-check loop, which is the cadence
        /// on which pending missions are assigned to idle captains, stalls are detected, the merge queue is
        /// processed, and dangling handoffs are re-driven.
        /// </summary>
        public static readonly int DefaultHeartbeatIntervalSeconds = 10;

        /// <summary>
        /// Default stall detection threshold in minutes.
        /// </summary>
        public static readonly int DefaultStallThresholdMinutes = 10;

        /// <summary>
        /// Default maximum number of auto-recovery attempts per captain.
        /// </summary>
        public static readonly int DefaultMaxRecoveryAttempts = 3;

        /// <summary>
        /// Default hard ceiling, in minutes, on how long a single mission may run before it is
        /// force-failed as a runaway. A generous backstop; set to 0 to disable.
        /// </summary>
        public static readonly int DefaultMaxMissionRuntimeMinutes = 240;

        /// <summary>
        /// Default minutes a mission may sit awaiting human review before the review watchdog
        /// escalates and releases the held captain (the mission and its dock are preserved for the
        /// reviewer). 0 disables the timeout.
        /// </summary>
        public static readonly int DefaultReviewTimeoutMinutes = 1440;

        /// <summary>
        /// Default global ceiling on the number of missions (working captains) that may run
        /// simultaneously. 0 means unlimited.
        /// </summary>
        public static readonly int DefaultMaxConcurrentMissions = 0;

        /// <summary>
        /// Default maximum log file size in bytes (10 MB).
        /// </summary>
        public static readonly long DefaultMaxLogFileSizeBytes = 10 * 1024 * 1024;

        /// <summary>
        /// Default maximum number of rotated log files to keep.
        /// </summary>
        public static readonly int DefaultMaxLogFileCount = 5;

        /// <summary>
        /// Objective ID prefix.
        /// </summary>
        public static readonly string ObjectiveIdPrefix = "obj_";

        /// <summary>
        /// ID prefix for background jobs.
        /// </summary>
        public static readonly string JobIdPrefix = "job_";

        /// <summary>
        /// Token-usage record ID prefix.
        /// </summary>
        public static readonly string TokenUsageIdPrefix = "tku_";

        /// <summary>
        /// Default per-phase timeout, in seconds, for the in-dock Definition-of-Done gate (30 minutes).
        /// </summary>
        public const int DefaultDefinitionOfDoneTimeoutSeconds = 1800;

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
        /// Planning session ID prefix.
        /// </summary>
        public static readonly string PlanningSessionIdPrefix = "psn_";

        /// <summary>
        /// Planning session message ID prefix.
        /// </summary>
        public static readonly string PlanningSessionMessageIdPrefix = "psm_";

        /// <summary>
        /// Playbook ID prefix.
        /// </summary>
        public static readonly string PlaybookIdPrefix = "pbk_";

        /// <summary>
        /// Environment ID prefix.
        /// </summary>
        public static readonly string EnvironmentIdPrefix = "env_";

        /// <summary>
        /// Request history ID prefix.
        /// </summary>
        public static readonly string RequestHistoryIdPrefix = "req_";

        /// <summary>
        /// Workflow profile ID prefix.
        /// </summary>
        public static readonly string WorkflowProfileIdPrefix = "wfp_";

        /// <summary>
        /// ID prefix for project profiles.
        /// </summary>
        public static readonly string ProjectProfileIdPrefix = "ppf_";

        /// <summary>
        /// ID prefix for skills.
        /// </summary>
        public static readonly string SkillIdPrefix = "skl_";

        /// <summary>
        /// Check run ID prefix.
        /// </summary>
        public static readonly string CheckRunIdPrefix = "chk_";

        /// <summary>
        /// Release ID prefix.
        /// </summary>
        public static readonly string ReleaseIdPrefix = "rel_";

        /// <summary>
        /// Deployment ID prefix.
        /// </summary>
        public static readonly string DeploymentIdPrefix = "dpl_";

        /// <summary>
        /// Incident ID prefix.
        /// </summary>
        public static readonly string IncidentIdPrefix = "inc_";

        /// <summary>
        /// Runbook execution ID prefix.
        /// </summary>
        public static readonly string RunbookExecutionIdPrefix = "rbx_";

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
        /// Default request-history retention period in days.
        /// </summary>
        public static readonly int DefaultRequestHistoryRetentionDays = 30;

        /// <summary>
        /// Default inactivity timeout in minutes for planning sessions with no active runtime process.
        /// </summary>
        public static readonly int DefaultPlanningSessionInactivityTimeoutMinutes = 60;

        /// <summary>
        /// Default abandonment timeout in minutes for planning sessions with no active runtime process.
        /// Sessions older than this are reclaimed even when the standard inactivity timeout is disabled.
        /// </summary>
        public static readonly int DefaultPlanningSessionAbandonmentTimeoutMinutes = 240;

        /// <summary>
        /// Default maximum number of request/response body bytes to persist.
        /// </summary>
        public static readonly int DefaultRequestHistoryMaxBodyBytes = 32768;

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
        /// Maximum request or response body size supported by the generic dashboard relay.
        /// Bodies larger than this are rejected until chunked relay support is intentionally added.
        /// </summary>
        public static readonly int DefaultRemoteRelayMaxBodyBytes = 8 * 1024 * 1024;

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
        /// Cookie name for authenticated Armada.Proxy browser sessions.
        /// </summary>
        public static readonly string ProxySessionCookieName = "armada_proxy_session";

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
        /// Display name for the credential seeded during first-boot setup.
        /// </summary>
        public static readonly string DefaultCredentialName = "Default Admin Credential";

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
