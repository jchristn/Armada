namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Registers MCP tools for workflow-profile management (the build, test, release, and deploy commands stage checks
    /// run). Every tool calls <see cref="WorkflowProfileAccessService"/>, the same caller-scoped layer the REST routes
    /// use, so visibility, ownership, validation, and default handling match REST exactly.
    /// </summary>
    public static class McpWorkflowProfileTools
    {
        #region Private-Members

        private const string UnavailableMessage = "Workflow profiles are not available on this server.";

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Registers workflow-profile MCP tools. With a null service each tool answers a typed Unavailable error.
        /// </summary>
        /// <param name="register">Registration delegate.</param>
        /// <param name="service">Caller-scoped workflow-profile service, or null when unavailable.</param>
        /// <exception cref="ArgumentNullException">Thrown when register is null.</exception>
        public static void Register(RegisterToolDelegate register, WorkflowProfileAccessService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            register(
                "get_workflow_profile",
                "Get a workflow profile by ID, including every command stage checks run (build, unit test, integration, e2e, migration, security scan, performance, package, release, deploy verification) and its environments.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Workflow profile ID (wfp_ prefix)" }
                    },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    WorkflowProfileArgs request = Read<WorkflowProfileArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.Id)) return (object)McpToolError.InvalidArgument("id is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    return (object)await service.GetAsync(caller, request.Id).ConfigureAwait(false);
                });

            register(
                "create_workflow_profile",
                "Create a workflow profile. At least one command is required. Scope Fleet needs fleetId and scope Vessel needs vesselId. Setting isDefault clears the default flag on the other profiles at the same scope. Tenant admins only, as over REST.",
                new
                {
                    type = "object",
                    properties = BuildProfileProperties(true, false),
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    WorkflowProfileArgs request = Read<WorkflowProfileArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.Name)) return (object)McpToolError.InvalidArgument("name is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    WorkflowProfile profile = BuildProfile(request);
                    profile.TenantId = request.TenantId;
                    profile.OwnershipScope = ScopedVisibility.ResolveCreateScope(caller, request.OwnershipScope);
                    return (object)await service.CreateAsync(caller, profile).ConfigureAwait(false);
                });

            register(
                "update_workflow_profile",
                "Update a workflow profile. Only the fields you pass change; omitted fields keep their stored values. Pass an empty string to clear a command, fleetId, or vesselId. Lists (languageHints, requiredInputs, expectedArtifacts, environments) replace the stored list when passed. The updated profile is validated before it is saved. Tenant admins only, as over REST.",
                new
                {
                    type = "object",
                    properties = BuildProfileProperties(false, true),
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    WorkflowProfileArgs request = Read<WorkflowProfileArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.Id)) return (object)McpToolError.InvalidArgument("id is required");
                    if (request.Name != null && String.IsNullOrWhiteSpace(request.Name)) return (object)McpToolError.InvalidArgument("name must not be empty");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    return (object)await service.UpdateAsync(
                        caller,
                        request.Id,
                        (existing, canChangeOwnership) => ApplyPartial(existing, request, canChangeOwnership)).ConfigureAwait(false);
                });

            register(
                "delete_workflow_profile",
                "Delete a workflow profile by ID. This cannot be undone. Tenant admins only, as over REST.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Workflow profile ID (wfp_ prefix)" }
                    },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    WorkflowProfileArgs request = Read<WorkflowProfileArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.Id)) return (object)McpToolError.InvalidArgument("id is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    WorkflowProfile deleted = await service.DeleteAsync(caller, request.Id).ConfigureAwait(false);
                    return (object)new { Status = "deleted", WorkflowProfileId = deleted.Id };
                });

            register(
                "delete_workflow_profiles",
                "Permanently delete multiple workflow profiles by ID. Ids that are missing, outside your scope, or not editable by you are skipped with a reason. Returns a summary of deleted and skipped entries. This cannot be undone. Tenant admins only, as over REST.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of workflow profile IDs to delete (wfp_ prefix), at most 1000" }
                    },
                    required = new[] { "ids" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    DeleteMultipleArgs request = Read<DeleteMultipleArgs>(args);
                    if (request.Ids == null || request.Ids.Count == 0) return (object)McpToolError.InvalidArgument("ids is required and must not be empty");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    return (object)await service.DeleteMultipleAsync(caller, request.Ids).ConfigureAwait(false);
                });

            register(
                "validate_workflow_profile",
                "Validate a workflow-profile definition without saving it. Returns isValid, errors, warnings, the available check types, and the resolved command previews. Tenant admins only, as over REST.",
                new
                {
                    type = "object",
                    properties = BuildProfileProperties(false, false),
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    WorkflowProfileArgs request = Read<WorkflowProfileArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.Name)) return (object)McpToolError.InvalidArgument("name is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    WorkflowProfile profile = BuildProfile(request);
                    if (request.OwnershipScope.HasValue) profile.OwnershipScope = request.OwnershipScope.Value;
                    return (object)await service.ValidateAsync(caller, profile).ConfigureAwait(false);
                });

            object vesselProperties = new
            {
                vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                workflowProfileId = new { type = "string", description = "Optional explicit workflow profile override (wfp_ prefix)" }
            };

            register(
                "resolve_workflow_profile",
                "Resolve the workflow profile that applies to a vessel: an explicit workflowProfileId when given, otherwise the vessel's assigned profile, then vessel, fleet, and global precedence. Returns the profile.",
                new { type = "object", properties = vesselProperties, required = new[] { "vesselId" } },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    WorkflowProfileVesselArgs request = Read<WorkflowProfileVesselArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.VesselId)) return (object)McpToolError.InvalidArgument("vesselId is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    return (object)await service.ResolveAsync(caller, request.VesselId, request.WorkflowProfileId).ConfigureAwait(false);
                });

            register(
                "preview_workflow_profile",
                "Preview the workflow commands a vessel would run: resolves the profile as resolve_workflow_profile does and returns resolvedProfile, resolutionMode, availableCheckTypes, and commandPreviews.",
                new { type = "object", properties = vesselProperties, required = new[] { "vesselId" } },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    WorkflowProfileVesselArgs request = Read<WorkflowProfileVesselArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.VesselId)) return (object)McpToolError.InvalidArgument("vesselId is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    return (object)await service.PreviewAsync(caller, request.VesselId, request.WorkflowProfileId).ConfigureAwait(false);
                });
        }

        #endregion

        #region Private-Methods

        private static T Read<T>(JsonElement? args) where T : class, new()
        {
            if (!args.HasValue || args.Value.ValueKind != JsonValueKind.Object) return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(args.Value, _JsonOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid arguments: " + ex.Message, ex);
            }
        }

        private static Dictionary<string, object> BuildProfileProperties(bool includeTenant, bool includeId)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>(StringComparer.Ordinal);
            if (includeId) properties["id"] = new { type = "string", description = "Workflow profile ID (wfp_ prefix)" };
            if (includeTenant) properties["tenantId"] = new { type = "string", description = "Tenant to create the profile in (global admins only; ignored for everyone else)" };
            properties["name"] = new { type = "string", description = "Human-readable profile name" };
            properties["description"] = new { type = "string", description = "Optional description" };
            properties["scope"] = new { type = "string", @enum = Enum.GetNames<WorkflowProfileScopeEnum>(), description = "Application scope: Global (default), Fleet (needs fleetId), or Vessel (needs vesselId)" };
            properties["fleetId"] = new { type = "string", description = "Fleet ID for a Fleet-scoped profile (flt_ prefix)" };
            properties["vesselId"] = new { type = "string", description = "Vessel ID for a Vessel-scoped profile (vsl_ prefix)" };
            properties["isDefault"] = new { type = "boolean", description = "Whether this is the default profile at its scope" };
            properties["active"] = new { type = "boolean", description = "Whether the profile is active (default true)" };
            properties["ownershipScope"] = new { type = "string", @enum = Enum.GetNames<ScopeEnum>(), description = "Ownership: UserSpecific (only you) or TenantWide (default for administrators); only tenant and global admins may change it" };
            properties["languageHints"] = new { type = "array", items = new { type = "string" }, description = "Language or framework hints" };
            properties["lintCommand"] = new { type = "string", description = "Lint command" };
            properties["buildCommand"] = new { type = "string", description = "Build command" };
            properties["unitTestCommand"] = new { type = "string", description = "Unit test command" };
            properties["abbreviatedUnitTestCommand"] = new { type = "string", description = "Abbreviated unit test command" };
            properties["integrationTestCommand"] = new { type = "string", description = "Integration test command" };
            properties["e2eTestCommand"] = new { type = "string", description = "End-to-end test command" };
            properties["migrationCommand"] = new { type = "string", description = "Migration command" };
            properties["securityScanCommand"] = new { type = "string", description = "Security scan command" };
            properties["performanceCommand"] = new { type = "string", description = "Performance test command" };
            properties["packageCommand"] = new { type = "string", description = "Package command" };
            properties["deploymentVerificationCommand"] = new { type = "string", description = "Deployment verification command" };
            properties["rollbackVerificationCommand"] = new { type = "string", description = "Rollback verification command" };
            properties["publishArtifactCommand"] = new { type = "string", description = "Publish artifact command" };
            properties["releaseVersioningCommand"] = new { type = "string", description = "Release versioning command" };
            properties["changelogGenerationCommand"] = new { type = "string", description = "Changelog generation command" };
            properties["requiredInputs"] = new
            {
                type = "array",
                description = "Required secret or configuration input references",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        provider = new { type = "string", @enum = Enum.GetNames<WorkflowInputReferenceProviderEnum>(), description = "Where the value comes from" },
                        key = new { type = "string", description = "Variable name, path, or key" },
                        environmentName = new { type = "string", description = "Optional environment the input applies to (must name one of environments)" },
                        description = new { type = "string", description = "Optional description" }
                    },
                    required = new[] { "key" }
                }
            };
            properties["expectedArtifacts"] = new { type = "array", items = new { type = "string" }, description = "Expected build or release artifacts" };
            properties["environments"] = new
            {
                type = "array",
                description = "Environment-specific delivery commands (environment names must be unique)",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        environmentName = new { type = "string", description = "Environment name" },
                        deployCommand = new { type = "string", description = "Deploy command" },
                        rollbackCommand = new { type = "string", description = "Rollback command" },
                        smokeTestCommand = new { type = "string", description = "Smoke test command" },
                        healthCheckCommand = new { type = "string", description = "Health check command" },
                        deploymentVerificationCommand = new { type = "string", description = "Deployment verification command" },
                        rollbackVerificationCommand = new { type = "string", description = "Rollback verification command" }
                    },
                    required = new[] { "environmentName" }
                }
            };
            return properties;
        }

        private static WorkflowProfile BuildProfile(WorkflowProfileArgs request)
        {
            WorkflowProfile profile = new WorkflowProfile
            {
                Name = request.Name!
            };
            WorkflowProfileAccessService.ApplyReplacement(profile, ToIncoming(profile, request), false);
            return profile;
        }

        private static WorkflowProfile ToIncoming(WorkflowProfile basis, WorkflowProfileArgs request)
        {
            return new WorkflowProfile
            {
                Name = String.IsNullOrWhiteSpace(request.Name) ? basis.Name : request.Name,
                Description = request.Description ?? basis.Description,
                Scope = request.Scope ?? basis.Scope,
                OwnershipScope = request.OwnershipScope ?? basis.OwnershipScope,
                FleetId = request.FleetId ?? basis.FleetId,
                VesselId = request.VesselId ?? basis.VesselId,
                IsDefault = request.IsDefault ?? basis.IsDefault,
                Active = request.Active ?? basis.Active,
                LanguageHints = request.LanguageHints ?? basis.LanguageHints,
                LintCommand = request.LintCommand ?? basis.LintCommand,
                BuildCommand = request.BuildCommand ?? basis.BuildCommand,
                UnitTestCommand = request.UnitTestCommand ?? basis.UnitTestCommand,
                AbbreviatedUnitTestCommand = request.AbbreviatedUnitTestCommand ?? basis.AbbreviatedUnitTestCommand,
                IntegrationTestCommand = request.IntegrationTestCommand ?? basis.IntegrationTestCommand,
                E2ETestCommand = request.E2ETestCommand ?? basis.E2ETestCommand,
                MigrationCommand = request.MigrationCommand ?? basis.MigrationCommand,
                SecurityScanCommand = request.SecurityScanCommand ?? basis.SecurityScanCommand,
                PerformanceCommand = request.PerformanceCommand ?? basis.PerformanceCommand,
                PackageCommand = request.PackageCommand ?? basis.PackageCommand,
                DeploymentVerificationCommand = request.DeploymentVerificationCommand ?? basis.DeploymentVerificationCommand,
                RollbackVerificationCommand = request.RollbackVerificationCommand ?? basis.RollbackVerificationCommand,
                PublishArtifactCommand = request.PublishArtifactCommand ?? basis.PublishArtifactCommand,
                ReleaseVersioningCommand = request.ReleaseVersioningCommand ?? basis.ReleaseVersioningCommand,
                ChangelogGenerationCommand = request.ChangelogGenerationCommand ?? basis.ChangelogGenerationCommand,
                RequiredInputs = request.RequiredInputs ?? basis.RequiredInputs,
                ExpectedArtifacts = request.ExpectedArtifacts ?? basis.ExpectedArtifacts,
                Environments = request.Environments ?? basis.Environments
            };
        }

        private static void ApplyPartial(WorkflowProfile existing, WorkflowProfileArgs request, bool canChangeOwnership)
        {
            // Merge the supplied fields over the stored profile, then reuse the REST replacement so blank handling,
            // ownership rules, and list defaults are identical on both surfaces.
            WorkflowProfile incoming = ToIncoming(existing, request);
            WorkflowProfileAccessService.ApplyReplacement(existing, incoming, canChangeOwnership && request.OwnershipScope.HasValue);
        }

        #endregion
    }
}
