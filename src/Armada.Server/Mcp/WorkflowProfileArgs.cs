namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// MCP tool arguments for workflow-profile get, create, update, delete, and validate. Every field is optional so
    /// update_workflow_profile changes only the fields the caller passes; null means not supplied.
    /// </summary>
    public class WorkflowProfileArgs
    {
        /// <summary>
        /// Workflow profile identifier (wfp_ prefix).
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Tenant to create the profile in (global admins only; ignored for everyone else).
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>
        /// Human-readable profile name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Application scope: Global, Fleet, or Vessel.
        /// </summary>
        public WorkflowProfileScopeEnum? Scope { get; set; }

        /// <summary>
        /// Fleet identifier for a Fleet-scoped profile.
        /// </summary>
        public string? FleetId { get; set; }

        /// <summary>
        /// Vessel identifier for a Vessel-scoped profile.
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// Whether the profile is the default at its scope.
        /// </summary>
        public bool? IsDefault { get; set; }

        /// <summary>
        /// Whether the profile is active.
        /// </summary>
        public bool? Active { get; set; }

        /// <summary>
        /// Ownership: UserSpecific or TenantWide.
        /// </summary>
        public ScopeEnum? OwnershipScope { get; set; }

        /// <summary>
        /// Language or framework hints.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? LanguageHints { get; set; }

        /// <summary>
        /// Lint command.
        /// </summary>
        public string? LintCommand { get; set; }

        /// <summary>
        /// Build command.
        /// </summary>
        public string? BuildCommand { get; set; }

        /// <summary>
        /// Unit test command.
        /// </summary>
        public string? UnitTestCommand { get; set; }

        /// <summary>
        /// Abbreviated unit test command.
        /// </summary>
        public string? AbbreviatedUnitTestCommand { get; set; }

        /// <summary>
        /// Integration test command.
        /// </summary>
        public string? IntegrationTestCommand { get; set; }

        /// <summary>
        /// End-to-end test command.
        /// </summary>
        public string? E2ETestCommand { get; set; }

        /// <summary>
        /// Migration command.
        /// </summary>
        public string? MigrationCommand { get; set; }

        /// <summary>
        /// Security scan command.
        /// </summary>
        public string? SecurityScanCommand { get; set; }

        /// <summary>
        /// Performance test command.
        /// </summary>
        public string? PerformanceCommand { get; set; }

        /// <summary>
        /// Package command.
        /// </summary>
        public string? PackageCommand { get; set; }

        /// <summary>
        /// Deployment verification command.
        /// </summary>
        public string? DeploymentVerificationCommand { get; set; }

        /// <summary>
        /// Rollback verification command.
        /// </summary>
        public string? RollbackVerificationCommand { get; set; }

        /// <summary>
        /// Publish artifact command.
        /// </summary>
        public string? PublishArtifactCommand { get; set; }

        /// <summary>
        /// Release versioning command.
        /// </summary>
        public string? ReleaseVersioningCommand { get; set; }

        /// <summary>
        /// Changelog generation command.
        /// </summary>
        public string? ChangelogGenerationCommand { get; set; }

        /// <summary>
        /// Required secret or configuration input references.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<WorkflowInputReference>? RequiredInputs { get; set; }

        /// <summary>
        /// Expected build or release artifacts.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? ExpectedArtifacts { get; set; }

        /// <summary>
        /// Environment-specific delivery commands.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<WorkflowEnvironmentProfile>? Environments { get; set; }
    }
}
