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
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for persona management.
    /// </summary>
    public class PersonaRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly PersonaWriteService _writes;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public PersonaRoutes(
            DatabaseDriver database,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _writes = new PersonaWriteService(database);
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
            // List all personas
            app.Get("/api/v1/personas", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Persona> result = await _database.Personas.EnumeratePageAsync(query, ScopedEnumerationFilter.ForScopedEntity(ctx)).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("List personas")
                .WithDescription("One page of the personas visible to the caller (admins see all; tenant admins their tenant; others their tenant's tenant-wide personas plus their own), counted and cut in the database, newest first with the id as the tie-break. Querystring: pageNumber, pageSize (1 to 1000, default 10), order, createdAfter, createdBefore.")
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Persona>>("Paginated persona list"))
                .WithSecurity("ApiKey"));

            // Enumerate personas
            app.Post<EnumerationQuery>("/api/v1/personas/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = JsonSerializer.Deserialize<EnumerationQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Persona> result = await _database.Personas.EnumeratePageAsync(query, ScopedEnumerationFilter.ForScopedEntity(ctx)).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("Enumerate personas")
                .WithDescription("One page of the personas visible to the caller, counted and cut in the database (the POST twin of GET /api/v1/personas; the query comes from the JSON body with querystring values applied on top).")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Persona>>("One page"))
                .WithSecurity("ApiKey"));

            // Get persona by name
            app.Get("/api/v1/personas/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = RouteParameters.Get(req, "name");
                Persona? persona = await _database.Personas.ReadByNameAsync(name).ConfigureAwait(false);
                if (persona == null || !ScopedVisibility.CanView(ctx, persona.Scope, persona.TenantId, persona.UserId)) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Persona not found" }; }
                return (object)persona;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("Get a persona by name")
                .WithDescription("Returns a single persona by its unique name.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Persona name (e.g. Worker, Architect)"))
                .WithResponse(200, OpenApiJson.For<Persona>("Persona details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Create persona
            // Untyped: the handler reads the body itself, so a Persona setter refusing an empty Name or PromptTemplateName
            // answers 400 rather than failing the request before the handler runs.
            app.Post("/api/v1/personas", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                Persona persona;
                try
                {
                    persona = JsonSerializer.Deserialize<Persona>(req.Http.Request.DataAsString, _jsonOptions)
                        ?? throw new InvalidOperationException("Request body could not be deserialized as Persona.");
                }
                catch (Exception ex) when (ex is ArgumentException || ex is JsonException)
                {
                    // Persona's setters reject an empty Name or PromptTemplateName while the body is read.
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid persona: " + ex.Message };
                }

                PersonaWriteResult created = await _writes.CreateAsync(ctx, persona).ConfigureAwait(false);
                if (created.Status != PersonaWriteStatusEnum.Ok) return Failure(req, created);
                req.Http.Response.StatusCode = 201;
                return created.Persona;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("Create a persona")
                .WithDescription("Creates a new persona with a name, description, and prompt template reference.")
                .WithRequestBody(OpenApiJson.BodyFor<Persona>("Persona data including optional ordered CaptainPriorities and availability/exhaustion policies", true))
                .WithResponse(201, OpenApiJson.For<Persona>("Created persona"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("A persona with that name already exists (Data: DuplicateEntityErrorDetail)"))
                .WithSecurity("ApiKey"));

            // Update persona by name
            app.Put<PersonaUpdateRequest>("/api/v1/personas/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = RouteParameters.Get(req, "name");
                PersonaUpdateRequest body = JsonSerializer.Deserialize<PersonaUpdateRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Persona.");
                // Default captain changes only when the body carries the field: a value assigns it and an explicit
                // null (or empty string) clears it, so the persona-detail editor can do both, while an editor that
                // does not show the default captain (the Personas list) leaves it unchanged.
                bool hasDefaultCaptain = PersonaUpdateFieldPresence.FromJson(req.Http.Request.DataAsString, _jsonOptions).HasDefaultCaptainId;
                PersonaWriteResult updated = await _writes.UpdateAsync(ctx, name, body, hasDefaultCaptain).ConfigureAwait(false);
                if (updated.Status != PersonaWriteStatusEnum.Ok) return Failure(req, updated);
                return (object)updated.Persona!;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("Update a persona")
                .WithDescription("Updates a persona by name. Omitted fields (including DefaultCaptainId) are preserved; an explicit null DefaultCaptainId clears the default captain, and an empty CaptainPriorities list clears the hierarchy.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Persona name (e.g. Worker, Architect)"))
                .WithRequestBody(OpenApiJson.BodyFor<PersonaUpdateRequest>("Updated persona data", true))
                .WithResponse(200, OpenApiJson.For<Persona>("Updated persona"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            // Delete persona by name
            app.Delete("/api/v1/personas/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = RouteParameters.Get(req, "name");
                PersonaWriteResult deleted = await _writes.DeleteAsync(ctx, name).ConfigureAwait(false);
                if (deleted.Status != PersonaWriteStatusEnum.Ok) return Failure(req, deleted);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("Delete a persona")
                .WithDescription("Deletes a persona by name. Built-in personas cannot be deleted.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Persona name (e.g. Worker, Architect)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private static ApiErrorResponse Failure(ApiRequest req, PersonaWriteResult result)
        {
            switch (result.Status)
            {
                case PersonaWriteStatusEnum.NotFound:
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = result.Message };
                case PersonaWriteStatusEnum.Forbidden:
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.Forbidden, Message = result.Message };
                default:
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = result.Message };
            }
        }
    }
}
