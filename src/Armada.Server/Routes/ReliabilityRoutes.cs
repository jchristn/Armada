namespace Armada.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST routes for pipeline reliability (RELIABILITY_SIMPLICITY_PLAN.md RS-12.1): a mission's attempt history, the
    /// reliability summary per model and persona, and the acceptance gate reports. Rules live in
    /// <see cref="ReliabilityAccessService"/>, shared with MCP.
    /// </summary>
    public class ReliabilityRoutes
    {
        #region Private-Members

        private readonly ReliabilityAccessService _Service;
        private readonly JsonSerializerOptions _JsonOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Caller-scoped reliability service.</param>
        /// <param name="jsonOptions">JSON options for request bodies.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public ReliabilityRoutes(ReliabilityAccessService service, JsonSerializerOptions jsonOptions)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Web server.</param>
        /// <param name="authenticate">Authenticator.</param>
        /// <param name="authz">Route authorization.</param>
        public void Register(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/missions/{id}/attempts", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.GetMissionAttemptsAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Reliability")
                .WithSummary("Get a mission's attempt history")
                .WithDescription("The mission's recovery counters (redispatches, output repairs, no-op redispatches, stall recoveries), escalation state (escalationCount, escalatedFromCaptainId, escalatedFromModel, tier, reworkEscalated), persona and mode, and every recorded attempt oldest first: captain, model, runtime, disposition (Completed, Relaunched, OutputRepair, Escalated, Failed, Interrupted, Cancelled, Pending), failure kind, runtime failure kind, contract repair kind, isRepair, escalated, nextCaptainId, Judge verdict and timing. A global admin sees every tenant, a tenant admin the tenant, a user their own missions. 404 when the mission does not exist or is out of scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<MissionAttemptHistory>("Attempt history"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/reliability/summary", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.GetSummaryAsync(ctx, ReadSummaryQuery(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Reliability")
                .WithSummary("Reliability per model and persona")
                .WithDescription("Pipeline reliability per (model, persona) over a window, counted from stage outcomes (one per attempt): attempts, completed, failed, relaunched, repairs, escalations, unclassified, completionRate (completed over completed, failed and escalated), unclassifiedRate, failure kinds, runtime failure kinds and the median duration, plus totals. The window defaults to the last 14 days and may be at most 90; production attempts only unless includeEvaluation. Sorted and paged on the server. A global admin sees every tenant (tenantId filters); everyone else their own tenant. Captain sessions are refused. 400 for a bad date, sort, or window.")
                .WithParameter(OpenApiParameterMetadata.Query("tenantId", "Tenant filter (global admins only; ignored otherwise)", false))
                .WithParameter(OpenApiParameterMetadata.Query("model", "Model (exact, case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("persona", "Persona name (exact, case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Vessel filter (vsl_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("includeEvaluation", "true to include battery and evaluation attempts (default false)", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Ended at or after (ISO 8601 UTC; default 14 days before toUtc)", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Ended before (ISO 8601 UTC; default now)", false))
                .WithParameter(OpenApiParameterMetadata.Query("sort", "Attempts (default), CompletionRate, UnclassifiedRate, Repairs, Escalations, Failures, Model or Persona", false))
                .WithParameter(OpenApiParameterMetadata.Query("descending", "Sort descending (default true)", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 50)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<ReliabilitySummary>("Summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/reliability/gate-reports", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    int? pageNumber = ReadInt(req, "pageNumber");
                    int? pageSize = ReadInt(req, "pageSize");
                    return (object)_Service.ListGateReports(ctx, pageNumber, pageSize);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Reliability")
                .WithSummary("List gate reports")
                .WithDescription("Stored acceptance gate reports (G1 to G12 with per-pipeline counts), newest first. Global and tenant admins (403 otherwise).")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-100, default 10)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<GateReport>>("One page of reports"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/reliability/gate-reports/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    GateReportQuery query = ReadGateReportQuery(req);
                    return (object)_Service.ListGateReports(ctx, query.PageNumber, query.PageSize);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Reliability")
                .WithSummary("Enumerate gate reports")
                .WithDescription("The POST form of GET /api/v1/reliability/gate-reports: stored acceptance gate reports, newest first. The JSON body (GateReportQuery) carries PageNumber and PageSize (1-100, default 10); an empty body returns the first page. Global and tenant admins (403 otherwise).")
                .WithRequestBody(OpenApiJson.BodyFor<GateReportQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<GateReport>>("One page of reports"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/reliability/gate-reports", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    GateReport? body = ReadReport(req);
                    GateReport stored = _Service.UploadGateReport(ctx, body);
                    req.Http.Response.StatusCode = 201;
                    return (object)stored;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Reliability")
                .WithSummary("Upload a gate report")
                .WithDescription("Stores an acceptance gate report (the Armada.Gate runner uploads the report it wrote to benchmarks/results). The Admiral assigns id and uploadedUtc; the newest 200 are kept. The report must hold at least one measure or pipeline and be at most 1 MiB; it must not contain endpoint addresses or credentials. Global admins only (403 otherwise).")
                .WithRequestBody(OpenApiJson.BodyFor<GateReport>("The gate report", true))
                .WithResponse(201, OpenApiJson.For<GateReport>("The stored report"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a global admin"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/reliability/gate-reports/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return (object)_Service.GetGateReport(ctx, req.Parameters["id"]);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Reliability")
                .WithSummary("Get a gate report")
                .WithDescription("One stored acceptance gate report; id latest returns the newest. Global and tenant admins (403 otherwise); 404 when none is stored.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Report ID (gate- prefix) or latest"))
                .WithResponse(200, OpenApiJson.For<GateReport>("The report"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private static ReliabilitySummaryQuery ReadSummaryQuery(ApiRequest req)
        {
            ReliabilitySummaryQuery query = new ReliabilitySummaryQuery();
            query.TenantId = QueryValueReader.Read(req.Query, "tenantId");
            query.Model = QueryValueReader.Read(req.Query, "model");
            query.Persona = QueryValueReader.Read(req.Query, "persona");
            query.VesselId = QueryValueReader.Read(req.Query, "vesselId");
            bool? includeEvaluation = ReadBool(req, "includeEvaluation");
            if (includeEvaluation.HasValue) query.IncludeEvaluation = includeEvaluation.Value;
            query.FromUtc = ReadUtc(req, "fromUtc");
            query.ToUtc = ReadUtc(req, "toUtc");
            string? sort = QueryValueReader.Read(req.Query, "sort");
            if (!String.IsNullOrWhiteSpace(sort))
            {
                if (!Enum.TryParse(sort.Trim(), true, out ReliabilitySortEnum parsed) || !Enum.IsDefined(parsed))
                    throw new ArgumentException("sort must be one of " + String.Join(", ", Enum.GetNames<ReliabilitySortEnum>()) + ".");
                query.Sort = parsed;
            }

            bool? descending = ReadBool(req, "descending");
            if (descending.HasValue) query.Descending = descending.Value;
            int? pageNumber = ReadInt(req, "pageNumber");
            if (pageNumber.HasValue) query.PageNumber = pageNumber.Value;
            int? pageSize = ReadInt(req, "pageSize");
            if (pageSize.HasValue) query.PageSize = pageSize.Value;
            return query;
        }

        private GateReportQuery ReadGateReportQuery(ApiRequest req)
        {
            string body = req.Http.Request.DataAsString ?? "";
            if (String.IsNullOrWhiteSpace(body)) return new GateReportQuery();
            try
            {
                return JsonSerializer.Deserialize<GateReportQuery>(body, _JsonOptions) ?? new GateReportQuery();
            }
            catch (JsonException ex) { throw new ArgumentException("Invalid request body: " + ex.Message, ex); }
        }

        private static int? ReadInt(ApiRequest req, string name)
        {
            string? value = QueryValueReader.Read(req.Query, name);
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) throw new ArgumentException(name + " must be an integer.");
            return parsed;
        }

        private static bool? ReadBool(ApiRequest req, string name)
        {
            string? value = QueryValueReader.Read(req.Query, name);
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!Boolean.TryParse(value, out bool parsed)) throw new ArgumentException(name + " must be true or false.");
            return parsed;
        }

        private static DateTime? ReadUtc(ApiRequest req, string name)
        {
            string? value = QueryValueReader.Read(req.Query, name);
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
                throw new ArgumentException(name + " must be an ISO 8601 UTC time.");
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        private GateReport? ReadReport(ApiRequest req)
        {
            string body = req.Http.Request.DataAsString ?? String.Empty;
            if (String.IsNullOrWhiteSpace(body)) return null;
            if (body.Length > GateReportStore.MaxReportBytes) throw new ArgumentException("A gate report must be at most 1 MiB.");
            try
            {
                return JsonSerializer.Deserialize<GateReport>(body, _JsonOptions);
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

        #endregion
    }
}
