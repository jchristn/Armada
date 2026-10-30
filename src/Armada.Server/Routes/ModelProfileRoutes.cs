namespace Armada.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST routes for model profiles (the model as served, with immutable revisions) under /api/v1/model-profiles.
    /// Every operation enforces the same rules as MCP through <see cref="ModelProfileAccessService"/>.
    /// </summary>
    public class ModelProfileRoutes
    {
        #region Private-Members

        private readonly ModelProfileAccessService _Service;
        private readonly JsonSerializerOptions _JsonOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Caller-scoped model profile service.</param>
        /// <param name="jsonOptions">Serializer options for request bodies.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelProfileRoutes(ModelProfileAccessService service, JsonSerializerOptions jsonOptions)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Web server.</param>
        /// <param name="authenticate">Authenticator.</param>
        /// <param name="authz">Route authorization.</param>
        public void Register(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/model-profiles", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListAsync(ctx, ParseQuery(key => req.Query.GetValueOrDefault(key))).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelProfiles")
                .WithSummary("List model profiles")
                .WithDescription("One page of model profiles (the model as served: runtime, provider, model and served context), newest first, without their revisions. A global admin sees every tenant; everyone else sees their own tenant. Profiles are advisory: they do not set a captain's tier or change routing.")
                .WithParameter(OpenApiParameterMetadata.Query("tenantId", "Tenant filter (global admins only; ignored otherwise)", false))
                .WithParameter(OpenApiParameterMetadata.Query("runtime", "Runtime filter (case-insensitive), for example ClaudeCode or Mux", false))
                .WithParameter(OpenApiParameterMetadata.Query("provider", "Provider filter (case-insensitive), for example Ollama", false))
                .WithParameter(OpenApiParameterMetadata.Query("model", "Only models whose name contains this text (case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 100)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<ModelProfile>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/model-profiles/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListAsync(ctx, ReadBody<ModelProfileQuery>(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelProfiles")
                .WithSummary("Enumerate model profiles")
                .WithDescription("One page of model profiles visible to the caller, newest first, without their revisions. The JSON body (ModelProfileQuery) carries TenantId (global admins only), Runtime, Provider, ModelContains, PageNumber, and PageSize (1-1000, default 100).")
                .WithRequestBody(OpenApiJson.BodyFor<ModelProfileQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<ModelProfile>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/model-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.GetAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelProfiles")
                .WithSummary("Get a model profile")
                .WithDescription("The profile with its revisions, oldest first. A revision (runtime version, model digest, sampling, tool configuration, first seen) is immutable; CompatibleWith lists the revisions an administrator marked compatible. 404 when the profile does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model profile ID (mdl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<ModelProfile>("The profile"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/model-profiles/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.UpdateAsync(ctx, req.Parameters["id"], ReadBody<ModelProfileUpdateRequest>(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelProfiles")
                .WithSummary("Update a model profile")
                .WithDescription("Updates the mutable parts of a profile: Capabilities, Rendering (explicit overrides; null values mean no override) and Notes. A section the body omits keeps its value; a section it carries replaces it whole; an empty Notes clears the notes. The identity and the revisions never change. Global admins and tenant admins of the profile's tenant only (403 otherwise).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model profile ID (mdl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ModelProfileUpdateRequest>("Capabilities, Rendering and Notes"))
                .WithResponse(200, OpenApiJson.For<ModelProfile>("The updated profile with its revisions"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/model-profiles/{id}/revisions/{revisionId}/compatible-with", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.SetCompatibleWithAsync(ctx, req.Parameters["id"], req.Parameters["revisionId"], ReadBody<ModelProfileCompatibilityRequest>(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelProfiles")
                .WithSummary("Mark model profile revisions compatible")
                .WithDescription("Replaces the set of revisions marked compatible with this revision (symmetric: each listed revision is marked compatible with it too), so their results may be pooled. An empty list removes every mark. Every listed revision must exist in the same tenant (400 otherwise). The revisions themselves are never edited. Global admins and tenant admins of the profile's tenant only (403 otherwise).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model profile ID (mdl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("revisionId", "Revision ID of that profile (mdv_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ModelProfileCompatibilityRequest>("CompatibleWith: revision IDs"))
                .WithResponse(200, OpenApiJson.For<ModelProfileRevision>("The revision with its compatibility marks"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        /// <summary>
        /// Parse list filters from name/value pairs (querystring); shared with MCP.
        /// </summary>
        /// <param name="value">Reads one value by name, or null.</param>
        /// <returns>The query.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        /// <exception cref="ArgumentException">Thrown for an invalid page number or size.</exception>
        public static ModelProfileQuery ParseQuery(Func<string, string?> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ModelProfileQuery query = new ModelProfileQuery
            {
                TenantId = Blank(value("tenantId")),
                Runtime = Blank(value("runtime")),
                Provider = Blank(value("provider")),
                ModelContains = Blank(value("model"))
            };

            int? pageNumber = ParseInt(value("pageNumber"), "pageNumber");
            if (pageNumber.HasValue) query.PageNumber = pageNumber.Value;
            int? pageSize = ParseInt(value("pageSize"), "pageSize");
            if (pageSize.HasValue) query.PageSize = pageSize.Value;
            return query;
        }

        #endregion

        #region Private-Methods

        private T ReadBody<T>(ApiRequest req) where T : class, new()
        {
            string body = req.Http.Request.DataAsString ?? String.Empty;
            if (String.IsNullOrWhiteSpace(body)) return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(body, _JsonOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid request body: " + ex.Message, ex);
            }
        }

        private static ApiErrorResponse? Deny(ApiRequest req, AuthContext ctx, IAuthorizationService authz)
        {
            if (authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery)) return null;
            req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
            return new ApiErrorResponse
            {
                Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized,
                Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
            };
        }

        private static int? ParseInt(string? value, string name)
        {
            string? text = Blank(value);
            if (text == null) return null;
            if (!Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) throw new ArgumentException("Invalid " + name + ": " + text);
            return parsed;
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
