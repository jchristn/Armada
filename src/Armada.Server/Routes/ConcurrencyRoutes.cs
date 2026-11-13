namespace Armada.Server.Routes
{
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST routes for mission hierarchy and wait detail: the voyage tree, a mission's wait intervals, a vessel's
    /// concurrency and windowed wait aggregates.
    /// </summary>
    public class ConcurrencyRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly ConcurrencyReportService _reports;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Instantiate.</summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="database"/> is null.</exception>
        public ConcurrencyRoutes(DatabaseDriver database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _reports = new ConcurrencyReportService(database);
        }

        /// <summary>Register routes with the application.</summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/voyages/{id}/tree", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                string id = req.Parameters["id"];
                Voyage? voyage = ctx.IsAdmin
                    ? await _database.Voyages.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Voyages.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Voyages.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (voyage == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" }; }
                List<MissionSummary> missions = await _database.Missions.EnumerateSummariesByVoyageAsync(voyage.Id).ConfigureAwait(false);
                return (object)MissionTreeBuilder.BuildVoyageTree(voyage, missions);
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Get a voyage's mission tree")
                .WithDescription("Returns the voyage's missions arranged as logical missions. Each node holds the root mission (MissionSummary), its pipeline stages in order (missions reached by DependsOnMissionId), and its child logical missions (ParentMissionId). Roots lists the top-level logical missions in creation order.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(200, OpenApiJson.For<VoyageTree>("The voyage tree"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/waits", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                EnumerationQuery query = new EnumerationQuery { PageSize = 100, Order = EnumerationOrderEnum.CreatedAscending };
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                return await WaitPageAsync(req, ctx, req.Parameters["id"], query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get a mission's wait intervals")
                .WithDescription("One page of the scheduling waits recorded for the mission, oldest first (order=CreatedDescending for newest first): the gate (MissionWaitGateEnum), when it started and ended (EndedUtc is null while the mission still waits on it) and the selection or blocker detail. Intervals are kept for Concurrency.WaitIntervalRetentionDays after they close. POST /api/v1/missions/{id}/waits/enumerate takes the same paging as a JSON body.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 100)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("order", "CreatedAscending (default) or CreatedDescending", false, OpenApiSchemaMetadata.String()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<MissionWaitInterval>>("One page of wait intervals"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/missions/{id}/waits/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                EnumerationQuery query = new EnumerationQuery { PageSize = 100, Order = EnumerationOrderEnum.CreatedAscending };
                string body = req.Http.Request.DataAsString ?? "";
                if (!String.IsNullOrWhiteSpace(body))
                {
                    try { query = JsonSerializer.Deserialize<EnumerationQuery>(body, _jsonOptions) ?? query; }
                    catch (JsonException)
                    {
                        req.Http.Response.StatusCode = 400;
                        return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Request body is not a valid EnumerationQuery." };
                    }
                }
                return await WaitPageAsync(req, ctx, req.Parameters["id"], query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Enumerate a mission's wait intervals")
                .WithDescription("POST form of GET /api/v1/missions/{id}/waits: PageNumber, PageSize and Order (CreatedAscending for oldest first, CreatedDescending for newest first) in an EnumerationQuery body.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<MissionWaitInterval>>("One page of wait intervals"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/concurrency", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                return (object)await _reports.GetVesselConcurrencyAsync(vessel,
                    ctx.IsAdmin ? null : ctx.TenantId, ctx.IsAdmin || ctx.IsTenantAdmin ? null : ctx.UserId).ConfigureAwait(false);
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Get a vessel's concurrency")
                .WithDescription("Returns the vessel's voyage slots in use against MaxParallelVoyages, the voyages holding them with running and Pending counts, the Pending missions waiting on a recorded gate (longest first, at most 100; WaitingCount is the total), and slot wait over the last 24 hours and 7 days (p50, p95 and max per mission, from recorded wait intervals). Callers who are not tenant administrators see only their own voyages and missions in the lists; the counts cover the whole vessel.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<VesselConcurrency>("Vessel concurrency"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/metrics/concurrency", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                if (!TryParseTime(req.Query.GetValueOrDefault("from"), out DateTime? from) || !TryParseTime(req.Query.GetValueOrDefault("to"), out DateTime? to))
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "from and to must be ISO 8601 times." }; }
                string? vesselId = req.Query.GetValueOrDefault("vesselId");
                if (!String.IsNullOrWhiteSpace(vesselId))
                {
                    Vessel? vessel = ctx.IsAdmin
                        ? await _database.Vessels.ReadAsync(vesselId!).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Vessels.ReadAsync(ctx.TenantId!, vesselId!).ConfigureAwait(false)
                            : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, vesselId!).ConfigureAwait(false);
                    if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                }
                else vesselId = null;
                string? captainId = req.Query.GetValueOrDefault("captainId");
                if (!String.IsNullOrWhiteSpace(captainId))
                {
                    Captain? captain = ctx.IsAdmin
                        ? await _database.Captains.ReadAsync(captainId!).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Captains.ReadAsync(ctx.TenantId!, captainId!).ConfigureAwait(false)
                            : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, captainId!).ConfigureAwait(false);
                    if (captain == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" }; }
                }
                else captainId = null;
                try
                {
                    return (object)await _reports.GetMetricsAsync(from, to, vesselId, ctx.IsAdmin ? null : ctx.TenantId, captainId).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message.Split(" (Parameter")[0] };
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get concurrency wait aggregates")
                .WithDescription("Aggregates recorded mission waits over a window for charts: slot wait p50/p95/max per mission, wait seconds by gate, and about 48 time buckets (5 minutes to 1 day wide) with missions waiting and wait seconds by gate. The window defaults to the last 24 hours and can be at most 31 days. Truncated is true when more than 50000 intervals matched. captainId narrows to waits attributed to a captain: the waits of missions it went on to run (stamped when it takes the mission) and the open waits of missions pinned to it with RequestedCaptainId. Unpinned missions that are still waiting belong to no captain yet.")
                .WithParameter(OpenApiParameterMetadata.Query("from", "Window start (ISO 8601; default 24 hours before to)", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("to", "Window end (ISO 8601; default now)", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Only waits on this vessel (vsl_ prefix)", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Only waits attributed to this captain (cpt_ prefix)", false, OpenApiSchemaMetadata.String()))
                .WithResponse(200, OpenApiJson.For<ConcurrencyMetrics>("Wait aggregates"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private async Task<object> WaitPageAsync(ApiRequest req, AuthContext ctx, string id, EnumerationQuery query)
        {
            MissionSummary? mission = ctx.IsAdmin
                ? await _database.Missions.ReadSummaryAsync(id).ConfigureAwait(false)
                : await _database.Missions.ReadSummaryAsync(ctx.TenantId!, id).ConfigureAwait(false);
            if (mission == null || (!ctx.IsAdmin && !ctx.IsTenantAdmin && !String.Equals(mission.UserId, ctx.UserId, StringComparison.Ordinal)))
            { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }
            // The mission is already authorized; intervals carry its tenant. One mission's waits (one row per gate
            // change) are read whole and cut to the page.
            List<MissionWaitInterval> all = await _database.MissionWaitIntervals.EnumerateByMissionAsync(mission.Id, null).ConfigureAwait(false);
            if (query.Order == EnumerationOrderEnum.CreatedDescending) all.Reverse();
            return EnumerationResult<MissionWaitInterval>.Page(query, all);
        }

        private static ApiErrorResponse? Deny(ApiRequest req, AuthContext ctx, IAuthorizationService authz)
        {
            if (authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery)) return null;
            req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
            return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
        }

        private static bool TryParseTime(string? value, out DateTime? result)
        {
            result = null;
            if (String.IsNullOrWhiteSpace(value)) return true;
            // Clients escape the colons; the querystring value may arrive still escaped.
            value = Uri.UnescapeDataString(value);
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)) return false;
            result = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }
    }
}
