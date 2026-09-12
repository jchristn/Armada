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
    /// REST API routes for registered Harbor (host runner) management.
    /// </summary>
    public class HarborRoutes
    {
        private readonly HarborService _Harbors;
        private readonly HarborConnectionManager _Connections;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harbors">Harbor service.</param>
        /// <param name="connections">Harbor connection manager (for the connectivity probe).</param>
        public HarborRoutes(HarborService harbors, HarborConnectionManager connections)
        {
            _Harbors = harbors ?? throw new ArgumentNullException(nameof(harbors));
            _Connections = connections ?? throw new ArgumentNullException(nameof(connections));
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
            app.Get("/api/v1/harbors", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                return await _Harbors.EnumerateAsync(ctx).ConfigureAwait(false);
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("List Harbors")
                .WithDescription("Returns all registered Harbors (host runners) visible to the caller.")
                .WithResponse(200, OpenApiJson.For<List<Harbor>>("Harbors"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Harbor request = JsonSerializer.Deserialize<Harbor>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Harbor.");
                try
                {
                    Harbor created = await _Harbors.CreateAsync(ctx, request).ConfigureAwait(false);
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
                .WithTag("Harbors")
                .WithSummary("Register a Harbor")
                .WithDescription("Pre-registers a Harbor. A Harbor also self-registers on first handshake.")
                .WithRequestBody(OpenApiJson.BodyFor<Harbor>("Harbor create request", true))
                .WithResponse(201, OpenApiJson.For<Harbor>("Created Harbor"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/harbors/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Harbor? harbor = await _Harbors.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (harbor == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Harbor not found" };
                }

                return harbor;
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Get a Harbor")
                .WithDescription("Returns one Harbor by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Harbor>("Harbor"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/harbors/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Harbor request = JsonSerializer.Deserialize<Harbor>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Harbor.");
                request.Id = req.Parameters["id"];
                try
                {
                    return await _Harbors.UpdateAsync(ctx, request).ConfigureAwait(false);
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
                .WithTag("Harbors")
                .WithSummary("Update a Harbor")
                .WithDescription("Updates operator-editable fields (name, capacity, enabled). Runtime state is managed by the link.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Harbor>("Harbor update request", true))
                .WithResponse(200, OpenApiJson.For<Harbor>("Updated Harbor"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/harbors/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _Harbors.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
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
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Delete a Harbor")
                .WithDescription("Deletes one Harbor registration within the caller scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors/{id}/enable", async (ApiRequest req) =>
            {
                return await SetEnabledAsync(req, authenticate, authz, true).ConfigureAwait(false);
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Enable a Harbor")
                .WithDescription("Enables a Harbor so the router may dispatch new missions to it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Harbor>("Updated Harbor"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors/{id}/disable", async (ApiRequest req) =>
            {
                return await SetEnabledAsync(req, authenticate, authz, false).ConfigureAwait(false);
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Disable a Harbor")
                .WithDescription("Disables a Harbor so it keeps its docks but receives no new missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Harbor>("Updated Harbor"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors/{id}/probe", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Harbor? harbor = await _Harbors.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (harbor == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Harbor not found" };
                }

                HarborProbeRequest probe = new HarborProbeRequest();
                if (!string.IsNullOrWhiteSpace(req.Http.Request.DataAsString))
                    probe = JsonSerializer.Deserialize<HarborProbeRequest>(req.Http.Request.DataAsString, _BodyJsonOptions) ?? new HarborProbeRequest();

                if (!_Connections.IsConnected(harbor.Id))
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Harbor is not connected; no link to probe over." };
                }

                RemoteHostCommandExecutor executor = new RemoteHostCommandExecutor(_Connections, harbor.Id);
                return await executor.RunAsync(new HostCommandRequest
                {
                    Executable = probe.Executable,
                    Arguments = probe.Arguments,
                    WorkingDirectory = probe.WorkingDirectory,
                    TimeoutMs = probe.TimeoutMs
                }).ConfigureAwait(false);
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Probe a Harbor")
                .WithDescription("Runs a one-off host command (default: git --version) on a connected Harbor over its link and returns the result. Verifies the end-to-end server-issues-work / Harbor-replies loop.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<HarborProbeRequest>("Optional command to run", false))
                .WithResponse(200, OpenApiJson.For<HostCommandResult>("Command result"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private async Task<object?> SetEnabledAsync(
            ApiRequest req,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz,
            bool enabled)
        {
            AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
            if (ctx == null) return BuildAuthError(req);

            try
            {
                return await _Harbors.SetEnabledAsync(ctx, req.Parameters["id"], enabled).ConfigureAwait(false);
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
