namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Server settings as returned by GET /api/v1/settings and accepted by PUT /api/v1/settings.
    /// </summary>
    public class SettingsData
    {
        #region Public-Members

        /// <summary>Mission/event/signal retention in days; zero disables expiry. Null leaves it unchanged on update.</summary>
        public int? DataRetentionDays { get; set; } = null;

        /// <summary>API request-history retention in days; zero disables expiry. Null leaves it unchanged on update.</summary>
        public int? RequestHistoryRetentionDays { get; set; } = null;

        /// <summary>
        /// REST port.
        /// </summary>
        public int? AdmiralPort { get; set; } = null;

        /// <summary>
        /// MCP port.
        /// </summary>
        public int? McpPort { get; set; } = null;

        /// <summary>
        /// Maximum captains.
        /// </summary>
        public int? MaxCaptains { get; set; } = null;

        /// <summary>
        /// Heartbeat interval in seconds.
        /// </summary>
        public int? HeartbeatIntervalSeconds { get; set; } = null;

        /// <summary>
        /// Stall threshold in minutes.
        /// </summary>
        public int? StallThresholdMinutes { get; set; } = null;

        /// <summary>
        /// Time limit in minutes for each stage-check command (1 to 240).
        /// </summary>
        public int? StageCheckTimeoutMinutes { get; set; } = null;

        /// <summary>
        /// Idle captain timeout in seconds.
        /// </summary>
        public int? IdleCaptainTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Planning session inactivity timeout in minutes.
        /// </summary>
        public int? PlanningSessionInactivityTimeoutMinutes { get; set; } = null;

        /// <summary>
        /// Planning session abandonment timeout in minutes.
        /// </summary>
        public int? PlanningSessionAbandonmentTimeoutMinutes { get; set; } = null;

        /// <summary>
        /// Planning transcript retention in days.
        /// </summary>
        public int? PlanningSessionRetentionDays { get; set; } = null;

        /// <summary>
        /// Global (default) landing mode for completed missions (MergeAndPush unless changed); vessels and voyages can
        /// override it.
        /// </summary>
        public Armada.Core.Enums.LandingModeEnum? LandingMode { get; set; } = null;

        /// <summary>
        /// Whether a mission waits for a Harbor instead of running on the Admiral host when no Harbor can take it;
        /// null when the server does not report it.
        /// </summary>
        public bool? RequireHarborForLaunch { get; set; } = null;

        /// <summary>
        /// When true, stages waiting on a failed stage show Blocked until it is restarted, instead of being cancelled.
        /// Null when the server does not report it (or, in an update, leaves it unchanged).
        /// </summary>
        public bool? BlockDependentsOfFailedStages { get; set; } = null;

        /// <summary>
        /// Data directory (read-only).
        /// </summary>
        public string? DataDirectory { get; set; } = null;

        /// <summary>
        /// Database path (read-only).
        /// </summary>
        public string? DatabasePath { get; set; } = null;

        /// <summary>
        /// Log directory (read-only).
        /// </summary>
        public string? LogDirectory { get; set; } = null;

        /// <summary>
        /// Docks directory (read-only).
        /// </summary>
        public string? DocksDirectory { get; set; } = null;

        /// <summary>
        /// Repositories directory (read-only).
        /// </summary>
        public string? ReposDirectory { get; set; } = null;

        /// <summary>
        /// Vessel used by Rebuild Armada, or null.
        /// </summary>
        public string? SelfVesselId { get; set; } = null;

        /// <summary>
        /// Rebuild slots to retain.
        /// </summary>
        public int? RebuildSlotRetentionCount { get; set; } = null;

        /// <summary>
        /// Remote control settings, or null.
        /// </summary>
        public Armada.Core.Settings.RemoteControlSettings? RemoteControl { get; set; } = null;

        /// <summary>
        /// Vessel import settings, or null.
        /// </summary>
        public Armada.Core.Settings.VesselImportSettings? Import { get; set; } = null;

        /// <summary>
        /// Fleet action settings, or null.
        /// </summary>
        public Armada.Core.Settings.FleetActionSettings? FleetActions { get; set; } = null;

        /// <summary>
        /// Repository health settings, or null.
        /// </summary>
        public Armada.Core.Settings.RepositoryHealthSettings? RepositoryHealth { get; set; } = null;

        /// <summary>
        /// Data retention settings (Ask threads, jobs, import batches, CLI permission requests), or null.
        /// </summary>
        public Armada.Core.Settings.RetentionSettings? Retention { get; set; } = null;

        /// <summary>Optional bounded background storage inventory settings.</summary>
        public Armada.Core.Settings.StorageSettings? Storage { get; set; } = null;

        /// <summary>
        /// CLI tool permission settings (Ask and mission default policies, owner approval, prompt timeout), or null.
        /// </summary>
        public Armada.Core.Settings.CliPermissionSettings? Permissions { get; set; } = null;

        /// <summary>
        /// Ask Armada settings (history window, proposal expiry, tracker interval, milestone narration, result reports,
        /// timeouts), or null when the server predates them.
        /// </summary>
        public Armada.Core.Settings.AskSettings? Ask { get; set; } = null;

        /// <summary>
        /// Push notification settings (ExpoAccessToken redacted when set).
        /// </summary>
        public Armada.Core.Settings.PushSettings? Push { get; set; } = null;

        /// <summary>
        /// Black box settings (capture, storage target with masked secrets, retention, sweeper), or null when the server
        /// predates them. A PUT replaces the whole object; a masked secret keeps the stored one.
        /// </summary>
        public Armada.Core.Settings.BlackBoxSettings? BlackBox { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SettingsData()
        {
        }

        #endregion
    }
}
