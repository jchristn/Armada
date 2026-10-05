namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Ask;
    using Armada.Core.Services.Interfaces;
    using Armada.Server.Ask;

    /// <summary>
    /// REST API routes for Ask Armada: the stateless assistant (/api/v1/ask), direct captain chat
    /// (/api/v1/captains/{id}/chat), and conversation threads (/api/v1/ask/threads/...). Every thread route is
    /// authenticated and owner-scoped: a thread of another user returns 404.
    /// </summary>
    public class AskRoutes
    {
        #region Private-Members
        private readonly CaptainChatService _CaptainChat;
        private readonly AskThreadService? _Threads;
        private readonly AskTurnCoordinator? _Turns;
        private readonly AskActionService? _Actions;
        private readonly JsonSerializerOptions _JsonOptions;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="captainChat">Direct captain chat.</param>
        /// <param name="jsonOptions">JSON options.</param>
        /// <param name="threads">Thread service; null disables the thread routes.</param>
        /// <param name="turns">Turn coordinator; null disables the thread routes.</param>
        /// <param name="actions">Action service; null disables the thread routes.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public AskRoutes(CaptainChatService captainChat, JsonSerializerOptions jsonOptions, AskThreadService? threads = null, AskTurnCoordinator? turns = null, AskActionService? actions = null)
        {
            _CaptainChat = captainChat ?? throw new ArgumentNullException(nameof(captainChat));
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
            _Threads = threads;
            _Turns = turns;
            _Actions = actions;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Request authenticator.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Post("/api/v1/captains/{id}/chat", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;

                string id = req.Parameters["id"];
                CaptainChatRequest request = JsonSerializer.Deserialize<CaptainChatRequest>(req.Http.Request.DataAsString, _BodyJsonOptions) ?? new CaptainChatRequest();
                CaptainChatResponse response = await _CaptainChat.ChatAsync(id, request, ctx).ConfigureAwait(false);
                return response;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Chat with a captain")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithDescription("Send a chat turn directly to a captain's configured model (Mux/Ollama endpoints) and return the reply plus per-turn timing and token metrics.")
                .WithRequestBody(OpenApiJson.BodyFor<CaptainChatRequest>("The message and prior conversation", true))
                .WithResponse(200, OpenApiJson.For<CaptainChatResponse>("The captain reply and metrics"))
                .WithSecurity("ApiKey"));

            if (_Threads != null && _Turns != null && _Actions != null) RegisterThreadRoutes(app, authenticate, authz, _Threads, _Turns, _Actions);
        }

        #endregion

        #region Private-Methods

        private void RegisterThreadRoutes(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz, AskThreadService threads, AskTurnCoordinator turns, AskActionService actions)
        {
            app.Post("/api/v1/ask/threads/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskThreadEnumerateRequest request = Body<AskThreadEnumerateRequest>(req) ?? new AskThreadEnumerateRequest();
                return await threads.EnumerateThreadsAsync(ctx, request).ConfigureAwait(false);
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Enumerate my Ask Armada threads")
                .WithDescription("Returns the caller's threads, pinned first, then by most recent activity. Archived threads are excluded unless IncludeArchived is true.")
                .WithRequestBody(OpenApiJson.BodyFor<AskThreadEnumerateRequest>("Paging, search, archive filter", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<AskThread>>("A page of threads"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskThreadCreateRequest request = Body<AskThreadCreateRequest>(req) ?? new AskThreadCreateRequest();
                try
                {
                    AskThread thread = await threads.CreateThreadAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return thread;
                }
                catch (ArgumentException ex)
                {
                    return Error(req, 400, ApiResultEnum.BadRequest, ex.Message);
                }
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Create an Ask Armada thread")
                .WithDescription("Creates a private conversation owned by the caller. The title defaults to \"New conversation\" and is replaced by the first message.")
                .WithRequestBody(OpenApiJson.BodyFor<AskThreadCreateRequest>("Optional title, captain, auto-approve", false))
                .WithResponse(201, OpenApiJson.For<AskThread>("The created thread"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/ask/threads/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskThreadDetail? detail = await threads.GetThreadDetailAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (detail == null) return Error(req, 404, ApiResultEnum.NotFound, "Thread not found");
                return detail;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Read an Ask Armada thread")
                .WithDescription("Returns the thread, its tracked work (each with its latest snapshot), and its pending proposals.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithResponse(200, OpenApiJson.For<AskThreadDetail>("The thread detail"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/ask/threads/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskThreadUpdateRequest request = ParseUpdate(req.Http.Request.DataAsString);
                try
                {
                    AskThread? thread = await threads.UpdateThreadAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                    if (thread == null) return Error(req, 404, ApiResultEnum.NotFound, "Thread not found");
                    return thread;
                }
                catch (ArgumentException ex)
                {
                    return Error(req, 400, ApiResultEnum.BadRequest, ex.Message);
                }
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Update an Ask Armada thread")
                .WithDescription("Updates title, captain, auto-approve, pinned, or archived. Absent fields are unchanged; CaptainId null clears the captain.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<AskThreadUpdateRequest>("Fields to change", true))
                .WithResponse(200, OpenApiJson.For<AskThread>("The updated thread"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/ask/threads/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                string id = req.Parameters["id"];
                if (turns.ActiveTurnId(id) != null) await turns.CancelAsync(ctx, id).ConfigureAwait(false);
                bool deleted = await threads.DeleteThreadAsync(ctx, id).ConfigureAwait(false);
                if (!deleted) return Error(req, 404, ApiResultEnum.NotFound, "Thread not found");
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Delete an Ask Armada thread")
                .WithDescription("Deletes the thread with its messages, tool calls, proposals, and tracked-work rows. The work itself is untouched.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads/{id}/messages/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskMessageEnumerateRequest request = Body<AskMessageEnumerateRequest>(req) ?? new AskMessageEnumerateRequest();
                AskMessagePage? page = await threads.EnumerateMessagesAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                if (page == null) return Error(req, 404, ApiResultEnum.NotFound, "Thread not found");
                return page;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Enumerate thread messages")
                .WithDescription("Returns the newest page of messages (or the page before BeforeSequence), oldest first within the page, each with ToolCalls, Proposal, and TrackedWork.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<AskMessageEnumerateRequest>("Paging", false))
                .WithResponse(200, OpenApiJson.For<AskMessagePage>("A page of messages"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads/{id}/messages", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskMessageSendRequest request = Body<AskMessageSendRequest>(req) ?? new AskMessageSendRequest();
                AskTurnStart start = await turns.SendMessageAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                if (start.StatusCode != 202) return Error(req, start.StatusCode, ToResult(start.StatusCode), start.Message ?? "Request failed");
                req.Http.Response.StatusCode = 202;
                return start.Response;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Send a message")
                .WithDescription("Persists the user message and runs the thread's captain turn in the background (202). Streaming arrives as ask.* WebSocket events. 409 when a turn is already running.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<AskMessageSendRequest>("Message", true))
                .WithResponse(202, OpenApiJson.For<AskMessageSendResponse>("Message and turn ids"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads/{id}/cancel", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                int status = await turns.CancelAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (status == 404) return Error(req, 404, ApiResultEnum.NotFound, "Thread not found");
                if (status == 409) return Error(req, 409, ApiResultEnum.Conflict, "No turn is running in this conversation");
                return new { Cancelled = true };
            },
            api => api
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Ask")
                .WithSummary("Stop the running turn")
                .WithDescription("Stops the captain turn running in the thread. 409 when no turn is running.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Conflicts with the current state"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads/{id}/summarize", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskTurnStart start = await turns.SummarizeAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (start.StatusCode != 202) return Error(req, start.StatusCode, ToResult(start.StatusCode), start.Message ?? "Request failed");
                req.Http.Response.StatusCode = 202;
                return start.Response;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Summarize a thread")
                .WithDescription("Writes a summary in the background (202): by the captain when the thread has one, otherwise deterministically. Posts a Summary message and sets SummaryText.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithResponse(202, OpenApiJson.For<AskMessageSendResponse>("Summary started"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads/{id}/read", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskThread? thread = await threads.MarkReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (thread == null) return Error(req, 404, ApiResultEnum.NotFound, "Thread not found");
                return thread;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Mark a thread read")
                .WithDescription("Resets UnreadCount to 0 and returns the thread.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithResponse(200, OpenApiJson.For<AskThread>("The thread"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads/{id}/actions", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskActionRequest? request;
                try { request = Body<AskActionRequest>(req); }
                catch (JsonException ex) { return Error(req, 400, ApiResultEnum.BadRequest, "Invalid body: " + ex.Message); }
                AskProposalDecision decision = await actions.SubmitQuickActionAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                if (decision.StatusCode != 200) return Error(req, decision.StatusCode, ToResult(decision.StatusCode), decision.Message ?? "Request failed");
                return decision.Proposal;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Run a quick action")
                .WithDescription("Runs an MCP tool (same names and argument shapes as MCP) as an already-approved QuickAction proposal and returns the executed proposal.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<AskActionRequest>("Tool name and arguments", true))
                .WithResponse(200, OpenApiJson.For<AskActionProposal>("The executed proposal"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads/{id}/proposals/{pid}/approve", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskProposalDecision decision = await actions.ApproveAsync(ctx, req.Parameters["id"], req.Parameters["pid"]).ConfigureAwait(false);
                if (decision.StatusCode != 200) return Error(req, decision.StatusCode, ToResult(decision.StatusCode), decision.Message ?? "Request failed");
                return decision.Proposal;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Approve a proposal")
                .WithDescription("Executes the proposed tool in-process through the same MCP tool handler and returns the proposal (Executed or Failed). 409 when it is no longer pending.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("pid", "Proposal ID (aap_ prefix)"))
                .WithResponse(200, OpenApiJson.For<AskActionProposal>("The decided proposal"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/ask/threads/{id}/proposals/{pid}/reject", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskProposalDecision decision = await actions.RejectAsync(ctx, req.Parameters["id"], req.Parameters["pid"]).ConfigureAwait(false);
                if (decision.StatusCode != 200) return Error(req, decision.StatusCode, ToResult(decision.StatusCode), decision.Message ?? "Request failed");
                return decision.Proposal;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Reject a proposal")
                .WithDescription("Marks the proposal Rejected; it never executes. 409 when it is no longer pending.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("pid", "Proposal ID (aap_ prefix)"))
                .WithResponse(200, OpenApiJson.For<AskActionProposal>("The rejected proposal"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/ask/threads/{id}/work/{workId}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                AskWorkSnapshot? snapshot = await threads.GetWorkSnapshotAsync(ctx, req.Parameters["id"], req.Parameters["workId"]).ConfigureAwait(false);
                if (snapshot == null) return Error(req, 404, ApiResultEnum.NotFound, "Tracked work not found");
                return snapshot;
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Read a work snapshot")
                .WithDescription("Builds the current snapshot (work card data) of one tracked item of the thread.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("workId", "Tracked work ID (atw_ prefix)"))
                .WithResponse(200, OpenApiJson.For<AskWorkSnapshot>("The snapshot"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/ask/quick-actions", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                return AskQuickActionCatalog.All().Where(a => actions.HasTool(a.ToolName)).ToList();
            },
            api => api
                .WithTag("Ask")
                .WithSummary("List quick actions")
                .WithDescription("Returns the built-in quick actions with their MCP tool and argument schema.")
                .WithResponse(200, OpenApiJson.For<List<AskQuickAction>>("Quick actions"))
                .WithSecurity("ApiKey"));
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

        private static ApiErrorResponse Error(ApiRequest req, int status, ApiResultEnum result, string message)
        {
            req.Http.Response.StatusCode = status;
            return new ApiErrorResponse { Error = result, Message = message };
        }

        private static ApiResultEnum ToResult(int status)
        {
            if (status == 401) return ApiResultEnum.NotAuthorized;
            if (status == 403) return ApiResultEnum.Forbidden;
            if (status == 404) return ApiResultEnum.NotFound;
            if (status == 409) return ApiResultEnum.Conflict;
            if (status >= 500) return ApiResultEnum.InternalError;
            return ApiResultEnum.BadRequest;
        }

        private static T? Body<T>(ApiRequest req) where T : class
        {
            string body = req.Http.Request.DataAsString;
            if (String.IsNullOrWhiteSpace(body)) return null;
            return JsonSerializer.Deserialize<T>(body, _BodyJsonOptions);
        }

        private static AskThreadUpdateRequest ParseUpdate(string? body)
        {
            if (String.IsNullOrWhiteSpace(body)) return new AskThreadUpdateRequest();
            AskThreadUpdateRequest request = JsonSerializer.Deserialize<AskThreadUpdateRequest>(body, _BodyJsonOptions) ?? new AskThreadUpdateRequest();

            // Distinguish an explicit "CaptainId": null (clear the captain) from an absent field (keep it).
            if (JsonNode.Parse(body) is JsonObject obj)
                request.CaptainIdSpecified = obj.Any(kvp => String.Equals(kvp.Key, "CaptainId", StringComparison.OrdinalIgnoreCase));
            return request;
        }

        #endregion
    }
}
