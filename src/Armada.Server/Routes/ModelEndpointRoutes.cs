namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
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
    /// REST API routes for managed model endpoint (embedding/inference) configuration, validation, and health.
    /// </summary>
    public class ModelEndpointRoutes
    {
        private readonly ModelEndpointService _Endpoints;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="endpoints">Model endpoint service.</param>
        public ModelEndpointRoutes(ModelEndpointService endpoints)
        {
            _Endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Web server.</param>
        /// <param name="authenticate">Authentication delegate.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/model-endpoints", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                return await _Endpoints.EnumerateAsync(ctx).ConfigureAwait(false);
            },
            api => api
                .WithTag("Model Endpoints")
                .WithSummary("List model endpoints")
                .WithDescription("Returns all managed embedding/inference endpoints visible to the caller. API keys are never returned.")
                .WithResponse(200, OpenApiJson.For<List<ModelEndpoint>>("Model endpoints"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/model-endpoints", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ModelEndpoint request = JsonSerializer.Deserialize<ModelEndpoint>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ModelEndpoint.");

                try
                {
                    ModelEndpoint created = await _Endpoints.CreateAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return created;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Model Endpoints")
                .WithSummary("Create a model endpoint")
                .WithDescription("Creates an embedding or inference endpoint. Supply the API key via the write-only apiKey field; it is stored but never returned.")
                .WithRequestBody(OpenApiJson.BodyFor<ModelEndpoint>("Model endpoint create request", true))
                .WithResponse(201, OpenApiJson.For<ModelEndpoint>("Created model endpoint"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/model-endpoints/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ModelEndpoint? endpoint = await _Endpoints.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (endpoint == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Model endpoint not found" };
                }

                return endpoint;
            },
            api => api
                .WithTag("Model Endpoints")
                .WithSummary("Get a model endpoint")
                .WithDescription("Returns one model endpoint by ID. The API key is never returned.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model endpoint ID (mep_ prefix)"))
                .WithResponse(200, OpenApiJson.For<ModelEndpoint>("Model endpoint"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/model-endpoints/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                ModelEndpoint request = JsonSerializer.Deserialize<ModelEndpoint>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ModelEndpoint.");
                request.Id = req.Parameters["id"];

                try
                {
                    return await _Endpoints.UpdateAsync(ctx, request).ConfigureAwait(false);
                }
                catch (KeyNotFoundException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Model Endpoints")
                .WithSummary("Update a model endpoint")
                .WithDescription("Updates a model endpoint. Omit apiKey to keep the stored key; send apiKey to replace it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model endpoint ID (mep_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ModelEndpoint>("Model endpoint update request", true))
                .WithResponse(200, OpenApiJson.For<ModelEndpoint>("Updated model endpoint"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/model-endpoints/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _Endpoints.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (KeyNotFoundException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    // Endpoint is still referenced by one or more captains.
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Model Endpoints")
                .WithSummary("Delete a model endpoint")
                .WithDescription("Deletes one model endpoint within the caller scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model endpoint ID (mep_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/model-endpoints/{id}/validate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    return await _Endpoints.ValidateAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (KeyNotFoundException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Model Endpoints")
                .WithSummary("Validate a model endpoint")
                .WithDescription("Issues a real request (embedding for embedding endpoints, a short completion for inference endpoints) to validate connectivity to the configured model, and updates the endpoint's health status.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model endpoint ID (mep_ prefix)"))
                .WithResponse(200, OpenApiJson.For<ModelEndpointProbeResult>("Validation result"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/model-endpoints/health-check", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                int probed = await _Endpoints.CheckHealthAllAsync().ConfigureAwait(false);
                return new ModelEndpointHealthSweepResponse { DistinctBaseUrlsProbed = probed };
            },
            api => api
                .WithTag("Model Endpoints")
                .WithSummary("Run a health sweep")
                .WithDescription("Probes all enabled endpoints, deduplicated by base URL, and refreshes their health status. Returns the number of distinct base URLs probed.")
                .WithResponse(200, OpenApiJson.For<ModelEndpointHealthSweepResponse>("Health sweep summary"))
                .WithSecurity("ApiKey"));
        }

        private static ApiErrorResponse BuildAuthError(ApiRequest req)
        {
            return new ApiErrorResponse
            {
                Error = ApiResultEnum.BadRequest,
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
    }
}
