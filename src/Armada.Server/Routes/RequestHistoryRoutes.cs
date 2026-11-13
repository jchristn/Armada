namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
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
    /// REST API routes for persistent request-history capture and summaries.
    /// </summary>
    public class RequestHistoryRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RequestHistoryRoutes(DatabaseDriver database, JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
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
            app.Post("/api/v1/request-history/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                string body = req.Http.Request.DataAsString;
                RequestHistoryQuery query = String.IsNullOrWhiteSpace(body)
                    ? new RequestHistoryQuery()
                    : JsonSerializer.Deserialize<RequestHistoryQuery>(body, _jsonOptions) ?? new RequestHistoryQuery();
                ApplyQuerystringOverrides(req, query);
                query.PageNumber = Math.Max(1, query.PageNumber);
                query.PageSize = Math.Clamp(query.PageSize, 1, 500);
                ApplyScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<RequestHistoryEntry> result = await _database.RequestHistory.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("RequestHistory")
                .WithSummary("Enumerate request-history entries")
                .WithDescription("One page of request-history entries scoped to the authenticated caller. The JSON body (RequestHistoryQuery) carries the filters, PageNumber, and PageSize (1 to 500, default 25); querystring values override it.")
                .WithRequestBody(OpenApiJson.BodyFor<RequestHistoryQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<RequestHistoryEntry>>("One page"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/request-history", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                RequestHistoryQuery query = BuildQueryFromRequest(req);
                ApplyScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<RequestHistoryEntry> result = await _database.RequestHistory.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("RequestHistory")
                .WithSummary("List request-history entries")
                .WithDescription("Returns paginated captured REST requests scoped to the authenticated caller.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("method", "Optional HTTP method filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("route", "Optional route/path filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("statusCode", "Optional status-code filter", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("principal", "Optional principal filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("tenantId", "Optional tenant filter (admin only)", false))
                .WithParameter(OpenApiParameterMetadata.Query("userId", "Optional user filter (admin or tenant admin)", false))
                .WithParameter(OpenApiParameterMetadata.Query("credentialId", "Optional credential filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("isSuccess", "Optional success-state filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Optional lower bound UTC timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Optional upper bound UTC timestamp", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<RequestHistoryEntry>>("Paginated request history"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/request-history/summary", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                RequestHistoryQuery query = BuildQueryFromRequest(req);
                if (!query.FromUtc.HasValue) query.FromUtc = DateTime.UtcNow.AddHours(-24);
                if (!query.ToUtc.HasValue) query.ToUtc = DateTime.UtcNow;
                if (query.BucketMinutes <= 0) query.BucketMinutes = 15;
                ApplyScope(ctx, query);

                try
                {
                    RequestHistorySummaryBuilder.ValidateWindow(query);
                    List<RequestHistoryEntry> entries = await _database.RequestHistory.EnumerateForSummaryAsync(query).ConfigureAwait(false);
                    return (object)RequestHistorySummaryBuilder.Build(entries, query);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("RequestHistory")
                .WithSummary("Summarize request history")
                .WithDescription("Returns aggregate counts and bucketed request activity for the supplied filters and time window. At most 1000 buckets: a window and bucketMinutes that would produce more is rejected with 400 (Data.Code CollectionLimitExceeded).")
                .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket width in minutes (default 15, 1 to 527040)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "UTC summary start timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "UTC summary end timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("method", "Optional HTTP method filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("route", "Optional route/path filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("statusCode", "Optional status-code filter", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("principal", "Optional principal filter", false))
                .WithResponse(200, OpenApiJson.For<RequestHistorySummaryResult>("Request-history summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/request-history/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                RequestHistoryQuery query = BuildQueryFromRequest(req);
                ApplyScope(ctx, query);
                RequestHistoryRecord? record = await _database.RequestHistory.ReadAsync(req.Parameters["id"], query).ConfigureAwait(false);
                if (record == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Request history entry not found" };
                }
                return record;
            },
            api => api
                .WithTag("RequestHistory")
                .WithSummary("Get one request-history entry")
                .WithDescription("Returns one captured request with expanded headers, parameters, and body snapshots.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Request history ID (req_ prefix)"))
                .WithResponse(200, OpenApiJson.For<RequestHistoryRecord>("Request-history record"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/request-history/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                RequestHistoryQuery query = BuildQueryFromRequest(req);
                ApplyScope(ctx, query);
                RequestHistoryRecord? record = await _database.RequestHistory.ReadAsync(req.Parameters["id"], query).ConfigureAwait(false);
                if (record == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Request history entry not found" };
                }

                await _database.RequestHistory.DeleteAsync(req.Parameters["id"], query).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("RequestHistory")
                .WithSummary("Delete one request-history entry")
                .WithDescription("Deletes one captured request-history record within the caller's scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Request history ID (req_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<DeleteMultipleRequest>("/api/v1/request-history/delete/multiple", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeleteMultipleRequest? body = JsonSerializer.Deserialize<DeleteMultipleRequest>(req.Http.Request.DataAsString, _jsonOptions);
                if (body == null || body.Ids == null || body.Ids.Count == 0)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Ids is required and must not be empty" };
                }

                // The request (and so the Skipped list derived from it) is bounded; nothing is deleted when it is over.
                try
                {
                    CollectionLimits.RequireRequestItems(body.Ids, "Ids");
                }
                catch (CollectionLimitExceededException limit)
                {
                    return RouteErrorMapper.LimitExceeded(req, limit);
                }

                RequestHistoryQuery scopeQuery = new RequestHistoryQuery();
                ApplyScope(ctx, scopeQuery);

                DeleteMultipleResult result = new DeleteMultipleResult();
                foreach (string id in body.Ids)
                {
                    if (String.IsNullOrWhiteSpace(id))
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id ?? String.Empty, "Empty ID"));
                        continue;
                    }

                    RequestHistoryRecord? record = await _database.RequestHistory.ReadAsync(id, scopeQuery).ConfigureAwait(false);
                    if (record == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }

                    await _database.RequestHistory.DeleteAsync(id, scopeQuery).ConfigureAwait(false);
                    result.Deleted++;
                }

                result.ResolveStatus();
                return result;
            },
            api => api
                .WithTag("RequestHistory")
                .WithSummary("Delete multiple request-history entries")
                .WithDescription("Deletes multiple request-history records by identifier within the caller's scope. At most 1000 ids; more is rejected with 400 (Data.Code CollectionLimitExceeded) and nothing is deleted. Skipped has one entry per id that was not deleted, so it is bounded by the request.")
                .WithRequestBody(OpenApiJson.BodyFor<DeleteMultipleRequest>("List of request-history IDs to delete"))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post<RequestHistoryQuery>("/api/v1/request-history/delete/by-filter", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                RequestHistoryQuery query = JsonSerializer.Deserialize<RequestHistoryQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new RequestHistoryQuery();
                ApplyScope(ctx, query);

                int deleted = await _database.RequestHistory.DeleteByFilterAsync(query).ConfigureAwait(false);
                DeleteMultipleResult result = new DeleteMultipleResult { Deleted = deleted };
                result.ResolveStatus();
                return result;
            },
            api => api
                .WithTag("RequestHistory")
                .WithSummary("Delete filtered request-history entries")
                .WithDescription("Deletes all request-history records matching the supplied filters within the caller's scope.")
                .WithRequestBody(OpenApiJson.BodyFor<RequestHistoryQuery>("Request-history filter query", false))
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

        private static RequestHistoryQuery BuildQueryFromRequest(ApiRequest req)
        {
            return ApplyQuerystringOverrides(req, new RequestHistoryQuery());
        }

        private static RequestHistoryQuery ApplyQuerystringOverrides(ApiRequest req, RequestHistoryQuery query)
        {
            if (int.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (int.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (int.TryParse(req.Query.GetValueOrDefault("statusCode"), out int statusCode))
                query.StatusCode = statusCode;
            if (int.TryParse(req.Query.GetValueOrDefault("bucketMinutes"), out int bucketMinutes))
                query.BucketMinutes = Math.Max(1, bucketMinutes);

            query.Method = NormalizeEmpty(req.Query.GetValueOrDefault("method")) ?? query.Method;
            query.Route = NormalizeEmpty(req.Query.GetValueOrDefault("route")) ?? query.Route;
            query.Principal = NormalizeEmpty(req.Query.GetValueOrDefault("principal")) ?? query.Principal;
            query.TenantId = NormalizeEmpty(req.Query.GetValueOrDefault("tenantId")) ?? query.TenantId;
            query.UserId = NormalizeEmpty(req.Query.GetValueOrDefault("userId")) ?? query.UserId;
            query.CredentialId = NormalizeEmpty(req.Query.GetValueOrDefault("credentialId")) ?? query.CredentialId;

            if (TryParseNullableBool(req.Query.GetValueOrDefault("isSuccess"), out bool? isSuccess)
                || TryParseNullableBool(req.Query.GetValueOrDefault("successOnly"), out isSuccess))
            {
                query.IsSuccess = isSuccess;
            }

            if (DateTime.TryParse(Uri.UnescapeDataString(req.Query.GetValueOrDefault("fromUtc") ?? ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(Uri.UnescapeDataString(req.Query.GetValueOrDefault("toUtc") ?? ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();

            return query;
        }

        private static void ApplyScope(AuthContext ctx, RequestHistoryQuery query)
        {
            if (ctx.IsAdmin) return;

            query.TenantId = ctx.TenantId;
            if (!ctx.IsTenantAdmin)
            {
                query.UserId = ctx.UserId;
            }
        }

        private static bool TryParseNullableBool(string? value, out bool? result)
        {
            result = null;
            if (String.IsNullOrWhiteSpace(value)) return false;

            if (bool.TryParse(value, out bool parsed))
            {
                result = parsed;
                return true;
            }

            if (value == "1")
            {
                result = true;
                return true;
            }

            if (value == "0")
            {
                result = false;
                return true;
            }

            return false;
        }

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : Uri.UnescapeDataString(value);
        }

    }
}
