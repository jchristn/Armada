namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Identity section of a black box record: the failed mission or voyage and everything it ran with.
    /// </summary>
    public class BlackBoxSubject
    {
        /// <summary>
        /// Mission or voyage.
        /// </summary>
        public BlackBoxSubjectTypeEnum SubjectType { get; set; } = BlackBoxSubjectTypeEnum.Mission;

        /// <summary>
        /// Tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owner.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Mission (msn_ prefix), or null for a voyage entry.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage (vyg_ prefix), or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Mission title (or voyage title for a voyage entry).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Voyage title, or null.
        /// </summary>
        public string? VoyageTitle { get; set; } = null;

        /// <summary>
        /// Vessel, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Vessel name, or null.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Vessel repository URL (credentials redacted), or null.
        /// </summary>
        public string? VesselRepoUrl { get; set; } = null;

        /// <summary>
        /// Vessel default (target) branch, or null.
        /// </summary>
        public string? VesselDefaultBranch { get; set; } = null;

        /// <summary>
        /// Fleet, or null.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Captain name, or null.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Captain runtime (AgentRuntimeEnum name), or null.
        /// </summary>
        public string? CaptainRuntime { get; set; } = null;

        /// <summary>
        /// Model the captain ran, or null.
        /// </summary>
        public string? CaptainModel { get; set; } = null;

        /// <summary>
        /// Model endpoint, or null.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Persona the mission routed as, or null.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Pipeline stage name, or null.
        /// </summary>
        public string? PipelineStage { get; set; } = null;

        /// <summary>
        /// Harbor the dock ran on, or null for the Admiral host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Harbor name, or null.
        /// </summary>
        public string? HarborName { get; set; } = null;

        /// <summary>
        /// Dock, or null.
        /// </summary>
        public string? DockId { get; set; } = null;

        /// <summary>
        /// Merge-queue entry, or null.
        /// </summary>
        public string? MergeEntryId { get; set; } = null;

        /// <summary>
        /// Incident, or null.
        /// </summary>
        public string? IncidentId { get; set; } = null;

        /// <summary>
        /// Stage outcome of the attempt, or null.
        /// </summary>
        public string? StageOutcomeId { get; set; } = null;

        /// <summary>
        /// Attempt number of this entry for the subject, starting at 1.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// Whether the mission is a rescue.
        /// </summary>
        public bool IsRescue { get; set; } = false;

        /// <summary>
        /// Parent mission, or null.
        /// </summary>
        public string? ParentMissionId { get; set; } = null;

        /// <summary>
        /// Mission branch, or null.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxSubject()
        {
        }
    }
}
