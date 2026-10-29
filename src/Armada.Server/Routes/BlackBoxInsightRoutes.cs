namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// REST routes for black box prevention, forensics and operations (tag BlackBox): overlap preview, soft directory
    /// reservations, brief lint, configuration doctor, attempt diff, nearby successes, combined timeline, voyage cascade
    /// graph and recovery statistics. Every route calls <see cref="BlackBoxInsightService"/>, which scopes to the caller
    /// (an entry, vessel or voyage the caller cannot see is 404). Collections are paged (pageNumber, pageSize 1 to 1000).
    /// </summary>
    public class BlackBoxInsightRoutes
    {
        #region Private-Members

        private const string Tag = "BlackBox";
        private readonly BlackBoxInsightService _Insights;

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
        /// <param name="insights">Insight service.</param>
        /// <exception cref="ArgumentNullException">Thrown when insights is null.</exception>
        public BlackBoxInsightRoutes(BlackBoxInsightService insights)
        {
            _Insights = insights ?? throw new ArgumentNullException(nameof(insights));
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
            app.Post("/api/v1/vessels/{id}/overlap-preview", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
            {
                MissionOverlapPreviewRequest body = ReadBody<MissionOverlapPreviewRequest>(req) ?? new MissionOverlapPreviewRequest();
                return await _Insights.PreviewOverlapAsync(ctx, req.Parameters["id"], body).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Preview a mission's overlaps")
                .WithDescription("Estimates the files a mission is about to touch (its scoped-files directive and the repository paths its brief names that exist in the vessel) and compares them with the vessel's active missions (their scoped files, brief paths, recent path commits, diff or branch), its black box conflict hotspots and the directories active missions reserve. Returns the likely files (first 200), up to 50 warnings strongest first, the mission dispatch would be chained after when the vessel has serializeOverlappingMissions, and the brief lint findings. Nothing is dispatched or changed.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<MissionOverlapPreviewRequest>("The mission: title, description, dependsOnMissionId, excludeMissionId", true))
                .WithResponse(200, OpenApiJson.For<MissionOverlapPreview>("Overlap preview"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/reservations", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
                await _Insights.EnumerateReservationsAsync(ctx, req.Parameters["id"], QueryOf(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("List vessel reservations")
                .WithDescription("One page of the vessel's soft directory reservations: active missions whose scoped files sit in a directory the black box marks as a conflict hotspot (at least blackBox.hotspotReservationThreshold entries; 0 turns reservations off and the page is empty). A second mission touching a reserved directory is warned at dispatch, or chained after the reserving mission when the vessel has serializeOverlappingMissions. Ordered by directory then mission id; at most 200 active missions and 1000 hotspot directories are read (scanTruncated).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false))
                .WithResponse(200, OpenApiJson.For<VesselReservationPage>("Reservations"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/vessels/{id}/reservations/enumerate", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
                await _Insights.EnumerateReservationsAsync(ctx, req.Parameters["id"], EnumerationRequestReader.Read(req, _BodyJsonOptions)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate vessel reservations")
                .WithDescription("POST form of GET /api/v1/vessels/{id}/reservations: the paging query comes from the body, and querystring values override it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging query", false))
                .WithResponse(200, OpenApiJson.For<VesselReservationPage>("Reservations"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/missions/lint-brief", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
            {
                BriefLintRequest body = ReadBody<BriefLintRequest>(req) ?? new BriefLintRequest();
                return await _Insights.LintBriefAsync(ctx, body).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Lint a mission brief")
                .WithDescription("Flags a brief that describes a dependency (\"after mission 2 lands\", \"depends on\", \"mission N\") without dependsOnMissionId (BRIEF-DEP-001), a verification-only brief likely to fail as NoOp (BRIEF-NOOP-001), a path whose top-level directory the vessel does not have (BRIEF-PATH-001, only when vesselId is given and the vessel's tracked files can be read) and a host or parent-relative path (BRIEF-PATH-002). At most 50 findings. Findings are warnings: dispatch runs the same lint and never refuses a brief because of it.")
                .WithRequestBody(OpenApiJson.BodyFor<BriefLintRequest>("Title, description, optional vesselId, dependsOnMissionId and voyageId", true))
                .WithResponse(200, OpenApiJson.For<BriefLintResult>("Lint result"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/blackbox-doctor", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
            {
                BlackBoxDoctorRequest query = new BlackBoxDoctorRequest();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                query.WindowDays = IntOf(req, "windowDays");
                return await _Insights.GetDoctorAsync(ctx, req.Parameters["id"], query.WindowDays, query).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Get the vessel's black box configuration doctor")
                .WithDescription("Configuration recommendations that recur across the vessel's black box entries in a window (windowDays, 1 to 365, default 30), grouped by rule and setting with the count, first and last seen and the newest entry as the example (apply it with apply_blackbox_recommendation). Most frequent first. The records of the newest 200 entries in the window are read (entriesScanned, scanTruncated); entries the caller cannot see are not counted.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("windowDays", "Window in days, 1 to 365 (default 30)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxDoctorPage>("Doctor findings"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/vessels/{id}/blackbox-doctor/enumerate", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
            {
                BlackBoxDoctorRequest query = ReadBody<BlackBoxDoctorRequest>(req) ?? new BlackBoxDoctorRequest();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                int? window = IntOf(req, "windowDays");
                if (window.HasValue) query.WindowDays = window;
                return await _Insights.GetDoctorAsync(ctx, req.Parameters["id"], query.WindowDays, query).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate the vessel's black box configuration doctor")
                .WithDescription("POST form of GET /api/v1/vessels/{id}/blackbox-doctor: windowDays and paging come from the body, and querystring values override them.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxDoctorRequest>("Window and paging", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxDoctorPage>("Doctor findings"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/recovery-stats", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
            {
                BlackBoxRecoveryStatsRequest query = new BlackBoxRecoveryStatsRequest();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                query.RuleId = req.Query.GetValueOrDefault("ruleId");
                return await _Insights.EnumerateRecoveryStatsAsync(ctx, query.RuleId, query.VesselId, query).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Get black box recovery statistics")
                .WithDescription("One page of recovery path statistics over the caller's confirmed recovery runs (a user's own runs, a tenant admin's tenant, an admin's every tenant), grouped by rule and path and paged in the database, most runs first: runs, succeeded (the mission completed afterwards), failed (it failed, was cancelled or was captured again, or a step failed), pending, expired (nothing terminal within 7 days) and successRate (succeeded over settled runs). The advisor uses the rates to break ties between equally weighted paths.")
                .WithParameter(OpenApiParameterMetadata.Query("ruleId", "Rule filter, for example BBX-LAND-001", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Vessel filter (vsl_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxRecoveryStat>>("Recovery statistics"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/recovery-stats/enumerate", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
            {
                BlackBoxRecoveryStatsRequest query = ReadBody<BlackBoxRecoveryStatsRequest>(req) ?? new BlackBoxRecoveryStatsRequest();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                string? rule = req.Query.GetValueOrDefault("ruleId");
                if (!String.IsNullOrEmpty(rule)) query.RuleId = rule;
                return await _Insights.EnumerateRecoveryStatsAsync(ctx, query.RuleId, query.VesselId, query).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate black box recovery statistics")
                .WithDescription("POST form of GET /api/v1/blackbox/recovery-stats: ruleId, vesselId and paging come from the body, and querystring values override them.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxRecoveryStatsRequest>("Filters and paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxRecoveryStat>>("Recovery statistics"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/{id}/diff/{otherId}", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
                await _Insights.DiffAsync(ctx, req.Parameters["id"], req.Parameters["otherId"]).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Diff two black box entries")
                .WithDescription("What changed between two entries (two attempts of one mission, or any two the caller can see): captain, runtime, model, persona, status, phase and step, Armada version, failure kind and fingerprint, base commit and target tips, every effective configuration value, and the files each mission changed (first 200 per side, with counts). At most 100 differences.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Black box entry ID (bbx_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("otherId", "Other black box entry ID (bbx_ prefix)"))
                .WithResponse(200, OpenApiJson.For<BlackBoxAttemptDiff>("Attempt diff"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/{id}/nearby-successes", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
                await _Insights.EnumerateNearbySuccessesAsync(ctx, req.Parameters["id"], QueryOf(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("List an entry's nearby successes")
                .WithDescription("One page of the successful landings (success flight recorder, kept blackBox.successRetentionDays) on the entry's vessel within blackBox.nearbySuccessWindowHours of the failure that changed files the failure also touched, most overlap first, then closest in time. At most 200 landings in the window are compared. The record's Related section carries the first ten.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Black box entry ID (bbx_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxNearbySuccess>>("Nearby successes"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/nearby-successes/enumerate", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
                await _Insights.EnumerateNearbySuccessesAsync(ctx, req.Parameters["id"], EnumerationRequestReader.Read(req, _BodyJsonOptions)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate an entry's nearby successes")
                .WithDescription("POST form of GET /api/v1/blackbox/{id}/nearby-successes: the paging query comes from the body, and querystring values override it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Black box entry ID (bbx_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxNearbySuccess>>("Nearby successes"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/{id}/timeline", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
                await _Insights.GetTimelineAsync(ctx, req.Parameters["id"], TimelineQueryOf(req, null)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get an entry's combined timeline")
                .WithDescription("One page of one timeline merged from the entry (captured, enriched, analyzed, resolved) and its notes, the mission (or voyage) and its events, the dock, the merge queue entry, the captain's events in the mission's window and the Harbor link events. Oldest first unless order=CreatedDescending. Each source is capped at 200 items and the timeline at 1000 (sources and truncated say where a cut applied).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Black box entry ID (bbx_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("order", "CreatedAscending (default) or CreatedDescending", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxTimelinePage>("Timeline"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/timeline/enumerate", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
                await _Insights.GetTimelineAsync(ctx, req.Parameters["id"], TimelineQueryOf(req, req.Http.Request.DataAsString)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate an entry's combined timeline")
                .WithDescription("POST form of GET /api/v1/blackbox/{id}/timeline: order and paging come from the body (oldest first when order is not given), and querystring values override them.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Black box entry ID (bbx_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Order and paging", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxTimelinePage>("Timeline"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/voyages/{id}/blackbox/cascade", (ApiRequest req) => HandleAsync(req, authenticate, authz, async ctx =>
                await _Insights.GetVoyageCascadeAsync(ctx, req.Parameters["id"]).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get a voyage's black box cascade graph")
                .WithDescription("The voyage's missions (status, failure kind, current black box entry; at most 200, oldest first) and the edges between them: DependsOn, CancelledBecause (a dependent stopped because its dependency failed), BlockedBy (a dependent held behind a failed dependency) and Sibling (from the cascade root, the earliest failure with no failed dependency, to the voyage's other independent failures). At most 1000 edges; truncated is set when a cap applied.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(200, OpenApiJson.For<BlackBoxCascadeGraph>("Cascade graph"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private static async Task<object> HandleAsync(ApiRequest req, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz, Func<AuthContext, Task<object>> body)
        {
            AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
            if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
            {
                req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                return new ApiErrorResponse
                {
                    Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized,
                    Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                };
            }
            if (String.IsNullOrEmpty(ctx.TenantId) && !ctx.IsAdmin) ctx.TenantId = Armada.Core.Constants.DefaultTenantId;
            try
            {
                return await body(ctx).ConfigureAwait(false);
            }
            catch (BlackBoxNotFoundException ex)
            {
                return Error(req, 404, ex.Message);
            }
            catch (BlackBoxDeletingException ex)
            {
                return Error(req, 409, ex.Message);
            }
            catch (JsonException ex)
            {
                return Error(req, 400, "Request body is not valid: " + ex.Message);
            }
            catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
            {
                return RouteErrorMapper.ToResponse(req, ex);
            }
        }

        private static T? ReadBody<T>(ApiRequest req) where T : class
        {
            string body = req.Http.Request.DataAsString;
            if (String.IsNullOrWhiteSpace(body)) return null;
            return JsonSerializer.Deserialize<T>(body, _BodyJsonOptions);
        }

        private static EnumerationQuery QueryOf(ApiRequest req)
        {
            EnumerationQuery query = new EnumerationQuery();
            query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
            return query;
        }

        private static EnumerationQuery TimelineQueryOf(ApiRequest req, string? body)
        {
            EnumerationQuery query = String.IsNullOrWhiteSpace(body) ? new EnumerationQuery() : (JsonSerializer.Deserialize<EnumerationQuery>(body, _BodyJsonOptions) ?? new EnumerationQuery());
            bool orderGiven = !String.IsNullOrEmpty(req.Query.GetValueOrDefault("order")) || (!String.IsNullOrWhiteSpace(body) && body.IndexOf("\"order\"", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!orderGiven) query.Order = EnumerationOrderEnum.CreatedAscending;
            query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
            return query;
        }

        private static int? IntOf(ApiRequest req, string key)
        {
            string? value = req.Query.GetValueOrDefault(key);
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!Int32.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
                throw new ArgumentException(key + " must be a whole number.");
            return parsed;
        }

        private static ApiErrorResponse Error(ApiRequest req, int status, string message)
        {
            req.Http.Response.StatusCode = status;
            return new ApiErrorResponse { Error = RouteErrorMapper.ResultFor(status), Message = message };
        }

        #endregion
    }
}
