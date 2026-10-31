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

                try
                {
                    TokenUsageSummaryBuilder.ValidateWindow(query);
                    List<TokenUsageRecord> records = await _database.TokenUsage.EnumerateForSummaryAsync(query).ConfigureAwait(false);
                    return (object)TokenUsageSummaryBuilder.Build(records, query);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("TokenUsage")
                .WithSummary("Summarize token usage")
                .WithDescription("Returns token usage aggregated into time buckets (with a per-model breakdown), a whole-window per-model aggregate ordered most-used first, and grand totals: the data behind the dashboard token-usage charts. At most 1000 buckets: a window and bucketMinutes that would produce more is rejected with 400 (Data.Code CollectionLimitExceeded). ByModel and each bucket's Models list the modelLimit most-used models; the rest are summed into OtherModels (OtherModelCount says how many), so totals always add up.")
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "UTC summary start timestamp (default now-24h)", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "UTC summary end timestamp (default now)", false))
                .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket width in minutes; fractional allowed, e.g. 0.5 for 30-second buckets (default 15, at most 527040)", false))
                .WithParameter(OpenApiParameterMetadata.Query("modelLimit", "Most models broken out by name (default 10, 1 to 100); the rest are summed into OtherModels", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("model", "Optional model filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("runtime", "Optional runtime filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("source", "Optional source filter (mission, chat, planning, synthetic)", false))
                .WithParameter(OpenApiParameterMetadata.Query("sourceId", "Optional exact work or synthetic batch identifier", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Optional captain filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("harborId", "Optional Harbor filter: usage of work that ran on this Harbor", false))
                .WithParameter(OpenApiParameterMetadata.Query("tenantId", "Optional tenant filter (admin only)", false))
                .WithParameter(OpenApiParameterMetadata.Query("userId", "Optional user filter (admin or tenant admin)", false))
                .WithResponse(200, OpenApiJson.For<TokenUsageSummaryResult>("Token-usage summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/token-usage/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                string body = req.Http.Request.DataAsString;
                TokenUsageQuery query = String.IsNullOrWhiteSpace(body)
                    ? new TokenUsageQuery()
                    : JsonSerializer.Deserialize<TokenUsageQuery>(body, _jsonOptions) ?? new TokenUsageQuery();
                ApplyQuerystringOverrides(req, query);
                query.PageNumber = Math.Max(1, query.PageNumber);
                query.PageSize = Math.Clamp(query.PageSize, 1, 500);
                ApplyScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<TokenUsageRecord> result = await _database.TokenUsage.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("TokenUsage")
                .WithSummary("Enumerate token-usage records")
                .WithDescription("One page of token-usage records scoped to the authenticated caller. The JSON body (TokenUsageQuery) carries the filters, PageNumber, and PageSize (1 to 500, default 25); querystring values override it.")
                .WithRequestBody(OpenApiJson.BodyFor<TokenUsageQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<TokenUsageRecord>>("One page"))
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
                .WithParameter(OpenApiParameterMetadata.Query("source", "Optional source filter (mission, chat, planning, synthetic)", false))
                .WithParameter(OpenApiParameterMetadata.Query("sourceId", "Optional exact work or synthetic batch identifier", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Optional captain filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("harborId", "Optional Harbor filter: usage of work that ran on this Harbor", false))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Optional lower bound UTC timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Optional upper bound UTC timestamp", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<TokenUsageRecord>>("Paginated token-usage records"))
                .WithSecurity("ApiKey"));

            app.Post<SyntheticActivityRequest>("/api/v1/activity/synthetic", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                if (!ctx.IsAdmin) { req.Http.Response.StatusCode = 403; return BuildAuthError(req); }
                try
                {
                    SyntheticActivityRequest body = JsonSerializer.Deserialize<SyntheticActivityRequest>(req.Http.Request.DataAsString, _jsonOptions)
                        ?? throw new ArgumentException("Synthetic activity profile is required.");
                    return (object)await SyntheticActivityGenerator.GenerateAsync(_database, body, ctx.TenantId!, ctx.UserId!).ConfigureAwait(false);
                }
                catch (ArgumentException ex) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message }; }
                catch (JsonException) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid synthetic activity JSON." }; }
            }, api => api.WithTag("SyntheticActivity")
                .WithSummary("Generate historical synthetic activity for screenshots")
                .WithDescription("Global admin only. Generates terminal missions, requests, events, read signals, and disabled screenshot Harbor metrics without executing work. Fixed profiles replay without overwriting existing records.")
                .WithRequestBody(OpenApiJson.BodyFor<SyntheticActivityRequest>("Generation profile", true))
                .WithResponse(200, OpenApiJson.For<SyntheticActivityResult>("Generation receipt"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest()).WithSecurity("ApiKey"));

            app.Post<TokenUsageSyntheticRequest>("/api/v1/token-usage/synthetic", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                if (!ctx.IsAdmin) { req.Http.Response.StatusCode = 403; return BuildAuthError(req); }
                try
                {
                    TokenUsageSyntheticRequest body = JsonSerializer.Deserialize<TokenUsageSyntheticRequest>(req.Http.Request.DataAsString, _jsonOptions)
                        ?? throw new ArgumentException("Synthetic import body is required.");
                    return (object)await TokenUsageSyntheticImporter.ImportAsync(_database.TokenUsage, body, ctx.TenantId!, ctx.UserId!, DateTime.UtcNow).ConfigureAwait(false);
                }
                catch (ArgumentException ex) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message }; }
                catch (JsonException) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid synthetic import JSON." }; }
                catch (TokenUsageSyntheticConflictException ex) { req.Http.Response.StatusCode = 409; return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message }; }
            }, api => api
                .WithTag("TokenUsage")
                .WithSummary("Import explicitly synthetic usage for screenshots")
                .WithDescription("Admin-only import of 1-500 historical simulated records. No inference calls or mission dispatch. Sources and ownership are assigned by the server; identical ordered batches are idempotent.")
                .WithRequestBody(OpenApiJson.BodyFor<TokenUsageSyntheticRequest>("Synthetic batch", true))
                .WithResponse(200, OpenApiJson.For<TokenUsageSyntheticResult>("Synthetic import receipt"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Batch identifier conflicts with existing content"))
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
            return ApplyQuerystringOverrides(req, new TokenUsageQuery());
        }

        private static TokenUsageQuery ApplyQuerystringOverrides(ApiRequest req, TokenUsageQuery query)
        {
            if (int.TryParse(QueryValueReader.Read(req.Query, "pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (int.TryParse(QueryValueReader.Read(req.Query, "pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (double.TryParse(QueryValueReader.Read(req.Query, "bucketMinutes"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double bucketMinutes) && bucketMinutes > 0)
                query.BucketMinutes = bucketMinutes;
            if (int.TryParse(QueryValueReader.Read(req.Query, "modelLimit"), out int modelLimit))
                query.ModelLimit = modelLimit;

            query.Model = NormalizeEmpty(QueryValueReader.Read(req.Query, "model")) ?? query.Model;
            query.Runtime = NormalizeEmpty(QueryValueReader.Read(req.Query, "runtime")) ?? query.Runtime;
            query.Source = NormalizeEmpty(QueryValueReader.Read(req.Query, "source")) ?? query.Source;
            query.SourceId = NormalizeEmpty(QueryValueReader.Read(req.Query, "sourceId")) ?? query.SourceId;
            query.VesselId = NormalizeEmpty(QueryValueReader.Read(req.Query, "vesselId")) ?? query.VesselId;
            query.CaptainId = NormalizeEmpty(QueryValueReader.Read(req.Query, "captainId")) ?? query.CaptainId;
            query.HarborId = NormalizeEmpty(QueryValueReader.Read(req.Query, "harborId")) ?? query.HarborId;
            query.TenantId = NormalizeEmpty(QueryValueReader.Read(req.Query, "tenantId")) ?? query.TenantId;
            query.UserId = NormalizeEmpty(QueryValueReader.Read(req.Query, "userId")) ?? query.UserId;

            if (DateTime.TryParse(QueryValueReader.Read(req.Query, "fromUtc"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(QueryValueReader.Read(req.Query, "toUtc"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime toUtc))
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
