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
            app.Post("/api/v1/planning-sessions/enumerate", async (ApiRequest req) =>
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
                try
                {
                    return await EnumerateSessionsAsync(ctx, query).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
                }
            },
            api => api
                .WithTag("Planning Sessions")
                .WithSummary("Enumerate planning sessions")
                .WithDescription("One page of the planning sessions visible to the caller, paged in the database: most recently updated first (CreatedDescending, the default) or least recently updated first (CreatedAscending), id as the tie-break. Filters: status, captainId, vesselId, fleetId, createdAfter, createdBefore; a global admin may narrow with tenantId and userId, a tenant admin with userId. Paging and filters come from the JSON body (EnumerationQuery) or the querystring (pageNumber, pageSize 1 to 1000, default 10).")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<PlanningSession>>("One page"))
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
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
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
                .WithResponse(201, OpenApiJson.For<PlanningSessionDetail>("Created planning session detail (Session, MessageCount, Captain, Vessel, LinkedObjectiveCount)"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported for the captain's runtime (Error NotImplemented)"))
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
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithResponse(200, OpenApiJson.For<PlanningSessionDetail>("Planning session detail (Session, MessageCount, Captain, Vessel, LinkedObjectiveCount)"))
                .WithTag("Planning")
                .WithSummary("Get a planning session")
                .WithDescription("Returns a planning session with its captain and vessel, the transcript's MessageCount, and LinkedObjectiveCount. The transcript itself is read with POST /api/v1/planning-sessions/{id}/messages/enumerate.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported for the captain's runtime (Error NotImplemented)"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/planning-sessions/{id}/messages/enumerate", async (ApiRequest req) =>
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
                try
                {
                    PlanningSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Planning session not found" };
                    }

                    return await _database.PlanningSessionMessages.EnumeratePageBySessionAsync(session.Id, query).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
                }
            },
            api => api
                .WithTag("Planning")
                .WithSummary("Enumerate a planning session's transcript")
                .WithDescription("One page of the session's messages, paged in the database: newest first (CreatedDescending, the default) or oldest first (CreatedAscending), by sequence with the id as the tie-break. TotalRecords is the transcript length. Paging comes from the JSON body (EnumerationQuery) or the querystring (pageNumber, pageSize 1 to 1000, default 10, order).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging and order", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<PlanningSessionMessage>>("One page of messages"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported by the database provider (Error NotImplemented)"))
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
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithResponse(200, OpenApiJson.For<PlanningSessionDetail>("Planning session detail (Session, MessageCount, Captain, Vessel, LinkedObjectiveCount)"))
                .WithTag("Planning")
                .WithSummary("Send a planning message")
                .WithDescription("Appends a user message to the planning transcript and launches the next planning turn.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<PlanningSessionMessageRequest>("Planning message request", true))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported for the captain's runtime (Error NotImplemented)"))
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
                    await PlanningDispatchLinker.LinkAsync(_objectives, ctx, session.Id, voyage.Id).ConfigureAwait(false);
                    return voyage;
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Planning")
                .WithSummary("Dispatch from a planning session")
                .WithDescription("Creates a voyage from selected or inferred planning output and links it to every objective linked to the session. When linking would push an objective's id lists over MaxLinkedIds the call fails with 400 CollectionLimitExceeded, naming the voyage that was dispatched.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<PlanningSessionDispatchRequest>("Planning dispatch request", false))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported for the captain's runtime (Error NotImplemented)"))
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
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Planning")
                .WithSummary("Summarize planning output into a dispatch draft")
                .WithDescription("Generates a server-owned dispatch draft from selected or inferred planning output without launching the voyage yet.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<PlanningSessionSummaryRequest>("Planning summary request", false))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported for the captain's runtime (Error NotImplemented)"))
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
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithResponse(200, OpenApiJson.For<PlanningSessionDetail>("Planning session detail (Session, MessageCount, Captain, Vessel, LinkedObjectiveCount)"))
                .WithTag("Planning")
                .WithSummary("Stop a planning session")
                .WithDescription("Stops an active planning session, releases the captain, and reclaims the planning dock.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported for the captain's runtime (Error NotImplemented)"))
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
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithResponse(200, OpenApiJson.For<PlanningSessionDetail>("Planning session detail (Session, MessageCount, Captain, Vessel, LinkedObjectiveCount)"))
                .WithTag("Planning")
                .WithSummary("Stop the current planning turn")
                .WithDescription("Aborts the in-flight planning turn (cancelling the captain runtime) while keeping the session active so the user can keep chatting.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Planning session ID (psn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported for the captain's runtime (Error NotImplemented)"))
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
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, ex.Message);
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
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Not supported for the captain's runtime (Error NotImplemented)"))
                .WithSecurity("ApiKey"));
        }

        private async Task<EnumerationResult<PlanningSession>> EnumerateSessionsAsync(AuthContext ctx, EnumerationQuery query)
        {
            if (ctx.IsAdmin)
                return await _database.PlanningSessions.EnumeratePageAsync(query.TenantId, query.UserId, query).ConfigureAwait(false);
            if (ctx.IsTenantAdmin)
                return await _database.PlanningSessions.EnumeratePageAsync(ctx.TenantId!, query.UserId, query).ConfigureAwait(false);
            return await _database.PlanningSessions.EnumeratePageAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
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

        private async Task<PlanningSessionDetail> BuildDetailResponseAsync(PlanningSession session, AuthContext ctx)
        {
            PlanningSession? refreshed = await ReadSessionForContextAsync(ctx, session.Id).ConfigureAwait(false);
            if (refreshed == null)
                throw new KeyNotFoundException("Planning session not found: " + session.Id);

            PlanningSessionDetail detail = new PlanningSessionDetail();
            detail.Session = refreshed;
            detail.MessageCount = await _database.PlanningSessionMessages.CountBySessionAsync(refreshed.Id).ConfigureAwait(false);
            detail.Captain = await ReadCaptainForContextAsync(ctx, refreshed.CaptainId).ConfigureAwait(false);
            detail.Vessel = await ReadVesselForContextAsync(ctx, refreshed.VesselId).ConfigureAwait(false);
            detail.LinkedObjectiveCount = await _objectives.CountByPlanningSessionAsync(ctx, refreshed.Id).ConfigureAwait(false);
            return detail;
        }
    }
}
