namespace Armada.Server.Routes
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for deployment environment management.
    /// </summary>
    public class EnvironmentRoutes
    {
        private readonly DeploymentEnvironmentService _Environments;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EnvironmentRoutes(DeploymentEnvironmentService environments)
        {
            _Environments = environments ?? throw new ArgumentNullException(nameof(environments));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/environments", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentEnvironmentQuery query = BuildQueryFromRequest(req);
                return await _Environments.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Environments")
                .WithSummary("List environments")
                .WithDescription("Returns paginated deployment environments.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("kind", "Optional environment kind filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("isDefault", "Optional default-environment filter", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("active", "Optional active-state filter", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional free-text search", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<DeploymentEnvironment>>("Paginated environments"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/environments/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentEnvironmentQuery query = JsonSerializer.Deserialize<DeploymentEnvironmentQuery>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? new DeploymentEnvironmentQuery();
                ApplyQuerystringOverrides(req, query);
                return await _Environments.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Environments")
                .WithSummary("Enumerate environments")
                .WithDescription("Paginated environment enumeration using a JSON body and optional querystring overrides.")
                .WithRequestBody(OpenApiJson.BodyFor<DeploymentEnvironmentQuery>("Environment query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<DeploymentEnvironment>>("Paginated environments"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/environments", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentEnvironmentUpsertRequest request = JsonSerializer.Deserialize<DeploymentEnvironmentUpsertRequest>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as DeploymentEnvironmentUpsertRequest.");

                try
                {
                    DeploymentEnvironment environment = await _Environments.CreateAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return environment;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Environments")
                .WithSummary("Create an environment")
                .WithDescription("Creates a first-class deployment environment for a vessel.")
                .WithRequestBody(OpenApiJson.BodyFor<DeploymentEnvironmentUpsertRequest>("Environment create request", true))
                .WithResponse(201, OpenApiJson.For<DeploymentEnvironment>("Created environment"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/environments/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentEnvironment? environment = await _Environments.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (environment == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Environment not found" };
                }

                return environment;
            },
            api => api
                .WithTag("Environments")
                .WithSummary("Get an environment")
                .WithDescription("Returns one environment by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Environment ID (env_ prefix)"))
                .WithResponse(200, OpenApiJson.For<DeploymentEnvironment>("Environment"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/environments/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentEnvironmentUpsertRequest request = JsonSerializer.Deserialize<DeploymentEnvironmentUpsertRequest>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as DeploymentEnvironmentUpsertRequest.");

                try
                {
                    return await _Environments.UpdateAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 404 : 400;
                    return new ApiErrorResponse
                    {
                        Error = req.Http.Response.StatusCode == 404 ? ApiResultEnum.NotFound : ApiResultEnum.BadRequest,
                        Message = ex.Message
                    };
                }
            },
            api => api
                .WithTag("Environments")
                .WithSummary("Update an environment")
                .WithDescription("Updates environment metadata for a vessel deployment target.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Environment ID (env_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<DeploymentEnvironmentUpsertRequest>("Environment update request", true))
                .WithResponse(200, OpenApiJson.For<DeploymentEnvironment>("Updated environment"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/environments/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _Environments.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Environments")
                .WithSummary("Delete an environment")
                .WithDescription("Deletes one environment record within the caller scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Environment ID (env_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private static DeploymentEnvironmentQuery BuildQueryFromRequest(ApiRequest req)
        {
            DeploymentEnvironmentQuery query = new DeploymentEnvironmentQuery();
            ApplyQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyQuerystringOverrides(ApiRequest req, DeploymentEnvironmentQuery query)
        {
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (Boolean.TryParse(req.Query.GetValueOrDefault("isDefault"), out bool isDefault))
                query.IsDefault = isDefault;
            if (Boolean.TryParse(req.Query.GetValueOrDefault("active"), out bool active))
                query.Active = active;
            if (Enum.TryParse(req.Query.GetValueOrDefault("kind"), true, out EnvironmentKindEnum kind))
                query.Kind = kind;

            query.VesselId = NormalizeEmpty(req.Query.GetValueOrDefault("vesselId")) ?? query.VesselId;
            query.Search = NormalizeEmpty(req.Query.GetValueOrDefault("search")) ?? query.Search;
        }

        private static ApiErrorResponse BuildAuthError(ApiRequest req)
        {
            return new ApiErrorResponse
            {
                Error = req.Http.Response.StatusCode == 401 ? ApiResultEnum.NotAuthorized : ApiResultEnum.Forbidden,
                Message = req.Http.Response.StatusCode == 401
                    ? "Authentication required"
                    : "You do not have permission to perform this action"
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

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
