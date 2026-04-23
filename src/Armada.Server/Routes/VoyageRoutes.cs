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
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral coordination service.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="webSocketHub">WebSocket hub for real-time notifications.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public VoyageRoutes(
            DatabaseDriver database,
            IAdmiralService admiral,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            ArmadaWebSocketHub? webSocketHub,
            LoggingModule logging,
            JsonSerializerOptions jsonOptions)
        {
            _database = database;
            _admiral = admiral;
            _emitEvent = emitEvent;
            _webSocketHub = webSocketHub;
            _logging = logging;
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Voyage> result = ctx.IsAdmin
                    ? await _database.Voyages.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Voyages.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Voyages.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                EnumerationQuery query = JsonSerializer.Deserialize<EnumerationQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Voyage> result = ctx.IsAdmin
                    ? await _database.Voyages.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Voyages.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Voyages.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Enumerate voyages")
                .WithDescription("Paginated enumeration of voyages with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Post<VoyageRequest>("/api/v1/voyages", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                VoyageRequest voyageReq = JsonSerializer.Deserialize<VoyageRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as VoyageRequest.");
                List<MissionDescription> missions = new List<MissionDescription>();
                if (voyageReq.Missions != null)
                {
                    foreach (MissionRequest m in voyageReq.Missions)
                    {
                        missions.Add(new MissionDescription(m.Title, m.Description));
                    }
                }

                // Resolve pipeline: explicit ID > name lookup > null (falls through to vessel/fleet default)
                string? pipelineId = voyageReq.PipelineId;
                if (String.IsNullOrEmpty(pipelineId) && !String.IsNullOrEmpty(voyageReq.Pipeline))
                {
                    Pipeline? namedPipeline = await _database.Pipelines.ReadByNameAsync(voyageReq.Pipeline).ConfigureAwait(false);
                    if (namedPipeline != null) pipelineId = namedPipeline.Id;
                    else { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Pipeline not found: " + voyageReq.Pipeline }; }
                }

                Voyage voyage;
                if (String.IsNullOrEmpty(voyageReq.VesselId) || missions.Count == 0)
                {
                    // Bare voyage creation (missions added separately)
                    voyage = new Voyage(voyageReq.Title, voyageReq.Description);
                    voyage.TenantId = ctx.TenantId;
                    voyage.UserId = ctx.UserId;
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
                    voyage = await _admiral.DispatchVoyageAsync(
                        voyageReq.Title,
                        voyageReq.Description,
                        voyageReq.VesselId,
                        missions,
                        pipelineId,
                        voyageReq.SelectedPlaybooks).ConfigureAwait(false);
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
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/voyages/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Voyage? voyage = ctx.IsAdmin
                    ? await _database.Voyages.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Voyages.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Voyages.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (voyage == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" }; }
                voyage.SelectedPlaybooks = await _database.Playbooks.GetVoyageSelectionsAsync(id).ConfigureAwait(false);
                List<Mission> missions = ctx.IsAdmin
                    ? await _database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false)
                    : await _database.Missions.EnumerateByVoyageAsync(ctx.TenantId!, id).ConfigureAwait(false);
                foreach (Mission mission in missions)
                {
                    mission.PlaybookSnapshots = await _database.Playbooks.GetMissionSnapshotsAsync(mission.Id).ConfigureAwait(false);
                }
                return (object)new { Voyage = voyage, Missions = missions };
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Get a voyage")
                .WithDescription("Returns a voyage and all its associated missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/voyages/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Voyage? voyage = ctx.IsAdmin
                    ? await _database.Voyages.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Voyages.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Voyages.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (voyage == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" }; }
                voyage.Status = VoyageStatusEnum.Cancelled;
                voyage.CompletedUtc = DateTime.UtcNow;
                voyage.LastUpdateUtc = DateTime.UtcNow;
                voyage = await _database.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                // Cancel all pending/assigned missions in the voyage
                List<Mission> missions = ctx.IsAdmin
                    ? await _database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false)
                    : await _database.Missions.EnumerateByVoyageAsync(ctx.TenantId!, id).ConfigureAwait(false);
                int cancelledCount = 0;
                foreach (Mission m in missions)
                {
                    if (m.Status == MissionStatusEnum.Pending || m.Status == MissionStatusEnum.Assigned)
                    {
                        // Release the captain if this mission was assigned to one
                        if (!String.IsNullOrEmpty(m.CaptainId))
                        {
                            Captain? captain = await _database.Captains.ReadAsync(m.CaptainId).ConfigureAwait(false);
                            if (captain != null && captain.CurrentMissionId == m.Id)
                            {
                                List<Mission> otherMissions = (ctx.IsAdmin
                                    ? await _database.Missions.EnumerateByCaptainAsync(captain.Id).ConfigureAwait(false)
                                    : await _database.Missions.EnumerateByCaptainAsync(ctx.TenantId!, captain.Id).ConfigureAwait(false))
                                    .Where(om => om.Id != m.Id && (om.Status == MissionStatusEnum.InProgress || om.Status == MissionStatusEnum.Assigned)).ToList();
                                if (otherMissions.Count == 0)
                                {
                                    captain.State = CaptainStateEnum.Idle;
                                    captain.CurrentMissionId = null;
                                    captain.CurrentDockId = null;
                                    captain.ProcessId = null;
                                    captain.RecoveryAttempts = 0;
                                    captain.LastUpdateUtc = DateTime.UtcNow;
                                    await _database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                                }
                            }
                        }

                        m.Status = MissionStatusEnum.Cancelled;
                        m.CompletedUtc = DateTime.UtcNow;
                        m.LastUpdateUtc = DateTime.UtcNow;
                        await _database.Missions.UpdateAsync(m).ConfigureAwait(false);
                        cancelledCount++;
                    }
                }
                // Broadcast voyage and mission cancellations for dashboard toast notifications
                if (_webSocketHub != null)
                {
                    _webSocketHub.BroadcastVoyageChange(id, VoyageStatusEnum.Cancelled.ToString(), voyage.Title);
                    foreach (Mission cm in missions)
                    {
                        if (cm.Status == MissionStatusEnum.Cancelled)
                        {
                            _webSocketHub.BroadcastMissionChange(cm.Id, MissionStatusEnum.Cancelled.ToString(), cm.Title);
                        }
                    }
                }

                return (object)new { Voyage = voyage, CancelledMissions = cancelledCount };
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Cancel a voyage")
                .WithDescription("Cancels a voyage and all its pending/assigned missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/voyages/{id}/purge", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    return (object)new { Error = "Conflict", Message = "Cannot delete voyage while status is " + voyage.Status + ". Cancel the voyage first." };
                }

                List<Mission> missions = ctx.IsAdmin
                    ? await _database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false)
                    : await _database.Missions.EnumerateByVoyageAsync(ctx.TenantId!, id).ConfigureAwait(false);

                // Block deletion if any missions are actively assigned or in progress
                List<Mission> activeMissions = missions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress).ToList();
                if (activeMissions.Count > 0)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot delete voyage with " + activeMissions.Count + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };
                }

                // Cascade delete all missions in this voyage
                foreach (Mission m in missions)
                {
                    await _database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                }

                // Delete the voyage itself
                await _database.Voyages.DeleteAsync(id).ConfigureAwait(false);

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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                DeleteMultipleRequest? body = JsonSerializer.Deserialize<DeleteMultipleRequest>(req.Http.Request.DataAsString, _jsonOptions);
                if (body == null || body.Ids == null || body.Ids.Count == 0)
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Ids is required and must not be empty" };

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
                .WithDescription("Permanently deletes multiple voyages and their associated missions from the database by ID. Voyages that are Open/InProgress or have active missions are skipped. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiJson.BodyFor<DeleteMultipleRequest>("List of voyage IDs to delete"))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));
        }
    }
}
