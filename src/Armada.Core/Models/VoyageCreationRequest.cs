namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Everything one voyage creation needs, from any surface (REST, MCP, WebSocket, planning, fleet actions, battery
    /// runs). <see cref="VoyageCreationService"/> is the only consumer.
    /// </summary>
    public class VoyageCreationRequest
    {
        /// <summary>
        /// Voyage title. Required.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Voyage description (the overall objective Architect slices hand to every Worker).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Target vessel. Null or empty creates a bare voyage when <see cref="AllowBareVoyage"/> is set.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Missions to dispatch. Empty creates a bare voyage when <see cref="AllowBareVoyage"/> is set.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<MissionDescription> Missions { get; set; } = new List<MissionDescription>();

        /// <summary>
        /// Pipeline id (a name is accepted too). Null for the vessel or fleet default.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Pipeline name, used when <see cref="PipelineId"/> is not set.
        /// </summary>
        public string? PipelineName { get; set; } = null;

        /// <summary>
        /// Playbooks applied to every mission.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();

        /// <summary>
        /// Per-persona captain overrides, stored on the voyage before any mission is assigned.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<CaptainAssignmentOverride> CaptainOverrides { get; set; } = new List<CaptainAssignmentOverride>();

        /// <summary>
        /// Landing mode, or null for the vessel default.
        /// </summary>
        public LandingModeEnum? LandingMode { get; set; } = null;

        /// <summary>
        /// Run approval options, or null.
        /// </summary>
        public RunApprovalOptions? ApprovalOptions { get; set; } = null;

        /// <summary>
        /// Whether the voyage is evaluation work.
        /// </summary>
        public bool IsEvaluation { get; set; } = false;

        /// <summary>
        /// Objective to link the voyage to, or null.
        /// </summary>
        public string? ObjectiveId { get; set; } = null;

        /// <summary>
        /// Planning session the voyage came from, or null.
        /// </summary>
        public string? SourcePlanningSessionId { get; set; } = null;

        /// <summary>
        /// Planning session message the voyage came from, or null.
        /// </summary>
        public string? SourcePlanningMessageId { get; set; } = null;

        /// <summary>
        /// Whether a request with no vessel or no missions creates a bare voyage (missions added later) rather than
        /// being refused.
        /// </summary>
        public bool AllowBareVoyage { get; set; } = false;
    }
}
