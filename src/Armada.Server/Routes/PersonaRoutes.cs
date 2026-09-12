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
    /// REST API routes for persona management.
    /// </summary>
    public class PersonaRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly JsonSerializerOptions _jsonOptions;

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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Persona> result = await _database.Personas.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.Scope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("List all personas")
                .WithDescription("Returns all personas with optional querystring filtering.")
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Persona>>("Paginated persona list"))
                .WithSecurity("ApiKey"));

            // Enumerate personas
            app.Post<EnumerationQuery>("/api/v1/personas/enumerate", async (ApiRequest req) =>
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
                EnumerationResult<Persona> result = await _database.Personas.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.Scope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("Enumerate personas")
                .WithDescription("Paginated enumeration of personas with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            // Get persona by name
            app.Get("/api/v1/personas/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
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
            app.Post<Persona>("/api/v1/personas", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                Persona persona = JsonSerializer.Deserialize<Persona>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Persona.");
                if (!String.IsNullOrEmpty(persona.DefaultCaptainId) && await _database.Captains.ReadAsync(persona.DefaultCaptainId).ConfigureAwait(false) == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Default captain '" + persona.DefaultCaptainId + "' not found" };
                }
                persona.TenantId = ctx.TenantId;
                persona.UserId = ctx.UserId;
                persona.Scope = ScopedVisibility.ResolveCreateScope(ctx, persona.Scope);
                persona = await _database.Personas.CreateAsync(persona).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return persona;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("Create a persona")
                .WithDescription("Creates a new persona with a name, description, and prompt template reference.")
                .WithRequestBody(OpenApiJson.BodyFor<Persona>("Persona data (Name, Description, PromptTemplateName)", true))
                .WithResponse(201, OpenApiJson.For<Persona>("Created persona"))
                .WithSecurity("ApiKey"));

            // Update persona by name
            app.Put<Persona>("/api/v1/personas/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                Persona? existing = await _database.Personas.ReadByNameAsync(name).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Persona not found" }; }
                if (!ScopedVisibility.CanEdit(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 403; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only modify your own personas; a tenant-wide persona requires a tenant admin." }; }
                Persona body = JsonSerializer.Deserialize<Persona>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Persona.");
                if (!String.IsNullOrEmpty(body.DefaultCaptainId) && await _database.Captains.ReadAsync(body.DefaultCaptainId).ConfigureAwait(false) == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Default captain '" + body.DefaultCaptainId + "' not found" };
                }
                if (body.Description != null) existing.Description = body.Description;
                if (body.PromptTemplateName != null) existing.PromptTemplateName = body.PromptTemplateName;
                // Default captain is set to whatever the body carries (including null to clear the default),
                // so the persona-detail editor can both assign and remove a default captain.
                existing.DefaultCaptainId = body.DefaultCaptainId;
                if (ctx.IsAdmin || ctx.IsTenantAdmin) existing.Scope = body.Scope;
                existing.LastUpdateUtc = DateTime.UtcNow;
                Persona updated = await _database.Personas.UpdateAsync(existing).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Personas")
                .WithSummary("Update a persona")
                .WithDescription("Updates an existing persona by name. Only non-null fields are updated.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Persona name (e.g. Worker, Architect)"))
                .WithRequestBody(OpenApiJson.BodyFor<Persona>("Updated persona data", true))
                .WithResponse(200, OpenApiJson.For<Persona>("Updated persona"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Delete persona by name
            app.Delete("/api/v1/personas/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                Persona? existing = await _database.Personas.ReadByNameAsync(name).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Persona not found" }; }
                if (!ScopedVisibility.CanEdit(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 403; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only delete your own personas; a tenant-wide persona requires a tenant admin." }; }
                if (existing.IsBuiltIn) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Built-in personas cannot be deleted" }; }
                await _database.Personas.DeleteAsync(existing.Id).ConfigureAwait(false);
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
    }
}
