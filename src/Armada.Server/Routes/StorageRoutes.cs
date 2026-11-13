namespace Armada.Server.Routes
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Storage;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>Global-administrator-only cached storage inventory and background refresh routes.</summary>
    public class StorageRoutes
    {
        private readonly StorageInventoryService _Inventory;
        private readonly JsonSerializerOptions _JsonOptions;

        /// <summary>Construct storage routes.</summary>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        public StorageRoutes(StorageInventoryService inventory, JsonSerializerOptions jsonOptions)
        {
            _Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>Register authenticated routes. Authorization is enforced centrally and in each handler.</summary>
        public void Register(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/storage", async (ApiRequest req) =>
            {
                AuthContext context = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(context, "GET", "/api/v1/storage")) return Denied(req, context);
                return (object)_Inventory.Read();
            }, api => api.WithTag("Storage").WithSummary("Read cached storage inventory")
                .WithDescription("Global administrators only. Returns bounded cached database, retention and process-local history measurements with host totals (HostCount, FreshHostCount, ObservedFileBytes). Host reports are not embedded: page them with GET /api/v1/storage/hosts. History breakdowns keep the 24 largest entries and fold the rest into one (other) entry. Does not trigger scans. Logical file bytes and database allocation are separate quantities.")
                .WithResponse(200, OpenApiJson.For<StorageSnapshot>("Cached storage snapshot"))
                .WithResponse(401, OpenApiResponseMetadata.Create("Authentication required"))
                .WithResponse(403, OpenApiResponseMetadata.Create("Global administrator required"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/storage/hosts", async (ApiRequest req) =>
            {
                AuthContext context = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(context, "GET", "/api/v1/storage/hosts")) return Denied(req, context);
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                return (object)_Inventory.EnumerateHosts(query);
            }, api => api.WithTag("Storage").WithSummary("List cached storage host reports")
                .WithDescription("Global administrators only. One page of the cached host reports (the Admiral first, then each Harbor by name): locations (at most StorageSettings.MaxLocations), volumes, warnings, and largest files. Querystring: pageNumber (default 1), pageSize (1 to 1000, default 10). Does not trigger scans.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<StorageHostReport>>("Host reports"))
                .WithResponse(401, OpenApiResponseMetadata.Create("Authentication required"))
                .WithResponse(403, OpenApiResponseMetadata.Create("Global administrator required"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/storage/hosts/enumerate", async (ApiRequest req) =>
            {
                AuthContext context = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(context, "POST", "/api/v1/storage/hosts/enumerate")) return Denied(req, context);
                EnumerationQuery query;
                try
                {
                    query = EnumerationRequestReader.Read(req, _JsonOptions);
                }
                catch (JsonException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Request body is not valid: " + ex.Message };
                }
                return (object)_Inventory.EnumerateHosts(query);
            }, api => api.WithTag("Storage").WithSummary("Enumerate cached storage host reports")
                .WithDescription("POST form of GET /api/v1/storage/hosts: the EnumerationQuery (pageNumber, pageSize) comes from the body, and querystring values override it. Global administrators only.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<StorageHostReport>>("Host reports"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(401, OpenApiResponseMetadata.Create("Authentication required"))
                .WithResponse(403, OpenApiResponseMetadata.Create("Global administrator required"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/storage/refresh", async (ApiRequest req) =>
            {
                AuthContext context = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(context, "POST", "/api/v1/storage/refresh")) return Denied(req, context);
                StorageRefreshRequest request;
                try
                {
                    request = String.IsNullOrWhiteSpace(req.Http.Request.DataAsString) ? new StorageRefreshRequest()
                        : JsonSerializer.Deserialize<StorageRefreshRequest>(req.Http.Request.DataAsString, _JsonOptions) ?? new StorageRefreshRequest();
                }
                catch (JsonException)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Expected a storage refresh request with an optional exactCounts boolean." };
                }
                _Inventory.RequestRefresh(request.ExactCounts);
                req.Http.Response.StatusCode = 202;
                return (object)_Inventory.Read();
            }, api => api.WithTag("Storage").WithSummary("Request background storage collection")
                .WithDescription("Global administrators only. Optional JSON body: {\"exactCounts\":false}. Concurrent requests coalesce. Exact counts scan database tables with timeouts. Returns immediately; poll GET /api/v1/storage for results. Harbor reports arrive on their own collection cadence.")
                .WithRequestBody(OpenApiJson.BodyFor<StorageRefreshRequest>("Optional exact record-count request", false))
                .WithResponse(202, OpenApiJson.For<StorageSnapshot>("Collection accepted or already running"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(401, OpenApiResponseMetadata.Create("Authentication required"))
                .WithResponse(403, OpenApiResponseMetadata.Create("Global administrator required"))
                .WithSecurity("ApiKey"));
        }

        private static object Denied(ApiRequest request, AuthContext context)
        {
            request.Http.Response.StatusCode = context.IsAuthenticated ? 403 : 401;
            return new ApiErrorResponse { Error = context.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = "Global administrator authentication required." };
        }
    }
}
