namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Health;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for vessel health: enumerate, summary, per-vessel detail, evaluation jobs, and manual overrides.
    /// Every route is scoped to the caller's tenant (from the auth context, never the request body); a vessel in another
    /// tenant returns 404.
    /// </summary>
    public class VesselHealthRoutes
    {
        #region Private-Members

        private readonly VesselHealthService _Health;

        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="health">Vessel health service.</param>
        /// <exception cref="ArgumentNullException">Thrown when health is null.</exception>
        public VesselHealthRoutes(VesselHealthService health)
        {
            _Health = health ?? throw new ArgumentNullException(nameof(health));
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
            app.Post("/api/v1/vessel-health/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                VesselHealthEnumerateRequest? body;
                if (!TryDeserialize(req, out body, out string? error))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = error };
                }

                return (object)await _Health.EnumerateAsync(ctx.TenantId!, body ?? new VesselHealthEnumerateRequest()).ConfigureAwait(false);
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Enumerate vessel health")
                .WithDescription("Filtered, sorted, paged vessel health rows for the caller's tenant. Every active vessel appears, including never-evaluated vessels (null id, Unknown statuses). Status columns are effective (override-aware). Response uses the standard EnumerationResult shape (objects, pageNumber, pageSize, totalPages, totalRecords, totalMs).")
                .WithRequestBody(OpenApiJson.BodyFor<VesselHealthEnumerateRequest>("Filter, sort, and paging request", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<VesselHealth>>("Vessel health rows"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessel-health/summary", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                return (object)await _Health.GetSummaryAsync(ctx.TenantId!).ConfigureAwait(false);
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Vessel health summary")
                .WithDescription("KPI counts by effective overall status for the caller's tenant (active vessels only), plus not-evaluated, outdated-major, and high-or-critical-vulnerability vessel counts.")
                .WithResponse(200, OpenApiJson.For<VesselHealthSummary>("Vessel health summary"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/vessel-health/evaluate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                VesselHealthEvaluateRequest? body;
                if (!TryDeserialize(req, out body, out string? error))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = error };
                }

                try
                {
                    VesselHealthEvaluationStart start = await _Health.StartEvaluationAsync(ctx.TenantId!, ctx.UserId, body, false).ConfigureAwait(false);
                    req.Http.Response.StatusCode = start.AlreadyRunning ? 409 : 202;
                    return (object)start;
                }
                catch (KeyNotFoundException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Evaluate vessel health")
                .WithDescription("Starts a background evaluation job (kind Report) for the given vessels, a fleet, or every active vessel. Returns 202 with the job id, or 409 with the running job's id when an evaluation is already running for the tenant. Manual evaluations force dependency checks unless force is false. Poll GET /api/v1/jobs/{jobId}; cancel with POST /api/v1/jobs/{jobId}/cancel.")
                .WithRequestBody(OpenApiJson.BodyFor<VesselHealthEvaluateRequest>("Evaluation request", false))
                .WithResponse(202, OpenApiJson.For<VesselHealthEvaluationStart>("Evaluation started"))
                .WithResponse(409, OpenApiJson.For<VesselHealthEvaluationStart>("An evaluation is already running"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/health", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                VesselHealthDetail? detail = await _Health.GetDetailAsync(ctx.TenantId!, req.Parameters["id"]).ConfigureAwait(false);
                if (detail == null) return NotFound(req);
                return (object)detail;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Get vessel health")
                .WithDescription("Returns the vessel's health row (effective statuses), raw findings, the number of outdated or vulnerable dependencies (DependencyCount), and overrides. Dependency rows are not embedded: page them with GET /api/v1/vessels/{id}/health/dependencies or POST .../health/dependencies/enumerate.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<VesselHealthDetail>("Vessel health detail"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/health/dependencies", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                EnumerationResult<VesselDependency>? page = await _Health.EnumerateDependenciesAsync(ctx.TenantId!, req.Parameters["id"], query).ConfigureAwait(false);
                if (page == null) return NotFound(req);
                return (object)page;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("List vessel dependencies")
                .WithDescription("One page of the vessel's outdated or vulnerable dependency rows from its last health evaluation, cut in the database and ordered by ecosystem, package name, project path, then id. pageNumber (default 1) and pageSize (1 to 1000, default 10) come from the querystring; out-of-range values are normalized.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<VesselDependency>>("Dependency rows"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/vessels/{id}/health/dependencies/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                EnumerationQuery query;
                try
                {
                    query = EnumerationRequestReader.Read(req, _BodyJsonOptions);
                }
                catch (JsonException ex)
                {
                    return BadRequest(req, "Request body is not valid: " + ex.Message);
                }

                EnumerationResult<VesselDependency>? page = await _Health.EnumerateDependenciesAsync(ctx.TenantId!, req.Parameters["id"], query).ConfigureAwait(false);
                if (page == null) return NotFound(req);
                return (object)page;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Enumerate vessel dependencies")
                .WithDescription("POST form of GET /api/v1/vessels/{id}/health/dependencies: the EnumerationQuery (pageNumber, pageSize) comes from the body, and querystring values override it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<VesselDependency>>("Dependency rows"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/vessels/{id}/health/overrides/{criterion}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                if (!TryParseCriterion(req.Parameters["criterion"], out VesselHealthCriterionEnum criterion))
                    return BadRequest(req, "Unknown criterion '" + req.Parameters["criterion"] + "'.");

                VesselHealthOverrideRequest? body;
                if (!TryDeserialize(req, out body, out string? error)) return BadRequest(req, error);
                if (body == null || body.Status == null) return BadRequest(req, "Request body must include a status (Pass, Warn, Fail, NotApplicable, or Unknown).");

                VesselHealthDetail? detail = await _Health.SetOverrideAsync(ctx.TenantId!, req.Parameters["id"], criterion, body.Status.Value, body.Note, ctx.UserId).ConfigureAwait(false);
                if (detail == null) return NotFound(req);
                return (object)detail;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Set a vessel health override")
                .WithDescription("Sets a manual status (with an optional note) for one criterion, or for Overall, and immediately recomputes the effective statuses from stored findings.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("criterion", "Criterion code, for example Dependencies or Overall"))
                .WithRequestBody(OpenApiJson.BodyFor<VesselHealthOverrideRequest>("Override status and note", true))
                .WithResponse(200, OpenApiJson.For<VesselHealthDetail>("Updated vessel health detail"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/vessels/{id}/health/overrides/{criterion}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                if (!TryParseCriterion(req.Parameters["criterion"], out VesselHealthCriterionEnum criterion))
                    return BadRequest(req, "Unknown criterion '" + req.Parameters["criterion"] + "'.");

                VesselHealthDetail? detail = await _Health.DeleteOverrideAsync(ctx.TenantId!, req.Parameters["id"], criterion).ConfigureAwait(false);
                if (detail == null) return NotFound(req);
                return (object)detail;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Remove a vessel health override")
                .WithDescription("Removes the manual override for one criterion and immediately recomputes the effective statuses.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("criterion", "Criterion code, for example Dependencies or Overall"))
                .WithResponse(200, OpenApiJson.For<VesselHealthDetail>("Updated vessel health detail"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private static bool TryParseCriterion(string? value, out VesselHealthCriterionEnum criterion)
        {
            criterion = VesselHealthCriterionEnum.Overall;
            if (String.IsNullOrWhiteSpace(value)) return false;
            if (Char.IsDigit(value[0]) || value[0] == '-') return false;
            return Enum.TryParse<VesselHealthCriterionEnum>(value.Trim(), true, out criterion) && Enum.IsDefined(typeof(VesselHealthCriterionEnum), criterion);
        }

        private static bool TryDeserialize<T>(ApiRequest req, out T? value, out string? error) where T : class
        {
            value = null;
            error = null;
            string body = req.Http.Request.DataAsString;
            if (String.IsNullOrWhiteSpace(body)) return true;
            try
            {
                value = JsonSerializer.Deserialize<T>(body, _BodyJsonOptions);
                return true;
            }
            catch (JsonException ex)
            {
                error = "Request body is not valid: " + ex.Message;
                return false;
            }
        }

        private static ApiErrorResponse NotFound(ApiRequest req)
        {
            req.Http.Response.StatusCode = 404;
            return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
        }

        private static ApiErrorResponse BadRequest(ApiRequest req, string? message)
        {
            req.Http.Response.StatusCode = 400;
            return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = message };
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

            if (String.IsNullOrEmpty(ctx.TenantId)) ctx.TenantId = Armada.Core.Constants.DefaultTenantId;
            return ctx;
        }

        #endregion
    }
}
