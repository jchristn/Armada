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
        public static readonly string ProductVersion = "0.1.0";

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
        /// Default WebSocket port.
        /// </summary>
        public static readonly int DefaultWebSocketPort = 7892;

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

        #endregion
    }
}
