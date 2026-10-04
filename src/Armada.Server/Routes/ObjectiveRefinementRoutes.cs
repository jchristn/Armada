namespace Armada.Server.Routes
{
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for captain-backed objective refinement sessions.
    /// </summary>
    public class ObjectiveRefinementRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly ObjectiveRefinementCoordinator _refinementSessions;
        private readonly ObjectiveService _objectives;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ObjectiveRefinementRoutes(
            DatabaseDriver database,
            ObjectiveRefinementCoordinator refinementSessions,
            ObjectiveService objectives,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _refinementSessions = refinementSessions ?? throw new ArgumentNullException(nameof(refinementSessions));
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
            app.Get("/api/v1/objectives/{id}/refinement-sessions", async (ApiRequest req) =>
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

                Objective? objective = await _objectives.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (objective == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective not found" };
                }

                List<ObjectiveRefinementSession> sessions = await EnumerateSessionsByObjectiveAsync(ctx, objective.Id).ConfigureAwait(false);
                return sessions.OrderByDescending(s => s.LastUpdateUtc).ToList();
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("List objective refinement sessions")
                .WithDescription("Returns captain-backed refinement sessions linked to the specified objective.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective ID (obj_ prefix)"))
                .WithResponse(200, OpenApiJson.For<List<ObjectiveRefinementSession>>("Objective refinement sessions"))
                .WithSecurity("ApiKey"));

            app.Post<ObjectiveRefinementSessionCreateRequest>("/api/v1/objectives/{id}/refinement-sessions", async (ApiRequest req) =>
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

                Objective? objective = await _objectives.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (objective == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective not found" };
                }

                ObjectiveRefinementSessionCreateRequest request = JsonSerializer.Deserialize<ObjectiveRefinementSessionCreateRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ObjectiveRefinementSessionCreateRequest.");
                if (String.IsNullOrWhiteSpace(request.CaptainId))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "CaptainId is required." };
                }

                try
                {
                    Captain? captain = await ReadCaptainForContextAsync(ctx, request.CaptainId).ConfigureAwait(false);
                    if (captain == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" };
                    }

                    Vessel? vessel = null;
                    string? vesselId = !String.IsNullOrWhiteSpace(request.VesselId)
                        ? request.VesselId
                        : objective.VesselIds.FirstOrDefault();
                    if (!String.IsNullOrWhiteSpace(vesselId))
                    {
                        vessel = await ReadVesselForContextAsync(ctx, vesselId).ConfigureAwait(false);
                        if (vessel == null)
                        {
                            req.Http.Response.StatusCode = 404;
                            return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                        }
                    }

                    ObjectiveRefinementSession session = await _refinementSessions
                        .CreateAsync(ctx.TenantId, ctx.UserId, objective, captain, vessel, request)
                        .ConfigureAwait(false);
                    await _objectives.LinkRefinementSessionAsync(ctx, objective.Id, session.Id).ConfigureAwait(false);

                    req.Http.Response.StatusCode = 201;
                    return await BuildDetailResponseAsync(session, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Create an objective refinement session")
                .WithDescription("Creates a refinement session for an objective using an explicitly selected captain.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective ID (obj_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveRefinementSessionCreateRequest>("Objective refinement session request", true))
                .WithResponse(201, OpenApiJson.For<ObjectiveRefinementSessionDetail>("Created objective refinement session detail"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/backlog/{id}/refinement-sessions", async (ApiRequest req) =>
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

                Objective? objective = await _objectives.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (objective == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Backlog item not found" };
                }

                List<ObjectiveRefinementSession> sessions = await EnumerateSessionsByObjectiveAsync(ctx, objective.Id).ConfigureAwait(false);
                return sessions.OrderByDescending(s => s.LastUpdateUtc).ToList();
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("List backlog refinement sessions")
                .WithDescription("Returns captain-backed refinement sessions linked to the specified backlog item.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Backlog item ID (obj_ prefix)"))
                .WithResponse(200, OpenApiJson.For<List<ObjectiveRefinementSession>>("Backlog refinement sessions"))
                .WithSecurity("ApiKey"));

            app.Post<ObjectiveRefinementSessionCreateRequest>("/api/v1/backlog/{id}/refinement-sessions", async (ApiRequest req) =>
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

                Objective? objective = await _objectives.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (objective == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Backlog item not found" };
                }

                ObjectiveRefinementSessionCreateRequest request = JsonSerializer.Deserialize<ObjectiveRefinementSessionCreateRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ObjectiveRefinementSessionCreateRequest.");
                if (String.IsNullOrWhiteSpace(request.CaptainId))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "CaptainId is required." };
                }

                try
                {
                    Captain? captain = await ReadCaptainForContextAsync(ctx, request.CaptainId).ConfigureAwait(false);
                    if (captain == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" };
                    }

                    Vessel? vessel = null;
                    string? vesselId = !String.IsNullOrWhiteSpace(request.VesselId)
                        ? request.VesselId
                        : objective.VesselIds.FirstOrDefault();
                    if (!String.IsNullOrWhiteSpace(vesselId))
                    {
                        vessel = await ReadVesselForContextAsync(ctx, vesselId).ConfigureAwait(false);
                        if (vessel == null)
                        {
                            req.Http.Response.StatusCode = 404;
                            return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                        }
                    }

                    ObjectiveRefinementSession session = await _refinementSessions
                        .CreateAsync(ctx.TenantId, ctx.UserId, objective, captain, vessel, request)
                        .ConfigureAwait(false);
                    await _objectives.LinkRefinementSessionAsync(ctx, objective.Id, session.Id).ConfigureAwait(false);

                    req.Http.Response.StatusCode = 201;
                    return await BuildDetailResponseAsync(session, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Create a backlog refinement session")
                .WithDescription("Creates a refinement session for a backlog item using an explicitly selected captain.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Backlog item ID (obj_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveRefinementSessionCreateRequest>("Backlog refinement session request", true))
                .WithResponse(201, OpenApiJson.For<ObjectiveRefinementSessionDetail>("Created backlog refinement session detail"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/objective-refinement-sessions/{id}", async (ApiRequest req) =>
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
                    ObjectiveRefinementSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective refinement session not found" };
                    }

                    return await BuildDetailResponseAsync(session, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Get an objective refinement session")
                .WithDescription("Returns an objective refinement session, its transcript, and linked captain/objective context.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective refinement session ID (ors_ prefix)"))
                .WithResponse(200, OpenApiJson.For<ObjectiveRefinementSessionDetail>("Objective refinement session detail"))
                .WithSecurity("ApiKey"));

            app.Post<ObjectiveRefinementMessageRequest>("/api/v1/objective-refinement-sessions/{id}/messages", async (ApiRequest req) =>
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

                ObjectiveRefinementMessageRequest request = JsonSerializer.Deserialize<ObjectiveRefinementMessageRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as ObjectiveRefinementMessageRequest.");
                if (String.IsNullOrWhiteSpace(request.Content))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Content is required." };
                }

                try
                {
                    ObjectiveRefinementSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective refinement session not found" };
                    }

                    await _refinementSessions.SendMessageAsync(session, request.Content).ConfigureAwait(false);
                    return await BuildDetailResponseAsync(session, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Send an objective refinement message")
                .WithDescription("Appends a user message to the refinement transcript and launches the next refinement turn.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective refinement session ID (ors_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveRefinementMessageRequest>("Objective refinement message request", true))
                .WithResponse(200, OpenApiJson.For<ObjectiveRefinementSessionDetail>("Updated objective refinement session detail"))
                .WithSecurity("ApiKey"));

            app.Post<ObjectiveRefinementSummaryRequest>("/api/v1/objective-refinement-sessions/{id}/summarize", async (ApiRequest req) =>
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

                ObjectiveRefinementSummaryRequest request = ReadOptionalBody<ObjectiveRefinementSummaryRequest>(req.Http.Request.DataAsString);

                try
                {
                    ObjectiveRefinementSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective refinement session not found" };
                    }

                    return await _refinementSessions.SummarizeAsync(session, request).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Summarize an objective refinement session")
                .WithDescription("Generates a structured objective summary from a selected or inferred assistant refinement message.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective refinement session ID (ors_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveRefinementSummaryRequest>("Objective refinement summary request", false))
                .WithResponse(200, OpenApiJson.For<ObjectiveRefinementSummaryResponse>("Objective refinement summary"))
                .WithSecurity("ApiKey"));

            app.Post<ObjectiveRefinementApplyRequest>("/api/v1/objective-refinement-sessions/{id}/apply", async (ApiRequest req) =>
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

                ObjectiveRefinementApplyRequest request = ReadOptionalBody<ObjectiveRefinementApplyRequest>(req.Http.Request.DataAsString);

                try
                {
                    ObjectiveRefinementSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective refinement session not found" };
                    }

                    Objective? objective = await _objectives.ReadAsync(ctx, session.ObjectiveId).ConfigureAwait(false);
                    if (objective == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective not found" };
                    }

                    (ObjectiveRefinementSummaryResponse Summary, Objective Objective) result = await _refinementSessions
                        .ApplyAsync(ctx, objective, session, request, _objectives)
                        .ConfigureAwait(false);

                    return new ObjectiveRefinementApplyResponse
                    {
                        Summary = result.Summary,
                        Objective = result.Objective
                    };
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Apply an objective refinement summary")
                .WithDescription("Summarizes the refinement transcript and applies the resulting summary back to the linked objective.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective refinement session ID (ors_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ObjectiveRefinementApplyRequest>("Objective refinement apply request", false))
                .WithResponse(200, OpenApiJson.For<ObjectiveRefinementApplyResponse>("Objective refinement apply response"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/objective-refinement-sessions/{id}/stop", async (ApiRequest req) =>
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
                    ObjectiveRefinementSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective refinement session not found" };
                    }

                    ObjectiveRefinementSession stopping = await _refinementSessions.RequestStopAsync(session).ConfigureAwait(false);
                    return await BuildDetailResponseAsync(stopping, ctx).ConfigureAwait(false);
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Stop an objective refinement session")
                .WithDescription("Stops an active refinement session and releases the selected captain.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective refinement session ID (ors_ prefix)"))
                .WithResponse(200, OpenApiJson.For<ObjectiveRefinementSessionDetail>("Stopped objective refinement session detail"))
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/objective-refinement-sessions/{id}", async (ApiRequest req) =>
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
                    ObjectiveRefinementSession? session = await ReadSessionForContextAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    if (session == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Objective refinement session not found" };
                    }

                    await _refinementSessions.DeleteAsync(session).ConfigureAwait(false);
                    await _objectives.UnlinkRefinementSessionAsync(ctx, session.ObjectiveId, session.Id).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null!;
                }
                catch (NotSupportedException ex)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Objectives")
                .WithSummary("Delete an objective refinement session")
                .WithDescription("Deletes an objective refinement session and its transcript. Active sessions are stopped first.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Objective refinement session ID (ors_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithSecurity("ApiKey"));
        }

        private async Task<List<ObjectiveRefinementSession>> EnumerateSessionsByObjectiveAsync(AuthContext ctx, string objectiveId)
        {
            List<ObjectiveRefinementSession> sessions = ctx.IsAdmin
                ? await _database.ObjectiveRefinementSessions.EnumerateAsync().ConfigureAwait(false)
                : ctx.IsTenantAdmin
                    ? await _database.ObjectiveRefinementSessions.EnumerateAsync(ctx.TenantId!).ConfigureAwait(false)
                    : await _database.ObjectiveRefinementSessions.EnumerateAsync(ctx.TenantId!, ctx.UserId!).ConfigureAwait(false);

            return sessions
                .Where(session => String.Equals(session.ObjectiveId, objectiveId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private async Task<ObjectiveRefinementSession?> ReadSessionForContextAsync(AuthContext ctx, string id)
        {
            if (ctx.IsAdmin)
                return await _database.ObjectiveRefinementSessions.ReadAsync(id).ConfigureAwait(false);
            if (ctx.IsTenantAdmin)
                return await _database.ObjectiveRefinementSessions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
            return await _database.ObjectiveRefinementSessions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
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

        private async Task<ObjectiveRefinementSessionDetail> BuildDetailResponseAsync(ObjectiveRefinementSession session, AuthContext ctx)
        {
            ObjectiveRefinementSession? refreshed = await ReadSessionForContextAsync(ctx, session.Id).ConfigureAwait(false);
            if (refreshed == null)
                throw new InvalidOperationException("Objective refinement session not found: " + session.Id);

            List<ObjectiveRefinementMessage> messages = await _database.ObjectiveRefinementMessages
                .EnumerateBySessionAsync(refreshed.Id)
                .ConfigureAwait(false);
            Captain? captain = await ReadCaptainForContextAsync(ctx, refreshed.CaptainId).ConfigureAwait(false);
            Vessel? vessel = !String.IsNullOrWhiteSpace(refreshed.VesselId)
                ? await ReadVesselForContextAsync(ctx, refreshed.VesselId!).ConfigureAwait(false)
                : null;
            Objective? objective = await _objectives.ReadAsync(ctx, refreshed.ObjectiveId).ConfigureAwait(false);

            return new ObjectiveRefinementSessionDetail
            {
                Session = refreshed,
                Messages = messages.OrderBy(message => message.Sequence).ToList(),
                Captain = captain,
                Vessel = vessel,
                Objective = objective
            };
        }

        private T ReadOptionalBody<T>(string? body)
            where T : new()
        {
            if (String.IsNullOrWhiteSpace(body))
                return new T();

                return JsonSerializer.Deserialize<T>(body, _jsonOptions) ?? new T();
        }
    }
}
