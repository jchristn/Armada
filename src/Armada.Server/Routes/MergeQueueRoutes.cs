namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.Text.Json;
    using SwiftStack;
    using SwiftStack.Rest;
    using SwiftStack.Rest.OpenApi;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for merge queue management.
    /// </summary>
    public class MergeQueueRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly IMergeQueueService _mergeQueue;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="mergeQueue">Merge queue service.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public MergeQueueRoutes(
            DatabaseDriver database,
            IMergeQueueService mergeQueue,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            JsonSerializerOptions jsonOptions)
        {
            _database = database;
            _mergeQueue = mergeQueue;
            _emitEvent = emitEvent;
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">SwiftStack application.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            SwiftStackApp app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            // Merge Queue
            app.Rest.Get("/api/v1/merge-queue", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                List<MergeEntry> all = await _mergeQueue.ListAsync().ConfigureAwait(false);
                if (!String.IsNullOrEmpty(query.Status))
                    all = all.Where(e => String.Equals(e.Status.ToString(), query.Status, StringComparison.OrdinalIgnoreCase)).ToList();
                int totalCount = all.Count;
                List<MergeEntry> page = all.Skip(query.Offset).Take(query.PageSize).ToList();
                EnumerationResult<MergeEntry> result = EnumerationResult<MergeEntry>.Create(query, page, totalCount);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("List merge queue entries")
                .WithDescription("Returns all entries in the merge queue, with optional status filter via query parameter.")
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<MergeEntry>>("Paginated merge queue list"))
                .WithSecurity("ApiKey"));

            app.Rest.Post<EnumerationQuery>("/api/v1/merge-queue/enumerate", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = JsonSerializer.Deserialize<EnumerationQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                List<MergeEntry> all = await _mergeQueue.ListAsync().ConfigureAwait(false);
                if (!String.IsNullOrEmpty(query.Status))
                    all = all.Where(e => String.Equals(e.Status.ToString(), query.Status, StringComparison.OrdinalIgnoreCase)).ToList();
                int totalCount = all.Count;
                List<MergeEntry> page = all.Skip(query.Offset).Take(query.PageSize).ToList();
                EnumerationResult<MergeEntry> result = EnumerationResult<MergeEntry>.Create(query, page, totalCount);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Enumerate merge queue entries")
                .WithDescription("Paginated enumeration of merge queue entries with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Rest.Post<MergeEntry>("/api/v1/merge-queue", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                MergeEntry entry = JsonSerializer.Deserialize<MergeEntry>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as MergeEntry.");
                entry = await _mergeQueue.EnqueueAsync(entry).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return entry;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Enqueue a branch for merge")
                .WithDescription("Adds a branch to the merge queue for testing and merging into the target branch.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<MergeEntry>("Merge entry", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<MergeEntry>("Enqueued entry"))
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/merge-queue/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                MergeEntry? entry = await _mergeQueue.GetAsync(id).ConfigureAwait(false);
                if (entry == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Merge entry not found" };
                return (object)entry;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Get a merge queue entry")
                .WithDescription("Returns a single merge queue entry by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Merge entry ID (mrg_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<MergeEntry>("Merge entry details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Delete("/api/v1/merge-queue/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                bool deleted = await _mergeQueue.DeleteAsync(id).ConfigureAwait(false);
                if (!deleted)
                {
                    // Fall back to cancel if not in a terminal state
                    await _mergeQueue.CancelAsync(id).ConfigureAwait(false);
                }
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Delete or cancel a merge queue entry")
                .WithDescription("Permanently deletes a terminal merge entry (Cancelled, Landed, Failed) or cancels an active one.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Merge entry ID (mrg_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithSecurity("ApiKey"));

            app.Rest.Post("/api/v1/merge-queue/{id}/process", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                MergeEntry? entry = await _mergeQueue.ProcessSingleAsync(id).ConfigureAwait(false);
                if (entry == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Merge entry not found or not in Queued status" };
                return (object)entry;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Process a single merge queue entry")
                .WithDescription("Processes a single merge queue entry by ID: creates integration branch, runs tests, and lands if passing.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Merge entry ID (mrg_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<MergeEntry>("Processed merge entry"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Post("/api/v1/merge-queue/process", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                await _mergeQueue.ProcessQueueAsync().ConfigureAwait(false);
                return new { Status = "processed" };
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Process the merge queue")
                .WithDescription("Triggers processing of all queued entries: creates integration branches, runs tests, and lands passing batches.")
                .WithSecurity("ApiKey"));

            app.Rest.Delete("/api/v1/merge-queue/{id}/purge", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                MergeEntry? entry = await _mergeQueue.GetAsync(id).ConfigureAwait(false);
                if (entry == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Merge entry not found" };

                bool deleted = await _mergeQueue.DeleteAsync(id).ConfigureAwait(false);
                if (!deleted)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot purge merge entry in non-terminal status " + entry.Status + ". Only Landed, Failed, or Cancelled entries can be purged." };
                }

                await _emitEvent("merge.purged", "Merge entry " + id + " purged",
                    "merge_entry", id, null, null, null, null).ConfigureAwait(false);

                return (object)new { Status = "purged", EntryId = id };
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Purge a single merge queue entry")
                .WithDescription("Permanently deletes a terminal merge queue entry from the database. Only entries in Landed, Failed, or Cancelled status can be purged. This cannot be undone.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Merge entry ID (mrg_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<object>("Purged merge entry"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiResponseMetadata.Json<object>("Entry is not in a terminal state"))
                .WithSecurity("ApiKey"));

            app.Rest.Post<PurgeMergeEntriesRequest>("/api/v1/merge-queue/purge", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                PurgeMergeEntriesRequest? body = JsonSerializer.Deserialize<PurgeMergeEntriesRequest>(req.Http.Request.DataAsString, _jsonOptions);
                if (body == null || body.EntryIds == null || body.EntryIds.Count == 0)
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "EntryIds is required and must not be empty" };

                MergeQueuePurgeResult result = await _mergeQueue.DeleteMultipleAsync(body.EntryIds).ConfigureAwait(false);

                await _emitEvent("merge.batch_purged", "Batch purged " + result.EntriesPurged + " merge entries",
                    "merge_entry", null, null, null, null, null).ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Batch purge merge queue entries")
                .WithDescription("Permanently deletes multiple terminal merge queue entries from the database by ID. Returns a summary of purged and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<PurgeMergeEntriesRequest>("List of merge entry IDs to purge"))
                .WithResponse(200, OpenApiResponseMetadata.Json<MergeQueuePurgeResult>("Purge result summary"))
                .WithSecurity("ApiKey"));
        }
    }
}
