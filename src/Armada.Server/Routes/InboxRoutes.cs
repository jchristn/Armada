namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API route for the operator "needs you" inbox.
    /// </summary>
    public class InboxRoutes
    {
        private readonly InboxService _inbox;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public InboxRoutes(InboxService inbox, JsonSerializerOptions jsonOptions)
        {
            _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
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
            app.Post("/api/v1/inbox/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }
                EnumerationQuery query = EnumerationRequestReader.Read(req, _jsonOptions);
                return await _inbox.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Inbox")
                .WithSummary("Enumerate the needs-you inbox")
                .WithDescription("One page of the items awaiting operator attention, paged at each source (missions, merge entries, and deployments are paged and counted in the database). Kinds are listed in a fixed urgency order (review, landing_failed, merge_failed, deployment_failed, deployment_approval, ask_proposal, cli_permission, failed, stalled_captain), newest first within a kind; TotalRecords is the exact count. Paging and filters come from the JSON body (EnumerationQuery) or the querystring (pageNumber, pageSize 1 to 1000, default 10).")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<InboxItem>>("One page"))
                .WithSecurity("ApiKey"));
        }
    }
}
