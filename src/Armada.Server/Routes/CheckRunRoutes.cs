namespace Armada.Server.Routes
{
    using System;
    using System.Diagnostics;
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
    /// REST API routes for structured check runs.
    /// </summary>
    public class CheckRunRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly CheckRunService _checkRuns;
        private readonly GitHubIntegrationService _gitHub;
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
        public CheckRunRoutes(
            DatabaseDriver database,
            CheckRunService checkRuns,
            GitHubIntegrationService gitHub,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _checkRuns = checkRuns ?? throw new ArgumentNullException(nameof(checkRuns));
            _gitHub = gitHub ?? throw new ArgumentNullException(nameof(gitHub));
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
            app.Get("/api/v1/check-runs", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                CheckRunQuery query = BuildQueryFromRequest(req);
                ApplyScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<CheckRun> result = await _database.CheckRuns.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("CheckRuns")
                .WithSummary("List check runs")
                .WithDescription("Returns paginated build, test, deploy, and verification check runs.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("workflowProfileId", "Optional workflow-profile filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Optional mission filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Optional voyage filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("deploymentId", "Optional deployment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("type", "Optional check-type filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Optional check status filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("source", "Optional check source filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("providerName", "Optional provider-name filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("externalId", "Optional provider external ID filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("environmentName", "Optional environment-name filter", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<CheckRun>>("Paginated check runs"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/check-runs/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                CheckRunQuery query = JsonSerializer.Deserialize<CheckRunQuery>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? new CheckRunQuery();
                ApplyQuerystringOverrides(req, query);
                ApplyScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<CheckRun> result = await _database.CheckRuns.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("CheckRuns")
                .WithSummary("Enumerate check runs")
                .WithDescription("Paginated check-run enumeration with body or query filters.")
                .WithRequestBody(OpenApiJson.BodyFor<CheckRunQuery>("Check-run query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<CheckRun>>("Paginated check runs"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/check-runs", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                CheckRunRequest request = JsonSerializer.Deserialize<CheckRunRequest>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as CheckRunRequest.");

                try
                {
                    CheckRun run = await _checkRuns.RunAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return run;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("CheckRuns")
                .WithSummary("Run a check")
                .WithDescription("Executes a structured build, test, deploy, or verification check and persists the result.")
                .WithRequestBody(OpenApiJson.BodyFor<CheckRunRequest>("Check-run request", true))
                .WithResponse(201, OpenApiJson.For<CheckRun>("Created check run"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/check-runs/import", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                CheckRunImportRequest request = JsonSerializer.Deserialize<CheckRunImportRequest>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as CheckRunImportRequest.");

                try
                {
                    CheckRun run = await _checkRuns.ImportAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return run;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("CheckRuns")
                .WithSummary("Import an external check")
                .WithDescription("Persists a structured check run that was executed outside Armada, such as CI or provider-hosted validation.")
                .WithRequestBody(OpenApiJson.BodyFor<CheckRunImportRequest>("Imported check-run payload", true))
                .WithResponse(201, OpenApiJson.For<CheckRun>("Created imported check run"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/check-runs/sync/github-actions", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                GitHubActionsSyncRequest request = JsonSerializer.Deserialize<GitHubActionsSyncRequest>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as GitHubActionsSyncRequest.");

                try
                {
                    return await _gitHub.SyncGitHubActionsAsync(ctx, request).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("CheckRuns")
                .WithSummary("Sync GitHub Actions runs")
                .WithDescription("Pulls recent GitHub Actions workflow runs into Armada check history and optionally links them to a deployment.")
                .WithRequestBody(OpenApiJson.BodyFor<GitHubActionsSyncRequest>("GitHub Actions sync request", true))
                .WithResponse(200, OpenApiJson.For<GitHubActionsSyncResult>("GitHub Actions sync result"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/check-runs/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                CheckRun? run = await _database.CheckRuns.ReadAsync(req.Parameters["id"], BuildScopedQuery(ctx)).ConfigureAwait(false);
                if (run == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Check run not found" };
                }

                return run;
            },
            api => api
                .WithTag("CheckRuns")
                .WithSummary("Get a check run")
                .WithDescription("Returns one structured check run by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Check run ID (chk_ prefix)"))
                .WithResponse(200, OpenApiJson.For<CheckRun>("Check run"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/check-runs/{id}/retry", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    CheckRun run = await _checkRuns.RetryAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return run;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("CheckRuns")
                .WithSummary("Retry a check run")
                .WithDescription("Re-executes a prior check run using the same resolved command and scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Check run ID (chk_ prefix)"))
                .WithResponse(201, OpenApiJson.For<CheckRun>("Retried check run"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/check-runs/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                CheckRunQuery scope = BuildScopedQuery(ctx);
                CheckRun? existing = await _database.CheckRuns.ReadAsync(req.Parameters["id"], scope).ConfigureAwait(false);
                if (existing == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Check run not found" };
                }

                await _database.CheckRuns.DeleteAsync(existing.Id, scope).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("CheckRuns")
                .WithSummary("Delete a check run")
                .WithDescription("Deletes one structured check run within the caller's scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Check run ID (chk_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
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

        private static CheckRunQuery BuildQueryFromRequest(ApiRequest req)
        {
            CheckRunQuery query = new CheckRunQuery();
            ApplyQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyQuerystringOverrides(ApiRequest req, CheckRunQuery query)
        {
            if (int.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (int.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (Enum.TryParse(req.Query.GetValueOrDefault("type"), true, out CheckRunTypeEnum type))
                query.Type = type;
            if (Enum.TryParse(req.Query.GetValueOrDefault("status"), true, out CheckRunStatusEnum status))
                query.Status = status;
            if (Enum.TryParse(req.Query.GetValueOrDefault("source"), true, out CheckRunSourceEnum source))
                query.Source = source;
            if (DateTime.TryParse(req.Query.GetValueOrDefault("fromUtc"), out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(req.Query.GetValueOrDefault("toUtc"), out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();

            query.WorkflowProfileId = NormalizeEmpty(req.Query.GetValueOrDefault("workflowProfileId")) ?? query.WorkflowProfileId;
            query.VesselId = NormalizeEmpty(req.Query.GetValueOrDefault("vesselId")) ?? query.VesselId;
            query.MissionId = NormalizeEmpty(req.Query.GetValueOrDefault("missionId")) ?? query.MissionId;
            query.VoyageId = NormalizeEmpty(req.Query.GetValueOrDefault("voyageId")) ?? query.VoyageId;
            query.DeploymentId = NormalizeEmpty(req.Query.GetValueOrDefault("deploymentId")) ?? query.DeploymentId;
            query.EnvironmentName = NormalizeEmpty(req.Query.GetValueOrDefault("environmentName")) ?? query.EnvironmentName;
            query.ProviderName = NormalizeEmpty(req.Query.GetValueOrDefault("providerName")) ?? query.ProviderName;
            query.ExternalId = NormalizeEmpty(req.Query.GetValueOrDefault("externalId")) ?? query.ExternalId;
        }

        private static void ApplyScope(AuthContext ctx, CheckRunQuery query)
        {
            if (ctx.IsAdmin) return;
            query.TenantId = ctx.TenantId;
            if (!ctx.IsTenantAdmin)
                query.UserId = ctx.UserId;
        }

        private static CheckRunQuery BuildScopedQuery(AuthContext ctx)
        {
            CheckRunQuery query = new CheckRunQuery();
            ApplyScope(ctx, query);
            return query;
        }

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
