namespace Armada.Server
{
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using SyslogLogging;
    using Voltaic;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server.Mcp;
    using Armada.Server.WebSocket;

    /// <summary>
    /// Request model for partial update of server settings.
    /// </summary>
    public class SettingsUpdateRequest
    {
        /// <summary>
        /// Admiral REST API port (1-65535).
        /// </summary>
        public int? AdmiralPort { get; set; }

        /// <summary>
        /// MCP server port (1-65535).
        /// </summary>
        public int? McpPort { get; set; }

        /// <summary>
        /// Maximum captains allowed (0 = unlimited).
        /// </summary>
        public int? MaxCaptains { get; set; }

        /// <summary>
        /// Heartbeat check interval in seconds (>= 5).
        /// </summary>
        public int? HeartbeatIntervalSeconds { get; set; }

        /// <summary>
        /// Stall detection threshold in minutes (>= 1).
        /// </summary>
        public int? StallThresholdMinutes { get; set; }

        /// <summary>
        /// Idle captain timeout in seconds (0 = disabled).
        /// </summary>
        public int? IdleCaptainTimeoutSeconds { get; set; }

        /// <summary>
        /// Idle planning-session timeout in minutes (0 = disabled).
        /// </summary>
        public int? PlanningSessionInactivityTimeoutMinutes { get; set; }

        /// <summary>
        /// Abandonment timeout in minutes for planning sessions without a running process (0 = disabled).
        /// </summary>
        public int? PlanningSessionAbandonmentTimeoutMinutes { get; set; }

        /// <summary>
        /// Retention period in days for stopped or failed planning sessions (0 = disabled).
        /// </summary>
        public int? PlanningSessionRetentionDays { get; set; }

        /// <summary>
        /// Global (default) landing mode for completed missions; vessels and voyages can override it.
        /// </summary>
        public Armada.Core.Enums.LandingModeEnum? LandingMode { get; set; }

        /// <summary>
        /// Identifier (vsl_ prefix) of the vessel holding Armada's own source, used by the "Rebuild Armada"
        /// feature. Send an empty string to clear it.
        /// </summary>
        public string? SelfVesselId { get; set; }

        /// <summary>
        /// Number of published rebuild slots to retain for rollback (minimum 1).
        /// </summary>
        public int? RebuildSlotRetentionCount { get; set; }

        /// <summary>
        /// Optional remote-control tunnel settings update.
        /// When supplied, replaces the full remoteControl settings object.
        /// </summary>
        public RemoteControlSettings? RemoteControl { get; set; }

        /// <summary>
        /// Optional vessel import settings update (allowedRoots, maxDepth, excludedDirectoryNames, inlineBatchLimit).
        /// When supplied, replaces the full import settings object; values are clamped by VesselImportSettings.
        /// </summary>
        public VesselImportSettings? Import { get; set; }

        /// <summary>
        /// Optional fleet action settings update (MaxConcurrency, DefaultTimeoutSeconds, MaxOutputBytes,
        /// RunRetentionDays). When supplied, replaces the full fleetActions object; omitted fields take their
        /// defaults and out-of-range values are clamped. Applied live.
        /// </summary>
        public FleetActionSettings? FleetActions { get; set; }

        /// <summary>
        /// Optional vessel health settings update. When supplied, replaces the full repositoryHealth settings object
        /// (values are clamped by the settings setters).
        /// </summary>
        public RepositoryHealthSettings? RepositoryHealth { get; set; }

        /// <summary>
        /// Optional retention settings update (AskThreadArchiveAfterDays, AskThreadDeleteAfterDays, JobRetentionDays,
        /// ImportBatchRetentionDays). When supplied, replaces the full retention object; omitted fields take their
        /// defaults and out-of-range values are clamped to 0..3650 (0 means never). Applied live.
        /// </summary>
        public RetentionSettings? Retention { get; set; }

        /// <summary>
        /// Optional CLI tool permission settings update (AskDefaultPolicy, MissionDefaultPolicy, AllowOwnerApproval,
        /// PromptTimeoutSeconds). When supplied, replaces the full permissions object; omitted fields take their defaults
        /// and PromptTimeoutSeconds is clamped to 10..3600. Applied live to the next launch.
        /// </summary>
        public CliPermissionSettings? Permissions { get; set; }

        /// <summary>
        /// Optional Ask Armada settings update (HistoryTurns, ProposalExpiryMinutes, TrackerIntervalSeconds,
        /// NarrateMilestones, ReportResultsOnCompletion, CaptainAutoApprove, NarrationTimeoutSeconds, TurnTimeoutMinutes).
        /// When supplied, replaces the full ask object; omitted fields take their defaults and out-of-range values are
        /// clamped. Applied live.
        /// </summary>
        public AskSettings? Ask { get; set; }

        /// <summary>
        /// Optional push notification settings update (Enabled, ExpoAccessToken, Categories, MaxPerUserPerMinute,
        /// DedupeWindowSeconds, MaxDevicesPerUser). When supplied, replaces the full push object; omitted fields take their defaults and
        /// out-of-range values are clamped. ExpoAccessToken sent back as the redacted value from GET keeps the stored
        /// token. Applied live.
        /// </summary>
        public PushSettings? Push { get; set; }
    }
}
