namespace Armada.Server.Routes
{
    using System;
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API route for the Ask Armada assistant.
    /// </summary>
    public class AskRoutes
    {
        private readonly AskArmadaService _ask;
        private readonly CaptainChatService _captainChat;
        private readonly JsonSerializerOptions _jsonOptions;
        private static readonly JsonSerializerOptions _bodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskRoutes(AskArmadaService ask, CaptainChatService captainChat, JsonSerializerOptions jsonOptions)
        {
            _ask = ask ?? throw new ArgumentNullException(nameof(ask));
            _captainChat = captainChat ?? throw new ArgumentNullException(nameof(captainChat));
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
            app.Post("/api/v1/ask", async (ApiRequest req) =>
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

                AskRequest request = JsonSerializer.Deserialize<AskRequest>(req.Http.Request.DataAsString, _bodyJsonOptions) ?? new AskRequest();
                AskResponse response = await _ask.AskAsync(request.Message, ctx).ConfigureAwait(false);
                return response;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Ask Armada a question")
                .WithDescription("A lightweight conversational assistant that answers read-only questions about fleet state and returns suggested navigation links.")
                .WithRequestBody(OpenApiJson.BodyFor<AskRequest>("The question", true))
                .WithResponse(200, OpenApiJson.For<AskResponse>("The assistant response"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/captains/{id}/chat", async (ApiRequest req) =>
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

                string id = req.Parameters["id"];
                CaptainChatRequest request = JsonSerializer.Deserialize<CaptainChatRequest>(req.Http.Request.DataAsString, _bodyJsonOptions) ?? new CaptainChatRequest();
                CaptainChatResponse response = await _captainChat.ChatAsync(id, request, ctx).ConfigureAwait(false);
                return response;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Chat with a captain")
                .WithDescription("Send a chat turn directly to a captain's configured model (Mux/Ollama endpoints) and return the reply plus per-turn timing and token metrics.")
                .WithRequestBody(OpenApiJson.BodyFor<CaptainChatRequest>("The message and prior conversation", true))
                .WithResponse(200, OpenApiJson.For<CaptainChatResponse>("The captain reply and metrics"))
                .WithSecurity("ApiKey"));
        }
    }
}
