namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Defines how a project builds, tests, releases, and deploys.
    /// </summary>
    public class WorkflowProfile
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
        public WorkflowProfileScopeEnum Scope { get; set; } = WorkflowProfileScopeEnum.Global;

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
        /// Built-in template Armada created this profile from (for example "dotnet" or "generic"; see
        /// <c>WorkflowProfileTemplates</c>), or null for a profile a person created. Armada replaces a "generic" one
        /// when it can later recognize the vessel's toolchain, and never changes a profile once it has been edited.
        /// </summary>
        public string? SourceTemplate { get; set; } = null;

        /// <summary>
        /// Optional language/runtime hints.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> LanguageHints { get; set; } = new List<string>();

        /// <summary>
        /// Lint command.
        /// </summary>
        public string? LintCommand { get; set; } = null;

        /// <summary>
        /// Build command.
        /// </summary>
        public string? BuildCommand { get; set; } = null;

        /// <summary>
        /// Full unit-test command: the complete unit-test suite. Stage checks run it unless the mission description or
        /// the stage persona's prompt asks for the abbreviated suite (see <see cref="UnitTestScopeDirective"/>), and it is
        /// the command behind the <c>UnitTest</c> check type.
        /// </summary>
        public string? UnitTestCommand { get; set; } = null;

        /// <summary>
        /// Abbreviated unit-test command: a fast subset of the unit tests (for example one test project or a filter).
        /// Used by the <c>AbbreviatedUnitTest</c> check type and by stage checks when the mission description or the
        /// stage persona's prompt contains <c>[ARMADA:UNIT-TESTS] abbreviated</c>. When empty, those fall back to
        /// <see cref="UnitTestCommand"/>.
        /// </summary>
        public string? AbbreviatedUnitTestCommand { get; set; } = null;

        /// <summary>
        /// Integration-test command.
        /// </summary>
        public string? IntegrationTestCommand { get; set; } = null;

        /// <summary>
        /// End-to-end test command.
        /// </summary>
        public string? E2ETestCommand { get; set; } = null;

        /// <summary>
        /// Migration command.
        /// </summary>
        public string? MigrationCommand { get; set; } = null;

        /// <summary>
        /// Security-scan command.
        /// </summary>
        public string? SecurityScanCommand { get; set; } = null;

        /// <summary>
        /// Performance-check command.
        /// </summary>
        public string? PerformanceCommand { get; set; } = null;

        /// <summary>
        /// Package command.
        /// </summary>
        public string? PackageCommand { get; set; } = null;

        /// <summary>
        /// Deployment-verification command.
        /// </summary>
        public string? DeploymentVerificationCommand { get; set; } = null;

        /// <summary>
        /// Rollback-verification command.
        /// </summary>
        public string? RollbackVerificationCommand { get; set; } = null;

        /// <summary>
        /// Publish-artifact command.
        /// </summary>
        public string? PublishArtifactCommand { get; set; } = null;

        /// <summary>
        /// Release-versioning command.
        /// </summary>
        public string? ReleaseVersioningCommand { get; set; } = null;

        /// <summary>
        /// Changelog-generation command.
        /// </summary>
        public string? ChangelogGenerationCommand { get; set; } = null;

        /// <summary>
        /// Required secret or configuration references.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> RequiredSecrets { get; set; } = new List<string>();

        /// <summary>
        /// Structured workflow input references exposed over the API and dashboard.
        /// These are persisted through the legacy RequiredSecrets backing list to avoid
        /// a dedicated schema migration for readiness/preflight support.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<WorkflowInputReference> RequiredInputs
        {
            get => WorkflowInputReference.ParseMany(RequiredSecrets);
            set => RequiredSecrets = WorkflowInputReference.SerializeMany(value?.Where(item => item != null) ?? Enumerable.Empty<WorkflowInputReference>());
        }

        /// <summary>
        /// Expected build or release artifacts.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> ExpectedArtifacts { get; set; } = new List<string>();

        /// <summary>
        /// Environment-specific delivery commands.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<WorkflowEnvironmentProfile> Environments { get; set; } = new List<WorkflowEnvironmentProfile>();

        /// <summary>
        /// Creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.WorkflowProfileIdPrefix, 24);
        private string _Name = "Default Workflow";
    }
}
