namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Standard error response from the API.
    /// </summary>
    public class ArmadaErrorResponse
    {
        /// <summary>
        /// Error code or category (e.g. "NotFound", "Conflict").
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Human-readable error description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// Wrapper returned when POST /api/v1/missions creates a mission that stays Pending.
    /// </summary>
    public class MissionCreateResponse
    {
        /// <summary>
        /// The created mission.
        /// </summary>
        public Mission? Mission { get; set; }

        /// <summary>
        /// Optional warning (e.g. no captain available).
        /// </summary>
        public string? Warning { get; set; }
    }

    /// <summary>
    /// Response from GET /api/v1/fleets/{id}.
    /// </summary>
    public class FleetDetailResponse
    {
        /// <summary>
        /// The fleet.
        /// </summary>
        public Fleet? Fleet { get; set; }

        /// <summary>
        /// Vessels belonging to this fleet.
        /// </summary>
        public List<Vessel>? Vessels { get; set; }
    }

    /// <summary>
    /// Response from GET /api/v1/voyages/{id} or armada_voyage_status.
    /// </summary>
    public class VoyageDetailResponse
    {
        /// <summary>
        /// The voyage.
        /// </summary>
        public Voyage? Voyage { get; set; }

        /// <summary>
        /// Missions in this voyage.
        /// </summary>
        public List<Mission>? Missions { get; set; }
    }

    /// <summary>
    /// Response from DELETE /api/v1/voyages/{id} or armada_cancel_voyage.
    /// </summary>
    public class CancelVoyageResponse
    {
        /// <summary>
        /// The cancelled voyage.
        /// </summary>
        public Voyage? Voyage { get; set; }

        /// <summary>
        /// Number of missions that were cancelled.
        /// </summary>
        public int CancelledMissions { get; set; }
    }

    /// <summary>
    /// Response from DELETE /api/v1/voyages/{id}/purge or armada_purge_voyage.
    /// </summary>
    public class PurgeVoyageResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the purged voyage.
        /// </summary>
        public string? VoyageId { get; set; }

        /// <summary>
        /// Number of missions deleted.
        /// </summary>
        public int MissionsDeleted { get; set; }

        /// <summary>
        /// Error message if the operation failed.
        /// </summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Response from DELETE /api/v1/missions/{id} or armada_purge_mission.
    /// </summary>
    public class DeleteMissionResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the deleted mission.
        /// </summary>
        public string? MissionId { get; set; }
    }

    /// <summary>
    /// Response from DELETE /api/v1/fleets/{id} or armada_delete_fleet.
    /// </summary>
    public class DeleteFleetResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the deleted fleet.
        /// </summary>
        public string? FleetId { get; set; }
    }

    /// <summary>
    /// Response from DELETE /api/v1/vessels/{id} or armada_delete_vessel.
    /// </summary>
    public class DeleteVesselResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the deleted vessel.
        /// </summary>
        public string? VesselId { get; set; }
    }

    /// <summary>
    /// Response from DELETE /api/v1/captains/{id} or armada_delete_captain.
    /// </summary>
    public class DeleteCaptainResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the deleted captain.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Error message if the operation failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Error detail message.
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// Response from POST /api/v1/captains/{id}/stop.
    /// </summary>
    public class StopCaptainResponse
    {
        /// <summary>
        /// Operation status (e.g. "stopped").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the stopped captain (MCP tool only).
        /// </summary>
        public string? CaptainId { get; set; }
    }

    /// <summary>
    /// Response from GET /api/v1/missions/{id}/diff.
    /// </summary>
    public class MissionDiffResponse
    {
        /// <summary>
        /// Mission ID.
        /// </summary>
        public string? MissionId { get; set; }

        /// <summary>
        /// Branch name.
        /// </summary>
        public string? Branch { get; set; }

        /// <summary>
        /// The diff content.
        /// </summary>
        public string? Diff { get; set; }

        /// <summary>
        /// Error message if diff retrieval failed.
        /// </summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Response from GET /api/v1/missions/{id}/log.
    /// </summary>
    public class MissionLogResponse
    {
        /// <summary>
        /// Mission ID.
        /// </summary>
        public string? MissionId { get; set; }

        /// <summary>
        /// Log content.
        /// </summary>
        public string? Log { get; set; }

        /// <summary>
        /// Number of lines returned.
        /// </summary>
        public int Lines { get; set; }

        /// <summary>
        /// Total number of lines in the log file.
        /// </summary>
        public int TotalLines { get; set; }

        /// <summary>
        /// Error message if log retrieval failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Error description.
        /// </summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// Response from GET /api/v1/captains/{id}/log.
    /// </summary>
    public class CaptainLogResponse
    {
        /// <summary>
        /// Captain ID.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Log content.
        /// </summary>
        public string? Log { get; set; }

        /// <summary>
        /// Number of lines returned.
        /// </summary>
        public int Lines { get; set; }

        /// <summary>
        /// Total number of lines in the log file.
        /// </summary>
        public int TotalLines { get; set; }

        /// <summary>
        /// Error message if log retrieval failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Error description.
        /// </summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// Response from GET /api/v1/status/health.
    /// </summary>
    public class HealthResponse
    {
        /// <summary>
        /// Health status (e.g. "healthy").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Current timestamp.
        /// </summary>
        public DateTime? Timestamp { get; set; }

        /// <summary>
        /// Server start time.
        /// </summary>
        public DateTime? StartUtc { get; set; }

        /// <summary>
        /// Uptime as a human-readable string.
        /// </summary>
        public string? Uptime { get; set; }

        /// <summary>
        /// Server version.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Port configuration.
        /// </summary>
        public HealthPorts? Ports { get; set; }
    }

    /// <summary>
    /// Port configuration in health response.
    /// </summary>
    public class HealthPorts
    {
        /// <summary>
        /// Admiral REST API port.
        /// </summary>
        public int Admiral { get; set; }

        /// <summary>
        /// MCP server port.
        /// </summary>
        public int Mcp { get; set; }

        /// <summary>
        /// WebSocket port.
        /// </summary>
        public int WebSocket { get; set; }
    }

    /// <summary>
    /// Response from POST /api/v1/merge-queue/process.
    /// </summary>
    public class ProcessMergeQueueResponse
    {
        /// <summary>
        /// Operation status (e.g. "processed").
        /// </summary>
        public string? Status { get; set; }
    }

    /// <summary>
    /// Response from armada_cancel_merge MCP tool.
    /// </summary>
    public class CancelMergeResponse
    {
        /// <summary>
        /// Operation status (e.g. "cancelled").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the cancelled entry.
        /// </summary>
        public string? EntryId { get; set; }
    }

    /// <summary>
    /// Response from POST /api/v1/server/stop.
    /// </summary>
    public class StopServerResponse
    {
        /// <summary>
        /// Operation status (e.g. "shutting_down").
        /// </summary>
        public string? Status { get; set; }
    }

    /// <summary>
    /// Response from GET /api/v1/doctor.
    /// </summary>
    public class DiagnosticCheck
    {
        /// <summary>
        /// Check name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Check status (Pass, Fail, Warn).
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Detail message.
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// WebSocket command result envelope.
    /// </summary>
    public class WsCommandResult
    {
        /// <summary>
        /// Message type (e.g. "command.result", "command.error").
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Action name (e.g. "list_fleets", "get_mission").
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// Error message (for command.error responses).
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Optional warning.
        /// </summary>
        public string? Warning { get; set; }
    }

    /// <summary>
    /// WebSocket broadcast message.
    /// </summary>
    public class WsBroadcast
    {
        /// <summary>
        /// Message type (e.g. "mission.changed", "voyage.changed").
        /// </summary>
        public string? Type { get; set; }
    }
}
