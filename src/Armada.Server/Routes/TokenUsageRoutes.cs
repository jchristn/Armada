namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for token-usage summaries and listings that power the dashboard token-usage charts.
    /// </summary>
    public class TokenUsageRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public TokenUsageRoutes(DatabaseDriver database, JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Web server.</param>
        /// <param name="authenticate">Authentication delegate.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/token-usage/summary", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                TokenUsageQuery query = BuildQueryFromRequest(req);
                if (!query.FromUtc.HasValue) query.FromUtc = DateTime.UtcNow.AddHours(-24);
                if (!query.ToUtc.HasValue) query.ToUtc = DateTime.UtcNow;
                if (query.BucketMinutes <= 0) query.BucketMinutes = 15;
                ApplyScope(ctx, query);

                List<TokenUsageRecord> records = await _database.TokenUsage.EnumerateForSummaryAsync(query).ConfigureAwait(false);
                return TokenUsageSummaryBuilder.Build(records, query);
            },
            api => api
                .WithTag("TokenUsage")
                .WithSummary("Summarize token usage")
                .WithDescription("Returns token usage aggregated into time buckets (with a per-model breakdown), a whole-window per-model aggregate ordered most-used first, and grand totals -- the data behind the dashboard token-usage charts.")
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "UTC summary start timestamp (default now-24h)", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "UTC summary end timestamp (default now)", false))
                .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket width in minutes; fractional allowed, e.g. 0.5 for 30-second buckets (default 15)", false))
                .WithParameter(OpenApiParameterMetadata.Query("model", "Optional model filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("runtime", "Optional runtime filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("source", "Optional source filter (mission, chat, planning)", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Optional captain filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("tenantId", "Optional tenant filter (admin only)", false))
                .WithParameter(OpenApiParameterMetadata.Query("userId", "Optional user filter (admin or tenant admin)", false))
                .WithResponse(200, OpenApiJson.For<TokenUsageSummaryResult>("Token-usage summary"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/token-usage", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                TokenUsageQuery query = BuildQueryFromRequest(req);
                ApplyScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<TokenUsageRecord> result = await _database.TokenUsage.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("TokenUsage")
                .WithSummary("List token-usage records")
                .WithDescription("Returns paginated token-usage records scoped to the authenticated caller.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("model", "Optional model filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("runtime", "Optional runtime filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("source", "Optional source filter (mission, chat, planning)", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Optional captain filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Optional lower bound UTC timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Optional upper bound UTC timestamp", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<TokenUsageRecord>>("Paginated token-usage records"))
                .WithSecurity("ApiKey"));

            app.Post<TokenUsageQuery>("/api/v1/token-usage/delete/by-filter", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                TokenUsageQuery query = JsonSerializer.Deserialize<TokenUsageQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new TokenUsageQuery();
                ApplyScope(ctx, query);

                int deleted = await _database.TokenUsage.DeleteByFilterAsync(query).ConfigureAwait(false);
                DeleteMultipleResult result = new DeleteMultipleResult { Deleted = deleted };
                result.ResolveStatus();
                return result;
            },
            api => api
                .WithTag("TokenUsage")
                .WithSummary("Delete filtered token-usage records")
                .WithDescription("Deletes all token-usage records matching the supplied filters within the caller's scope.")
                .WithRequestBody(OpenApiJson.BodyFor<TokenUsageQuery>("Token-usage filter query", false))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete summary"))
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

        private static TokenUsageQuery BuildQueryFromRequest(ApiRequest req)
        {
            TokenUsageQuery query = new TokenUsageQuery();

            if (int.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (int.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (double.TryParse(req.Query.GetValueOrDefault("bucketMinutes"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double bucketMinutes) && bucketMinutes > 0)
                query.BucketMinutes = bucketMinutes;

            query.Model = NormalizeEmpty(req.Query.GetValueOrDefault("model"));
            query.Runtime = NormalizeEmpty(req.Query.GetValueOrDefault("runtime"));
            query.Source = NormalizeEmpty(req.Query.GetValueOrDefault("source"));
            query.VesselId = NormalizeEmpty(req.Query.GetValueOrDefault("vesselId"));
            query.CaptainId = NormalizeEmpty(req.Query.GetValueOrDefault("captainId"));
            query.TenantId = NormalizeEmpty(req.Query.GetValueOrDefault("tenantId"));
            query.UserId = NormalizeEmpty(req.Query.GetValueOrDefault("userId"));

            if (DateTime.TryParse(req.Query.GetValueOrDefault("fromUtc"), out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(req.Query.GetValueOrDefault("toUtc"), out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();

            return query;
        }

        private static void ApplyScope(AuthContext ctx, TokenUsageQuery query)
        {
            if (ctx.IsAdmin) return;

            query.TenantId = ctx.TenantId;
            if (!ctx.IsTenantAdmin)
            {
                query.UserId = ctx.UserId;
            }
        }

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
