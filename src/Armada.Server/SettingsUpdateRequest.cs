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
        /// Whether to auto-create pull requests on mission completion.
        /// </summary>
        public bool? AutoCreatePr { get; set; }

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
    }
}
