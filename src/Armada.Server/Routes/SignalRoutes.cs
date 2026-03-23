namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.Text.Json;
    using SwiftStack;
    using SwiftStack.Rest;
    using SwiftStack.Rest.OpenApi;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for signal management.
    /// </summary>
    public class SignalRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public SignalRoutes(
            DatabaseDriver database,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            JsonSerializerOptions jsonOptions)
        {
            _database = database;
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
            // Signals
            app.Rest.Get("/api/v1/signals", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Signal> result = ctx.IsAdmin
                    ? await _database.Signals.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Signals.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Signals.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("List recent signals")
                .WithDescription("Returns the 50 most recent signals (inter-agent messages).")
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Signal>>("Paginated signal list"))
                .WithSecurity("ApiKey"));

            app.Rest.Post<EnumerationQuery>("/api/v1/signals/enumerate", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                EnumerationQuery query = JsonSerializer.Deserialize<EnumerationQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Signal> result = ctx.IsAdmin
                    ? await _database.Signals.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Signals.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Signals.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Enumerate signals")
                .WithDescription("Paginated enumeration of signals with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Rest.Post<Signal>("/api/v1/signals", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                Signal signal = JsonSerializer.Deserialize<Signal>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Signal.");
                signal.TenantId = ctx.TenantId;
                signal.UserId = ctx.UserId;
                signal = await _database.Signals.CreateAsync(signal).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return signal;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Send a signal")
                .WithDescription("Creates a new signal (message between admiral and captains).")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Signal>("Signal data", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Signal>("Created signal"))
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/signals/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Signal? signal = ctx.IsAdmin
                    ? await _database.Signals.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Signals.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Signals.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (signal == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Signal not found" }; }
                return (object)signal;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Get a signal")
                .WithDescription("Returns a single signal by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Signal ID (sig_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Signal>("Signal details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Put("/api/v1/signals/{id}/read", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Signal? signal;
                if (ctx.IsAdmin)
                {
                    signal = await _database.Signals.ReadAsync(id).ConfigureAwait(false);
                    if (signal == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Signal not found" }; }
                    await _database.Signals.MarkReadAsync(id).ConfigureAwait(false);
                    Signal? updated = await _database.Signals.ReadAsync(id).ConfigureAwait(false);
                    return (object)updated!;
                }
                else if (ctx.IsTenantAdmin)
                {
                    signal = await _database.Signals.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    if (signal == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Signal not found" }; }
                    await _database.Signals.MarkReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    Signal? updated = await _database.Signals.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    return (object)updated!;
                }
                else
                {
                    signal = await _database.Signals.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    if (signal == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Signal not found" }; }
                    await _database.Signals.MarkReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    Signal? updated = await _database.Signals.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    return (object)updated!;
                }
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Mark a signal as read")
                .WithDescription("Marks a signal as read by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Signal ID (sig_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Signal>("Updated signal"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/signals/recipient/{captainId}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string captainId = req.Parameters["captainId"];
                string unreadOnlyStr = req.Query.GetValueOrDefault("unreadOnly");
                bool unreadOnly = String.IsNullOrEmpty(unreadOnlyStr) || bool.Parse(unreadOnlyStr);
                List<Signal> signals = ctx.IsAdmin
                    ? await _database.Signals.EnumerateByRecipientAsync(captainId, unreadOnly).ConfigureAwait(false)
                    : await _database.Signals.EnumerateByRecipientAsync(ctx.TenantId!, captainId, unreadOnly).ConfigureAwait(false);
                return signals;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Enumerate signals by recipient")
                .WithDescription("Returns signals addressed to a specific captain. Defaults to unread only.")
                .WithParameter(OpenApiParameterMetadata.Path("captainId", "Captain ID (cpt_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("unreadOnly", "Filter to unread signals only (default: true)", false, OpenApiSchemaMetadata.Boolean()))
                .WithResponse(200, OpenApiResponseMetadata.Json<List<Signal>>("Signal list"))
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/signals/recent", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string countStr = req.Query.GetValueOrDefault("count");
                int count = 50;
                if (!String.IsNullOrEmpty(countStr) && int.TryParse(countStr, out int parsedCount)) count = parsedCount;
                List<Signal> signals = ctx.IsAdmin
                    ? await _database.Signals.EnumerateRecentAsync(count).ConfigureAwait(false)
                    : await _database.Signals.EnumerateRecentAsync(ctx.TenantId!, count).ConfigureAwait(false);
                return signals;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Get recent signals")
                .WithDescription("Returns the most recent signals, ordered by creation time descending.")
                .WithParameter(OpenApiParameterMetadata.Query("count", "Maximum number of signals to return (default: 50)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiResponseMetadata.Json<List<Signal>>("Signal list"))
                .WithSecurity("ApiKey"));

            app.Rest.Delete("/api/v1/signals/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Signal? signal = ctx.IsAdmin
                    ? await _database.Signals.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Signals.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Signals.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (signal == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Signal not found" }; }
                if (ctx.IsAdmin)
                    await _database.Signals.DeleteAsync(id).ConfigureAwait(false);
                else if (ctx.IsTenantAdmin)
                    await _database.Signals.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                else
                    await _database.Signals.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Delete a signal")
                .WithDescription("Permanently deletes a signal by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Signal ID (sig_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Post<DeleteMultipleRequest>("/api/v1/signals/delete/multiple", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                DeleteMultipleRequest? body = JsonSerializer.Deserialize<DeleteMultipleRequest>(req.Http.Request.DataAsString, _jsonOptions);
                if (body == null || body.Ids == null || body.Ids.Count == 0)
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Ids is required and must not be empty" };

                DeleteMultipleResult result = new DeleteMultipleResult();
                foreach (string id in body.Ids)
                {
                    if (String.IsNullOrEmpty(id))
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                        continue;
                    }
                    Signal? signal = ctx.IsAdmin
                        ? await _database.Signals.ReadAsync(id).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Signals.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                            : await _database.Signals.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    if (signal == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    if (ctx.IsAdmin)
                        await _database.Signals.DeleteAsync(id).ConfigureAwait(false);
                    else if (ctx.IsTenantAdmin)
                        await _database.Signals.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    else
                        await _database.Signals.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await _emitEvent("signal.batch_deleted", "Batch deleted " + result.Deleted + " signals",
                    "signal", null, null, null, null, null).ConfigureAwait(false);

                result.ResolveStatus();
                return (object)result;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Batch delete multiple signals")
                .WithDescription("Permanently deletes multiple signals by ID. Returns a summary of deleted and skipped entries.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of signal IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));
        }
    }
}
