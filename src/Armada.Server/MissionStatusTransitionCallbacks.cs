namespace Armada.Server
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Server callbacks for a manual status change: audit events, dashboard broadcasts, and logging.
    /// A null callback is skipped. When the event callbacks are null, the status operation still writes the event row.
    /// </summary>
    public sealed class MissionStatusTransitionCallbacks
    {
        #region Public-Members

        /// <summary>
        /// Writes and broadcasts an audit event. Null writes the event row only.
        /// </summary>
        public Func<string, string, string?, string?, string?, string?, string?, string?, Task>? EmitEvent { get; set; }

        /// <summary>
        /// Writes and broadcasts <c>mission.status_changed</c>. Null writes the event row only.
        /// </summary>
        public Func<Mission, MissionStatusEnum?, string, Task>? EmitMissionStatusChanged { get; set; }

        /// <summary>
        /// Broadcasts <c>mission.changed</c>. Null skips the broadcast.
        /// </summary>
        public Action<Mission, string?>? BroadcastMissionChange { get; set; }

        /// <summary>
        /// Broadcasts <c>approval-needed</c> when the new status is Review. Null skips the broadcast.
        /// </summary>
        public Action<Mission>? BroadcastApprovalNeeded { get; set; }

        /// <summary>
        /// Log destination. Null skips the log lines.
        /// </summary>
        public LoggingModule? Logging { get; set; }

        #endregion
    }
}
