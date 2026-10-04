namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// REST API routes for fleet actions (definitions) and fleet action runs. Path-based authorization makes reads
    /// and enumerations Authenticated and every other write TenantAdmin (see AuthorizationConfig); the service also
    /// enforces tenant admin for Command-kind actions because that check depends on the body. All reads are scoped to
    /// the caller's tenant. Target output is returned only by the single-target endpoint.
    /// </summary>
    public class FleetActionRoutes
    {
        #region Private-Members

        private readonly string _Header = "[FleetActionRoutes] ";
        private readonly FleetActionService _Service;
        private readonly JsonSerializerOptions _JsonOptions;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Fleet action service.</param>
        /// <param name="jsonOptions">JSON serializer options for request bodies.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public FleetActionRoutes(FleetActionService service, JsonSerializerOptions jsonOptions, LoggingModule logging)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Post<FleetActionEnumerateRequest>("/api/v1/fleet-actions/enumerate", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    FleetActionEnumerateRequest query = Deserialize<FleetActionEnumerateRequest>(req) ?? new FleetActionEnumerateRequest();
                    query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                    Stopwatch sw = Stopwatch.StartNew();
                    EnumerationResult<FleetAction> result = await _Service.EnumerateActionsAsync(ctx, query).ConfigureAwait(false);
                    result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                    return result;
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Enumerate fleet actions")
                .WithDescription("Paged fleet action definitions in the caller's tenant, newest first. Built-in actions are seeded on first use. Set includeInactive to include soft-deleted built-ins.")
                .WithRequestBody(OpenApiJson.BodyFor<FleetActionEnumerateRequest>("Enumeration query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<FleetAction>>("Paged fleet actions"))
                .WithSecurity("ApiKey"));

            app.Post<FleetActionUpsertRequest>("/api/v1/fleet-actions", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    FleetActionUpsertRequest body = Deserialize<FleetActionUpsertRequest>(req) ?? throw new ArgumentException("A request body is required.");
                    FleetAction created = await _Service.CreateActionAsync(ctx, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return created;
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Create a fleet action")
                .WithDescription("Creates a Command or Mission fleet action. Command actions require tenant admin. Templates may use {{vessel.name}}, {{vessel.id}}, {{vessel.defaultBranch}}, {{vessel.workingDirectory}}, {{vessel.buildCommand}} and {{health.summary}}; any other variable is rejected with 400.")
                .WithRequestBody(OpenApiJson.BodyFor<FleetActionUpsertRequest>("Action definition", true))
                .WithResponse(201, OpenApiJson.For<FleetAction>("Created action"))
                .WithSecurity("ApiKey"));

            app.Post<FleetActionRunRequest>("/api/v1/fleet-actions/run", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    FleetActionRunRequest body = Deserialize<FleetActionRunRequest>(req) ?? throw new ArgumentException("A request body is required.");
                    FleetActionRun run = await _Service.StartRunAsync(ctx, null, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 202;
                    return FleetActionRunStartResult.FromRun(run);
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Start an ad hoc fleet action run")
                .WithDescription("Starts a run from an inline definition (no saved action). Returns 202 with the run id; poll the run endpoints for progress.")
                .WithRequestBody(OpenApiJson.BodyFor<FleetActionRunRequest>("Run request with definition", true))
                .WithResponse(202, OpenApiJson.For<FleetActionRunStartResult>("Run accepted"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/fleet-actions/{id}", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    return await _Service.ReadActionAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Get a fleet action")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet action ID (fac_ prefix)"))
                .WithResponse(200, OpenApiJson.For<FleetAction>("Action"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put<FleetActionUpsertRequest>("/api/v1/fleet-actions/{id}", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    FleetActionUpsertRequest body = Deserialize<FleetActionUpsertRequest>(req) ?? throw new ArgumentException("A request body is required.");
                    return await _Service.UpdateActionAsync(ctx, req.Parameters["id"], body).ConfigureAwait(false);
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Update a fleet action")
                .WithDescription("Partially updates a fleet action: only supplied fields change. Built-in actions can be edited.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet action ID (fac_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<FleetActionUpsertRequest>("Fields to change", true))
                .WithResponse(200, OpenApiJson.For<FleetAction>("Updated action"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/fleet-actions/{id}", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    await _Service.DeleteActionAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Delete a fleet action")
                .WithDescription("Deletes a fleet action. Built-in actions are soft-deleted so they are never re-seeded. Past runs keep their snapshot.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet action ID (fac_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<FleetActionRunRequest>("/api/v1/fleet-actions/{id}/run", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    FleetActionRunRequest body = Deserialize<FleetActionRunRequest>(req) ?? throw new ArgumentException("A request body is required.");
                    FleetActionRun run = await _Service.StartRunAsync(ctx, req.Parameters["id"], body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 202;
                    return FleetActionRunStartResult.FromRun(run);
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Run a fleet action")
                .WithDescription("Starts a run of a saved action over the given vessels. Every vessel must belong to the caller's tenant or the whole request is rejected with 404. Returns 202 with the run id.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet action ID (fac_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<FleetActionRunRequest>("Run request", true))
                .WithResponse(202, OpenApiJson.For<FleetActionRunStartResult>("Run accepted"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/fleet-action-runs/enumerate", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    EnumerationQuery query = Deserialize<EnumerationQuery>(req) ?? new EnumerationQuery();
                    query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                    Stopwatch sw = Stopwatch.StartNew();
                    EnumerationResult<FleetActionRun> result = await _Service.EnumerateRunsAsync(ctx, query).ConfigureAwait(false);
                    result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                    return result;
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Enumerate fleet action runs")
                .WithDescription("Paged fleet action runs in the caller's tenant, newest first. Filter by status (Pending, Running, Completed, CompletedWithFailures, Cancelled, Failed).")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<FleetActionRun>>("Paged runs"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/fleet-action-runs/{id}", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    return await _Service.ReadRunAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Get a fleet action run")
                .WithDescription("Returns the run and summaries of all of its targets. Output text is not included; use the target endpoint.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Run ID (far_ prefix)"))
                .WithResponse(200, OpenApiJson.For<FleetActionRunDetail>("Run with target summaries"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<FleetActionTargetEnumerateRequest>("/api/v1/fleet-action-runs/{id}/targets/enumerate", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    FleetActionTargetEnumerateRequest query = Deserialize<FleetActionTargetEnumerateRequest>(req) ?? new FleetActionTargetEnumerateRequest();
                    Stopwatch sw = Stopwatch.StartNew();
                    EnumerationResult<FleetActionRunTargetSummary> result = await _Service.EnumerateTargetSummariesAsync(ctx, req.Parameters["id"], query).ConfigureAwait(false);
                    result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                    return result;
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Enumerate fleet action run targets")
                .WithDescription("Paged target summaries for a run, optionally filtered by status. Output text is not included.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Run ID (far_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<FleetActionTargetEnumerateRequest>("Paging and status filter", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<FleetActionRunTargetSummary>>("Paged target summaries"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/fleet-action-runs/{id}/targets/{targetId}", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    return await _Service.ReadTargetAsync(ctx, req.Parameters["id"], req.Parameters["targetId"]).ConfigureAwait(false);
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Get a fleet action run target")
                .WithDescription("Returns one target including RenderedText, OutputText and ErrorText.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Run ID (far_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("targetId", "Target ID (fat_ prefix)"))
                .WithResponse(200, OpenApiJson.For<FleetActionRunTarget>("Target with output"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/fleet-action-runs/{id}/cancel", async (ApiRequest req) =>
            {
                return await HandleAsync(req, authenticate, authz, async ctx =>
                {
                    return await _Service.CancelRunAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("FleetActions")
                .WithSummary("Cancel a fleet action run")
                .WithDescription("Pending targets become Cancelled; running Command targets are killed; Mission targets have their unlanded voyages cancelled. Returns 409 when the run already finished.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Run ID (far_ prefix)"))
                .WithResponse(200, OpenApiJson.For<FleetActionRun>("Cancelled run"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private T? Deserialize<T>(ApiRequest req) where T : class
        {
            string body = req.Http.Request.DataAsString;
            if (String.IsNullOrWhiteSpace(body)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(body, _JsonOptions);
            }
            catch (JsonException e)
            {
                throw new ArgumentException("The request body is not valid JSON for " + typeof(T).Name + ": " + e.Message);
            }
        }

        private async Task<object?> HandleAsync(
            ApiRequest req,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz,
            Func<AuthContext, Task<object?>> handler)
        {
            AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
            if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
            {
                req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                if (ctx.IsAuthenticated) LogDenied(req, ctx, "path permission");
                return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
            }

            try
            {
                return await handler(ctx).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException e)
            {
                req.Http.Response.StatusCode = 403;
                LogDenied(req, ctx, e.Message);
                return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = e.Message };
            }
            catch (KeyNotFoundException e)
            {
                req.Http.Response.StatusCode = 404;
                return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = e.Message };
            }
            catch (ArgumentException e)
            {
                req.Http.Response.StatusCode = 400;
                return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = e.Message };
            }
            catch (InvalidOperationException e)
            {
                req.Http.Response.StatusCode = 409;
                return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = e.Message };
            }
        }

        private void LogDenied(ApiRequest req, AuthContext ctx, string reason)
        {
            string requestId = req.Http.Request.Headers.Get("X-Request-Id") ?? req.Http.Request.Headers.Get("X-Correlation-Id") ?? "(none)";
            _Logging.Info(_Header + "authorization denied: " + req.Http.Request.Method + " " + req.Http.Request.Url.RawWithoutQuery
                + " user=" + (ctx.UserId ?? "(none)") + " tenant=" + (ctx.TenantId ?? "(none)") + " requestId=" + requestId + " reason=" + reason);
        }

        #endregion
    }
}
