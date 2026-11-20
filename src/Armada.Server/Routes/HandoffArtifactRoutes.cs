namespace Armada.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST routes for handoff artifacts (the structured, versioned record each stage attempt hands to the next stage).
    /// Every operation enforces the same rules as MCP through <see cref="HandoffArtifactAccessService"/>.
    /// </summary>
    public class HandoffArtifactRoutes
    {
        #region Private-Members

        private readonly HandoffArtifactAccessService _Service;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Caller-scoped handoff artifact service.</param>
        /// <exception cref="ArgumentNullException">Thrown when service is null.</exception>
        public HandoffArtifactRoutes(HandoffArtifactAccessService service)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
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
            app.Get("/api/v1/missions/{id}/handoff-artifacts", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListByMissionAsync(ctx, req.Parameters["id"], ReadInt(req, "pageNumber"), ReadInt(req, "pageSize")).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("HandoffArtifacts")
                .WithSummary("List a mission's handoff artifacts")
                .WithDescription("One page of the handoff artifacts of a mission (one per stage attempt), newest first. Output and diff text are left out (outputLength and diffLength stay); get one artifact for them. A global admin sees every tenant, everyone else their own tenant; a captain session sees its own voyage. 404 when the mission does not exist or is out of scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 25)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<HandoffArtifact>>("One page"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/missions/{id}/handoff-artifacts/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    EnumerationQuery page = EnumerationRequestReader.Read(req, _BodyJsonOptions);
                    return await _Service.ListByMissionAsync(ctx, req.Parameters["id"], page.PageNumber, page.PageSize).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid request body: " + ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("HandoffArtifacts")
                .WithSummary("Enumerate a mission's handoff artifacts")
                .WithDescription("One page of the handoff artifacts recorded for a mission, newest first. The JSON body (EnumerationQuery) carries PageNumber and PageSize (1-1000, default 10).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<HandoffArtifact>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/handoff-artifacts/{id}", async (ApiRequest req) =>
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
                .WithTag("HandoffArtifacts")
                .WithSummary("Get a handoff artifact")
                .WithDescription("The complete artifact: numbered findings, acceptance criteria, required actions, check results, commit references, protocol lines, the full stage output and the full diff (or a pointer to regenerate it). 404 when it does not exist or is out of scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Artifact ID (hoa_ prefix)"))
                .WithResponse(200, OpenApiJson.For<HandoffArtifact>("The artifact"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private static int? ReadInt(ApiRequest req, string key)
        {
            string? value = QueryValueReader.Read(req.Query, key);
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new ArgumentException(key + " must be an integer.");
            return parsed;
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

        #endregion
    }
}
