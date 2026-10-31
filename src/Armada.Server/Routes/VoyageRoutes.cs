namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Server;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Server.WebSocket;
    using SyslogLogging;

    /// <summary>
    /// REST API routes for voyage management.
    /// </summary>
    public class VoyageRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly IAdmiralService _admiral;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly ArmadaWebSocketHub? _webSocketHub;
        private readonly LoggingModule _logging;
        private readonly ObjectiveService _objectives;
        private readonly IMissionService? _missionService;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral coordination service.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="webSocketHub">WebSocket hub for real-time notifications.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="objectives">Objective linkage service.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        /// <param name="missionService">Mission service, used to explain why Pending missions wait (optional).</param>
        public VoyageRoutes(
            DatabaseDriver database,
            IAdmiralService admiral,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            ArmadaWebSocketHub? webSocketHub,
            LoggingModule logging,
            ObjectiveService objectives,
            JsonSerializerOptions jsonOptions,
            IMissionService? missionService = null)
        {
            _missionService = missionService;
            _database = database;
            _admiral = admiral;
            _emitEvent = emitEvent;
            _webSocketHub = webSocketHub;
            _logging = logging;
            _objectives = objectives;
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            string _Header = "[ArmadaServer] ";

            // Voyages
            app.Get("/api/v1/voyages", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Voyage> result = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(ctx, query, q => _database.Voyages.EnumerateAsync(q), (t, q) => _database.Voyages.EnumerateAsync(t, q), (t, u, q) => _database.Voyages.EnumerateAsync(t, u, q)).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("List all voyages")
                .WithDescription("Returns all voyages, optionally filtered by status (Active, Complete, Cancelled).")
                .WithParameter(OpenApiParameterMetadata.Query("status", "Filter by voyage status", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Voyage>>("Paginated voyage list"))
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/voyages/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = JsonSerializer.Deserialize<EnumerationQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Voyage> result = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(ctx, query, q => _database.Voyages.EnumerateAsync(q), (t, q) => _database.Voyages.EnumerateAsync(t, q), (t, u, q) => _database.Voyages.EnumerateAsync(t, u, q)).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Enumerate voyages")
                .WithDescription("Paginated enumeration of voyages with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Voyage>>("Paginated voyage list"))
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/voyages/progress/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = EnumerationRequestReader.Read(req, _jsonOptions);
                try
                {
                    return await VoyageProgressReader.EnumerateAsync(_database, ctx, query).ConfigureAwait(false);
                }
                catch (ArgumentException ex) when (!(ex is CollectionLimitExceededException))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Enumerate voyage progress")
                .WithDescription("One page of voyages with mission counts (VoyageProgress), paged in the database. With no status filter the view is the active voyages: InProgress first, then Open, newest first within each (the voyages the status snapshot counts in ActiveVoyages). A status filter selects one voyage status. Paging and filters come from the JSON body (EnumerationQuery) or the querystring (pageNumber, pageSize 1 to 1000, default 10, status, order). The missions of a voyage are read with POST /api/v1/missions/enumerate and voyageId.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<VoyageProgress>>("One page of voyage progress"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post<VoyageRequest>("/api/v1/voyages", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                VoyageRequest voyageReq = JsonSerializer.Deserialize<VoyageRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as VoyageRequest.");
                try
                {
                    voyageReq.ApprovalOptions?.ValidateFor(ctx);
                    CollectionLimits.RequireRequestItems(voyageReq.Missions, "Missions");
                    CollectionLimits.RequireConfigItems(voyageReq.SelectedPlaybooks, "SelectedPlaybooks");
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex)) { return RouteErrorMapper.ToResponse(req, ex); }
                List<MissionDescription> missions = new List<MissionDescription>();
                if (voyageReq.Missions != null)
                {
                    foreach (MissionRequest m in voyageReq.Missions)
                    {
                        missions.Add(new MissionDescription(m.Title, m.Description) { Tier = m.Tier, RequestedCaptainId = m.RequestedCaptainId });
                    }
                }

                // Shared validation: objective existence, pipeline-by-name resolution, and bare-voyage
                // detection all live in one place so REST and MCP accept and reject the same inputs.
                DispatchValidationResult validation = await _admiral.ValidateDispatchAsync(
                    voyageReq.ObjectiveId, voyageReq.PipelineId, voyageReq.Pipeline, voyageReq.VesselId, missions.Count, allowBareVoyage: true).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    bool notFound = validation.Error == DispatchValidationErrorEnum.ObjectiveNotFound
                        || validation.Error == DispatchValidationErrorEnum.PipelineNotFound;
                    req.Http.Response.StatusCode = notFound ? 404 : 400;
                    return new ApiErrorResponse { Error = notFound ? ApiResultEnum.NotFound : ApiResultEnum.BadRequest, Message = validation.Message ?? "Invalid dispatch request" };
                }

                string? pipelineId = validation.ResolvedPipelineId;

                Voyage voyage;
                if (validation.IsBareVoyage)
                {
                    // Bare voyage creation (missions added separately)
                    voyage = new Voyage(voyageReq.Title, voyageReq.Description);
                    voyage.TenantId = ctx.TenantId;
                    voyage.UserId = ctx.UserId;
                    voyage.LandingMode = voyageReq.LandingMode;
                    voyage.ApprovalOptions = voyageReq.ApprovalOptions?.Snapshot();
                    voyage.IsEvaluation = voyageReq.IsEvaluation;
                    voyage = await _database.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                    voyage.SelectedPlaybooks = voyageReq.SelectedPlaybooks ?? new List<SelectedPlaybook>();
                    if (voyage.SelectedPlaybooks.Count > 0)
                    {
                        PlaybookService playbookService = new PlaybookService(_database, _logging);
                        await playbookService.ResolveSelectionsAsync(ctx.TenantId!, voyage.SelectedPlaybooks).ConfigureAwait(false);
                        await _database.Playbooks.SetVoyageSelectionsAsync(voyage.Id, voyage.SelectedPlaybooks).ConfigureAwait(false);
                    }
                    _logging.Info(_Header + "created bare voyage " + voyage.Id + ": " + voyageReq.Title);
                }
                else
                {
                    // Serialize the per-persona captain overrides and pass them in so they are persisted on the
                    // voyage BEFORE any mission is created and assigned -- otherwise the first stage's inline
                    // assignment runs before the overrides exist and auto-assigns an arbitrary idle captain.
                    string? overridesJson = (voyageReq.CaptainAssignments != null && voyageReq.CaptainAssignments.Count > 0)
                        ? MissionService.SerializeCaptainOverrides(voyageReq.CaptainAssignments)
                        : null;

                    voyage = await _admiral.DispatchVoyageAsync(
                        voyageReq.Title,
                        voyageReq.Description,
                        voyageReq.VesselId,
                        missions,
                        pipelineId,
                        voyageReq.SelectedPlaybooks,
                        overridesJson,
                        voyageReq.LandingMode, approvalOptions: voyageReq.ApprovalOptions, isEvaluation: voyageReq.IsEvaluation).ConfigureAwait(false);
                }

                if (!String.IsNullOrWhiteSpace(voyageReq.ObjectiveId))
                {
                    await _objectives.LinkVoyageAsync(ctx, voyageReq.ObjectiveId, voyage.Id).ConfigureAwait(false);
                }

                // For a bare voyage (missions added separately), there was no inline dispatch, so persist the
                // overrides now; they will apply when missions are added later.
                if (validation.IsBareVoyage && voyageReq.CaptainAssignments != null && voyageReq.CaptainAssignments.Count > 0)
                {
                    voyage.CaptainOverridesJson = MissionService.SerializeCaptainOverrides(voyageReq.CaptainAssignments);
                    voyage = await _database.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                }

                req.Http.Response.StatusCode = 201;
                return voyage;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Create a voyage")
                .WithDescription("Creates a new voyage with optional missions. Missions are automatically dispatched to the target vessel.")
                .WithRequestBody(OpenApiJson.BodyFor<VoyageRequest>("Voyage with missions", true))
                .WithResponse(201, OpenApiJson.For<Voyage>("Created voyage"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/voyages/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Voyage? voyage = ctx.IsAdmin
                    ? await _database.Voyages.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Voyages.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Voyages.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (voyage == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" }; }
                voyage.SelectedPlaybooks = await _database.Playbooks.GetVoyageSelectionsAsync(id).ConfigureAwait(false);
                return (object)await VoyageProgressReader.ReadAsync(_database, voyage).ConfigureAwait(false);
            },
            api => api
                .WithResponse(200, OpenApiJson.For<VoyageProgress>("The voyage with mission counts"))
                .WithTag("Voyages")
                .WithSummary("Get a voyage")
                .WithDescription("Returns a voyage with counts of its missions (VoyageProgress: TotalMissions, CompletedMissions, FailedMissions, InProgressMissions, MissionCountsByStatus, VesselId, VesselCount). The missions are not embedded: read them one page at a time with GET /api/v1/missions?voyageId= or POST /api/v1/missions/enumerate with VoyageId; a Pending mission's AssignmentBlocker comes from GET /api/v1/missions/{id}.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/voyages/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Voyage? voyage = ctx.IsAdmin
                    ? await _database.Voyages.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Voyages.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Voyages.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (voyage == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" }; }
                VoyageCancelResult cancel = await VoyageCancellation.CancelAsync(_database, _admiral, voyage, ctx.IsAdmin ? null : ctx.TenantId).ConfigureAwait(false);
                // Broadcast voyage and mission cancellations for dashboard toast notifications
                if (_webSocketHub != null)
                {
                    _webSocketHub.BroadcastVoyageChange(cancel.Voyage, VoyageStatusEnum.Cancelled.ToString());
                    foreach (Mission cm in cancel.Cancelled)
                    {
                        _webSocketHub.BroadcastMissionChange(cm, MissionStatusEnum.Cancelled.ToString());
                    }
                }

                return (object)cancel;
            },
            api => api
                .WithResponse(200, OpenApiJson.For<VoyageCancelResult>("The cancelled voyage with counts"))
                .WithTag("Voyages")
                .WithSummary("Cancel a voyage")
                .WithDescription("Cancels a voyage and its Pending, Assigned, and Blocked missions. Returns the voyage with CancelledMissions and NotCancelledMissions counts (and FirstNotCancelledReason); which missions remain is read through the missions enumeration filtered by voyageId and status.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/voyages/{id}/purge", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Voyage? voyage = ctx.IsAdmin
                    ? await _database.Voyages.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Voyages.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Voyages.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (voyage == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" }; }

                // Block deletion of active voyages
                if (voyage.Status == VoyageStatusEnum.Open || voyage.Status == VoyageStatusEnum.InProgress)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = "Cannot delete voyage while status is " + voyage.Status + ". Cancel the voyage first." };
                }

                List<Mission> missions = ctx.IsAdmin
                    ? await _database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false)
                    : await _database.Missions.EnumerateByVoyageAsync(ctx.TenantId!, id).ConfigureAwait(false);

                // Block deletion if any missions are actively assigned or in progress
                List<Mission> activeMissions = missions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress).ToList();
                if (activeMissions.Count > 0)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = "Cannot delete voyage with " + activeMissions.Count + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };
                }

                // Cascade delete all missions in this voyage, and the telemetry events that referenced them.
                foreach (Mission m in missions)
                {
                    await _database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                    await CascadeCleanup.RemoveEventsForMissionAsync(_database, m.Id).ConfigureAwait(false);
                }

                // Delete the voyage itself
                await _database.Voyages.DeleteAsync(id).ConfigureAwait(false);

                // Remove telemetry events that referenced this voyage so they do not dangle.
                await CascadeCleanup.RemoveEventsForVoyageAsync(_database, id).ConfigureAwait(false);

                await _emitEvent("voyage.deleted", "Voyage " + id + " permanently deleted with " + missions.Count + " missions",
                    "voyage", id, null, null, null, null).ConfigureAwait(false);

                return (object)new { Status = "deleted", VoyageId = id, MissionsDeleted = missions.Count };
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Permanently delete a voyage")
                .WithDescription("Permanently deletes a voyage and all its associated missions from the database. This cannot be undone. Blocked if voyage is Open/InProgress or has active missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(200, OpenApiJson.For<object>("Deleted voyage and missions"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<object>("Voyage cannot be deleted while active"))
                .WithSecurity("ApiKey"));

            app.Post<DeleteMultipleRequest>("/api/v1/voyages/delete/multiple", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                DeleteMultipleRequest? body = JsonSerializer.Deserialize<DeleteMultipleRequest>(req.Http.Request.DataAsString, _jsonOptions);
                if (body == null || body.Ids == null || body.Ids.Count == 0)
                    { req.Http.Response.StatusCode = 400; return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Ids is required and must not be empty" }; }
                try { body.RequireWithinLimit(); }
                catch (CollectionLimitExceededException limit) { return RouteErrorMapper.LimitExceeded(req, limit); }

                DeleteMultipleResult result = new DeleteMultipleResult();
                foreach (string id in body.Ids)
                {
                    if (String.IsNullOrEmpty(id))
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                        continue;
                    }
                    Voyage? voyage = ctx.IsAdmin
                        ? await _database.Voyages.ReadAsync(id).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Voyages.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                            : await _database.Voyages.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    if (voyage == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    if (voyage.Status == VoyageStatusEnum.Open || voyage.Status == VoyageStatusEnum.InProgress)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete voyage while status is " + voyage.Status + ". Cancel the voyage first."));
                        continue;
                    }
                    List<Mission> missions = ctx.IsAdmin
                        ? await _database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false)
                        : await _database.Missions.EnumerateByVoyageAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    List<Mission> activeMissions = missions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress).ToList();
                    if (activeMissions.Count > 0)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete voyage with " + activeMissions.Count + " active mission(s). Cancel or complete them first."));
                        continue;
                    }
                    foreach (Mission m in missions)
                    {
                        await _database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                    }
                    await _database.Voyages.DeleteAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await _emitEvent("voyage.batch_deleted", "Batch deleted " + result.Deleted + " voyages",
                    "voyage", null, null, null, null, null).ConfigureAwait(false);

                result.ResolveStatus();
                return (object)result;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Batch delete multiple voyages")
                .WithDescription("Permanently deletes multiple voyages and their associated missions from the database by ID (at most 1000 ids; more is a 400 with Data.Code CollectionLimitExceeded). Voyages that are Open/InProgress or have active missions are skipped. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiJson.BodyFor<DeleteMultipleRequest>("List of voyage IDs to delete"))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete result summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));
        }
    }
}
