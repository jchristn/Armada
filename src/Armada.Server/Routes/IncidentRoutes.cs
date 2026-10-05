namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
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
    /// REST API routes for incident management.
    /// </summary>
    public class IncidentRoutes
    {
        private readonly IncidentService _Incidents;
        private readonly ObjectiveService _Objectives;
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public IncidentRoutes(IncidentService incidents, ObjectiveService objectives)
        {
            _Incidents = incidents ?? throw new ArgumentNullException(nameof(incidents));
            _Objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/incidents", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                IncidentQuery query = BuildQueryFromRequest(req);
                return await _Incidents.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Incidents")
                .WithSummary("List incidents")
                .WithDescription("Returns paginated incident records tied to deployments, releases, environments, and hotfix work.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("environmentId", "Optional environment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("deploymentId", "Optional deployment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("releaseId", "Optional release filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Optional mission filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Optional voyage filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Optional status filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("severity", "Optional severity filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional free-text search", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Incident>>("Paginated incidents"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/incidents/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                IncidentQuery query = JsonSerializer.Deserialize<IncidentQuery>(req.Http.Request.DataAsString, _JsonOptions) ?? new IncidentQuery();
                ApplyQuerystringOverrides(req, query);
                return await _Incidents.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Incidents")
                .WithSummary("Enumerate incidents")
                .WithDescription("Paginated incident enumeration using a JSON body and optional querystring overrides.")
                .WithRequestBody(OpenApiJson.BodyFor<IncidentQuery>("Incident query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Incident>>("Paginated incidents"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/incidents/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Incident? incident = await _Incidents.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (incident == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Incident not found" };
                }

                return incident;
            },
            api => api
                .WithTag("Incidents")
                .WithSummary("Get an incident")
                .WithDescription("Returns one incident by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Incident ID (inc_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Incident>("Incident"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/incidents", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                IncidentUpsertRequest request = JsonSerializer.Deserialize<IncidentUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as IncidentUpsertRequest.");

                try
                {
                    await ValidateObjectivesAsync(ctx, request.ObjectiveIds).ConfigureAwait(false);
                    Incident incident = await _Incidents.CreateAsync(ctx, request).ConfigureAwait(false);
                    await LinkObjectivesAsync(ctx, incident, request.ObjectiveIds).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return incident;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Incidents")
                .WithSummary("Create an incident")
                .WithDescription("Creates an operational incident tied to current delivery entities.")
                .WithRequestBody(OpenApiJson.BodyFor<IncidentUpsertRequest>("Incident create request", true))
                .WithResponse(201, OpenApiJson.For<Incident>("Created incident"))
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/incidents/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                IncidentUpsertRequest request = JsonSerializer.Deserialize<IncidentUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as IncidentUpsertRequest.");

                try
                {
                    await ValidateObjectivesAsync(ctx, request.ObjectiveIds).ConfigureAwait(false);
                    Incident incident = await _Incidents.UpdateAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                    await LinkObjectivesAsync(ctx, incident, request.ObjectiveIds).ConfigureAwait(false);
                    return incident;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Incidents")
                .WithSummary("Update an incident")
                .WithDescription("Updates incident status, impact, root cause, recovery, and postmortem details.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Incident ID (inc_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<IncidentUpsertRequest>("Incident update request", true))
                .WithResponse(200, OpenApiJson.For<Incident>("Updated incident"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/incidents/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _Incidents.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Incidents")
                .WithSummary("Delete an incident")
                .WithDescription("Deletes one incident and its snapshots.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Incident ID (inc_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private static IncidentQuery BuildQueryFromRequest(ApiRequest req)
        {
            IncidentQuery query = new IncidentQuery();
            ApplyQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyQuerystringOverrides(ApiRequest req, IncidentQuery query)
        {
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (Enum.TryParse(req.Query.GetValueOrDefault("status"), true, out IncidentStatusEnum status))
                query.Status = status;
            if (Enum.TryParse(req.Query.GetValueOrDefault("severity"), true, out IncidentSeverityEnum severity))
                query.Severity = severity;

            query.VesselId = NormalizeEmpty(req.Query.GetValueOrDefault("vesselId")) ?? query.VesselId;
            query.EnvironmentId = NormalizeEmpty(req.Query.GetValueOrDefault("environmentId")) ?? query.EnvironmentId;
            query.DeploymentId = NormalizeEmpty(req.Query.GetValueOrDefault("deploymentId")) ?? query.DeploymentId;
            query.ReleaseId = NormalizeEmpty(req.Query.GetValueOrDefault("releaseId")) ?? query.ReleaseId;
            query.MissionId = NormalizeEmpty(req.Query.GetValueOrDefault("missionId")) ?? query.MissionId;
            query.VoyageId = NormalizeEmpty(req.Query.GetValueOrDefault("voyageId")) ?? query.VoyageId;
            query.Search = NormalizeEmpty(req.Query.GetValueOrDefault("search")) ?? query.Search;
        }

        private async Task ValidateObjectivesAsync(AuthContext auth, IEnumerable<string>? objectiveIds)
        {
            if (objectiveIds == null) return;

            foreach (string objectiveId in objectiveIds)
            {
                string? normalized = NormalizeEmpty(objectiveId);
                if (String.IsNullOrWhiteSpace(normalized))
                    continue;

                Objective? objective = await _Objectives.ReadAsync(auth, normalized).ConfigureAwait(false);
                if (objective == null)
                    throw new KeyNotFoundException("Objective not found: " + normalized);
            }
        }

        private async Task LinkObjectivesAsync(AuthContext auth, Incident incident, IEnumerable<string>? explicitObjectiveIds)
        {
            HashSet<string> objectiveIds = await ResolveObjectiveIdsAsync(
                auth,
                explicitObjectiveIds,
                incident.DeploymentId,
                incident.ReleaseId,
                incident.MissionId,
                incident.VoyageId,
                incident.RollbackDeploymentId).ConfigureAwait(false);

            foreach (string objectiveId in objectiveIds)
            {
                await _Objectives.LinkIncidentAsync(auth, objectiveId, incident.Id).ConfigureAwait(false);
            }
        }

        private async Task<HashSet<string>> ResolveObjectiveIdsAsync(
            AuthContext auth,
            IEnumerable<string>? explicitObjectiveIds,
            string? deploymentId,
            string? releaseId,
            string? missionId,
            string? voyageId,
            string? rollbackDeploymentId)
        {
            HashSet<string> objectiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (explicitObjectiveIds != null)
            {
                foreach (string objectiveId in explicitObjectiveIds)
                {
                    string? normalized = NormalizeEmpty(objectiveId);
                    if (!String.IsNullOrWhiteSpace(normalized))
                        objectiveIds.Add(normalized);
                }
            }

            await AddObjectivesForQueryAsync(auth, objectiveIds, query => query.DeploymentId = NormalizeEmpty(deploymentId)).ConfigureAwait(false);
            await AddObjectivesForQueryAsync(auth, objectiveIds, query => query.DeploymentId = NormalizeEmpty(rollbackDeploymentId)).ConfigureAwait(false);
            await AddObjectivesForQueryAsync(auth, objectiveIds, query => query.ReleaseId = NormalizeEmpty(releaseId)).ConfigureAwait(false);
            await AddObjectivesForQueryAsync(auth, objectiveIds, query => query.MissionId = NormalizeEmpty(missionId)).ConfigureAwait(false);
            await AddObjectivesForQueryAsync(auth, objectiveIds, query => query.VoyageId = NormalizeEmpty(voyageId)).ConfigureAwait(false);
            return objectiveIds;
        }

        private async Task AddObjectivesForQueryAsync(
            AuthContext auth,
            HashSet<string> objectiveIds,
            Action<ObjectiveQuery> configure)
        {
            ObjectiveQuery query = new ObjectiveQuery
            {
                PageNumber = 1,
                PageSize = 200
            };
            configure(query);

            if (String.IsNullOrWhiteSpace(query.DeploymentId)
                && String.IsNullOrWhiteSpace(query.ReleaseId)
                && String.IsNullOrWhiteSpace(query.MissionId)
                && String.IsNullOrWhiteSpace(query.VoyageId))
                return;

            while (true)
            {
                EnumerationResult<Objective> results = await _Objectives.EnumerateAsync(auth, query).ConfigureAwait(false);
                foreach (Objective objective in results.Objects)
                    objectiveIds.Add(objective.Id);

                if (results.PageNumber >= results.TotalPages || results.Objects.Count == 0)
                    return;

                query.PageNumber++;
            }
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
