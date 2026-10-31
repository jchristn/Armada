namespace Armada.Server.Routes
{
    using System;
    using System.Linq;
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
    /// REST API routes for cross-entity Armada history and reporting.
    /// </summary>
    public class HistoryRoutes
    {
        private readonly HistoricalTimelineService _History;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HistoryRoutes(HistoricalTimelineService history)
        {
            _History = history ?? throw new ArgumentNullException(nameof(history));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/history", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                HistoricalTimelineQuery query = BuildQueryFromRequest(req);
                ApplyScope(ctx, query);
                return await _History.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("History")
                .WithSummary("List historical timeline entries")
                .WithDescription("Returns a unified cross-entity timeline spanning missions, voyages, planning sessions, checks, requests, merge entries, and events.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1 to 1000, default 50)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("order", "CreatedDescending (newest first, default) or CreatedAscending", false))
                .WithParameter(OpenApiParameterMetadata.Query("objectiveId", "Optional objective filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("environmentId", "Optional environment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("deploymentId", "Optional deployment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("incidentId", "Optional incident filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("postmortemOnly", "Optional flag that narrows results to incidents with postmortem data and directly linked lifecycle entries", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Optional mission filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Optional voyage filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("actor", "Optional actor or principal filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("text", "Optional free-text search", false))
                .WithParameter(OpenApiParameterMetadata.Query("sourceType", "Optional comma-separated source types", false))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Optional lower-bound UTC timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Optional upper-bound UTC timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("excludeReadRequests", "Optional flag that leaves out request-history entries for GET, HEAD, and OPTIONS requests", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<HistoricalTimelineEntry>>("Historical timeline entries"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/history/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                HistoricalTimelineQuery query = JsonSerializer.Deserialize<HistoricalTimelineQuery>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? new HistoricalTimelineQuery();
                ApplyQuerystringOverrides(req, query);
                ApplyScope(ctx, query);
                return await _History.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("History")
                .WithSummary("Enumerate historical timeline entries")
                .WithDescription("One page of the unified cross-entity timeline using a JSON body (HistoricalTimelineQuery: the standard EnumerationQuery paging and Order plus the timeline filters) and optional querystring overrides. PageSize is 1 to 1000 (default 50); TotalRecords and TotalPages count every matching entry; ordering is by occurrence time with a stable tie-break, so consecutive pages never overlap.")
                .WithRequestBody(OpenApiJson.BodyFor<HistoricalTimelineQuery>("Historical timeline query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<HistoricalTimelineEntry>>("Historical timeline entries"))
                .WithSecurity("ApiKey"));
        }

        private static HistoricalTimelineQuery BuildQueryFromRequest(ApiRequest req)
        {
            HistoricalTimelineQuery query = new HistoricalTimelineQuery();
            ApplyQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyQuerystringOverrides(ApiRequest req, HistoricalTimelineQuery query)
        {
            // PageNumber and PageSize clamp themselves (EnumerationQuery: page 1 or later, 1 to 1000 rows).
            if (Int32.TryParse(QueryValueReader.Read(req.Query, "pageNumber"), out int pageNumber))
                query.PageNumber = pageNumber;
            if (Int32.TryParse(QueryValueReader.Read(req.Query, "pageSize"), out int pageSize))
                query.PageSize = pageSize;
            if (Enum.TryParse<EnumerationOrderEnum>(QueryValueReader.Read(req.Query, "order"), true, out EnumerationOrderEnum order) && Enum.IsDefined(typeof(EnumerationOrderEnum), order))
                query.Order = order;
            if (DateTime.TryParse(QueryValueReader.Read(req.Query, "fromUtc"), out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(QueryValueReader.Read(req.Query, "toUtc"), out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();
            if (Boolean.TryParse(QueryValueReader.Read(req.Query, "postmortemOnly"), out bool postmortemOnly))
                query.PostmortemOnly = postmortemOnly;
            if (Boolean.TryParse(QueryValueReader.Read(req.Query, "excludeReadRequests"), out bool excludeReadRequests))
                query.ExcludeReadRequests = excludeReadRequests;

            query.ObjectiveId = NormalizeEmpty(QueryValueReader.Read(req.Query, "objectiveId")) ?? query.ObjectiveId;
            query.VesselId = NormalizeEmpty(QueryValueReader.Read(req.Query, "vesselId")) ?? query.VesselId;
            query.EnvironmentId = NormalizeEmpty(QueryValueReader.Read(req.Query, "environmentId")) ?? query.EnvironmentId;
            query.DeploymentId = NormalizeEmpty(QueryValueReader.Read(req.Query, "deploymentId")) ?? query.DeploymentId;
            query.IncidentId = NormalizeEmpty(QueryValueReader.Read(req.Query, "incidentId")) ?? query.IncidentId;
            query.MissionId = NormalizeEmpty(QueryValueReader.Read(req.Query, "missionId")) ?? query.MissionId;
            query.VoyageId = NormalizeEmpty(QueryValueReader.Read(req.Query, "voyageId")) ?? query.VoyageId;
            query.Actor = NormalizeEmpty(QueryValueReader.Read(req.Query, "actor")) ?? query.Actor;
            query.Text = NormalizeEmpty(QueryValueReader.Read(req.Query, "text")) ?? query.Text;

            string? sourceTypeValue = NormalizeEmpty(QueryValueReader.Read(req.Query, "sourceType"));
            if (!String.IsNullOrWhiteSpace(sourceTypeValue))
            {
                query.SourceTypes = sourceTypeValue
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(item => !String.IsNullOrWhiteSpace(item))
                    .ToList();
            }
        }

        private static void ApplyScope(AuthContext ctx, HistoricalTimelineQuery query)
        {
            if (ctx.IsAdmin)
                return;

            query.TenantId = ctx.TenantId;
            if (!ctx.IsTenantAdmin)
                query.UserId = ctx.UserId;
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
