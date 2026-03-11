namespace Armada.Server.WebSocket
{
    using Armada.Core.Models;

    /// <summary>
    /// Envelope for WebSocket commands sent by clients.
    /// Covers all command shapes: ID-based, query-based, and status transitions.
    /// </summary>
    public class WebSocketCommand
    {
        /// <summary>
        /// Command action name (e.g. "list_fleets", "create_fleet").
        /// </summary>
        public string Action { get; set; } = "";

        /// <summary>
        /// Entity identifier (used by get, update, delete, transition commands).
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Status value (used by transition_mission_status).
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Captain identifier (used by stop_captain).
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Enumeration query (used by list commands).
        /// </summary>
        public EnumerationQuery? Query { get; set; }

        /// <summary>
        /// Entity type for enumerate command (e.g. "fleets", "missions", "merge_queue").
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Number of log lines to return (used by get_mission_log, get_captain_log).
        /// </summary>
        public int? Lines { get; set; }

        /// <summary>
        /// Offset for log pagination (used by get_mission_log, get_captain_log).
        /// </summary>
        public int? Offset { get; set; }

        /// <summary>
        /// File path (used by restore command).
        /// </summary>
        public string? FilePath { get; set; }

        /// <summary>
        /// Output path (used by backup command).
        /// </summary>
        public string? OutputPath { get; set; }
    }
}
