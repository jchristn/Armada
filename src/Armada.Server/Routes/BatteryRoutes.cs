namespace Armada.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST routes for the fixture battery: GET /api/v1/battery/packs, POST /api/v1/battery/runs,
    /// GET /api/v1/battery/runs/{id} and its paged results (GET .../results, POST .../results/enumerate). Rules live in <see cref="BatteryAccessService"/>, shared with MCP.
    /// </summary>
    public class BatteryRoutes
    {
        #region Private-Members

        private readonly BatteryAccessService _Service;
        private readonly JsonSerializerOptions _JsonOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Battery access service.</param>
        /// <param name="jsonOptions">JSON options for request bodies.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BatteryRoutes(BatteryAccessService service, JsonSerializerOptions jsonOptions)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Authenticates a request.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/battery/packs", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    int? pageNumber = ParseInt(QueryValueReader.Read(req.Query, "pageNumber"), "pageNumber");
                    int? pageSize = ParseInt(QueryValueReader.Read(req.Query, "pageSize"), "pageSize");
                    return _Service.ListPacks(ctx, pageNumber, pageSize);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Battery")
                .WithSummary("List battery packs")
                .WithDescription("One page of fixture battery packs (each a fixture repository plus its tasks, defined as data in battery/<pack>/battery-pack.json), ordered by pack directory name (only the page's manifests are read), with each task's persona, grader and toolchain. Malformed packs are listed with valid false and their validation errors; only valid packs can run.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 100)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BatteryPackInfo>>("One page of packs"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/battery/packs/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    EnumerationQuery page = EnumerationRequestReader.Read(req, _JsonOptions);
                    return _Service.ListPacks(ctx, page.PageNumber, page.PageSize);
                }
                catch (JsonException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid request body: " + ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Battery")
                .WithSummary("Enumerate fixture battery packs")
                .WithDescription("One page of the fixture battery packs the Admiral found. The JSON body (EnumerationQuery) carries PageNumber and PageSize (1-1000).")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BatteryPackInfo>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/battery/runs", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    BatteryRunRequest? body = ReadBody(req);
                    BatteryRun run = await _Service.RunAsync(ctx, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return run;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Battery")
                .WithSummary("Run the fixture battery")
                .WithDescription("Runs the named packs against a captain: before each task the fixture is reset, then the task is dispatched as a mission on the pack's fixture vessel under the built-in ModelEvaluation pipeline (pinned to the captain, landing mode None) and graded per its manifest. Returns the queued run at once; poll GET /api/v1/battery/runs/{id}. runCount defaults to 3 (1-10). A task whose toolchain the Harbor lacks is CouldNotRun, never Failed; rubric-graded tasks are PendingEvaluation with the raw output stored. The seed, sampling settings, battery version and configuration revision are recorded; battery missions never count in production stage outcomes. Global admins and tenant admins of the captain's tenant only (403 otherwise). 400 for no packs, an unknown or malformed pack, or a missing fixture vessel without createFixtureVessel.")
                .WithRequestBody(OpenApiJson.BodyFor<BatteryRunRequest>("Packs, captain, run count, seed, createFixtureVessel", true))
                .WithResponse(201, OpenApiJson.For<BatteryRun>("The queued run"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/battery/runs/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.GetRunAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Battery")
                .WithSummary("Get a battery run")
                .WithDescription("The run with its provenance (seed, sampling settings, battery version, configuration revision) and outcome counts. The per-task results are not embedded: page them with GET /api/v1/battery/runs/{id}/results or POST /api/v1/battery/runs/{id}/results/enumerate. 404 when the run does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Run ID (bat_ prefix)"))
                .WithResponse(200, OpenApiJson.For<BatteryRun>("The run"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/battery/runs/{id}/results", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    EnumerationQuery query = EnumerationRequestReader.Read(req, _JsonOptions);
                    return await _Service.EnumerateResultsAsync(ctx, req.Parameters["id"], query).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid request body: " + ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Battery")
                .WithSummary("List a battery run's results")
                .WithDescription("One page of the run's per-task results (Passed, Failed, CouldNotRun or PendingEvaluation, with the grader's evidence and the captain's raw output), paged in the database. status filters by outcome; order is CreatedDescending (default) or CreatedAscending, with the id as the tie-break. pageSize 1-1000 (default 10). 404 when the run does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Run ID (bat_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 10)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Outcome filter: Passed, Failed, CouldNotRun or PendingEvaluation", false))
                .WithParameter(OpenApiParameterMetadata.Query("order", "CreatedDescending (default) or CreatedAscending", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BatteryTaskResult>>("One page of results"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/battery/runs/{id}/results/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    EnumerationQuery query = EnumerationRequestReader.Read(req, _JsonOptions);
                    return await _Service.EnumerateResultsAsync(ctx, req.Parameters["id"], query).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid request body: " + ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Battery")
                .WithSummary("Enumerate a battery run's results")
                .WithDescription("One page of the run's per-task results (Passed, Failed, CouldNotRun or PendingEvaluation, with the grader's evidence and the captain's raw output), paged in the database. status filters by outcome; order is CreatedDescending (default) or CreatedAscending, with the id as the tie-break. pageSize 1-1000 (default 10). 404 when the run does not exist or belongs to another tenant." + " The JSON body is an EnumerationQuery; querystring values override it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Run ID (bat_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging, order and outcome filter (Status)", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BatteryTaskResult>>("One page of results"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private BatteryRunRequest? ReadBody(ApiRequest req)
        {
            string body = req.Http.Request.DataAsString ?? String.Empty;
            if (String.IsNullOrWhiteSpace(body)) return null;
            try
            {
                return JsonSerializer.Deserialize<BatteryRunRequest>(body, _JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid request body: " + ex.Message, ex);
            }
        }

        private static ApiErrorResponse? Deny(ApiRequest req, AuthContext ctx, IAuthorizationService authz)
        {
            if (authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery)) return null;
            req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
            return new ApiErrorResponse
            {
                Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized,
                Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
            };
        }

        private static int? ParseInt(string? value, string name)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!Int32.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) throw new ArgumentException("Invalid " + name + ": " + value);
            return parsed;
        }

        #endregion
    }
}
