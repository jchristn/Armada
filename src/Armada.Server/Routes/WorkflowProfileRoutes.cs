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
        private readonly WorkflowProfileAccessService _access;
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
            _access = new WorkflowProfileAccessService(_database, _workflowProfiles);
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

                return await _access.ValidateAsync(ctx, profile).ConfigureAwait(false);
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

                try
                {
                    return await _access.PreviewAsync(ctx, req.Parameters["vesselId"], QueryValueReader.Read(req.Query, "workflowProfileId")).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
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

                try
                {
                    return await _access.ResolveAsync(ctx, req.Parameters["vesselId"], QueryValueReader.Read(req.Query, "workflowProfileId")).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
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

                try
                {
                    WorkflowProfile created = await _access.CreateAsync(ctx, profile).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return created;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Create a workflow profile")
                .WithDescription("Creates a tenant-scoped workflow profile used to run builds, tests, release helpers, and deploy checks.")
                .WithRequestBody(OpenApiJson.BodyFor<WorkflowProfile>("Workflow profile", true))
                .WithResponse(201, OpenApiJson.For<WorkflowProfile>("Created workflow profile"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workflow-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    return await _access.GetAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
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

                try
                {
                    // The body is read inside apply, after the 404 and 403 checks, as it was before the shared service.
                    return await _access.UpdateAsync(
                        ctx,
                        req.Parameters["id"],
                        (existing, canChangeOwnership) =>
                        {
                            WorkflowProfile incoming = JsonSerializer.Deserialize<WorkflowProfile>(req.Http.Request.DataAsString, _bodyJsonOptions)
                                ?? throw new InvalidOperationException("Request body could not be deserialized as WorkflowProfile.");
                            WorkflowProfileAccessService.ApplyReplacement(existing, incoming, canChangeOwnership);
                        }).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is KeyNotFoundException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Update a workflow profile")
                .WithDescription("Updates an existing workflow profile.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Workflow profile ID (wfp_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<WorkflowProfile>("Workflow profile", true))
                .WithResponse(200, OpenApiJson.For<WorkflowProfile>("Updated workflow profile"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/workflow-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _access.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }

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

            app.Post("/api/v1/workflow-profiles/delete/multiple", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeleteMultipleRequest? body = String.IsNullOrWhiteSpace(req.Http.Request.DataAsString)
                    ? null
                    : JsonSerializer.Deserialize<DeleteMultipleRequest>(req.Http.Request.DataAsString, _bodyJsonOptions);
                try
                {
                    return await _access.DeleteMultipleAsync(ctx, body?.Ids).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("WorkflowProfiles")
                .WithSummary("Batch delete multiple workflow profiles")
                .WithDescription("Permanently deletes multiple workflow profiles by ID (at most 1000 ids; more is 400 CollectionLimitExceeded). Ids that are missing, outside the caller's scope, or not editable by the caller are skipped with a reason. This cannot be undone.")
                .WithRequestBody(OpenApiJson.BodyFor<DeleteMultipleRequest>("List of workflow profile IDs to delete"))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete result summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));
        }

        private static ApiErrorResponse BuildAuthError(ApiRequest req)
        {
            return new ApiErrorResponse
            {
                Error = req.Http.Response.StatusCode == 401 ? ApiResultEnum.NotAuthorized : ApiResultEnum.Forbidden,
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
            if (int.TryParse(QueryValueReader.Read(req.Query, "pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (int.TryParse(QueryValueReader.Read(req.Query, "pageSize"), out int pageSize))
                query.PageSize = pageSize;
            if (Enum.TryParse(QueryValueReader.Read(req.Query, "scope"), true, out WorkflowProfileScopeEnum scope))
                query.Scope = scope;
            if (DateTime.TryParse(QueryValueReader.Read(req.Query, "fromUtc"), out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(QueryValueReader.Read(req.Query, "toUtc"), out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();

            query.FleetId = NormalizeEmpty(QueryValueReader.Read(req.Query, "fleetId")) ?? query.FleetId;
            query.VesselId = NormalizeEmpty(QueryValueReader.Read(req.Query, "vesselId")) ?? query.VesselId;
            query.Search = NormalizeEmpty(QueryValueReader.Read(req.Query, "search")) ?? query.Search;

            if (TryParseNullableBool(QueryValueReader.Read(req.Query, "active"), out bool? active))
                query.Active = active;
        }

        private static void ApplyReadScope(AuthContext ctx, WorkflowProfileQuery query)
        {
            if (ctx.IsAdmin) return;
            query.TenantId = ctx.TenantId;
            query.UserId = null;
            // A regular user sees tenant-wide rows plus their own; the data source applies it before counting, so
            // TotalRecords and every page are exact for the caller.
            query.ViewerUserId = ctx.IsTenantAdmin ? null : ctx.UserId;
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
