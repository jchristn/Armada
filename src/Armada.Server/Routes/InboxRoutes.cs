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
            app.Get("/api/v1/inbox", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ApiResultEnum.BadRequest,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }

                List<InboxItem> items = await _inbox.GetInboxAsync(ctx).ConfigureAwait(false);
                return items;
            },
            api => api
                .WithTag("Inbox")
                .WithSummary("Get the needs-you inbox")
                .WithDescription("Returns a consolidated, most-urgent-first list of items awaiting operator attention (reviews, failed landings, failed missions, stalled captains).")
                .WithResponse(200, OpenApiJson.For<List<InboxItem>>("Inbox items"))
                .WithSecurity("ApiKey"));
        }
    }
}
