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
    /// REST API routes for fleet management.
    /// </summary>
    public class FleetRoutes
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
        public FleetRoutes(
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
            // Fleets
            app.Rest.Get("/api/v1/fleets", async (AppRequest req) =>
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
                EnumerationResult<Fleet> result = ctx.IsAdmin
                    ? await _database.Fleets.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Fleets.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Fleets.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("List all fleets")
                .WithDescription("Returns all registered fleets (repository collections).")
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Fleet>>("Paginated fleet list"))
                .WithSecurity("ApiKey"));

            app.Rest.Post<EnumerationQuery>("/api/v1/fleets/enumerate", async (AppRequest req) =>
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
                EnumerationResult<Fleet> result = ctx.IsAdmin
                    ? await _database.Fleets.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Fleets.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Fleets.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Enumerate fleets")
                .WithDescription("Paginated enumeration of fleets with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Rest.Post<Fleet>("/api/v1/fleets", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                Fleet fleet = JsonSerializer.Deserialize<Fleet>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Fleet.");
                fleet.TenantId = ctx.TenantId;
                fleet.UserId = ctx.UserId;
                fleet = await _database.Fleets.CreateAsync(fleet).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return fleet;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Create a fleet")
                .WithDescription("Creates a new fleet and returns it with an assigned ID.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Fleet>("Fleet data", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Fleet>("Created fleet"))
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/fleets/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Fleet? fleet = ctx.IsAdmin
                    ? await _database.Fleets.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Fleets.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Fleets.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (fleet == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Fleet not found" }; }
                List<Vessel> vessels = ctx.IsAdmin
                    ? await _database.Vessels.EnumerateByFleetAsync(id).ConfigureAwait(false)
                    : await _database.Vessels.EnumerateByFleetAsync(ctx.TenantId!, id).ConfigureAwait(false);
                return (object)new { Fleet = fleet, Vessels = vessels };
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Get a fleet")
                .WithDescription("Returns a single fleet by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet ID (flt_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Fleet>("Fleet details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Put<Fleet>("/api/v1/fleets/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Fleet? existing = ctx.IsAdmin
                    ? await _database.Fleets.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Fleets.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Fleets.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Fleet not found" }; }
                Fleet updated = JsonSerializer.Deserialize<Fleet>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Fleet.");
                updated.Id = id;
                updated = await _database.Fleets.UpdateAsync(updated).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Update a fleet")
                .WithDescription("Updates an existing fleet by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet ID (flt_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Fleet>("Updated fleet data", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Fleet>("Updated fleet"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Delete("/api/v1/fleets/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                if (ctx.IsAdmin)
                    await _database.Fleets.DeleteAsync(id).ConfigureAwait(false);
                else if (ctx.IsTenantAdmin)
                    await _database.Fleets.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                else
                    await _database.Fleets.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Delete a fleet")
                .WithDescription("Deletes a fleet by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet ID (flt_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithSecurity("ApiKey"));

            app.Rest.Post<DeleteMultipleRequest>("/api/v1/fleets/delete/multiple", async (AppRequest req) =>
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
                    Fleet? existing = ctx.IsAdmin
                        ? await _database.Fleets.ReadAsync(id).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Fleets.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                            : await _database.Fleets.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    if (existing == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    if (ctx.IsAdmin)
                        await _database.Fleets.DeleteAsync(id).ConfigureAwait(false);
                    else if (ctx.IsTenantAdmin)
                        await _database.Fleets.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    else
                        await _database.Fleets.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await _emitEvent("fleet.batch_deleted", "Batch deleted " + result.Deleted + " fleets",
                    "fleet", null, null, null, null, null).ConfigureAwait(false);

                result.ResolveStatus();
                return (object)result;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Batch delete multiple fleets")
                .WithDescription("Permanently deletes multiple fleets from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of fleet IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));
        }
    }
}
