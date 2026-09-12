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
    /// REST API routes for project-profile management and resolution.
    /// </summary>
    public class ProjectProfileRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly ProjectProfileService _projectProfiles;
        private readonly IPromptTemplateService _promptTemplates;
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
        public ProjectProfileRoutes(
            DatabaseDriver database,
            ProjectProfileService projectProfiles,
            IPromptTemplateService promptTemplates,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _projectProfiles = projectProfiles ?? throw new ArgumentNullException(nameof(projectProfiles));
            _promptTemplates = promptTemplates ?? throw new ArgumentNullException(nameof(promptTemplates));
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
            app.Get("/api/v1/project-profiles", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ProjectProfileQuery query = new ProjectProfileQuery();
                ApplyQuerystringOverrides(req, query);
                ApplyReadScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<ProjectProfile> result = await _database.ProjectProfiles.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.OwnershipScope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("List project profiles")
                .WithDescription("Returns paginated project profiles scoped to the authenticated tenant.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("scope", "Optional scope filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Optional fleet filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional name/description search", false))
                .WithParameter(OpenApiParameterMetadata.Query("active", "Optional active-state filter", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<ProjectProfile>>("Paginated project profiles"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/project-profiles/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ProjectProfileQuery query = JsonSerializer.Deserialize<ProjectProfileQuery>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? new ProjectProfileQuery();
                ApplyQuerystringOverrides(req, query);
                ApplyReadScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<ProjectProfile> result = await _database.ProjectProfiles.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.OwnershipScope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("Enumerate project profiles")
                .WithDescription("Paginated project-profile enumeration with body or query filters.")
                .WithRequestBody(OpenApiJson.BodyFor<ProjectProfileQuery>("Project-profile query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<ProjectProfile>>("Paginated project profiles"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/project-profiles/validate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ProjectProfile profile = JsonSerializer.Deserialize<ProjectProfile>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ProjectProfile.");

                if (!ctx.IsAdmin)
                    profile.TenantId = ctx.TenantId;

                return await _projectProfiles.ValidateAsync(profile).ConfigureAwait(false);
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("Validate a project profile")
                .WithDescription("Validates a project-profile definition (scope consistency, referenced entities, persona overrides).")
                .WithRequestBody(OpenApiJson.BodyFor<ProjectProfile>("Project profile", true))
                .WithResponse(200, OpenApiJson.For<ProjectProfileValidationResult>("Validation result"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/project-profiles/resolve/vessels/{vesselId}", async (ApiRequest req) =>
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

                string? explicitProfileId = NormalizeEmpty(req.Query.GetValueOrDefault("projectProfileId"));
                ProjectProfileResolutionResult resolved = await _projectProfiles.ResolveWithModeForVesselAsync(ctx, vessel, explicitProfileId).ConfigureAwait(false);
                if (resolved.Profile == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No project profile could be resolved for this vessel" };
                }

                return resolved;
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("Resolve the active project profile for a vessel")
                .WithDescription("Resolves the best matching active project profile for a vessel using vessel, fleet, then global precedence.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID"))
                .WithParameter(OpenApiParameterMetadata.Query("projectProfileId", "Optional explicit project-profile override", false))
                .WithResponse(200, OpenApiJson.For<ProjectProfileResolutionResult>("Resolved project profile"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/project-profiles/{id}/persona-preview/{persona}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ProjectProfile? profile = await _database.ProjectProfiles.ReadAsync(
                    req.Parameters["id"],
                    BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (profile == null || !ScopedVisibility.CanView(ctx, profile.OwnershipScope, profile.TenantId, profile.UserId))
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Project profile not found" };
                }

                string persona = req.Parameters["persona"];
                PersonaPromptPreview preview = await ProjectProfileService.BuildPersonaPreviewAsync(profile, persona, _promptTemplates).ConfigureAwait(false);
                return preview;
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("Preview a persona prompt for a project profile")
                .WithDescription("Returns the base and effective (override-applied) persona prompt so the dashboard can render a live diff.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Project profile ID (ppf_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("persona", "Persona name (e.g. Architect, Worker, Test Engineer)"))
                .WithResponse(200, OpenApiJson.For<PersonaPromptPreview>("Persona prompt preview"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/project-profiles", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ProjectProfile profile = JsonSerializer.Deserialize<ProjectProfile>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ProjectProfile.");

                profile.TenantId = ctx.IsAdmin ? (NormalizeEmpty(profile.TenantId) ?? ctx.TenantId) : ctx.TenantId;
                profile.UserId = ctx.UserId;
                profile.OwnershipScope = ScopedVisibility.ResolveCreateScope(ctx, profile.OwnershipScope);

                ProjectProfileValidationResult validation = await _projectProfiles.ValidateAsync(profile).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = String.Join(" ", validation.Errors) };
                }

                await EnsureUniqueDefaultAsync(profile).ConfigureAwait(false);
                ProjectProfile created = await _database.ProjectProfiles.CreateAsync(profile).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return created;
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("Create a project profile")
                .WithDescription("Creates a tenant-scoped project profile bundling a project's pipeline, workflow profile, persona overrides, and skills.")
                .WithRequestBody(OpenApiJson.BodyFor<ProjectProfile>("Project profile", true))
                .WithResponse(201, OpenApiJson.For<ProjectProfile>("Created project profile"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/project-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ProjectProfile? profile = await _database.ProjectProfiles.ReadAsync(
                    req.Parameters["id"],
                    BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (profile == null || !ScopedVisibility.CanView(ctx, profile.OwnershipScope, profile.TenantId, profile.UserId))
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Project profile not found" };
                }

                return profile;
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("Get a project profile")
                .WithDescription("Returns a single project profile by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Project profile ID (ppf_ prefix)"))
                .WithResponse(200, OpenApiJson.For<ProjectProfile>("Project profile"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/project-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ProjectProfile? existing = await _database.ProjectProfiles.ReadAsync(
                    req.Parameters["id"],
                    BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.OwnershipScope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Project profile not found" };
                }
                if (!ScopedVisibility.CanEdit(ctx, existing.OwnershipScope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only modify your own project profiles; a tenant-wide profile requires a tenant admin." };
                }

                ProjectProfile incoming = JsonSerializer.Deserialize<ProjectProfile>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ProjectProfile.");

                existing.Name = incoming.Name;
                existing.Description = incoming.Description;
                existing.Scope = incoming.Scope;
                if (ctx.IsAdmin || ctx.IsTenantAdmin) existing.OwnershipScope = incoming.OwnershipScope;
                existing.FleetId = NormalizeEmpty(incoming.FleetId);
                existing.VesselId = NormalizeEmpty(incoming.VesselId);
                existing.IsDefault = incoming.IsDefault;
                existing.Active = incoming.Active;
                existing.DefaultPipelineId = NormalizeEmpty(incoming.DefaultPipelineId);
                existing.WorkflowProfileId = NormalizeEmpty(incoming.WorkflowProfileId);
                existing.PersonaOverrides = incoming.PersonaOverrides ?? new List<PersonaOverride>();
                existing.Skills = incoming.Skills ?? new List<string>();
                existing.LastUpdateUtc = DateTime.UtcNow;

                ProjectProfileValidationResult validation = await _projectProfiles.ValidateAsync(existing).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = String.Join(" ", validation.Errors) };
                }

                await EnsureUniqueDefaultAsync(existing).ConfigureAwait(false);
                ProjectProfile updated = await _database.ProjectProfiles.UpdateAsync(existing).ConfigureAwait(false);
                return updated;
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("Update a project profile")
                .WithDescription("Updates an existing project profile.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Project profile ID (ppf_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ProjectProfile>("Project profile", true))
                .WithResponse(200, OpenApiJson.For<ProjectProfile>("Updated project profile"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/project-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ProjectProfile? existing = await _database.ProjectProfiles.ReadAsync(
                    req.Parameters["id"],
                    BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.OwnershipScope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Project profile not found" };
                }
                if (!ScopedVisibility.CanEdit(ctx, existing.OwnershipScope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only delete your own project profiles; a tenant-wide profile requires a tenant admin." };
                }

                await _database.ProjectProfiles.DeleteAsync(existing.Id, BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("ProjectProfiles")
                .WithSummary("Delete a project profile")
                .WithDescription("Deletes a project profile.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Project profile ID (ppf_ prefix)"))
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

        private static void ApplyQuerystringOverrides(ApiRequest req, ProjectProfileQuery query)
        {
            if (int.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (int.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (Enum.TryParse(req.Query.GetValueOrDefault("scope"), true, out ProjectProfileScopeEnum scope))
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

        private static void ApplyReadScope(AuthContext ctx, ProjectProfileQuery query)
        {
            if (ctx.IsAdmin) return;
            query.TenantId = ctx.TenantId;
            query.UserId = null;
        }

        private static ProjectProfileQuery BuildScopedReadQuery(AuthContext ctx)
        {
            ProjectProfileQuery query = new ProjectProfileQuery();
            ApplyReadScope(ctx, query);
            return query;
        }

        private async Task EnsureUniqueDefaultAsync(ProjectProfile profile)
        {
            if (!profile.IsDefault) return;

            ProjectProfileQuery query = new ProjectProfileQuery
            {
                TenantId = profile.TenantId,
                Scope = profile.Scope,
                PageNumber = 1,
                PageSize = 1000
            };

            if (profile.Scope == ProjectProfileScopeEnum.Fleet)
                query.FleetId = profile.FleetId;
            if (profile.Scope == ProjectProfileScopeEnum.Vessel)
                query.VesselId = profile.VesselId;

            List<ProjectProfile> peers = await _database.ProjectProfiles.EnumerateAllAsync(query).ConfigureAwait(false);
            foreach (ProjectProfile peer in peers.Where(item => item.IsDefault && !String.Equals(item.Id, profile.Id, StringComparison.Ordinal)))
            {
                peer.IsDefault = false;
                await _database.ProjectProfiles.UpdateAsync(peer).ConfigureAwait(false);
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
