namespace Armada.Server.Routes
{
    using System;
    using System.Diagnostics;
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
    using SyslogLogging;

    /// <summary>
    /// REST API routes for playbook management.
    /// </summary>
    public class PlaybookRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly LoggingModule _logging;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlaybookRoutes(DatabaseDriver database, LoggingModule logging, JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        public void Register(
            Webserver app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/playbooks", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }

                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Playbook> result = ctx.IsAdmin
                    ? await _database.Playbooks.EnumerateAsync(query).ConfigureAwait(false)
                    : await _database.Playbooks.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Playbooks")
                .WithSummary("List playbooks")
                .WithDescription("Returns tenant-scoped playbooks with optional filtering and pagination.")
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Playbook>>("Paginated playbook list"))
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/playbooks/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }

                EnumerationQuery query = JsonSerializer.Deserialize<EnumerationQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Playbook> result = ctx.IsAdmin
                    ? await _database.Playbooks.EnumerateAsync(query).ConfigureAwait(false)
                    : await _database.Playbooks.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Playbooks")
                .WithSummary("Enumerate playbooks")
                .WithDescription("Paginated enumeration of tenant-scoped playbooks.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Post<Playbook>("/api/v1/playbooks", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }

                Playbook playbook = JsonSerializer.Deserialize<Playbook>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Playbook.");
                playbook.TenantId = ctx.TenantId;
                playbook.UserId = ctx.UserId;

                PlaybookService playbookService = new PlaybookService(_database, _logging);
                playbookService.Validate(playbook);

                bool fileNameExists = await _database.Playbooks.ExistsByFileNameAsync(ctx.TenantId!, playbook.FileName).ConfigureAwait(false);
                if (fileNameExists)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = "A playbook with that file name already exists." };
                }

                playbook = await _database.Playbooks.CreateAsync(playbook).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return playbook;
            },
            api => api
                .WithTag("Playbooks")
                .WithSummary("Create a playbook")
                .WithDescription("Creates a tenant-scoped markdown playbook.")
                .WithRequestBody(OpenApiJson.BodyFor<Playbook>("Playbook data", true))
                .WithResponse(201, OpenApiJson.For<Playbook>("Created playbook"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/playbooks/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }

                string id = req.Parameters["id"];
                Playbook? playbook = ctx.IsAdmin
                    ? await _database.Playbooks.ReadAsync(id).ConfigureAwait(false)
                    : await _database.Playbooks.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                if (playbook == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Playbook not found" };
                }

                return playbook;
            },
            api => api
                .WithTag("Playbooks")
                .WithSummary("Get a playbook")
                .WithDescription("Returns a single playbook by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Playbook ID (pbk_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Playbook>("Playbook details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put<Playbook>("/api/v1/playbooks/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }

                string id = req.Parameters["id"];
                Playbook? existing = ctx.IsAdmin
                    ? await _database.Playbooks.ReadAsync(id).ConfigureAwait(false)
                    : await _database.Playbooks.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                if (existing == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Playbook not found" };
                }

                Playbook incoming = JsonSerializer.Deserialize<Playbook>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Playbook.");

                existing.FileName = incoming.FileName;
                existing.Description = incoming.Description;
                existing.Content = incoming.Content;
                existing.Active = incoming.Active;
                existing.LastUpdateUtc = DateTime.UtcNow;

                PlaybookService playbookService = new PlaybookService(_database, _logging);
                playbookService.Validate(existing);

                Playbook? duplicate = await _database.Playbooks.ReadByFileNameAsync(existing.TenantId!, existing.FileName).ConfigureAwait(false);
                if (duplicate != null && !String.Equals(duplicate.Id, existing.Id, StringComparison.Ordinal))
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = "A playbook with that file name already exists." };
                }

                existing = await _database.Playbooks.UpdateAsync(existing).ConfigureAwait(false);
                return existing;
            },
            api => api
                .WithTag("Playbooks")
                .WithSummary("Update a playbook")
                .WithDescription("Updates a tenant-scoped markdown playbook.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Playbook ID (pbk_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Playbook>("Updated playbook data", true))
                .WithResponse(200, OpenApiJson.For<Playbook>("Updated playbook"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/playbooks/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }

                string id = req.Parameters["id"];
                Playbook? existing = ctx.IsAdmin
                    ? await _database.Playbooks.ReadAsync(id).ConfigureAwait(false)
                    : await _database.Playbooks.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                if (existing == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Playbook not found" };
                }

                await _database.Playbooks.DeleteAsync(id).ConfigureAwait(false);
                return new { Status = "deleted", PlaybookId = id };
            },
            api => api
                .WithTag("Playbooks")
                .WithSummary("Delete a playbook")
                .WithDescription("Deletes a playbook. Existing mission snapshots remain immutable.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Playbook ID (pbk_ prefix)"))
                .WithResponse(200, OpenApiJson.For<object>("Deletion result"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }
    }
}
