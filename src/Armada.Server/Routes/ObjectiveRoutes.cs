namespace Armada.Server.Routes
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Authorization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for objective and intake-style scoping records.
    /// </summary>
    public class ObjectiveRoutes
    {
        private readonly ObjectiveService _Objectives;
        private readonly GitHubIntegrationService _GitHub;
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ObjectiveRoutes(ObjectiveService objectives, GitHubIntegrationService gitHub)
        {
            _Objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
            _GitHub = gitHub ?? throw new ArgumentNullException(nameof(gitHub));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/objectives", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveQuery query = BuildQueryFromRequest(req);
                return await _Objectives.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("List objectives")
                .WithDescription("Returns paginated objective and intake-style records tied to repositories, planning, releases, deployments, and incidents.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("owner", "Optional owner filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("category", "Optional category filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("parentObjectiveId", "Optional parent objective filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Optional fleet filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("planningSessionId", "Optional planning-session filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Optional voyage filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Optional mission filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("checkRunId", "Optional check-run filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("releaseId", "Optional release filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("deploymentId", "Optional deployment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("incidentId", "Optional incident filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("tag", "Optional tag filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Optional status filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("backlogState", "Optional backlog-state filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("kind", "Optional kind filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("priority", "Optional priority filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("effort", "Optional effort filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("targetVersion", "Optional target-version filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional free-text search", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Objective>>("Paginated objectives"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/objectives/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveQuery query = JsonSerializer.Deserialize<ObjectiveQuery>(req.Http.Request.DataAsString, _JsonOptions) ?? new ObjectiveQuery();
                ApplyQuerystringOverrides(req, query);
                return await _Objectives.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Enumerate objectives")
                .WithDescription("Paginated objective enumeration using a JSON body and optional querystring overrides.")
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveQuery>("Objective query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Objective>>("Paginated objectives"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/objectives/reorder", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveReorderRequest request = JsonSerializer.Deserialize<ObjectiveReorderRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? new ObjectiveReorderRequest();

                try
                {
                    return await _Objectives.ReorderAsync(ctx, request).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Reorder objectives")
                .WithDescription("Applies one or more explicit backlog rank updates.")
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveReorderRequest>("Objective reorder request", true))
                .WithResponse(200, OpenApiJson.For<List<Objective>>("Updated objectives"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/objectives/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Objective? objective = await _Objectives.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (objective == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective not found" };
                }

                return objective;
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Get an objective")
                .WithDescription("Returns one objective or intake-style record by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective ID (obj_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Objective>("Objective"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/objectives", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveUpsertRequest request = JsonSerializer.Deserialize<ObjectiveUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ObjectiveUpsertRequest.");

                try
                {
                    Objective objective = await _Objectives.CreateAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return objective;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Create an objective")
                .WithDescription("Creates an internal-first objective or intake record with linked repositories, planning, releases, deployments, and incidents.")
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveUpsertRequest>("Objective create request", true))
                .WithResponse(201, OpenApiJson.For<Objective>("Created objective"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/objectives/import/github", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                GitHubObjectiveImportRequest request = JsonSerializer.Deserialize<GitHubObjectiveImportRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as GitHubObjectiveImportRequest.");

                try
                {
                    Objective objective = await _GitHub.ImportObjectiveAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = String.IsNullOrWhiteSpace(request.ObjectiveId) ? 201 : 200;
                    return objective;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Import an objective from GitHub")
                .WithDescription("Imports or refreshes an objective from a GitHub issue or pull request using the vessel repository URL and resolved GitHub token.")
                .WithRequestBody(OpenApiJson.BodyFor<GitHubObjectiveImportRequest>("GitHub objective import request", true))
                .WithResponse(200, OpenApiJson.For<Objective>("Updated objective"))
                .WithResponse(201, OpenApiJson.For<Objective>("Created objective"))
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/objectives/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveUpsertRequest request = JsonSerializer.Deserialize<ObjectiveUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ObjectiveUpsertRequest.");

                try
                {
                    return await _Objectives.UpdateAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 404 : 400;
                    return new ApiErrorResponse
                    {
                        Error = req.Http.Response.StatusCode == 404 ? ApiResultEnum.NotFound : ApiResultEnum.BadRequest,
                        Message = ex.Message
                    };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Update an objective")
                .WithDescription("Updates scope, acceptance criteria, linked entities, and current status for an objective.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective ID (obj_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveUpsertRequest>("Objective update request", true))
                .WithResponse(200, OpenApiJson.For<Objective>("Updated objective"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/objectives/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                try
                {
                    await _Objectives.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Delete an objective")
                .WithDescription("Deletes one objective and its snapshot chain.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective ID (obj_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/backlog", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveQuery query = BuildQueryFromRequest(req);
                return await _Objectives.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("List backlog items")
                .WithDescription("Returns paginated backlog items using the user-facing backlog terminology.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("owner", "Optional owner filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("category", "Optional category filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("parentObjectiveId", "Optional parent objective filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Optional fleet filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("planningSessionId", "Optional planning-session filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Optional voyage filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Optional mission filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("checkRunId", "Optional check-run filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("releaseId", "Optional release filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("deploymentId", "Optional deployment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("incidentId", "Optional incident filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("tag", "Optional tag filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Optional status filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("backlogState", "Optional backlog-state filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("kind", "Optional kind filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("priority", "Optional priority filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("effort", "Optional effort filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("targetVersion", "Optional target-version filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional free-text search", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Objective>>("Paginated backlog items"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/backlog/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveQuery query = JsonSerializer.Deserialize<ObjectiveQuery>(req.Http.Request.DataAsString, _JsonOptions) ?? new ObjectiveQuery();
                ApplyQuerystringOverrides(req, query);
                return await _Objectives.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Enumerate backlog items")
                .WithDescription("Paginated backlog enumeration using a JSON body and optional querystring overrides.")
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveQuery>("Backlog query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Objective>>("Paginated backlog items"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/backlog/reorder", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveReorderRequest request = JsonSerializer.Deserialize<ObjectiveReorderRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? new ObjectiveReorderRequest();

                try
                {
                    return await _Objectives.ReorderAsync(ctx, request).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Reorder backlog items")
                .WithDescription("Applies one or more explicit backlog rank updates using backlog terminology.")
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveReorderRequest>("Backlog reorder request", true))
                .WithResponse(200, OpenApiJson.For<List<Objective>>("Updated backlog items"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/backlog/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Objective? objective = await _Objectives.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (objective == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Backlog item not found" };
                }

                return objective;
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Get a backlog item")
                .WithDescription("Returns one backlog item by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Backlog item ID (obj_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Objective>("Backlog item"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/backlog", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveUpsertRequest request = JsonSerializer.Deserialize<ObjectiveUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ObjectiveUpsertRequest.");

                try
                {
                    Objective objective = await _Objectives.CreateAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return objective;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Create a backlog item")
                .WithDescription("Creates a backlog item using the user-facing backlog terminology.")
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveUpsertRequest>("Backlog create request", true))
                .WithResponse(201, OpenApiJson.For<Objective>("Created backlog item"))
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/backlog/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                ObjectiveUpsertRequest request = JsonSerializer.Deserialize<ObjectiveUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ObjectiveUpsertRequest.");

                try
                {
                    return await _Objectives.UpdateAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 404 : 400;
                    return new ApiErrorResponse
                    {
                        Error = req.Http.Response.StatusCode == 404 ? ApiResultEnum.NotFound : ApiResultEnum.BadRequest,
                        Message = ex.Message
                    };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Update a backlog item")
                .WithDescription("Updates backlog scope, rank, prioritization, and linked entities.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Backlog item ID (obj_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveUpsertRequest>("Backlog update request", true))
                .WithResponse(200, OpenApiJson.For<Objective>("Updated backlog item"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/backlog/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                try
                {
                    await _Objectives.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Delete a backlog item")
                .WithDescription("Deletes one backlog item and its snapshot chain.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Backlog item ID (obj_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private static ObjectiveQuery BuildQueryFromRequest(ApiRequest req)
        {
            ObjectiveQuery query = new ObjectiveQuery();
            ApplyQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyQuerystringOverrides(ApiRequest req, ObjectiveQuery query)
        {
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (DateTime.TryParse(req.Query.GetValueOrDefault("fromUtc"), out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(req.Query.GetValueOrDefault("toUtc"), out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();
            if (Enum.TryParse(req.Query.GetValueOrDefault("status"), true, out ObjectiveStatusEnum status))
                query.Status = status;
            if (Enum.TryParse(req.Query.GetValueOrDefault("backlogState"), true, out ObjectiveBacklogStateEnum backlogState))
                query.BacklogState = backlogState;
            if (Enum.TryParse(req.Query.GetValueOrDefault("kind"), true, out ObjectiveKindEnum kind))
                query.Kind = kind;
            if (Enum.TryParse(req.Query.GetValueOrDefault("priority"), true, out ObjectivePriorityEnum priority))
                query.Priority = priority;
            if (Enum.TryParse(req.Query.GetValueOrDefault("effort"), true, out ObjectiveEffortEnum effort))
                query.Effort = effort;

            query.Owner = NormalizeEmpty(req.Query.GetValueOrDefault("owner")) ?? query.Owner;
            query.Category = NormalizeEmpty(req.Query.GetValueOrDefault("category")) ?? query.Category;
            query.ParentObjectiveId = NormalizeEmpty(req.Query.GetValueOrDefault("parentObjectiveId")) ?? query.ParentObjectiveId;
            query.VesselId = NormalizeEmpty(req.Query.GetValueOrDefault("vesselId")) ?? query.VesselId;
            query.FleetId = NormalizeEmpty(req.Query.GetValueOrDefault("fleetId")) ?? query.FleetId;
            query.PlanningSessionId = NormalizeEmpty(req.Query.GetValueOrDefault("planningSessionId")) ?? query.PlanningSessionId;
            query.VoyageId = NormalizeEmpty(req.Query.GetValueOrDefault("voyageId")) ?? query.VoyageId;
            query.MissionId = NormalizeEmpty(req.Query.GetValueOrDefault("missionId")) ?? query.MissionId;
            query.CheckRunId = NormalizeEmpty(req.Query.GetValueOrDefault("checkRunId")) ?? query.CheckRunId;
            query.ReleaseId = NormalizeEmpty(req.Query.GetValueOrDefault("releaseId")) ?? query.ReleaseId;
            query.DeploymentId = NormalizeEmpty(req.Query.GetValueOrDefault("deploymentId")) ?? query.DeploymentId;
            query.IncidentId = NormalizeEmpty(req.Query.GetValueOrDefault("incidentId")) ?? query.IncidentId;
            query.Tag = NormalizeEmpty(req.Query.GetValueOrDefault("tag")) ?? query.Tag;
            query.TargetVersion = NormalizeEmpty(req.Query.GetValueOrDefault("targetVersion")) ?? query.TargetVersion;
            query.Search = NormalizeEmpty(req.Query.GetValueOrDefault("search")) ?? query.Search;
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

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
