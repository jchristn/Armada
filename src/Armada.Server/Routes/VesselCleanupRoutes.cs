namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST routes for vessel cleanup: a read-only inventory of a vessel's docks, worktrees, and Armada-managed branches,
    /// and a cleanup of selected safe resources that revalidates each one before removing it. The vessel is resolved in
    /// the caller's scope (another tenant's vessel returns 404).
    /// </summary>
    public class VesselCleanupRoutes
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly VesselCleanupService _Cleanup;

        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="cleanup">Vessel cleanup service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public VesselCleanupRoutes(DatabaseDriver database, VesselCleanupService cleanup)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/vessels/{id}/cleanup", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Vessel? vessel = await ReadVesselAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (vessel == null) return NotFound(req);
                return (object)await _Cleanup.ScanAsync(vessel).ConfigureAwait(false);
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Vessel cleanup inventory")
                .WithDescription("Read-only inventory summary of the vessel's docks, orphaned worktrees, and Armada-managed local branches (armada/ prefix): ResourceCount, Safe/InUse/Unknown counts, and measured safe bytes. The resources are not embedded: page them with GET /api/v1/vessels/{id}/cleanup/resources or POST .../cleanup/resources/enumerate. Resources are inspected on the owning host; a disconnected Harbor makes its resources Unknown. Nothing is deleted.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<VesselCleanupInventory>("Cleanup inventory summary"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/cleanup/resources", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Vessel? vessel = await ReadVesselAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (vessel == null) return NotFound(req);
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
                try
                {
                    return (object)await _Cleanup.EnumerateResourcesAsync(vessel, query).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(req, ex.Message);
                }
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("List vessel cleanup resources")
                .WithDescription("One page of the vessel's cleanup inventory: docks (oldest first), then each repository's orphaned worktrees and Armada-managed local branches, each classified Safe, InUse, or Unknown with a reason, owning mission/voyage/dock, and a measured size when available. The inventory is read from git and the file system on the owning host and classified as a whole on every request; the page is cut from it and TotalRecords is exact. Querystring: pageNumber (default 1), pageSize (1 to 1000, default 10), status (Safe, InUse, or Unknown). Nothing is deleted.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Disposition filter: Safe, InUse, or Unknown", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<VesselCleanupResource>>("Cleanup resources"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/vessels/{id}/cleanup/resources/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Vessel? vessel = await ReadVesselAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (vessel == null) return NotFound(req);
                try
                {
                    EnumerationQuery query = EnumerationRequestReader.Read(req, _BodyJsonOptions);
                    return (object)await _Cleanup.EnumerateResourcesAsync(vessel, query).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    return BadRequest(req, "Request body is not valid: " + ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(req, ex.Message);
                }
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Enumerate vessel cleanup resources")
                .WithDescription("POST form of GET /api/v1/vessels/{id}/cleanup/resources: the EnumerationQuery (pageNumber, pageSize, status) comes from the body, and querystring values override it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging query; status filters by disposition", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<VesselCleanupResource>>("Cleanup resources"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/vessels/{id}/cleanup", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Vessel? vessel = await ReadVesselAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (vessel == null) return NotFound(req);

                VesselCleanupRequest? body = null;
                string data = req.Http.Request.DataAsString;
                if (!String.IsNullOrWhiteSpace(data))
                {
                    try
                    {
                        body = JsonSerializer.Deserialize<VesselCleanupRequest>(data, _BodyJsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        return BadRequest(req, "Request body is not valid: " + ex.Message);
                    }
                }

                if (body == null || body.Keys == null || body.Keys.Count == 0)
                    return BadRequest(req, "Keys is required and must list at least one resource.");
                CollectionLimits.RequireRequestItems(body.Keys, "Keys");

                try
                {
                    return (object)await _Cleanup.CleanupAsync(vessel, body).ConfigureAwait(false);
                }
                catch (ArgumentException ex) when (!(ex is CollectionLimitExceededException))
                {
                    return BadRequest(req, ex.Message);
                }
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Clean up vessel resources")
                .WithDescription("Remove the selected inventory resources (Keys from GET /cleanup). Each key is re-inventoried immediately before removal and removed only if it is still Safe; docks run first, then orphaned worktrees, then local branches. Remote branches, default branches, primary checkouts, and paths outside Armada's docks are never removed. Keys holds at most 1000 entries (more is 400 CollectionLimitExceeded). Returns per-item Deleted/Skipped/Failed outcomes (one per distinct key) and the refreshed inventory summary; page the refreshed resources with GET .../cleanup/resources.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<VesselCleanupRequest>("Selected resource keys", true))
                .WithResponse(200, OpenApiJson.For<VesselCleanupResult>("Per-item outcomes and refreshed inventory"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private async Task<Vessel?> ReadVesselAsync(AuthContext ctx, string id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            if (ctx.IsAdmin) return await _Database.Vessels.ReadAsync(id).ConfigureAwait(false);
            if (ctx.IsTenantAdmin) return await _Database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
            return await _Database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
        }

        private static ApiErrorResponse NotFound(ApiRequest req)
        {
            req.Http.Response.StatusCode = 404;
            return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
        }

        private static ApiErrorResponse BadRequest(ApiRequest req, string? message)
        {
            req.Http.Response.StatusCode = 400;
            return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = message };
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

            if (String.IsNullOrEmpty(ctx.TenantId)) ctx.TenantId = Armada.Core.Constants.DefaultTenantId;
            return ctx;
        }

        #endregion
    }
}
