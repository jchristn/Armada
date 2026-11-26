namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST read of stage submissions (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2): the plans, verdicts and results a mission's
    /// captains submitted through the submit tools. Submitting is MCP only (a captain's mission-scoped token). The same
    /// rules as MCP apply through <see cref="StageSubmissionAccessService"/>.
    /// </summary>
    public class StageSubmissionRoutes
    {
        #region Private-Members

        private readonly StageSubmissionAccessService _Service;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Caller-scoped stage submission service.</param>
        /// <exception cref="ArgumentNullException">Thrown when service is null.</exception>
        public StageSubmissionRoutes(StageSubmissionAccessService service)
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
            app.Get("/api/v1/missions/{id}/submissions", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListByMissionAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("StageSubmissions")
                .WithSummary("List a mission's stage submissions")
                .WithDescription("Every plan, verdict or result the mission's captains submitted through armada_submit_plan, armada_submit_verdict or armada_submit_result, newest first, with attempt id, source (Mcp or Native) and the typed submission. The latest submission of an attempt decided that attempt. A global admin sees every tenant, everyone else their own tenant; a captain session sees its own voyage. 404 when the mission does not exist or is out of scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<List<StageSubmission>>("Submissions"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

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
