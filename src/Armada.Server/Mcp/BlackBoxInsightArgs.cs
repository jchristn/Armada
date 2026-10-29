namespace Armada.Server.Mcp
{
    using System;

    /// <summary>
    /// Arguments of the black box insight MCP tools (preview_mission_overlap, list_vessel_reservations,
    /// lint_mission_brief, get_vessel_blackbox_doctor, diff_blackbox_entries, list_blackbox_nearby_successes,
    /// get_blackbox_timeline, get_voyage_blackbox_cascade, get_blackbox_recovery_stats). Each tool reads the fields it
    /// documents.
    /// </summary>
    public class BlackBoxInsightArgs
    {
        /// <summary>Vessel (vsl_ prefix).</summary>
        public string? VesselId { get; set; } = null;

        /// <summary>Black box entry (bbx_ prefix).</summary>
        public string? EntryId { get; set; } = null;

        /// <summary>Other black box entry (bbx_ prefix).</summary>
        public string? OtherEntryId { get; set; } = null;

        /// <summary>Voyage (vyg_ prefix).</summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>Mission title.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Mission brief.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Dependency already set (msn_ prefix).</summary>
        public string? DependsOnMissionId { get; set; } = null;

        /// <summary>Active mission to leave out (msn_ prefix).</summary>
        public string? ExcludeMissionId { get; set; } = null;

        /// <summary>Rule filter.</summary>
        public string? RuleId { get; set; } = null;

        /// <summary>Doctor window in days.</summary>
        public int? WindowDays { get; set; } = null;

        /// <summary>Order: CreatedAscending or CreatedDescending.</summary>
        public string? Order { get; set; } = null;

        /// <summary>Page number.</summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>Page size.</summary>
        public int? PageSize { get; set; } = null;
    }
}
