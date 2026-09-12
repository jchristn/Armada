namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A per-project (or global/fleet default) aggregate that customizes how a project runs: which
    /// pipeline it uses, which workflow profile drives its checks, per-persona prompt overrides, and an
    /// attached set of skills. Project profiles reference other entities rather than re-storing their
    /// payloads, and resolve Global -> Fleet -> Vessel so a global default can be overridden per project.
    /// This is the foundation the persona-resolution, skills, and pipeline-builder features layer onto.
    /// </summary>
    public class ProjectProfile
    {
        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Ownership scope for user-scoping (tenant-wide vs user-specific). Distinct from <see cref="Scope"/>,
        /// which is the application scope (Global/Fleet/Vessel). Tenant-wide profiles are visible to everyone in
        /// the tenant but editable only by tenant/global admins; user-specific profiles are owned by <see cref="UserId"/>.
        /// </summary>
        public Armada.Core.Enums.ScopeEnum OwnershipScope { get; set; } = Armada.Core.Enums.ScopeEnum.TenantWide;

        /// <summary>
        /// Human-readable profile name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value.Trim();
            }
        }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Scope of the profile.
        /// </summary>
        public ProjectProfileScopeEnum Scope { get; set; } = ProjectProfileScopeEnum.Global;

        /// <summary>
        /// Fleet identifier when scope is Fleet.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Vessel identifier when scope is Vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Whether this is the default profile for its scope.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Whether the profile is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Optional pipeline this project uses (references <see cref="Pipeline.Id"/>). Null defers to
        /// the vessel/fleet default pipeline.
        /// </summary>
        public string? DefaultPipelineId { get; set; } = null;

        /// <summary>
        /// Optional workflow profile this project uses (references <see cref="WorkflowProfile.Id"/>).
        /// Null defers to normal workflow-profile resolution.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Per-persona prompt overrides applied to this project's pipeline personas.
        /// </summary>
        public List<PersonaOverride> PersonaOverrides { get; set; } = new List<PersonaOverride>();

        /// <summary>
        /// Skill identifiers or names attached to this project (a forward hook for the skills directory).
        /// </summary>
        public List<string> Skills { get; set; } = new List<string>();

        /// <summary>
        /// Creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.ProjectProfileIdPrefix, 24);
        private string _Name = "Default Project Profile";
    }
}
