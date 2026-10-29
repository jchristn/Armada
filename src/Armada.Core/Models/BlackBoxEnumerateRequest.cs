namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Filters and paging for black box entries (REST GET/POST /api/v1/blackbox/enumerate, MCP list_blackbox_entries). Inherits VesselId, VoyageId, MissionId, CaptainId, FleetId, CreatedAfter, CreatedBefore, Order and paging from EnumerationQuery. Order is CreatedDescending by default with the id as the tie-break.
    /// </summary>
    public class BlackBoxEnumerateRequest : EnumerationQuery
    {
        /// <summary>
        /// Filter by subject type.
        /// </summary>
        public BlackBoxSubjectTypeEnum? SubjectType { get; set; } = null;

        /// <summary>
        /// Filter by phase.
        /// </summary>
        public BlackBoxPhaseEnum? Phase { get; set; } = null;

        /// <summary>
        /// Filter by step.
        /// </summary>
        public BlackBoxStepEnum? Step { get; set; } = null;

        /// <summary>
        /// Filter by failure kind.
        /// </summary>
        public MissionFailureKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Filter by triage verdict.
        /// </summary>
        public BlackBoxTriageEnum? Triage { get; set; } = null;

        /// <summary>
        /// Filter by resolution.
        /// </summary>
        public BlackBoxResolutionEnum? Resolution { get; set; } = null;

        /// <summary>
        /// Filter by state. Deleting entries are hidden unless State is Deleting.
        /// </summary>
        public BlackBoxStateEnum? State { get; set; } = null;

        /// <summary>
        /// Filter by capture mode.
        /// </summary>
        public BlackBoxCaptureModeEnum? CaptureMode { get; set; } = null;

        /// <summary>
        /// Filter by fingerprint.
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// Filter by pinned.
        /// </summary>
        public bool? Pinned { get; set; } = null;

        /// <summary>
        /// Filter by current.
        /// </summary>
        public bool? IsCurrent { get; set; } = null;

        /// <summary>
        /// Filter by Harbor.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Entries with a recorded file at this path (any role).
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Server-set full-text filter over reason, summary and notes (case-insensitive substring). Set by search, never read from a filter body.
        /// </summary>
        [JsonIgnore]
        public string? SearchText { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEnumerateRequest()
        {
        }
    }
}
