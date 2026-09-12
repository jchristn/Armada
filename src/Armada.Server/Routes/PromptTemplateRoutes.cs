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
    /// REST API routes for prompt template management.
    /// </summary>
    public class PromptTemplateRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly IPromptTemplateService _promptTemplateService;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="promptTemplateService">Prompt template service.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public PromptTemplateRoutes(
            DatabaseDriver database,
            IPromptTemplateService promptTemplateService,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _promptTemplateService = promptTemplateService ?? throw new ArgumentNullException(nameof(promptTemplateService));
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
            // List all prompt templates
            app.Get("/api/v1/prompt-templates", async (ApiRequest req) =>
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
                EnumerationResult<PromptTemplate> result = await _database.PromptTemplates.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.Scope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("List all prompt templates")
                .WithDescription("Returns all prompt templates with optional querystring filtering.")
                .WithResponse(200, OpenApiJson.For<EnumerationResult<PromptTemplate>>("Paginated prompt template list"))
                .WithSecurity("ApiKey"));

            // Enumerate prompt templates
            app.Post<EnumerationQuery>("/api/v1/prompt-templates/enumerate", async (ApiRequest req) =>
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
                EnumerationResult<PromptTemplate> result = await _database.PromptTemplates.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                if (!ctx.IsAdmin) result.Objects = result.Objects.Where(p => ScopedVisibility.CanView(ctx, p.Scope, p.TenantId, p.UserId)).ToList();
                return result;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Enumerate prompt templates")
                .WithDescription("Paginated enumeration of prompt templates with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            // Create prompt template
            app.Post<PromptTemplate>("/api/v1/prompt-templates", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                PromptTemplate template = JsonSerializer.Deserialize<PromptTemplate>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as PromptTemplate.");

                string normalizedName = template.Name.Trim();
                string normalizedCategory = template.Category.Trim();
                if (String.IsNullOrWhiteSpace(normalizedName))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Prompt template name is required" };
                }
                if (String.IsNullOrWhiteSpace(normalizedCategory))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Prompt template category is required" };
                }
                if (String.IsNullOrWhiteSpace(template.Content))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Prompt template content is required" };
                }

                template.Name = normalizedName;
                template.Category = normalizedCategory;
                template.Description = String.IsNullOrWhiteSpace(template.Description) ? null : template.Description.Trim();
                template.IsBuiltIn = false;
                template.TenantId = ctx.TenantId;
                template.UserId = ctx.UserId;
                template.Scope = ScopedVisibility.ResolveCreateScope(ctx, template.Scope);
                template.CreatedUtc = DateTime.UtcNow;
                template.LastUpdateUtc = DateTime.UtcNow;

                PromptTemplate? existing = await _database.PromptTemplates.ReadByNameAsync(template.Name).ConfigureAwait(false);
                if (existing != null)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Prompt template already exists" };
                }

                PromptTemplate created = await _database.PromptTemplates.CreateAsync(template).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return created;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Create a prompt template")
                .WithDescription("Creates a new prompt template with a unique name, category, and prompt content.")
                .WithRequestBody(OpenApiJson.BodyFor<PromptTemplate>("Prompt template data (Name, Category, Description, Content)", true))
                .WithResponse(201, OpenApiJson.For<PromptTemplate>("Created prompt template"))
                .WithSecurity("ApiKey"));

            // Get prompt template by name
            app.Get("/api/v1/prompt-templates/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                PromptTemplate? template = await _database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                if (template == null || !ScopedVisibility.CanView(ctx, template.Scope, template.TenantId, template.UserId)) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Prompt template not found" }; }
                return (object)template;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Get a prompt template by name")
                .WithDescription("Returns a single prompt template by its unique name.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Template name (e.g. mission.rules)"))
                .WithResponse(200, OpenApiJson.For<PromptTemplate>("Prompt template details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Update prompt template by name
            app.Put<PromptTemplate>("/api/v1/prompt-templates/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                PromptTemplate? existing = await _database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                if (existing == null || !ScopedVisibility.CanView(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Prompt template not found" }; }
                if (!ScopedVisibility.CanEdit(ctx, existing.Scope, existing.TenantId, existing.UserId)) { req.Http.Response.StatusCode = 403; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only modify your own prompt templates; a tenant-wide template requires a tenant admin." }; }
                PromptTemplate body = JsonSerializer.Deserialize<PromptTemplate>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as PromptTemplate.");
                if (body.Content != null) existing.Content = body.Content;
                if (body.Description != null) existing.Description = body.Description;
                if (ctx.IsAdmin || ctx.IsTenantAdmin) existing.Scope = body.Scope;
                existing.LastUpdateUtc = DateTime.UtcNow;
                PromptTemplate updated = await _database.PromptTemplates.UpdateAsync(existing).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Update a prompt template")
                .WithDescription("Updates the content and/or description of an existing prompt template by name.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Template name (e.g. mission.rules)"))
                .WithRequestBody(OpenApiJson.BodyFor<PromptTemplate>("Updated template data (Content and Description fields)", true))
                .WithResponse(200, OpenApiJson.For<PromptTemplate>("Updated prompt template"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Reset prompt template to default
            app.Post("/api/v1/prompt-templates/{name}/reset", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                PromptTemplate? current = await _database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                if (current != null && !ScopedVisibility.CanEdit(ctx, current.Scope, current.TenantId, current.UserId)) { req.Http.Response.StatusCode = 403; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only reset your own prompt templates; a tenant-wide template requires a tenant admin." }; }
                PromptTemplate? result = await _promptTemplateService.ResetToDefaultAsync(name).ConfigureAwait(false);
                if (result == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No embedded default exists for template '" + name + "'" }; }
                return (object)result;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Reset a prompt template to default")
                .WithDescription("Resets a prompt template to its embedded resource default content.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Template name (e.g. mission.rules)"))
                .WithResponse(200, OpenApiJson.For<PromptTemplate>("Reset prompt template"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }
    }
}
