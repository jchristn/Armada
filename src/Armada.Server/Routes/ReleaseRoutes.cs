namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for release management.
    /// </summary>
    public class ReleaseRoutes
    {
        private readonly ReleaseService _Releases;
        private readonly ObjectiveService _Objectives;
        private readonly GitHubIntegrationService _GitHub;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReleaseRoutes(ReleaseService releases, ObjectiveService objectives, GitHubIntegrationService gitHub)
        {
            _Releases = releases ?? throw new ArgumentNullException(nameof(releases));
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
            app.Get("/api/v1/releases", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ReleaseQuery query = BuildQueryFromRequest(req);
                return await _Releases.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Releases")
                .WithSummary("List releases")
                .WithDescription("Returns paginated first-class release records.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("workflowProfileId", "Optional workflow-profile filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Optional linked voyage filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Optional linked mission filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("checkRunId", "Optional linked check-run filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Optional release status filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional free-text search", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Release>>("Paginated release records"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/releases/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ReleaseQuery query = JsonSerializer.Deserialize<ReleaseQuery>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? new ReleaseQuery();
                ApplyQuerystringOverrides(req, query);
                return await _Releases.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Releases")
                .WithSummary("Enumerate releases")
                .WithDescription("Paginated release enumeration using a JSON body and optional querystring overrides.")
                .WithRequestBody(OpenApiJson.BodyFor<ReleaseQuery>("Release query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Release>>("Paginated release records"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/releases", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ReleaseUpsertRequest request = JsonSerializer.Deserialize<ReleaseUpsertRequest>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ReleaseUpsertRequest.");

                try
                {
                    CollectionBounds.Validate(request);
                    await ValidateObjectivesAsync(ctx, request.ObjectiveIds).ConfigureAwait(false);
                    Release release = await _Releases.CreateAsync(ctx, request).ConfigureAwait(false);
                    await LinkObjectivesAsync(ctx, request.ObjectiveIds, release.Id).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return release;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Releases")
                .WithSummary("Create a release")
                .WithDescription("Creates a draft or shipped release record from linked voyages, missions, and check runs.")
                .WithRequestBody(OpenApiJson.BodyFor<ReleaseUpsertRequest>("Release create request", true))
                .WithResponse(201, OpenApiJson.For<Release>("Created release"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/releases/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Release? release = await _Releases.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (release == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Release not found" };
                }

                return release;
            },
            api => api
                .WithTag("Releases")
                .WithSummary("Get a release")
                .WithDescription("Returns one release by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Release ID (rel_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Release>("Release"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/releases/{id}/github/pull-requests/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Release? release = await _Releases.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (release == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Release not found" };
                }

                try
                {
                    EnumerationQuery query = EnumerationRequestReader.Read(req, _BodyJsonOptions);
                    return await _GitHub.EnumerateReleasePullRequestsAsync(ctx, release, query).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Releases")
                .WithSummary("Enumerate a release's GitHub pull requests")
                .WithDescription("One page of the GitHub pull requests linked to a release's missions, ordered by pull request number (newest first) then repository. Only the requested page is read from GitHub; TotalRecords counts the distinct pull requests across the release's linked missions (at most 1000). Paging comes from the JSON body (EnumerationQuery) or the querystring (pageNumber, pageSize 1 to 1000, default 10).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Release ID (rel_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<GitHubPullRequestDetail>>("One page"))
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/releases/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ReleaseUpsertRequest request = JsonSerializer.Deserialize<ReleaseUpsertRequest>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ReleaseUpsertRequest.");

                try
                {
                    CollectionBounds.Validate(request);
                    await ValidateObjectivesAsync(ctx, request.ObjectiveIds).ConfigureAwait(false);
                    Release release = await _Releases.UpdateAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                    await LinkObjectivesAsync(ctx, request.ObjectiveIds, release.Id).ConfigureAwait(false);
                    return release;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Releases")
                .WithSummary("Update a release")
                .WithDescription("Updates a release record and revalidates its linked work.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Release ID (rel_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ReleaseUpsertRequest>("Release update request", true))
                .WithResponse(200, OpenApiJson.For<Release>("Updated release"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/releases/{id}/refresh", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    return await _Releases.RefreshAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Releases")
                .WithSummary("Refresh a release")
                .WithDescription("Rebuilds derived release fields such as linked mission scope and artifacts from the current linked work.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Release ID (rel_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Release>("Refreshed release"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/releases/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _Releases.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Releases")
                .WithSummary("Delete a release")
                .WithDescription("Deletes one release record within the caller scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Release ID (rel_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private static ReleaseQuery BuildQueryFromRequest(ApiRequest req)
        {
            ReleaseQuery query = new ReleaseQuery();
            ApplyQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyQuerystringOverrides(ApiRequest req, ReleaseQuery query)
        {
            if (Int32.TryParse(QueryValueReader.Read(req.Query, "pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (Int32.TryParse(QueryValueReader.Read(req.Query, "pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (DateTime.TryParse(QueryValueReader.Read(req.Query, "fromUtc"), out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(QueryValueReader.Read(req.Query, "toUtc"), out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();
            if (Enum.TryParse(QueryValueReader.Read(req.Query, "status"), true, out ReleaseStatusEnum status))
                query.Status = status;

            query.VesselId = NormalizeEmpty(QueryValueReader.Read(req.Query, "vesselId")) ?? query.VesselId;
            query.WorkflowProfileId = NormalizeEmpty(QueryValueReader.Read(req.Query, "workflowProfileId")) ?? query.WorkflowProfileId;
            query.VoyageId = NormalizeEmpty(QueryValueReader.Read(req.Query, "voyageId")) ?? query.VoyageId;
            query.MissionId = NormalizeEmpty(QueryValueReader.Read(req.Query, "missionId")) ?? query.MissionId;
            query.CheckRunId = NormalizeEmpty(QueryValueReader.Read(req.Query, "checkRunId")) ?? query.CheckRunId;
            query.Search = NormalizeEmpty(QueryValueReader.Read(req.Query, "search")) ?? query.Search;
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

        private async Task LinkObjectivesAsync(AuthContext auth, IEnumerable<string>? objectiveIds, string releaseId)
        {
            if (objectiveIds == null) return;

            foreach (string objectiveId in objectiveIds)
            {
                string? normalized = NormalizeEmpty(objectiveId);
                if (String.IsNullOrWhiteSpace(normalized))
                    continue;

                await _Objectives.LinkReleaseAsync(auth, normalized, releaseId).ConfigureAwait(false);
            }
        }

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
