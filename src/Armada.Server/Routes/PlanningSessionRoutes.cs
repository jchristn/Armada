namespace Armada.Server.Routes
{
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for planning session lifecycle and transcript-to-dispatch flow.
    /// </summary>
    public class PlanningSessionRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly PlanningSessionCoordinator _planningSessions;
        private readonly ObjectiveService _objectives;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionRoutes(
            DatabaseDriver database,
            PlanningSessionCoordinator planningSessions,
            ObjectiveService objectives,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _planningSessions = planningSessions ?? throw new ArgumentNullException(nameof(planningSessions));
            _objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
            _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        public void Register(
            Webserver app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/planning-sessions", async (ApiRequest req) =>
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

                try
                {
                    List<PlanningSession> sessions = await EnumerateSessionsAsync(ctx).ConfigureAwait(false);
                    return sessions.OrderByDescending(s => s.LastUpdateUtc).ToList();
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("List planning sessions")
                .WithDescription("Returns planning sessions visible to the authenticated user.")
                .WithResponse(200, OpenApiJson.For<List<PlanningSession>>("Planning sessions"))
                .WithSecurity("ApiKey"));

            app.Post<PlanningSessionCreateRequest>("/api/v1/planning-sessions", async (ApiRequest req) =>
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

                PlanningSessionCreateRequest request = JsonSerializer.Deserialize<PlanningSessionCreateRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as PlanningSessionCreateRequest.");

                if (String.IsNullOrWhiteSpace(request.CaptainId) || String.IsNullOrWhiteSpace(request.VesselId))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "CaptainId and VesselId are required." };
                }

                try
                {
                    Captain? captain = await ReadCaptainForContextAsync(ctx, request.CaptainId).ConfigureAwait(false);
                    if (captain == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" };
                    }

                    Vessel? vessel = await ReadVesselForContextAsync(ctx, request.VesselId).ConfigureAwait(false);
                    if (vessel == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                    }

                    if (!String.IsNullOrWhiteSpace(request.ObjectiveId))
                    {
                        Objective? objective = await _objectives.ReadAsync(ctx, request.ObjectiveId).ConfigureAwait(false);
                        if (objective == null)
                        {
                            req.Http.Response.StatusCode = 404;
                            return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective not found" };
                        }
                    }

                    PlanningSession session = await _planningSessions
                        .CreateAsync(ctx.TenantId, ctx.UserId, captain, vessel, request)
                        .ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(request.ObjectiveId))
                    {
                        await _objectives.LinkPlanningSessionAsync(ctx, request.ObjectiveId, session.Id).ConfigureAwait(false);
                    }

                    req.Http.Response.StatusCode = 201;
                    return await BuildDetailResponseAsync(session, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Create a planning session")
                .WithDescription("Creates a planning session, reserves a captain, and provisions a planning dock.")
                .WithRequestBody(OpenApiJson.BodyFor<PlanningSessionCreateRequest>("Planning session request", true))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/planning-sessions/{id}", async (ApiRequest req) =>
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

                try
                {
                    PlanningSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Planning session not found" };
                    }

                    return await BuildDetailResponseAsync(session, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Get a planning session")
                .WithDescription("Returns a planning session, its transcript, and related captain/vessel context.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithSecurity("ApiKey"));

            app.Post<PlanningSessionMessageRequest>("/api/v1/planning-sessions/{id}/messages", async (ApiRequest req) =>
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

                PlanningSessionMessageRequest request = JsonSerializer.Deserialize<PlanningSessionMessageRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as PlanningSessionMessageRequest.");
                if (String.IsNullOrWhiteSpace(request.Content))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Content is required." };
                }

                try
                {
                    PlanningSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Planning session not found" };
                    }

                    await _planningSessions.SendMessageAsync(session, request.Content, request.ShowThinking, request.Stream).ConfigureAwait(false);
                    return await BuildDetailResponseAsync(session, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Send a planning message")
                .WithDescription("Appends a user message to the planning transcript and launches the next planning turn.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<PlanningSessionMessageRequest>("Planning message request", true))
                .WithSecurity("ApiKey"));

            app.Post<PlanningSessionDispatchRequest>("/api/v1/planning-sessions/{id}/dispatch", async (ApiRequest req) =>
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

                PlanningSessionDispatchRequest request = JsonSerializer.Deserialize<PlanningSessionDispatchRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? new PlanningSessionDispatchRequest();

                try
                {
                    PlanningSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Planning session not found" };
                    }

                    Voyage voyage = await _planningSessions.DispatchAsync(session, request).ConfigureAwait(false);
                    List<Objective> linkedObjectives = await _objectives.EnumerateByPlanningSessionAsync(ctx, session.Id).ConfigureAwait(false);
                    foreach (Objective objective in linkedObjectives)
                    {
                        await _objectives.LinkVoyageAsync(ctx, objective.Id, voyage.Id).ConfigureAwait(false);
                    }
                    return voyage;
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Dispatch from a planning session")
                .WithDescription("Creates a voyage from selected or inferred planning output.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<PlanningSessionDispatchRequest>("Planning dispatch request", false))
                .WithSecurity("ApiKey"));

            app.Post<PlanningSessionSummaryRequest>("/api/v1/planning-sessions/{id}/summarize", async (ApiRequest req) =>
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

                PlanningSessionSummaryRequest request = JsonSerializer.Deserialize<PlanningSessionSummaryRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? new PlanningSessionSummaryRequest();

                try
                {
                    PlanningSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Planning session not found" };
                    }

                    PlanningSessionSummaryResponse draft = await _planningSessions.SummarizeAsync(session, request).ConfigureAwait(false);
                    return draft;
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Summarize planning output into a dispatch draft")
                .WithDescription("Generates a server-owned dispatch draft from selected or inferred planning output without launching the voyage yet.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<PlanningSessionSummaryRequest>("Planning summary request", false))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/planning-sessions/{id}/stop", async (ApiRequest req) =>
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

                try
                {
                    PlanningSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Planning session not found" };
                    }

                    PlanningSession stopping = await _planningSessions.RequestStopAsync(session).ConfigureAwait(false);
                    return await BuildDetailResponseAsync(stopping, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Stop a planning session")
                .WithDescription("Stops an active planning session, releases the captain, and reclaims the planning dock.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/planning-sessions/{id}/stop-turn", async (ApiRequest req) =>
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

                try
                {
                    PlanningSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Planning session not found" };
                    }

                    PlanningSession aborted = await _planningSessions.AbortTurnAsync(session).ConfigureAwait(false);
                    return await BuildDetailResponseAsync(aborted, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Stop the current planning turn")
                .WithDescription("Aborts the in-flight planning turn (cancelling the captain runtime) while keeping the session active so the user can keep chatting.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/planning-sessions/{id}", async (ApiRequest req) =>
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

                try
                {
                    PlanningSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Planning session not found" };
                    }

                    await _planningSessions.DeleteAsync(session).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null!;
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Delete a planning session")
                .WithDescription("Deletes a planning session and its transcript. Active sessions are stopped first.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithSecurity("ApiKey"));
        }

        private async Task<List<PlanningSession>> EnumerateSessionsAsync(AuthContext ctx)
        {
            if (ctx.IsAdmin)
                return await _database.PlanningSessions.EnumerateAsync().ConfigureAwait(false);
            if (ctx.IsTenantAdmin)
                return await _database.PlanningSessions.EnumerateAsync(ctx.TenantId!).ConfigureAwait(false);
            return await _database.PlanningSessions.EnumerateAsync(ctx.TenantId!, ctx.UserId!).ConfigureAwait(false);
        }

        private async Task<PlanningSession?> ReadSessionForContextAsync(AuthContext ctx, string id)
        {
            if (ctx.IsAdmin)
                return await _database.PlanningSessions.ReadAsync(id).ConfigureAwait(false);
            if (ctx.IsTenantAdmin)
                return await _database.PlanningSessions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
            return await _database.PlanningSessions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
        }

        private async Task<Captain?> ReadCaptainForContextAsync(AuthContext ctx, string id)
        {
            if (ctx.IsAdmin)
                return await _database.Captains.ReadAsync(id).ConfigureAwait(false);
            if (ctx.IsTenantAdmin)
                return await _database.Captains.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
            return await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
        }

        private async Task<Vessel?> ReadVesselForContextAsync(AuthContext ctx, string id)
        {
            if (ctx.IsAdmin)
                return await _database.Vessels.ReadAsync(id).ConfigureAwait(false);
            if (ctx.IsTenantAdmin)
                return await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
            return await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
        }

        private async Task<object> BuildDetailResponseAsync(PlanningSession session, AuthContext ctx)
        {
            PlanningSession? refreshed = await ReadSessionForContextAsync(ctx, session.Id).ConfigureAwait(false);
            if (refreshed == null)
                throw new KeyNotFoundException("Planning session not found: " + session.Id);

            List<PlanningSessionMessage> messages = await _database.PlanningSessionMessages
                .EnumerateBySessionAsync(refreshed.Id)
                .ConfigureAwait(false);

            Captain? captain = await ReadCaptainForContextAsync(ctx, refreshed.CaptainId).ConfigureAwait(false);
            Vessel? vessel = await ReadVesselForContextAsync(ctx, refreshed.VesselId).ConfigureAwait(false);

            return new
            {
                Session = refreshed,
                Messages = messages.OrderBy(m => m.Sequence).ToList(),
                Captain = captain,
                Vessel = vessel
            };
        }
    }
}
