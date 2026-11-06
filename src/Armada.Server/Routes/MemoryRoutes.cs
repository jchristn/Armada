namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
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
    /// REST API routes for durable agent memory: list/search, enumerate, create (upsert), read, update, and delete.
    /// Memories are scoped to the caller exactly like other configuration entities.
    /// </summary>
    public class MemoryRoutes
    {
        private readonly MemoryService _Memories;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="memories">Memory service.</param>
        public MemoryRoutes(MemoryService memories)
        {
            _Memories = memories ?? throw new ArgumentNullException(nameof(memories));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Web server.</param>
        /// <param name="authenticate">Authentication delegate.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/memories", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                MemoryEnumerateRequest query = new MemoryEnumerateRequest();
                query.ApplyMemoryQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
                query.ApplyDefaultSort(!String.IsNullOrEmpty(QueryValueReader.Read(req.Query, "order")));
                return await _Memories.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Memories")
                .WithSummary("List/search memories")
                .WithDescription("Returns durable memories visible to the caller. Accepts the standard enumeration query parameters (pageNumber, pageSize, order, createdAfter, createdBefore, vesselId, voyageId, missionId, tenantId, userId) plus search, type, topic, topicContains, vesselName, summaryContains, scope, minSalience, and sortBy. Without sortBy or order, results are ordered by salience, then newest first.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (1-based)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000)", false))
                .WithParameter(OpenApiParameterMetadata.Query("order", "CreatedAscending or CreatedDescending; the direction of sortBy", false))
                .WithParameter(OpenApiParameterMetadata.Query("sortBy", "Created, Updated, Salience, Vessel, Type, Topic, Summary, or Visibility", false))
                .WithParameter(OpenApiParameterMetadata.Query("createdAfter", "Only memories created after this timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("createdBefore", "Only memories created before this timestamp", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Filter by vessel or source vessel ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Filter by source voyage ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Filter by source mission ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Case-insensitive text matched against content, topic, and tags", false))
                .WithParameter(OpenApiParameterMetadata.Query("type", "Memory type (Episodic, Semantic, Procedural)", false))
                .WithParameter(OpenApiParameterMetadata.Query("topic", "Exact topic (case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("topicContains", "Topic substring (case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselName", "Vessel name substring (case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("summaryContains", "Summary (or content) substring (case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("scope", "Visibility (TenantWide or UserSpecific)", false))
                .WithParameter(OpenApiParameterMetadata.Query("minSalience", "Minimum salience (0.0-1.0)", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Memory>>("Paged memories"))
                .WithSecurity("ApiKey"));

            app.Post<MemoryEnumerateRequest>("/api/v1/memories/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                MemoryEnumerateRequest query = JsonSerializer.Deserialize<MemoryEnumerateRequest>(req.Http.Request.DataAsString, _BodyJsonOptions) ?? new MemoryEnumerateRequest();
                MemoryEnumerateOrderProbe? probe = JsonSerializer.Deserialize<MemoryEnumerateOrderProbe>(req.Http.Request.DataAsString, _BodyJsonOptions);
                query.ApplyMemoryQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
                query.ApplyDefaultSort(probe?.Order != null || !String.IsNullOrEmpty(QueryValueReader.Read(req.Query, "order")));
                return await _Memories.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Memories")
                .WithSummary("Enumerate memories")
                .WithDescription("Paginated enumeration of the memories visible to the caller with filtering and sorting. Accepts every EnumerationQuery field plus search, type, topic, topicContains, vesselName, summaryContains, scope, minSalience, and sortBy. Order is the sort direction. Without sortBy or order, results are ordered by salience, then newest first.")
                .WithRequestBody(OpenApiJson.BodyFor<MemoryEnumerateRequest>("Memory enumeration query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Memory>>("Paged memories"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/memories", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Memory request = JsonSerializer.Deserialize<Memory>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Memory.");

                try
                {
                    Memory saved = await _Memories.UpsertAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return saved;
                }
                catch (CollectionLimitExceededException ex)
                {
                    return RouteErrorMapper.LimitExceeded(req, ex);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Memories")
                .WithSummary("Create or upsert a memory")
                .WithDescription("Creates a memory, or updates it in place when a memory with the same key already exists in the caller's tenant.")
                .WithRequestBody(OpenApiJson.BodyFor<Memory>("Memory create/upsert request", true))
                .WithResponse(201, OpenApiJson.For<Memory>("Created or updated memory"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/memories/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Memory? memory = await _Memories.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (memory == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Memory not found" };
                }

                return memory;
            },
            api => api
                .WithTag("Memories")
                .WithSummary("Get a memory")
                .WithDescription("Returns one memory by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Memory ID (mem_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Memory>("Memory"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/memories/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Memory request = JsonSerializer.Deserialize<Memory>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Memory.");
                request.Id = req.Parameters["id"];

                try
                {
                    return await _Memories.UpdateAsync(ctx, request).ConfigureAwait(false);
                }
                catch (KeyNotFoundException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.Forbidden, Message = ex.Message };
                }
                catch (CollectionLimitExceededException ex)
                {
                    return RouteErrorMapper.LimitExceeded(req, ex);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Memories")
                .WithSummary("Update a memory")
                .WithDescription("Updates a memory's fields. Increments the memory's version.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Memory ID (mem_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Memory>("Memory update request", true))
                .WithResponse(200, OpenApiJson.For<Memory>("Updated memory"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/memories/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _Memories.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (KeyNotFoundException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.Forbidden, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Memories")
                .WithSummary("Delete a memory")
                .WithDescription("Deletes one memory within the caller scope (e.g. a stale or superseded memory).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Memory ID (mem_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
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
    }
}
