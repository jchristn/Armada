namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.IO;
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
    using Armada.Core.Settings;
    using Armada.Server.WebSocket;
    using SyslogLogging;

    /// <summary>
    /// REST API routes for mission management.
    /// </summary>
    public class MissionRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly IAdmiralService _admiral;
        private readonly ArmadaSettings _settings;
        private readonly IGitService _git;
        private readonly ILandingService _landingService;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly Func<Mission, Dock, Task> _handleMissionComplete;
        private readonly ArmadaWebSocketHub? _webSocketHub;
        private readonly LoggingModule _logging;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral coordination service.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="git">Git operations service.</param>
        /// <param name="landingService">Mission landing service.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="handleMissionComplete">Mission completion callback.</param>
        /// <param name="webSocketHub">WebSocket hub for real-time notifications.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public MissionRoutes(
            DatabaseDriver database,
            IAdmiralService admiral,
            ArmadaSettings settings,
            IGitService git,
            ILandingService landingService,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            Func<Mission, Dock, Task> handleMissionComplete,
            ArmadaWebSocketHub? webSocketHub,
            LoggingModule logging,
            JsonSerializerOptions jsonOptions)
        {
            _database = database;
            _admiral = admiral;
            _settings = settings;
            _git = git;
            _landingService = landingService;
            _emitEvent = emitEvent;
            _handleMissionComplete = handleMissionComplete;
            _webSocketHub = webSocketHub;
            _logging = logging;
            _jsonOptions = jsonOptions;
        }

        private async Task<string> ReadFileSharedAsync(string path)
        {
            using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(fs);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        private async Task<string[]> ReadLinesSharedAsync(string path)
        {
            List<string> lines = new List<string>();
            using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(fs);
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lines.Add(line);
            }
            return lines.ToArray();
        }

        private async Task<(string FileName, string Path)?> ResolveMissionInstructionsPathAsync(AuthContext ctx, Mission mission)
        {
            Captain? captain = null;
            if (!String.IsNullOrEmpty(mission.CaptainId))
            {
                captain = ctx.IsAdmin
                    ? await _database.Captains.ReadAsync(mission.CaptainId).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.ReadAsync(ctx.TenantId!, mission.CaptainId).ConfigureAwait(false)
                        : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, mission.CaptainId).ConfigureAwait(false);
            }

            Dock? dock = null;
            string? dockId = mission.DockId ?? captain?.CurrentDockId;
            if (!String.IsNullOrEmpty(dockId))
            {
                dock = ctx.IsAdmin
                    ? await _database.Docks.ReadAsync(dockId).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Docks.ReadAsync(ctx.TenantId!, dockId).ConfigureAwait(false)
                        : await _database.Docks.ReadAsync(ctx.TenantId!, ctx.UserId!, dockId).ConfigureAwait(false);
            }

            if (dock == null || String.IsNullOrEmpty(dock.WorktreePath) || !Directory.Exists(dock.WorktreePath))
                return null;

            string fileName = MissionPromptBuilder.GetInstructionsFileName(captain != null ? captain.Runtime.ToString() : null);
            string path = Path.Combine(dock.WorktreePath, fileName);
            if (File.Exists(path))
                return (fileName, path);

            string[] fallbackNames = { "CLAUDE.md", "CODEX.md", "CURSOR.md", "AGENTS.md", "GEMINI.md" };
            foreach (string fallbackName in fallbackNames)
            {
                string fallbackPath = Path.Combine(dock.WorktreePath, fallbackName);
                if (File.Exists(fallbackPath))
                    return (fallbackName, fallbackPath);
            }

            return (fileName, path);
        }

        private bool IsValidTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            return (current, target) switch
            {
                (MissionStatusEnum.Pending, MissionStatusEnum.Assigned) => true,
                (MissionStatusEnum.Pending, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.Assigned, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Assigned, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.WorkProduced) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Testing) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Review) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.WorkProduced, MissionStatusEnum.PullRequestOpen) => true,
                (MissionStatusEnum.WorkProduced, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.WorkProduced, MissionStatusEnum.LandingFailed) => true,
                (MissionStatusEnum.WorkProduced, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.PullRequestOpen, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.PullRequestOpen, MissionStatusEnum.LandingFailed) => true,
                (MissionStatusEnum.PullRequestOpen, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Review) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.LandingFailed, MissionStatusEnum.WorkProduced) => true,
                (MissionStatusEnum.LandingFailed, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.LandingFailed, MissionStatusEnum.Cancelled) => true,
                _ => false
            };
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

            // Missions
            app.Get("/api/v1/missions", async (ApiRequest req) =>
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
                EnumerationResult<Mission> result = ctx.IsAdmin
                    ? await _database.Missions.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Missions.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                foreach (Mission m in result.Objects) m.DiffSnapshot = null;
                return result;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("List all missions")
                .WithDescription("Returns all missions, filterable by status, vesselId, captainId, or voyageId.")
                .WithParameter(OpenApiParameterMetadata.Query("status", "Filter by mission status (Pending, Assigned, InProgress, WorkProduced, Testing, Review, Complete, Failed, LandingFailed, Cancelled)", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Filter by vessel ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Filter by captain ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Filter by voyage ID", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Mission>>("Paginated mission list"))
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/missions/enumerate", async (ApiRequest req) =>
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
                EnumerationResult<Mission> result = ctx.IsAdmin
                    ? await _database.Missions.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Missions.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                foreach (Mission m in result.Objects) m.DiffSnapshot = null;
                return result;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Enumerate missions")
                .WithDescription("Paginated enumeration of missions with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Post<Mission>("/api/v1/missions", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                Mission mission = JsonSerializer.Deserialize<Mission>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Mission.");
                mission.TenantId = ctx.TenantId;
                mission.UserId = ctx.UserId;
                mission = await _admiral.DispatchMissionAsync(mission).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                if (mission.Status == MissionStatusEnum.Pending)
                {
                    return (object)new
                    {
                        Mission = mission,
                        Warning = "Mission created but could not be assigned to any captain. It will be retried on the next health check cycle."
                    };
                }
                return mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Create a mission")
                .WithDescription("Creates and dispatches a new mission. If a vesselId is provided, the Admiral will assign a captain and set up a worktree.")
                .WithRequestBody(OpenApiJson.BodyFor<Mission>("Mission data", true))
                .WithResponse(201, OpenApiJson.For<Mission>("Created mission"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }
                mission.DiffSnapshot = null;
                mission.PlaybookSnapshots = await _database.Playbooks.GetMissionSnapshotsAsync(id).ConfigureAwait(false);
                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get a mission")
                .WithDescription("Returns a single mission by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Mission>("Mission details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put<Mission>("/api/v1/missions/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? existing = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }
                Mission incoming = JsonSerializer.Deserialize<Mission>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Mission.");

                // Merge only metadata fields onto the existing record
                existing.Title = incoming.Title;
                existing.Description = incoming.Description;
                existing.Priority = incoming.Priority;
                existing.VesselId = incoming.VesselId;
                existing.VoyageId = incoming.VoyageId;
                existing.BranchName = incoming.BranchName;
                existing.PrUrl = incoming.PrUrl;
                existing.ParentMissionId = incoming.ParentMissionId;
                existing.LastUpdateUtc = DateTime.UtcNow;

                // Preserve operational/timestamp fields: CreatedUtc, StartedUtc, CompletedUtc,
                // Status, CaptainId, DockId, ProcessId, CommitHash, DiffSnapshot

                existing = await _database.Missions.UpdateAsync(existing).ConfigureAwait(false);
                return (object)existing;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Update a mission")
                .WithDescription("Updates an existing mission by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Mission>("Updated mission data", true))
                .WithResponse(200, OpenApiJson.For<Mission>("Updated mission"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put<StatusTransitionRequest>("/api/v1/missions/{id}/status", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }

                StatusTransitionRequest transition = JsonSerializer.Deserialize<StatusTransitionRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as StatusTransitionRequest.");
                if (String.IsNullOrEmpty(transition.Status))
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Status is required" };

                if (!Enum.TryParse<MissionStatusEnum>(transition.Status, true, out MissionStatusEnum newStatus))
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid status: " + transition.Status };

                // Validate transitions
                bool valid = IsValidTransition(mission.Status, newStatus);
                if (!valid)
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid transition from " + mission.Status + " to " + newStatus };

                // If manually transitioning to Complete and an active dock exists, route through
                // the full landing pipeline (PR creation, merge, branch cleanup, dock reclaim)
                // instead of just mutating the status. This ensures manual completion has the
                // same semantics as agent-driven completion.
                if (newStatus == MissionStatusEnum.Complete && !String.IsNullOrEmpty(mission.DockId))
                {
                    Dock? landingDock = ctx.IsAdmin
                        ? await _database.Docks.ReadAsync(mission.DockId).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Docks.ReadAsync(ctx.TenantId!, mission.DockId).ConfigureAwait(false)
                            : await _database.Docks.ReadAsync(ctx.TenantId!, ctx.UserId!, mission.DockId).ConfigureAwait(false);
                    if (landingDock != null && landingDock.Active)
                    {
                        // Capture diff before landing
                        if (_admiral.OnCaptureDiff != null)
                        {
                            try
                            {
                                await _admiral.OnCaptureDiff.Invoke(mission, landingDock).ConfigureAwait(false);
                            }
                            catch (Exception diffEx)
                            {
                                _logging.Warn(_Header + "error capturing diff during manual completion of " + id + ": " + diffEx.Message);
                            }
                        }

                        // Set to WorkProduced first so the landing handler can process it
                        mission.Status = MissionStatusEnum.WorkProduced;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                        _logging.Info(_Header + "manual Complete transition for " + id + " — routing through landing pipeline");

                        // Invoke the full landing pipeline (same as agent-driven completion)
                        await _handleMissionComplete(mission, landingDock).ConfigureAwait(false);

                        // Re-read the mission to get the final state after landing
                        mission = await _database.Missions.ReadAsync(id).ConfigureAwait(false);
                        if (mission == null)
                            return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found after landing" };

                        Signal landingSignal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " manual completion — landed as " + mission.Status);
                        if (!String.IsNullOrEmpty(mission.CaptainId)) landingSignal.FromCaptainId = mission.CaptainId;
                        await _database.Signals.CreateAsync(landingSignal).ConfigureAwait(false);

                        await _emitEvent("mission.status_changed", "Mission " + id + " manually completed — landed as " + mission.Status,
                            "mission", id, mission.CaptainId, id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);

                        if (_webSocketHub != null)
                            _webSocketHub.BroadcastMissionChange(id, mission.Status.ToString(), mission.Title);

                        return (object)mission;
                    }
                }

                // Standard transition: no dock available or not transitioning to Complete
                mission.Status = newStatus;
                mission.LastUpdateUtc = DateTime.UtcNow;

                if (newStatus == MissionStatusEnum.InProgress && mission.StartedUtc == null)
                {
                    mission.StartedUtc = DateTime.UtcNow;
                }

                if (newStatus == MissionStatusEnum.Complete || newStatus == MissionStatusEnum.Failed ||
                    newStatus == MissionStatusEnum.LandingFailed || newStatus == MissionStatusEnum.Cancelled)
                {
                    mission.CompletedUtc = DateTime.UtcNow;
                }

                await _database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                // Audit event: manual Complete without an active dock bypasses the landing pipeline.
                // This is allowed (operators may need it after restarts/cleanup) but should be visible.
                if (newStatus == MissionStatusEnum.Complete)
                {
                    _logging.Warn(_Header + "mission " + id + " manually completed without active dock — landing pipeline was skipped");
                    await _emitEvent("mission.manual_complete_no_dock",
                        "Mission " + id + " manually marked Complete without an active dock (landing pipeline skipped)",
                        "mission", id, mission.CaptainId, id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);
                }

                Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " transitioned to " + newStatus);
                if (!String.IsNullOrEmpty(mission.CaptainId)) signal.FromCaptainId = mission.CaptainId;
                await _database.Signals.CreateAsync(signal).ConfigureAwait(false);

                await _emitEvent("mission.status_changed", "Mission " + id + " transitioned to " + newStatus,
                    "mission", id, mission.CaptainId, id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);

                // Broadcast specific mission change for dashboard toast notifications
                if (_webSocketHub != null)
                {
                    _webSocketHub.BroadcastMissionChange(id, newStatus.ToString(), mission.Title);
                }

                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Transition mission status")
                .WithDescription("Transitions a mission to a new status. Valid transitions: Pending→Assigned, Assigned→InProgress, InProgress→Testing/Review/Complete/Failed, Testing→Review/InProgress/Complete/Failed, Review→Complete/InProgress/Failed. Most states allow →Cancelled.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<StatusTransitionRequest>("Target status", true))
                .WithResponse(200, OpenApiJson.For<Mission>("Updated mission"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/missions/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }
                mission.Status = MissionStatusEnum.Cancelled;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                mission = await _database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Cancel a mission")
                .WithDescription("Cancels a mission by setting its status to Cancelled. Returns the full updated mission.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Mission>("Cancelled mission"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/missions/{id}/purge", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }

                if (ctx.IsAdmin)
                    await _database.Missions.DeleteAsync(id).ConfigureAwait(false);
                else if (ctx.IsTenantAdmin)
                    await _database.Missions.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                else
                    await _database.Missions.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);

                await _emitEvent("mission.deleted", "Mission " + id + " permanently deleted",
                    "mission", id, null, null, null, null).ConfigureAwait(false);

                return (object)new { Status = "deleted", MissionId = id };
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Permanently delete a mission")
                .WithDescription("Permanently deletes a mission from the database. This cannot be undone.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<object>("Deleted mission"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<DeleteMultipleRequest>("/api/v1/missions/delete/multiple", async (ApiRequest req) =>
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
                    Mission? mission = ctx.IsAdmin
                        ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                            : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    if (mission == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    if (ctx.IsAdmin)
                        await _database.Missions.DeleteAsync(id).ConfigureAwait(false);
                    else if (ctx.IsTenantAdmin)
                        await _database.Missions.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    else
                        await _database.Missions.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await _emitEvent("mission.batch_deleted", "Batch deleted " + result.Deleted + " missions",
                    "mission", null, null, null, null, null).ConfigureAwait(false);

                result.ResolveStatus();
                return (object)result;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Batch delete multiple missions")
                .WithDescription("Permanently deletes multiple missions from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiJson.BodyFor<DeleteMultipleRequest>("List of mission IDs to delete"))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            app.Post<MissionRestartRequest>("/api/v1/missions/{id}/restart", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }

                if (mission.Status != MissionStatusEnum.Failed && mission.Status != MissionStatusEnum.Cancelled)
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Only Failed or Cancelled missions can be restarted" };

                try
                {
                    MissionRestartRequest body = JsonSerializer.Deserialize<MissionRestartRequest>(req.Http.Request.DataAsString, _jsonOptions)
                        ?? throw new InvalidOperationException("Request body could not be deserialized as MissionRestartRequest.");
                    if (!String.IsNullOrEmpty(body.Title)) mission.Title = body.Title;
                    if (!String.IsNullOrEmpty(body.Description)) mission.Description = body.Description;
                }
                catch { }

                mission.Status = MissionStatusEnum.Pending;
                mission.CreatedUtc = DateTime.UtcNow;
                mission.CaptainId = null;
                mission.BranchName = null;
                mission.PrUrl = null;
                mission.CommitHash = null;
                mission.DockId = null;
                mission.ProcessId = null;
                mission.DiffSnapshot = null;
                mission.StartedUtc = null;
                mission.CompletedUtc = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                mission = await _database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " restarted");
                await _database.Signals.CreateAsync(signal).ConfigureAwait(false);

                await _emitEvent("mission.restarted", "Mission " + id + " restarted",
                    "mission", id, null, id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);

                // Broadcast specific mission change for dashboard toast notifications
                if (_webSocketHub != null)
                {
                    _webSocketHub.BroadcastMissionChange(id, MissionStatusEnum.Pending.ToString(), mission.Title);
                }

                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Restart a failed or cancelled mission")
                .WithDescription("Resets a Failed or Cancelled mission back to Pending so it can be re-dispatched. Optionally update the title and description (instructions) before restarting. Clears captain assignment, branch, PR URL, and timing fields.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<MissionRestartRequest>("Optional updated instructions", false))
                .WithResponse(200, OpenApiJson.For<Mission>("Restarted mission"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/missions/{id}/retry-landing", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }

                if (mission.Status != MissionStatusEnum.WorkProduced && mission.Status != MissionStatusEnum.LandingFailed)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Only WorkProduced or LandingFailed missions can retry landing (current: " + mission.Status + ")" };
                }

                bool success = await _landingService.RetryLandingAsync(id, ctx.TenantId).ConfigureAwait(false);
                mission = await _database.Missions.ReadAsync(id).ConfigureAwait(false);
                mission = await _database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (!success)
                {
                    string reason = "Landing failed.";
                    if (mission != null)
                    {
                        if (String.IsNullOrEmpty(mission.BranchName)) reason = "Mission has no branch name -- the branch may have been cleaned up.";
                        else if (String.IsNullOrEmpty(mission.VesselId)) reason = "Mission has no vessel assigned.";
                        else if (mission.Status == MissionStatusEnum.LandingFailed) reason = "Rebase or merge failed -- the branch may have conflicts with the target branch.";
                        else if (mission.Status == MissionStatusEnum.WorkProduced) reason = "Landing handler failed -- check server logs for details.";
                    }
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = reason };
                }
                return (object)new { Status = "landed", MissionId = id, MissionStatus = mission?.Status.ToString() };
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Retry landing for a mission")
                .WithDescription("Rebases the mission branch onto the current target and re-attempts landing. Only available for WorkProduced or LandingFailed missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<object>("Landing result"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/diff", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }

                // Check for a saved diff file first (captured at completion time)
                string savedDiffPath = Path.Combine(_settings.LogDirectory, "diffs", id + ".diff");
                if (File.Exists(savedDiffPath))
                {
                    string savedDiff = await ReadFileSharedAsync(savedDiffPath).ConfigureAwait(false);
                    return (object)new { MissionId = id, Branch = mission.BranchName ?? "", Diff = savedDiff };
                }

                // Check for database-persisted diff snapshot
                if (!String.IsNullOrEmpty(mission.DiffSnapshot))
                {
                    return (object)new { MissionId = id, Branch = mission.BranchName ?? "", Diff = mission.DiffSnapshot };
                }

                // Fall back to live worktree diff
                Dock? dock = null;
                if (!String.IsNullOrEmpty(mission.DockId))
                {
                    dock = ctx.IsAdmin
                        ? await _database.Docks.ReadAsync(mission.DockId).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Docks.ReadAsync(ctx.TenantId!, mission.DockId).ConfigureAwait(false)
                            : await _database.Docks.ReadAsync(ctx.TenantId!, ctx.UserId!, mission.DockId).ConfigureAwait(false);
                }

                if (dock == null && !String.IsNullOrEmpty(mission.CaptainId))
                {
                    Captain? captain = ctx.IsAdmin
                        ? await _database.Captains.ReadAsync(mission.CaptainId).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Captains.ReadAsync(ctx.TenantId!, mission.CaptainId).ConfigureAwait(false)
                            : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, mission.CaptainId).ConfigureAwait(false);
                    if (captain != null && !String.IsNullOrEmpty(captain.CurrentDockId))
                    {
                        dock = ctx.IsAdmin
                            ? await _database.Docks.ReadAsync(captain.CurrentDockId).ConfigureAwait(false)
                            : ctx.IsTenantAdmin
                                ? await _database.Docks.ReadAsync(ctx.TenantId!, captain.CurrentDockId).ConfigureAwait(false)
                                : await _database.Docks.ReadAsync(ctx.TenantId!, ctx.UserId!, captain.CurrentDockId).ConfigureAwait(false);
                    }
                }

                if (dock == null && !String.IsNullOrEmpty(mission.BranchName) && !String.IsNullOrEmpty(mission.VesselId))
                {
                    List<Dock> docks = ctx.IsAdmin
                        ? await _database.Docks.EnumerateByVesselAsync(mission.VesselId).ConfigureAwait(false)
                        : await _database.Docks.EnumerateByVesselAsync(ctx.TenantId!, mission.VesselId).ConfigureAwait(false);
                    dock = docks.FirstOrDefault(d => d.BranchName == mission.BranchName && d.Active);
                }

                if (dock == null || String.IsNullOrEmpty(dock.WorktreePath) || !Directory.Exists(dock.WorktreePath))
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No diff available — worktree was already reclaimed and no saved diff exists" };

                string baseBranch = "main";
                if (!String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? vessel = await _database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                    if (vessel != null) baseBranch = vessel.DefaultBranch;
                }

                string diff = await _git.DiffAsync(dock.WorktreePath, baseBranch).ConfigureAwait(false);
                return (object)new { MissionId = id, Branch = dock.BranchName ?? "", Diff = diff };
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get diff for a mission")
                .WithDescription("Returns the git diff of changes made by a captain in the mission's worktree.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/log", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }

                string logPath = Path.Combine(_settings.LogDirectory, "missions", id + ".log");
                if (!File.Exists(logPath))
                    return (object)new { MissionId = id, Log = "", Lines = 0, TotalLines = 0 };

                try
                {
                    string[] allLines = await ReadLinesSharedAsync(logPath).ConfigureAwait(false);
                    int totalLines = allLines.Length;

                    int offset = 0;
                    int lineCount = 200;

                    string? offsetParam = req.Query.GetValueOrDefault("offset");
                    if (!String.IsNullOrEmpty(offsetParam) && Int32.TryParse(offsetParam, out int parsedOffset))
                        offset = Math.Max(0, parsedOffset);

                    string? linesParam = req.Query.GetValueOrDefault("lines");
                    if (!String.IsNullOrEmpty(linesParam) && Int32.TryParse(linesParam, out int parsedLines))
                        lineCount = Math.Max(1, parsedLines);

                    string[] slice = allLines.Skip(offset).Take(lineCount).ToArray();
                    string log = String.Join("\n", slice);

                    return (object)new { MissionId = id, Log = log, Lines = slice.Length, TotalLines = totalLines };
                }
                catch (IOException)
                {
                    // File may be locked, deleted, or in use -- return empty rather than 500
                    return (object)new { MissionId = id, Log = "", Lines = 0, TotalLines = 0 };
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get log for a mission")
                .WithDescription("Returns the session log for a mission. Supports pagination via ?lines=N (default 200) and ?offset=N query parameters.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/instructions", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }

                (string FileName, string Path)? resolved = await ResolveMissionInstructionsPathAsync(ctx, mission).ConfigureAwait(false);
                if (resolved == null)
                {
                    string instructionsDir = Path.Combine(_settings.LogDirectory, "instructions");
                    string[] candidates = Directory.Exists(instructionsDir)
                        ? Directory.GetFiles(instructionsDir, id + ".*")
                        : Array.Empty<string>();

                    if (candidates.Length > 0)
                    {
                        string snapshotPath = candidates[0];
                        string snapshotFileName = Path.GetFileName(snapshotPath);
                        try
                        {
                            string snapshotContent = await ReadFileSharedAsync(snapshotPath).ConfigureAwait(false);
                            return (object)new { MissionId = id, FileName = snapshotFileName, Content = snapshotContent };
                        }
                        catch (IOException)
                        {
                            return (object)new { MissionId = id, FileName = snapshotFileName, Content = "" };
                        }
                    }

                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission instructions are unavailable because neither a live dock/worktree nor a saved instructions snapshot could be found" };
                }

                try
                {
                    string content = File.Exists(resolved.Value.Path)
                        ? await ReadFileSharedAsync(resolved.Value.Path).ConfigureAwait(false)
                        : "";
                    return (object)new { MissionId = id, FileName = resolved.Value.FileName, Content = content };
                }
                catch (IOException)
                {
                    return (object)new { MissionId = id, FileName = resolved.Value.FileName, Content = "" };
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get mission instructions")
                .WithDescription("Returns the runtime-specific instruction file generated for a mission, such as CLAUDE.md, CODEX.md, CURSOR.md, AGENTS.md, or GEMINI.md.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }
    }
}
