namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Services.Push;

    /// <summary>
    /// REST routes for push notification devices of the mobile apps: register or refresh, list, update, delete, and send
    /// a test push. Scoping is enforced by <see cref="PushDeviceService"/> (owner, tenant admins of the device's tenant,
    /// global admins; never a captain session). Tokens are masked in every response.
    /// </summary>
    public class PushRoutes
    {
        #region Private-Members

        private readonly PushDeviceService _Devices;
        private readonly PushNotificationService _Push;
        private readonly JsonSerializerOptions _JsonOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="devices">Device service.</param>
        /// <param name="push">Push notification service (test pushes).</param>
        /// <param name="jsonOptions">Serializer options for request bodies.</param>
        public PushRoutes(PushDeviceService devices, PushNotificationService push, JsonSerializerOptions jsonOptions)
        {
            _Devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _Push = push ?? throw new ArgumentNullException(nameof(push));
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
            app.Post("/api/v1/push/devices", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    PushDeviceRegisterRequest body = ReadBody<PushDeviceRegisterRequest>(req);
                    PushDeviceRegistration registration = await _Devices.RegisterAsync(ctx, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = registration.Created ? 201 : 200;
                    return registration.Device;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Push")
                .WithSummary("Register or refresh a push device")
                .WithDescription("Registers the calling user's mobile device for push notifications, keyed by its Expo push token (idempotent: a known token is refreshed and reactivated, 200; a new token is created, 201). A token previously registered by another user or tenant is deleted there and registered for the caller as a new device with a new id and fresh categories (201). Registering or reactivating a device beyond Push.MaxDevicesPerUser active devices deactivates the caller's least recently seen active devices. Platform (Ios, Android) and ExpoPushToken are required; DeviceName, AppVersion, Locale, and Categories are optional (Categories null keeps the device's categories, or uses Push.Categories for a new device). Captain sessions cannot register (403). The token is masked in the response.")
                .WithRequestBody(OpenApiJson.BodyFor<PushDeviceRegisterRequest>("Registration", true))
                .WithResponse(200, OpenApiJson.For<PushDevice>("The refreshed device"))
                .WithResponse(201, OpenApiJson.For<PushDevice>("The registered device"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Captain sessions cannot register devices"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/push/devices/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    EnumerationQuery query = EnumerationRequestReader.Read(req, _JsonOptions);
                    return await _Devices.EnumerateAsync(ctx, Blank(query.UserId), Blank(query.TenantId), query).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Push")
                .WithSummary("Enumerate push devices")
                .WithDescription("One page of the caller's devices (oldest first). A tenant admin may filter by userId within their tenant; a global admin may filter by userId and tenantId. Paging and filters come from the JSON body (EnumerationQuery) or the querystring (pageNumber, pageSize 1 to 1000, default 10).")
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not allowed to list another user's devices"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<PushDevice>>("One page"))
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/push/devices/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    PushDeviceUpdateRequest body = ReadBody<PushDeviceUpdateRequest>(req);
                    return await _Devices.UpdateAsync(ctx, req.Parameters["id"], body).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Push")
                .WithSummary("Update a push device")
                .WithDescription("Updates the device name and/or enabled categories (omitted fields are kept; an empty Categories list mutes the device). Allowed for the owner, tenant admins of the device's tenant, and global admins; 404 otherwise.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Device ID (pdv_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<PushDeviceUpdateRequest>("Update", true))
                .WithResponse(200, OpenApiJson.For<PushDevice>("The updated device"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/push/devices/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    await _Devices.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Push")
                .WithSummary("Delete a push device")
                .WithDescription("Removes the device (the app calls this on sign-out). Allowed for the owner, tenant admins of the device's tenant, and global admins; 404 otherwise.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Device ID (pdv_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.Create("Deleted"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/push/devices/{id}/test", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    PushDevice device = await _Devices.ReadManagedAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    return await _Push.SendTestAsync(device).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Push")
                .WithSummary("Send a test push")
                .WithDescription("Sends a test notification to the device now and returns the outcome: Sent (accepted by the Expo Push Service, with its ticket id), Disabled (Push.Enabled is false), DeviceInactive, DeviceNotRegistered (the device was deactivated), RateLimited, or Failed. Ignores categories and deduplication; counts toward the per-user rate limit. Allowed for the owner, tenant admins of the device's tenant, and global admins; 404 otherwise.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Device ID (pdv_ prefix)"))
                .WithResponse(200, OpenApiJson.For<PushTestResult>("Outcome"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
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

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
