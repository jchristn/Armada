namespace Armada.Server.Routes
{
    using System;
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
        /// <param name="app">SwiftStack application.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            SwiftStackApp app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            // List all prompt templates
            app.Rest.Get("/api/v1/prompt-templates", async (AppRequest req) =>
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
                return result;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("List all prompt templates")
                .WithDescription("Returns all prompt templates with optional querystring filtering.")
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<PromptTemplate>>("Paginated prompt template list"))
                .WithSecurity("ApiKey"));

            // Enumerate prompt templates
            app.Rest.Post<EnumerationQuery>("/api/v1/prompt-templates/enumerate", async (AppRequest req) =>
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
                return result;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Enumerate prompt templates")
                .WithDescription("Paginated enumeration of prompt templates with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            // Get prompt template by name
            app.Rest.Get("/api/v1/prompt-templates/{name}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                PromptTemplate? template = await _database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                if (template == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Prompt template not found" }; }
                return (object)template;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Get a prompt template by name")
                .WithDescription("Returns a single prompt template by its unique name.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Template name (e.g. mission.rules)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<PromptTemplate>("Prompt template details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Update prompt template by name
            app.Rest.Put<PromptTemplate>("/api/v1/prompt-templates/{name}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                PromptTemplate? existing = await _database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Prompt template not found" }; }
                PromptTemplate body = JsonSerializer.Deserialize<PromptTemplate>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as PromptTemplate.");
                if (body.Content != null) existing.Content = body.Content;
                if (body.Description != null) existing.Description = body.Description;
                existing.LastUpdateUtc = DateTime.UtcNow;
                PromptTemplate updated = await _database.PromptTemplates.UpdateAsync(existing).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Update a prompt template")
                .WithDescription("Updates the content and/or description of an existing prompt template by name.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Template name (e.g. mission.rules)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<PromptTemplate>("Updated template data (Content and Description fields)", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<PromptTemplate>("Updated prompt template"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Reset prompt template to default
            app.Rest.Post("/api/v1/prompt-templates/{name}/reset", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string name = req.Parameters["name"];
                PromptTemplate? result = await _promptTemplateService.ResetToDefaultAsync(name).ConfigureAwait(false);
                if (result == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No embedded default exists for template '" + name + "'" }; }
                return (object)result;
            },
            api => api
                .WithTag("Prompt Templates")
                .WithSummary("Reset a prompt template to default")
                .WithDescription("Resets a prompt template to its embedded resource default content.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Template name (e.g. mission.rules)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<PromptTemplate>("Reset prompt template"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }
    }
}
