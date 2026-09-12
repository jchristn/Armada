namespace Armada.Server.Routes
{
    using System;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for the skills directory.
    /// </summary>
    public class SkillRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly JsonSerializerOptions _jsonOptions;
        private static readonly JsonSerializerOptions _bodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SkillRoutes(DatabaseDriver database, JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/skills", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                SkillQuery query = new SkillQuery();
                ApplyQuerystringOverrides(req, query);
                ApplyReadScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Skill> result = await _database.Skills.EnumerateAsync(query).ConfigureAwait(false);
                if (!ctx.IsAdmin && !ctx.IsTenantAdmin)
                    result.Objects = result.Objects.Where(s => ScopedVisibility.CanView(ctx, s.Scope, s.TenantId, s.UserId)).ToList();
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Skills")
                .WithSummary("List skills")
                .WithDescription("Returns paginated skills scoped to the authenticated tenant.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("category", "Optional category filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional name/description search", false))
                .WithParameter(OpenApiParameterMetadata.Query("active", "Optional active-state filter", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Skill>>("Paginated skills"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/skills/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                SkillQuery query = JsonSerializer.Deserialize<SkillQuery>(req.Http.Request.DataAsString, _bodyJsonOptions) ?? new SkillQuery();
                ApplyQuerystringOverrides(req, query);
                ApplyReadScope(ctx, query);

                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Skill> result = await _database.Skills.EnumerateAsync(query).ConfigureAwait(false);
                if (!ctx.IsAdmin && !ctx.IsTenantAdmin)
                    result.Objects = result.Objects.Where(s => ScopedVisibility.CanView(ctx, s.Scope, s.TenantId, s.UserId)).ToList();
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Skills")
                .WithSummary("Enumerate skills")
                .WithRequestBody(OpenApiJson.BodyFor<SkillQuery>("Skill query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Skill>>("Paginated skills"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/skills", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Skill skill = JsonSerializer.Deserialize<Skill>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Skill.");
                skill.TenantId = ctx.IsAdmin ? (NormalizeEmpty(skill.TenantId) ?? ctx.TenantId) : ctx.TenantId;
                skill.UserId = ctx.UserId;
                // Regular users may create their own user-specific skills; admins may create tenant-wide.
                skill.Scope = ScopedVisibility.ResolveCreateScope(ctx, skill.Scope);

                if (String.IsNullOrWhiteSpace(skill.Name))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Name is required" };
                }

                Skill created = await _database.Skills.CreateAsync(skill).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return created;
            },
            api => api
                .WithTag("Skills")
                .WithSummary("Create a skill")
                .WithRequestBody(OpenApiJson.BodyFor<Skill>("Skill", true))
                .WithResponse(201, OpenApiJson.For<Skill>("Created skill"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/skills/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Skill? skill = await _database.Skills.ReadAsync(req.Parameters["id"], BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (skill == null || !ScopedVisibility.CanView(ctx, skill.Scope, skill.TenantId, skill.UserId))
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Skill not found" };
                }
                return skill;
            },
            api => api
                .WithTag("Skills")
                .WithSummary("Get a skill")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Skill ID (skl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Skill>("Skill"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/skills/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Skill? existing = await _database.Skills.ReadAsync(req.Parameters["id"], BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (existing == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Skill not found" };
                }
                if (!ScopedVisibility.CanEdit(ctx, existing.Scope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only modify your own skills; a tenant-wide skill requires a tenant admin." };
                }

                Skill incoming = JsonSerializer.Deserialize<Skill>(req.Http.Request.DataAsString, _bodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Skill.");
                existing.Name = incoming.Name;
                existing.Description = NormalizeEmpty(incoming.Description);
                existing.Category = NormalizeEmpty(incoming.Category);
                existing.Content = incoming.Content ?? String.Empty;
                existing.Active = incoming.Active;
                if (ctx.IsAdmin || ctx.IsTenantAdmin) existing.Scope = incoming.Scope;
                existing.LastUpdateUtc = DateTime.UtcNow;

                Skill updated = await _database.Skills.UpdateAsync(existing).ConfigureAwait(false);
                return updated;
            },
            api => api
                .WithTag("Skills")
                .WithSummary("Update a skill")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Skill ID (skl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Skill>("Skill", true))
                .WithResponse(200, OpenApiJson.For<Skill>("Updated skill"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/skills/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Skill? existing = await _database.Skills.ReadAsync(req.Parameters["id"], BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                if (existing == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Skill not found" };
                }
                if (!ScopedVisibility.CanEdit(ctx, existing.Scope, existing.TenantId, existing.UserId))
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "You may only delete your own skills; a tenant-wide skill requires a tenant admin." };
                }

                await _database.Skills.DeleteAsync(existing.Id, BuildScopedReadQuery(ctx)).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Skills")
                .WithSummary("Delete a skill")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Skill ID (skl_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private static ApiErrorResponse BuildAuthError(ApiRequest req)
        {
            return new ApiErrorResponse
            {
                Error = ApiResultEnum.BadRequest,
                Message = req.Http.Response.StatusCode == 401 ? "Authentication required" : "You do not have permission to perform this action"
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

        private static void ApplyQuerystringOverrides(ApiRequest req, SkillQuery query)
        {
            if (int.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (int.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            query.Category = NormalizeEmpty(req.Query.GetValueOrDefault("category")) ?? query.Category;
            query.Search = NormalizeEmpty(req.Query.GetValueOrDefault("search")) ?? query.Search;
            if (TryParseNullableBool(req.Query.GetValueOrDefault("active"), out bool? active))
                query.Active = active;
        }

        private static void ApplyReadScope(AuthContext ctx, SkillQuery query)
        {
            if (ctx.IsAdmin) return;
            query.TenantId = ctx.TenantId;
            query.UserId = null;
        }

        private static SkillQuery BuildScopedReadQuery(AuthContext ctx)
        {
            SkillQuery query = new SkillQuery();
            ApplyReadScope(ctx, query);
            return query;
        }

        private static bool TryParseNullableBool(string? value, out bool? result)
        {
            result = null;
            if (String.IsNullOrWhiteSpace(value)) return false;
            if (bool.TryParse(value, out bool parsed)) { result = parsed; return true; }
            if (value == "1") { result = true; return true; }
            if (value == "0") { result = false; return true; }
            return false;
        }

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
