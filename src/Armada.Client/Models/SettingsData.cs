namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Server settings as returned by GET /api/v1/settings and accepted by PUT /api/v1/settings.
    /// </summary>
    public class SettingsData
    {
        #region Public-Members

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

        /// <summary>
        /// CLI tool permission settings (Ask and mission default policies, owner approval, prompt timeout), or null.
        /// </summary>
        public Armada.Core.Settings.CliPermissionSettings? Permissions { get; set; } = null;

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
