namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
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
        private readonly IMissionService _missionService;
        private readonly ArmadaSettings _settings;
        private readonly IGitService _git;
        private readonly ILandingService _landingService;
        private readonly LandingPreviewService _landingPreview;
        private readonly GitHubIntegrationService _gitHub;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly Func<Mission, Dock, Task> _handleMissionComplete;
        private readonly ArmadaWebSocketHub? _webSocketHub;
        private readonly LoggingModule _logging;
        private readonly JsonSerializerOptions _jsonOptions;

        private sealed class MissionInstructionsPath
        {
            public string FileName { get; set; } = String.Empty;
            public string Path { get; set; } = String.Empty;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral coordination service.</param>
        /// <param name="missionService">Mission lifecycle service.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="git">Git operations service.</param>
        /// <param name="landingService">Mission landing service.</param>
        /// <param name="landingPreview">Mission landing-preview service.</param>
        /// <param name="gitHub">GitHub integration service.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="handleMissionComplete">Mission completion callback.</param>
        /// <param name="webSocketHub">WebSocket hub for real-time notifications.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public MissionRoutes(
            DatabaseDriver database,
            IAdmiralService admiral,
            IMissionService missionService,
            ArmadaSettings settings,
            IGitService git,
            ILandingService landingService,
            LandingPreviewService landingPreview,
            GitHubIntegrationService gitHub,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            Func<Mission, Dock, Task> handleMissionComplete,
            ArmadaWebSocketHub? webSocketHub,
            LoggingModule logging,
            JsonSerializerOptions jsonOptions)
        {
            _database = database;
            _admiral = admiral;
            _missionService = missionService;
            _settings = settings;
            _git = git;
            _landingService = landingService;
            _landingPreview = landingPreview ?? throw new ArgumentNullException(nameof(landingPreview));
            _gitHub = gitHub ?? throw new ArgumentNullException(nameof(gitHub));
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

        private async Task<MissionInstructionsPath?> ResolveMissionInstructionsPathAsync(AuthContext ctx, Mission mission)
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
            {
                return new MissionInstructionsPath
                {
                    FileName = fileName,
                    Path = path
                };
            }

            string[] fallbackNames = { "CLAUDE.md", "CODEX.md", "CURSOR.md", "AGENTS.md", "GEMINI.md", "MUX.md" };
            foreach (string fallbackName in fallbackNames)
            {
                string fallbackPath = Path.Combine(dock.WorktreePath, fallbackName);
                if (File.Exists(fallbackPath))
                {
                    return new MissionInstructionsPath
                    {
                        FileName = fallbackName,
                        Path = fallbackPath
                    };
                }
            }

            return new MissionInstructionsPath
            {
                FileName = fileName,
                Path = path
            };
        }

        private bool IsValidTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            if (current == MissionStatusEnum.Pending)
                return target == MissionStatusEnum.Assigned || target == MissionStatusEnum.Cancelled;
            if (current == MissionStatusEnum.Assigned)
                return target == MissionStatusEnum.InProgress || target == MissionStatusEnum.Cancelled;
            if (current == MissionStatusEnum.InProgress)
            {
                return target == MissionStatusEnum.WorkProduced
                    || target == MissionStatusEnum.Testing
                    || target == MissionStatusEnum.Review
                    || target == MissionStatusEnum.Complete
                    || target == MissionStatusEnum.Failed
                    || target == MissionStatusEnum.Cancelled;
            }
            if (current == MissionStatusEnum.WorkProduced)
            {
                return target == MissionStatusEnum.PullRequestOpen
                    || target == MissionStatusEnum.Complete
                    || target == MissionStatusEnum.LandingFailed
                    || target == MissionStatusEnum.Cancelled;
            }
            if (current == MissionStatusEnum.PullRequestOpen)
            {
                return target == MissionStatusEnum.Complete
                    || target == MissionStatusEnum.LandingFailed
                    || target == MissionStatusEnum.Cancelled;
            }
            if (current == MissionStatusEnum.Testing)
            {
                return target == MissionStatusEnum.Review
                    || target == MissionStatusEnum.InProgress
                    || target == MissionStatusEnum.Complete
                    || target == MissionStatusEnum.Failed;
            }
            if (current == MissionStatusEnum.Review)
                return target == MissionStatusEnum.Complete || target == MissionStatusEnum.InProgress || target == MissionStatusEnum.Failed;
            if (current == MissionStatusEnum.LandingFailed)
                return target == MissionStatusEnum.WorkProduced || target == MissionStatusEnum.Failed || target == MissionStatusEnum.Cancelled;
            return false;
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
                EnumerationResult<Mission> result = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(ctx, query, q => _database.Missions.EnumerateAsync(q), (t, q) => _database.Missions.EnumerateAsync(t, q), (t, u, q) => _database.Missions.EnumerateAsync(t, u, q)).ConfigureAwait(false);
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
                EnumerationResult<Mission> result = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(ctx, query, q => _database.Missions.EnumerateAsync(q), (t, q) => _database.Missions.EnumerateAsync(t, q), (t, u, q) => _database.Missions.EnumerateAsync(t, u, q)).ConfigureAwait(false);
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

            app.Get("/api/v1/missions/summaries", async (ApiRequest req) =>
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
                EnumerationResult<MissionSummary> result = ctx.IsAdmin
                    ? await _database.Missions.EnumerateSummariesAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.EnumerateSummariesAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Missions.EnumerateSummariesAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("List lightweight mission summaries")
                .WithDescription("Returns lightweight mission summaries without large description, diff, or agent-output payloads.")
                .WithParameter(OpenApiParameterMetadata.Query("status", "Filter by mission status", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Filter by vessel ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Filter by captain ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Filter by voyage ID", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<MissionSummary>>("Paginated mission summary list"))
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/missions/summaries/enumerate", async (ApiRequest req) =>
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
                EnumerationResult<MissionSummary> result = ctx.IsAdmin
                    ? await _database.Missions.EnumerateSummariesAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.EnumerateSummariesAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Missions.EnumerateSummariesAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Enumerate lightweight mission summaries")
                .WithDescription("Paginated enumeration of lightweight mission summaries with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<MissionSummary>>("Paginated mission summary list"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/history", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                MissionHistoryQuery query = new MissionHistoryQuery();
                if (DateTime.TryParse(req.Query.GetValueOrDefault("fromUtc"), out DateTime fromUtc)) query.FromUtc = fromUtc.ToUniversalTime();
                if (DateTime.TryParse(req.Query.GetValueOrDefault("toUtc"), out DateTime toUtc)) query.ToUtc = toUtc.ToUniversalTime();
                if (int.TryParse(req.Query.GetValueOrDefault("bucketMinutes"), out int bucketMinutes) && bucketMinutes > 0) query.BucketMinutes = bucketMinutes;
                string? fleetId = req.Query.GetValueOrDefault("fleetId");
                if (!String.IsNullOrEmpty(fleetId)) query.FleetId = fleetId;
                string? vesselId = req.Query.GetValueOrDefault("vesselId");
                if (!String.IsNullOrEmpty(vesselId)) query.VesselId = vesselId;

                List<MissionHistoryPoint> points = ctx.IsAdmin
                    ? await _database.Missions.EnumerateHistoryPointsAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.EnumerateHistoryPointsAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Missions.EnumerateHistoryPointsAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);

                if (!String.IsNullOrEmpty(query.FleetId))
                {
                    List<Vessel> vessels = ctx.IsAdmin
                        ? await _database.Vessels.EnumerateAsync().ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Vessels.EnumerateAsync(ctx.TenantId!).ConfigureAwait(false)
                            : await _database.Vessels.EnumerateAsync(ctx.TenantId!, ctx.UserId!).ConfigureAwait(false);
                    HashSet<string> allowedVesselIds = vessels
                        .Where(v => String.Equals(v.FleetId, query.FleetId, StringComparison.Ordinal))
                        .Select(v => v.Id)
                        .ToHashSet(StringComparer.Ordinal);
                    points = points
                        .Where(point => !String.IsNullOrEmpty(point.VesselId) && allowedVesselIds.Contains(point.VesselId!))
                        .ToList();
                }

                return BuildMissionHistorySummary(query, points);
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get aggregated mission history")
                .WithDescription("Returns aggregated mission counts by time bucket for dashboard history charts.")
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Inclusive UTC start time", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Exclusive UTC end time", false))
                .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket size in minutes", false))
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Optional fleet filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithResponse(200, OpenApiJson.For<MissionHistorySummaryResult>("Mission history summary"))
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

            app.Get("/api/v1/missions/{id}/github/pull-request", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string missionId = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(missionId).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, missionId).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, missionId).ConfigureAwait(false);
                if (mission == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };
                }

                try
                {
                    return await _gitHub.GetMissionPullRequestAsync(ctx, mission).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get GitHub pull-request evidence for a mission")
                .WithDescription("Returns normalized GitHub pull-request review and check evidence for the mission pull request.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<GitHubPullRequestDetail>("GitHub pull-request evidence"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/landing-preview", async (ApiRequest req) =>
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
                if (mission == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };
                }

                if (String.IsNullOrWhiteSpace(mission.VesselId))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Mission does not have an associated vessel" };
                }

                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, mission.VesselId).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, mission.VesselId).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission vessel not found" };
                }

                return await _landingPreview.PreviewForMissionAsync(ctx, vessel, mission).ConfigureAwait(false);
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Preview mission landing readiness")
                .WithDescription("Predicts how Armada would land this mission, including branch policy, check requirements, and likely blockers.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<LandingPreviewResult>("Mission landing preview"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/evaluate-autoland", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };
                }

                AutoLandDecision? decision = await _missionService.EvaluateAutoLandAsync(id).ConfigureAwait(false);
                if (decision == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Mission does not have an associated vessel" };
                }

                return decision;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Dry-run the auto-land predicate for a mission")
                .WithDescription("Evaluates the vessel's auto-land rules against this mission's captured diff without landing it, returning whether it would auto-land and, if not, the hold reason.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<AutoLandDecision>("Auto-land decision"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
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
                    if (newStatus == MissionStatusEnum.Review)
                        _webSocketHub.BroadcastApprovalNeeded(mission);
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

            app.Post<MissionReviewDecisionRequest>("/api/v1/missions/{id}/review/approve", async (ApiRequest req) =>
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
                MissionReviewDecisionRequest body = JsonSerializer.Deserialize<MissionReviewDecisionRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? new MissionReviewDecisionRequest();

                try
                {
                    Mission mission = await _missionService.ApproveReviewAsync(id, ctx.UserId, body.Comment, body.Conditional).ConfigureAwait(false);
                    Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " review approved");
                    await _database.Signals.CreateAsync(signal).ConfigureAwait(false);
                    await _emitEvent("mission.review_approved", "Mission " + id + " review approved",
                        "mission", id, mission.CaptainId, id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);
                    _webSocketHub?.BroadcastMissionChange(id, mission.Status.ToString(), mission.Title);
                    return (object)mission;
                }
                catch (InvalidOperationException ioe)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ioe.Message };
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Approve a mission review gate")
                .WithDescription("Approves a mission waiting at a review gate. Non-terminal stages continue to the next pipeline stage, and terminal stages continue to landing.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<MissionReviewDecisionRequest>("Optional review comment", false))
                .WithResponse(200, OpenApiJson.For<Mission>("Updated mission"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<MissionReviewDecisionRequest>("/api/v1/missions/{id}/review/deny", async (ApiRequest req) =>
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
                MissionReviewDecisionRequest body = JsonSerializer.Deserialize<MissionReviewDecisionRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? new MissionReviewDecisionRequest();

                ReviewDenyActionEnum? denyAction = null;
                if (!String.IsNullOrWhiteSpace(body.Action) && Enum.TryParse<ReviewDenyActionEnum>(body.Action, true, out ReviewDenyActionEnum parsedDenyAction))
                {
                    denyAction = parsedDenyAction;
                }

                try
                {
                    Mission mission = await _missionService.DenyReviewAsync(id, ctx.UserId, body.Comment, denyAction).ConfigureAwait(false);
                    Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " review denied");
                    await _database.Signals.CreateAsync(signal).ConfigureAwait(false);
                    await _emitEvent("mission.review_denied", "Mission " + id + " review denied",
                        "mission", id, mission.CaptainId, id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);
                    _webSocketHub?.BroadcastMissionChange(id, mission.Status.ToString(), mission.Title);
                    return (object)mission;
                }
                catch (InvalidOperationException ioe)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ioe.Message };
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Deny a mission review gate")
                .WithDescription("Denies a mission waiting at a review gate. The mission either returns to Pending for rework or fails the pipeline, depending on its review policy.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<MissionReviewDecisionRequest>("Optional review comment", false))
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

                // Remove telemetry events that referenced this mission so they do not dangle.
                await CascadeCleanup.RemoveEventsForMissionAsync(_database, id).ConfigureAwait(false);

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
                if (!success)
                {
                    string reason = "Landing failed.";
                    if (mission != null)
                    {
                        // Resolve the effective landing mode the same way the landing handler does:
                        // voyage > vessel > global default.
                        Vessel? landingVessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                        Voyage? landingVoyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await _database.Voyages.ReadAsync(mission.VoyageId).ConfigureAwait(false);
                        LandingModeEnum? effectiveMode = landingVoyage?.LandingMode ?? landingVessel?.LandingMode ?? _settings.LandingMode;
                        string branch = String.IsNullOrEmpty(mission.BranchName) ? "(unknown)" : mission.BranchName;

                        if (String.IsNullOrEmpty(mission.BranchName))
                        {
                            reason = "Mission has no branch name -- the branch may have been cleaned up, so there is nothing to land.";
                        }
                        else if (String.IsNullOrEmpty(mission.VesselId))
                        {
                            reason = "Mission has no vessel assigned, so its branch cannot be landed.";
                        }
                        else if (!effectiveMode.HasValue || effectiveMode.Value == LandingModeEnum.None)
                        {
                            // Not a failure: there is simply no automatic landing configured. The work is done
                            // and the branch is available for manual integration.
                            reason = "No automatic landing is configured for this "
                                + (effectiveMode.HasValue ? "vessel (landing mode: None)" : "vessel, voyage, or Admiral (landing mode: not set)")
                                + ". The mission's work is complete and its branch '" + branch + "' is available in the repository for manual integration. "
                                + "To land it automatically, set a landing mode (Local Merge, Pull Request, or Merge Queue) on the vessel or voyage and retry; "
                                + "or merge the branch yourself from the vessel's Manage Branches view.";
                        }
                        else if (mission.Status == MissionStatusEnum.LandingFailed)
                        {
                            reason = "Landing for mode " + effectiveMode.Value + " failed to rebase or merge branch '" + branch
                                + "' onto the target -- the branch likely has conflicts that must be resolved before it can land.";
                        }
                        else if (mission.Status == MissionStatusEnum.WorkProduced)
                        {
                            reason = "Landing for mode " + effectiveMode.Value + " did not complete for branch '" + branch
                                + "'. The work is preserved as WorkProduced. Check the [MissionLanding] entries in the server log for the specific git or provider error.";
                        }
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
                    // Mux writes newline-delimited JSON protocol events (one per token for streamed text) to
                    // the log, which is unreadable raw. Render those into plain, human-readable transcript lines
                    // (contiguous assistant text, concise tool-call lines, run summaries) before paginating.
                    string[] allLines = RenderMissionLog(await ReadLinesSharedAsync(logPath).ConfigureAwait(false));
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

                MissionInstructionsPath? resolved = await ResolveMissionInstructionsPathAsync(ctx, mission).ConfigureAwait(false);
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
                    string content = File.Exists(resolved.Path)
                        ? await ReadFileSharedAsync(resolved.Path).ConfigureAwait(false)
                        : "";
                    return (object)new { MissionId = id, FileName = resolved.FileName, Content = content };
                }
                catch (IOException)
                {
                    return (object)new { MissionId = id, FileName = resolved.FileName, Content = "" };
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get mission instructions")
                .WithDescription("Returns the runtime-specific instruction file generated for a mission, such as CLAUDE.md, CODEX.md, CURSOR.md, AGENTS.md, GEMINI.md, or MUX.md.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        /// <summary>
        /// Render a raw mission log into human-readable lines. Runtimes such as Mux write newline-delimited
        /// JSON protocol events -- including one event per streamed token -- which is unreadable as-is. This
        /// collapses streamed assistant text into contiguous lines, summarizes tool calls and run start/finish,
        /// and drops protocol noise (heartbeats, approvals). Plain lines (the launch header, non-JSON runtimes)
        /// pass through unchanged.
        /// </summary>
        private static string[] RenderMissionLog(string[] rawLines)
        {
            if (rawLines == null || rawLines.Length == 0) return rawLines ?? Array.Empty<string>();

            List<string> output = new List<string>(rawLines.Length);
            StringBuilder assistantText = new StringBuilder();

            foreach (string raw in rawLines)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
                {
                    FlushAssistantText(output, assistantText);
                    output.Add(raw);
                    continue;
                }

                JsonDocument? doc = null;
                try { doc = JsonDocument.Parse(trimmed); }
                catch (JsonException) { }

                if (doc == null
                    || doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("eventType", out JsonElement eventTypeElement)
                    || eventTypeElement.ValueKind != JsonValueKind.String)
                {
                    doc?.Dispose();
                    FlushAssistantText(output, assistantText);
                    output.Add(raw);
                    continue;
                }

                using (doc)
                {
                    JsonElement root = doc.RootElement;
                    string eventType = eventTypeElement.GetString() ?? String.Empty;
                    switch (eventType)
                    {
                        case "assistant_text":
                            if (root.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String)
                                assistantText.Append(textElement.GetString());
                            break;

                        case "tool_call_proposed":
                            FlushAssistantText(output, assistantText);
                            if (root.TryGetProperty("toolCall", out JsonElement toolCall) && toolCall.ValueKind == JsonValueKind.Object)
                            {
                                string name = GetLogString(toolCall, "name");
                                string args = toolCall.TryGetProperty("arguments", out JsonElement argsElement)
                                    ? TruncateForLog(argsElement.GetRawText(), 400) : String.Empty;
                                output.Add("> tool: " + name + (args.Length > 0 ? " " + args : String.Empty));
                            }
                            break;

                        case "tool_call_completed":
                            FlushAssistantText(output, assistantText);
                            {
                                string name = GetLogString(root, "toolName");
                                bool ok = !(root.TryGetProperty("result", out JsonElement result)
                                    && result.ValueKind == JsonValueKind.Object
                                    && result.TryGetProperty("success", out JsonElement success)
                                    && success.ValueKind == JsonValueKind.False);
                                string elapsed = root.TryGetProperty("elapsedMs", out JsonElement elapsedElement) && elapsedElement.ValueKind == JsonValueKind.Number
                                    ? " (" + elapsedElement.GetDouble().ToString("0") + "ms)" : String.Empty;
                                output.Add("  " + (name.Length > 0 ? name + " " : String.Empty) + "-> " + (ok ? "ok" : "failed") + elapsed);
                            }
                            break;

                        case "run_started":
                            FlushAssistantText(output, assistantText);
                            output.Add("-- run started (" + GetLogString(root, "model") + ") --");
                            break;

                        case "run_completed":
                            FlushAssistantText(output, assistantText);
                            {
                                string dur = root.TryGetProperty("durationMs", out JsonElement d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble().ToString("0") : "?";
                                string iters = root.TryGetProperty("iterationsCompleted", out JsonElement it) && it.ValueKind == JsonValueKind.Number ? it.GetInt32().ToString() : "?";
                                string calls = root.TryGetProperty("toolCallCount", out JsonElement cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt32().ToString() : "?";
                                output.Add("-- run completed: " + dur + "ms, " + iters + " iteration(s), " + calls + " tool call(s) --");
                            }
                            break;

                        // Protocol noise that adds nothing to a human transcript.
                        case "assistant_thinking":
                        case "tool_call_approved":
                        case "heartbeat":
                        default:
                            break;
                    }
                }
            }

            FlushAssistantText(output, assistantText);
            return output.ToArray();
        }

        private static void FlushAssistantText(List<string> output, StringBuilder assistantText)
        {
            if (assistantText.Length == 0) return;
            string text = assistantText.ToString();
            assistantText.Clear();
            foreach (string line in text.Split('\n'))
            {
                output.Add(line.TrimEnd('\r'));
            }
        }

        private static string GetLogString(JsonElement obj, string property)
        {
            return obj.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.String
                ? (element.GetString() ?? String.Empty) : String.Empty;
        }

        private static string TruncateForLog(string? value, int max)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            string collapsed = value!.Replace("\r", " ").Replace("\n", " ");
            return collapsed.Length <= max ? collapsed : collapsed.Substring(0, max) + "...";
        }

        private static MissionHistorySummaryResult BuildMissionHistorySummary(MissionHistoryQuery query, IEnumerable<MissionHistoryPoint> points)
        {
            DateTime fromUtc = query.FromUtc.Kind == DateTimeKind.Utc ? query.FromUtc : query.FromUtc.ToUniversalTime();
            DateTime toUtc = query.ToUtc.Kind == DateTimeKind.Utc ? query.ToUtc : query.ToUtc.ToUniversalTime();
            int bucketMinutes = query.BucketMinutes > 0 ? query.BucketMinutes : 60;
            TimeSpan bucketSize = TimeSpan.FromMinutes(bucketMinutes);
            long bucketTicks = Math.Max(bucketSize.Ticks, TimeSpan.FromMinutes(1).Ticks);

            MissionHistorySummaryResult result = new MissionHistorySummaryResult
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                BucketMinutes = bucketMinutes
            };

            SortedDictionary<long, MissionHistoryBucket> buckets = new SortedDictionary<long, MissionHistoryBucket>();
            long startTicks = (fromUtc.Ticks / bucketTicks) * bucketTicks;
            for (long ticks = startTicks; ticks < toUtc.Ticks; ticks += bucketTicks)
            {
                buckets[ticks] = new MissionHistoryBucket { StartUtc = new DateTime(ticks, DateTimeKind.Utc) };
            }

            foreach (MissionHistoryPoint point in points)
            {
                DateTime createdUtc = point.CreatedUtc.Kind == DateTimeKind.Utc ? point.CreatedUtc : point.CreatedUtc.ToUniversalTime();
                if (createdUtc < fromUtc || createdUtc >= toUtc) continue;

                long bucketStartTicks = (createdUtc.Ticks / bucketTicks) * bucketTicks;
                if (!buckets.TryGetValue(bucketStartTicks, out MissionHistoryBucket? bucket))
                {
                    bucket = new MissionHistoryBucket { StartUtc = new DateTime(bucketStartTicks, DateTimeKind.Utc) };
                    buckets[bucketStartTicks] = bucket;
                }

                // "Complete" (green) covers every state where the captain has produced work or gone
                // further, not just fully-landed missions -- so with landing off, produced/reviewed work
                // still reads as done. Failed/LandingFailed are red; only genuinely in-flight (Pending/
                // Assigned/InProgress) and Cancelled are grey "Other".
                if (point.Status == MissionStatusEnum.WorkProduced
                    || point.Status == MissionStatusEnum.PullRequestOpen
                    || point.Status == MissionStatusEnum.Testing
                    || point.Status == MissionStatusEnum.Review
                    || point.Status == MissionStatusEnum.Complete)
                {
                    bucket.CompleteCount++;
                    result.CompleteCount++;
                }
                else if (point.Status == MissionStatusEnum.Failed || point.Status == MissionStatusEnum.LandingFailed)
                {
                    bucket.FailedCount++;
                    result.FailedCount++;
                }
                else
                {
                    bucket.OtherCount++;
                    result.OtherCount++;
                }
            }

            result.TotalCount = result.CompleteCount + result.FailedCount + result.OtherCount;
            result.Buckets = buckets.Values.ToList();
            return result;
        }
    }
}
