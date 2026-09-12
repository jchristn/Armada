namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for workflow-profile management and resolution.
    /// </summary>
    public class WorkflowProfileRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly WorkflowProfileService _workflowProfiles;
        private readonly JsonSerializerOptions _jsonOptions;
        private static readonly JsonSerializerOptions _bodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WorkflowProfileRoutes(
            DatabaseDriver database,
            WorkflowProfileService workflowProfiles,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _workflowProfiles = workflowProfiles ?? throw new ArgumentNullException(nameof(workflowProfiles));
            _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/workflow-profiles", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                WorkflowProfileQuery query = BuildQueryFromRequest(req);
                ApplyReadScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<WorkflowProfile> result = await _database.WorkflowProfiles.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.OwnershipScope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("List workflow profiles")
                .WithDescription("Returns paginated workflow profiles scoped to the authenticated tenant.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("scope", "Optional scope filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Optional fleet filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional name/description search", false))
                .WithParameter(OpenApiParameterMetadata.Query("active", "Optional active-state filter", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<WorkflowProfile>>("Paginated workflow profiles"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/workflow-profiles/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                WorkflowProfileQuery query = JsonSerializer.Deserialize<WorkflowProfileQuery>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? new WorkflowProfileQuery();
                ApplyQuerystringOverrides(req, query);
                ApplyReadScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<WorkflowProfile> result = await _database.WorkflowProfiles.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.OwnershipScope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Enumerate workflow profiles")
                .WithDescription("Paginated workflow-profile enumeration with body or query filters.")
                .WithRequestBody(OpenApiJson.BodyFor<WorkflowProfileQuery>("Workflow-profile query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<WorkflowProfile>>("Paginated workflow profiles"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/workflow-profiles/validate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                WorkflowProfile profile = JsonSerializer.Deserialize<WorkflowProfile>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as WorkflowProfile.");

                if (!ctx.IsAdmin)
                    profile.TenantId = ctx.TenantId;

                return await _workflowProfiles.ValidateAsync(profile).ConfigureAwait(false);
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Validate a workflow profile")
                .WithDescription("Validates a workflow-profile definition and previews the resolved command set and available check types.")
                .WithRequestBody(OpenApiJson.BodyFor<WorkflowProfile>("Workflow profile", true))
                .WithResponse(200, OpenApiJson.For<WorkflowProfileValidationResult>("Validation result"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workflow-profiles/preview/vessels/{vesselId}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                string vesselId = req.Parameters["vesselId"];
                Vessel? vessel = await ReadAccessibleVesselAsync(ctx, vesselId).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                string? explicitProfileId = NormalizeEmpty(req.Query.GetValueOrDefault("workflowProfileId"));
                WorkflowProfileResolutionPreviewResult? preview = await _workflowProfiles.PreviewForVesselAsync(ctx, vessel, explicitProfileId).ConfigureAwait(false);
                if (preview == null || preview.ResolvedProfile == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No workflow profile could be resolved for this vessel" };
                }

                return preview;
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Preview resolved workflow commands for a vessel")
                .WithDescription("Resolves the active workflow profile for a vessel and returns the fully resolved command set, available check types, and resolution mode.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID"))
                .WithParameter(OpenApiParameterMetadata.Query("workflowProfileId", "Optional explicit workflow-profile override", false))
                .WithResponse(200, OpenApiJson.For<WorkflowProfileResolutionPreviewResult>("Resolved workflow preview"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workflow-profiles/resolve/vessels/{vesselId}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                string vesselId = req.Parameters["vesselId"];
                Vessel? vessel = await ReadAccessibleVesselAsync(ctx, vesselId).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                string? explicitProfileId = NormalizeEmpty(req.Query.GetValueOrDefault("workflowProfileId"));
                WorkflowProfile? profile = await _workflowProfiles.ResolveForVesselAsync(ctx, vessel, explicitProfileId).ConfigureAwait(false);
                if (profile == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No workflow profile could be resolved for this vessel" };
                }

                return profile;
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Resolve the active workflow profile for a vessel")
                .WithDescription("Resolves the best matching active workflow profile for a vessel using vessel, fleet, then global precedence.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID"))
                .WithParameter(OpenApiParameterMetadata.Query("workflowProfileId", "Optional explicit workflow-profile override", false))
                .WithResponse(200, OpenApiJson.For<WorkflowProfile>("Resolved workflow profile"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/workflow-profiles", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                WorkflowProfile profile = JsonSerializer.Deserialize<WorkflowProfile>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as WorkflowProfile.");

                profile.TenantId = ctx.IsAdmin ? (NormalizeEmpty(profile.TenantId) ?? ctx.TenantId) : ctx.TenantId;
                profile.UserId = ctx.UserId;
                profile.OwnershipScope = ScopedVisibility.ResolveCreateScope(ctx, profile.OwnershipScope);

                WorkflowProfileValidationResult validation = await _workflowProfiles.ValidateAsync(profile).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse
                    {
                        Error = ApiResultEnum.BadRequest,
                        Message = String.Join(" ", validation.Errors)
                    };
                }

                await EnsureUniqueDefaultAsync(profile).ConfigureAwait(false);
                WorkflowProfile created = await _database.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return created;
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Create a workflow profile")
                .WithDescription("Creates a tenant-scoped workflow profile used to run builds, tests, release helpers, and deploy checks.")
                .WithRequestBody(OpenApiJson.BodyFor<WorkflowProfile>("Workflow profile", true))
                .WithResponse(201, OpenApiJson.For<WorkflowProfile>("Created workflow profile"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workflow-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                WorkflowProfile? profile = await _database.WorkflowProfiles.ReadAsync(
                    req.Parameters["id"],
                    BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (profile == null || !ScopedVisibility.CanView(ctx, profile.OwnershipScope, profile.TenantId, profile.UserId))
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Workflow profile not found" };
                }

                return profile;
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Get a workflow profile")
                .WithDescription("Returns a single workflow profile by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Workflow profile ID (wfp_ prefix)"))
                .WithResponse(200, OpenApiJson.For<WorkflowProfile>("Workflow profile"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/workflow-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                WorkflowProfile? existing = await _database.WorkflowProfiles.ReadAsync(
                    req.Parameters["id"],
                    BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.OwnershipScope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Workflow profile not found" };
                }
                if (!ScopedVisibility.CanEdit(ctx, existing.OwnershipScope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only modify your own workflow profiles; a tenant-wide profile requires a tenant admin." };
                }

                WorkflowProfile incoming = JsonSerializer.Deserialize<WorkflowProfile>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as WorkflowProfile.");

                existing.Name = incoming.Name;
                existing.Description = incoming.Description;
                existing.Scope = incoming.Scope;
                if (ctx.IsAdmin || ctx.IsTenantAdmin) existing.OwnershipScope = incoming.OwnershipScope;
                existing.FleetId = NormalizeEmpty(incoming.FleetId);
                existing.VesselId = NormalizeEmpty(incoming.VesselId);
                existing.IsDefault = incoming.IsDefault;
                existing.Active = incoming.Active;
                existing.LanguageHints = incoming.LanguageHints ?? new List<string>();
                existing.LintCommand = NormalizeEmpty(incoming.LintCommand);
                existing.BuildCommand = NormalizeEmpty(incoming.BuildCommand);
                existing.UnitTestCommand = NormalizeEmpty(incoming.UnitTestCommand);
                existing.IntegrationTestCommand = NormalizeEmpty(incoming.IntegrationTestCommand);
                existing.E2ETestCommand = NormalizeEmpty(incoming.E2ETestCommand);
                existing.MigrationCommand = NormalizeEmpty(incoming.MigrationCommand);
                existing.SecurityScanCommand = NormalizeEmpty(incoming.SecurityScanCommand);
                existing.PerformanceCommand = NormalizeEmpty(incoming.PerformanceCommand);
                existing.PackageCommand = NormalizeEmpty(incoming.PackageCommand);
                existing.DeploymentVerificationCommand = NormalizeEmpty(incoming.DeploymentVerificationCommand);
                existing.RollbackVerificationCommand = NormalizeEmpty(incoming.RollbackVerificationCommand);
                existing.PublishArtifactCommand = NormalizeEmpty(incoming.PublishArtifactCommand);
                existing.ReleaseVersioningCommand = NormalizeEmpty(incoming.ReleaseVersioningCommand);
                existing.ChangelogGenerationCommand = NormalizeEmpty(incoming.ChangelogGenerationCommand);
                existing.RequiredInputs = incoming.RequiredInputs ?? new List<WorkflowInputReference>();
                existing.ExpectedArtifacts = incoming.ExpectedArtifacts ?? new List<string>();
                existing.Environments = incoming.Environments ?? new List<WorkflowEnvironmentProfile>();
                existing.LastUpdateUtc = DateTime.UtcNow;

                WorkflowProfileValidationResult validation = await _workflowProfiles.ValidateAsync(existing).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse
                    {
                        Error = ApiResultEnum.BadRequest,
                        Message = String.Join(" ", validation.Errors)
                    };
                }

                await EnsureUniqueDefaultAsync(existing).ConfigureAwait(false);
                WorkflowProfile updated = await _database.WorkflowProfiles.UpdateAsync(existing).ConfigureAwait(false);
                return updated;
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Update a workflow profile")
                .WithDescription("Updates an existing workflow profile.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Workflow profile ID (wfp_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<WorkflowProfile>("Workflow profile", true))
                .WithResponse(200, OpenApiJson.For<WorkflowProfile>("Updated workflow profile"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/workflow-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                WorkflowProfile? existing = await _database.WorkflowProfiles.ReadAsync(
                    req.Parameters["id"],
                    BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.OwnershipScope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Workflow profile not found" };
                }
                if (!ScopedVisibility.CanEdit(ctx, existing.OwnershipScope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only delete your own workflow profiles; a tenant-wide profile requires a tenant admin." };
                }

                await _database.WorkflowProfiles.DeleteAsync(existing.Id, BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Delete a workflow profile")
                .WithDescription("Deletes a workflow profile.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Workflow profile ID (wfp_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private static ApiErrorResponse BuildAuthError(ApiRequest req)
        {
            return new ApiErrorResponse
            {
                Error = ApiResultEnum.BadRequest,
                Message = req.Http.Response.StatusCode == 401
                    ? "Authentication required"
                    : "You do not have permission to perform this action"
            };
        }

        private static async Task<AuthContext?> AuthorizeAsync(
            ApiRequest req,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
            if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
            {
                req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                return null;
            }

            return ctx;
        }

        private static WorkflowProfileQuery BuildQueryFromRequest(ApiRequest req)
        {
            WorkflowProfileQuery query = new WorkflowProfileQuery();
            ApplyQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyQuerystringOverrides(ApiRequest req, WorkflowProfileQuery query)
        {
            if (int.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (int.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (Enum.TryParse(req.Query.GetValueOrDefault("scope"), true, out WorkflowProfileScopeEnum scope))
                query.Scope = scope;
            if (DateTime.TryParse(req.Query.GetValueOrDefault("fromUtc"), out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(req.Query.GetValueOrDefault("toUtc"), out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();

            query.FleetId = NormalizeEmpty(req.Query.GetValueOrDefault("fleetId")) ?? query.FleetId;
            query.VesselId = NormalizeEmpty(req.Query.GetValueOrDefault("vesselId")) ?? query.VesselId;
            query.Search = NormalizeEmpty(req.Query.GetValueOrDefault("search")) ?? query.Search;

            if (TryParseNullableBool(req.Query.GetValueOrDefault("active"), out bool? active))
                query.Active = active;
        }

        private static void ApplyReadScope(AuthContext ctx, WorkflowProfileQuery query)
        {
            if (ctx.IsAdmin) return;
            query.TenantId = ctx.TenantId;
            query.UserId = null;
        }

        private static WorkflowProfileQuery BuildScopedReadQuery(AuthContext ctx)
        {
            WorkflowProfileQuery query = new WorkflowProfileQuery();
            ApplyReadScope(ctx, query);
            return query;
        }

        private async Task EnsureUniqueDefaultAsync(WorkflowProfile profile)
        {
            if (!profile.IsDefault) return;

            WorkflowProfileQuery query = new WorkflowProfileQuery
            {
                TenantId = profile.TenantId,
                Scope = profile.Scope,
                PageNumber = 1,
                PageSize = 1000
            };

            if (profile.Scope == WorkflowProfileScopeEnum.Fleet)
                query.FleetId = profile.FleetId;
            if (profile.Scope == WorkflowProfileScopeEnum.Vessel)
                query.VesselId = profile.VesselId;

            List<WorkflowProfile> peers = await _database.WorkflowProfiles.EnumerateAllAsync(query).ConfigureAwait(false);
            foreach (WorkflowProfile peer in peers.Where(item => item.IsDefault && !String.Equals(item.Id, profile.Id, StringComparison.Ordinal)))
            {
                peer.IsDefault = false;
                await _database.WorkflowProfiles.UpdateAsync(peer).ConfigureAwait(false);
            }
        }

        private async Task<Vessel?> ReadAccessibleVesselAsync(AuthContext ctx, string vesselId)
        {
            if (ctx.IsAdmin)
                return await _database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
            if (ctx.IsTenantAdmin)
                return await _database.Vessels.ReadAsync(ctx.TenantId!, vesselId).ConfigureAwait(false);
            return await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, vesselId).ConfigureAwait(false);
        }

        private static bool TryParseNullableBool(string? value, out bool? result)
        {
            result = null;
            if (String.IsNullOrWhiteSpace(value)) return false;

            if (bool.TryParse(value, out bool parsed))
            {
                result = parsed;
                return true;
            }

            if (value == "1")
            {
                result = true;
                return true;
            }

            if (value == "0")
            {
                result = false;
                return true;
            }

            return false;
        }

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
