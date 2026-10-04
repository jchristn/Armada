namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// What the upgrade suite seeded through the baseline Admiral's API, and what it expects to find after the
    /// current build migrates the same database.
    /// </summary>
    public sealed class UpgradeSeedManifest
    {
        /// <summary>
        /// Version the baseline reported from /api/v1/status/health.
        /// </summary>
        public string BaselineVersion { get; set; } = "";

        /// <summary>
        /// Schema version the baseline left in the database.
        /// </summary>
        public int BaselineSchemaVersion { get; set; } = 0;

        /// <summary>
        /// Seeded fleet (admin tenant).
        /// </summary>
        public string FleetId { get; set; } = "";

        /// <summary>
        /// Fleet name.
        /// </summary>
        public string FleetName { get; set; } = "";

        /// <summary>
        /// Fleet description after the edit made on the baseline.
        /// </summary>
        public string FleetDescription { get; set; } = "";

        /// <summary>
        /// Vessel in the seeded fleet.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string VesselName { get; set; } = "";

        /// <summary>
        /// Vessel repository URL.
        /// </summary>
        public string VesselRepoUrl { get; set; } = "";

        /// <summary>
        /// Vessel project context.
        /// </summary>
        public string VesselProjectContext { get; set; } = "";

        /// <summary>
        /// Vessel without a fleet.
        /// </summary>
        public string LooseVesselId { get; set; } = "";

        /// <summary>
        /// Captain created on the baseline.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Expected status of every seeded mission, keyed by mission ID.
        /// </summary>
        public Dictionary<string, MissionStatusEnum> MissionStatuses { get; set; } = new Dictionary<string, MissionStatusEnum>();

        /// <summary>
        /// Title of every seeded mission, keyed by mission ID.
        /// </summary>
        public Dictionary<string, string> MissionTitles { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Voyage created with missions.
        /// </summary>
        public string VoyageId { get; set; } = "";

        /// <summary>
        /// Missions dispatched with <see cref="VoyageId"/>.
        /// </summary>
        public List<string> VoyageMissionIds { get; set; } = new List<string>();

        /// <summary>
        /// Voyage created without missions.
        /// </summary>
        public string BareVoyageId { get; set; } = "";

        /// <summary>
        /// Merge queue entries and their status as read back from the baseline, keyed by entry ID.
        /// </summary>
        public Dictionary<string, string> MergeEntryStatuses { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Custom persona name.
        /// </summary>
        public string CustomPersonaName { get; set; } = "";

        /// <summary>
        /// Custom persona description after its edit.
        /// </summary>
        public string CustomPersonaDescription { get; set; } = "";

        /// <summary>
        /// Built-in persona whose description was edited.
        /// </summary>
        public string EditedBuiltInPersonaName { get; set; } = "";

        /// <summary>
        /// Edited built-in persona description.
        /// </summary>
        public string EditedBuiltInPersonaDescription { get; set; } = "";

        /// <summary>
        /// Custom pipeline name.
        /// </summary>
        public string CustomPipelineName { get; set; } = "";

        /// <summary>
        /// Custom pipeline description after its edit.
        /// </summary>
        public string CustomPipelineDescription { get; set; } = "";

        /// <summary>
        /// Persona names of the custom pipeline's stages, in order, after its edit.
        /// </summary>
        public List<string> CustomPipelineStagePersonas { get; set; } = new List<string>();

        /// <summary>
        /// Built-in pipeline whose description was edited.
        /// </summary>
        public string EditedBuiltInPipelineName { get; set; } = "";

        /// <summary>
        /// Edited built-in pipeline description.
        /// </summary>
        public string EditedBuiltInPipelineDescription { get; set; } = "";

        /// <summary>
        /// Built-in non-persona prompt template overridden on the baseline.
        /// </summary>
        public string OverriddenTemplateName { get; set; } = "";

        /// <summary>
        /// Override content.
        /// </summary>
        public string OverriddenTemplateContent { get; set; } = "";

        /// <summary>
        /// Built-in persona prompt template overridden on the baseline.
        /// </summary>
        public string OverriddenPersonaTemplateName { get; set; } = "";

        /// <summary>
        /// Persona override content.
        /// </summary>
        public string OverriddenPersonaTemplateContent { get; set; } = "";

        /// <summary>
        /// Built-in persona template left as shipped (built-in upgrades should apply to it).
        /// </summary>
        public string UntouchedPersonaTemplateName { get; set; } = "";

        /// <summary>
        /// Custom prompt template created on the baseline.
        /// </summary>
        public string CustomTemplateName { get; set; } = "";

        /// <summary>
        /// Custom template content.
        /// </summary>
        public string CustomTemplateContent { get; set; } = "";

        /// <summary>
        /// Prompt template names the baseline had.
        /// </summary>
        public List<string> BaselineTemplateNames { get; set; } = new List<string>();

        /// <summary>
        /// Seeded signals.
        /// </summary>
        public List<string> SignalIds { get; set; } = new List<string>();

        /// <summary>
        /// Seeded objective (backlog item).
        /// </summary>
        public string ObjectiveId { get; set; } = "";

        /// <summary>
        /// Objective title.
        /// </summary>
        public string ObjectiveTitle { get; set; } = "";

        /// <summary>
        /// Second tenant.
        /// </summary>
        public string TenantId { get; set; } = "";

        /// <summary>
        /// Tenant admin user in the second tenant.
        /// </summary>
        public string UserId { get; set; } = "";

        /// <summary>
        /// Credential of that user.
        /// </summary>
        public string CredentialId { get; set; } = "";

        /// <summary>
        /// Bearer token of that credential.
        /// </summary>
        public string BearerToken { get; set; } = "";

        /// <summary>
        /// Fleet created in the second tenant with the user's bearer token.
        /// </summary>
        public string TenantFleetId { get; set; } = "";

        /// <summary>
        /// Events recorded by the baseline.
        /// </summary>
        public long EventCount { get; set; } = 0;

        /// <summary>
        /// Request history rows recorded by the baseline.
        /// </summary>
        public long RequestHistoryCount { get; set; } = 0;
    }
}
