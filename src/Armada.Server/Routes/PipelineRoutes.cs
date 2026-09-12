namespace Armada.Server.Routes
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Server;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for pipeline management.
    /// </summary>
    public class PipelineRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public PipelineRoutes(
            DatabaseDriver database,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            // List all pipelines
            app.Get("/api/v1/pipelines", async (ApiRequest req) =>
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
                EnumerationResult<Pipeline> result = await _database.Pipelines.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.Scope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("Pipelines")
                .WithSummary("List all pipelines")
                .WithDescription("Returns all pipelines including their stages with optional querystring filtering.")
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Pipeline>>("Paginated pipeline list"))
                .WithSecurity("ApiKey"));

            // Enumerate pipelines
            app.Post<EnumerationQuery>("/api/v1/pipelines/enumerate", async (ApiRequest req) =>
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
                EnumerationResult<Pipeline> result = await _database.Pipelines.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.Scope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("Pipelines")
                .WithSummary("Enumerate pipelines")
                .WithDescription("Paginated enumeration of pipelines with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            // Get pipeline by name
            app.Get("/api/v1/pipelines/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                Pipeline? pipeline = await _database.Pipelines.ReadByNameAsync(name).ConfigureAwait(false);
                if (pipeline == null || !ScopedVisibility.CanView(ctx, pipeline.Scope, pipeline.TenantId, pipeline.UserId)) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Pipeline not found" }; }
                return (object)pipeline;
            },
            api => api
                .WithTag("Pipelines")
                .WithSummary("Get a pipeline by name")
                .WithDescription("Returns a single pipeline by its unique name, including its stages.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Pipeline name (e.g. WorkerOnly, FullPipeline)"))
                .WithResponse(200, OpenApiJson.For<Pipeline>("Pipeline details with stages"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Create pipeline
            app.Post<Pipeline>("/api/v1/pipelines", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                Pipeline pipeline = JsonSerializer.Deserialize<Pipeline>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Pipeline.");
                pipeline.TenantId = ctx.TenantId;
                pipeline.UserId = ctx.UserId;
                pipeline.Scope = ScopedVisibility.ResolveCreateScope(ctx, pipeline.Scope);
                pipeline = await _database.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return pipeline;
            },
            api => api
                .WithTag("Pipelines")
                .WithSummary("Create a pipeline")
                .WithDescription("Creates a new pipeline with stages defining the persona workflow.")
                .WithRequestBody(OpenApiJson.BodyFor<Pipeline>("Pipeline data (Name, Description, Stages array)", true))
                .WithResponse(201, OpenApiJson.For<Pipeline>("Created pipeline"))
                .WithSecurity("ApiKey"));

            // Update pipeline by name
            app.Put<Pipeline>("/api/v1/pipelines/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                Pipeline? existing = await _database.Pipelines.ReadByNameAsync(name).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Pipeline not found" }; }
                if (!ScopedVisibility.CanEdit(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 403; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only modify your own pipelines; a tenant-wide pipeline requires a tenant admin." }; }
                Pipeline body = JsonSerializer.Deserialize<Pipeline>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Pipeline.");
                if (body.Description != null) existing.Description = body.Description;
                if (body.Stages != null && body.Stages.Count > 0) existing.Stages = body.Stages;
                if (ctx.IsAdmin || ctx.IsTenantAdmin) existing.Scope = body.Scope;
                existing.LastUpdateUtc = DateTime.UtcNow;
                Pipeline updated = await _database.Pipelines.UpdateAsync(existing).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Pipelines")
                .WithSummary("Update a pipeline")
                .WithDescription("Updates an existing pipeline by name. Replaces stages if provided.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Pipeline name (e.g. WorkerOnly, FullPipeline)"))
                .WithRequestBody(OpenApiJson.BodyFor<Pipeline>("Updated pipeline data", true))
                .WithResponse(200, OpenApiJson.For<Pipeline>("Updated pipeline"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Delete pipeline by name
            app.Delete("/api/v1/pipelines/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                Pipeline? existing = await _database.Pipelines.ReadByNameAsync(name).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Pipeline not found" }; }
                if (!ScopedVisibility.CanEdit(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 403; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only delete your own pipelines; a tenant-wide pipeline requires a tenant admin." }; }
                if (existing.IsBuiltIn) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Built-in pipelines cannot be deleted" }; }
                await _database.Pipelines.DeleteAsync(existing.Id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Pipelines")
                .WithSummary("Delete a pipeline")
                .WithDescription("Deletes a pipeline by name. Built-in pipelines cannot be deleted.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Pipeline name (e.g. WorkerOnly, FullPipeline)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }
    }
}
