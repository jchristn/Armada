namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
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
    using Armada.Core.Protocol;
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
        private readonly Func<Mission, MissionStatusEnum?, string, Task> _emitMissionStatusChanged;
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
        /// Looks up a running mission's current captain activity, returned on the single-mission route as Activity; null
        /// when not set.
        /// </summary>
        public Func<string, RuntimeActivity?>? MissionActivityLookup { get; set; } = null;

        /// <summary>
        /// Black box capture for manual transitions to Failed, LandingFailed, Cancelled or Blocked (step
        /// OperatorTransition), or null.
        /// </summary>
        public Armada.Core.BlackBox.IBlackBoxCapture? BlackBox { get; set; } = null;

        /// <summary>Resolve file and git operations on the dock's owning host.</summary>
        public DockHostResolver? DockHosts { get; set; } = null;

        /// <summary>
        /// Black box lifecycle hooks (retention cascades and expiry recomputes after deletes and updates), or null.
        /// </summary>
        public Armada.Core.BlackBox.IBlackBoxLifecycleHooks? BlackBoxHooks { get; set; } = null;

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
        /// <param name="emitMissionStatusChanged">mission.status_changed callback (mission after the change, previous status, message).</param>
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
            Func<Mission, MissionStatusEnum?, string, Task> emitMissionStatusChanged,
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
            _emitMissionStatusChanged = emitMissionStatusChanged ?? throw new ArgumentNullException(nameof(emitMissionStatusChanged));
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
            return MissionStateMachine.IsValidTransition(current, target);
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                MissionHistoryQuery query = new MissionHistoryQuery();
                if (DateTime.TryParse(Uri.UnescapeDataString(req.Query.GetValueOrDefault("fromUtc") ?? ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime fromUtc)) query.FromUtc = fromUtc.ToUniversalTime();
                if (DateTime.TryParse(Uri.UnescapeDataString(req.Query.GetValueOrDefault("toUtc") ?? ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime toUtc)) query.ToUtc = toUtc.ToUniversalTime();
                if (int.TryParse(req.Query.GetValueOrDefault("bucketMinutes"), out int bucketMinutes) && bucketMinutes > 0) query.BucketMinutes = bucketMinutes;
                string? fleetId = req.Query.GetValueOrDefault("fleetId");
                if (!String.IsNullOrEmpty(fleetId)) query.FleetId = fleetId;
                string? vesselId = req.Query.GetValueOrDefault("vesselId");
                if (!String.IsNullOrEmpty(vesselId)) query.VesselId = vesselId;

                // A window that would produce more than MaxSummaryBuckets buckets is a typed 400.
                try { query.Validate(); }
                catch (CollectionLimitExceededException limit) { return RouteErrorMapper.LimitExceeded(req, limit); }

                // Counted in the database per bucket and status (vessel and fleet filters included), so the server never
                // loads one row per mission.
                List<MissionHistoryBucketCount> counts = ctx.IsAdmin
                    ? await _database.Missions.CountHistoryBucketsAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.CountHistoryBucketsAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Missions.CountHistoryBucketsAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);

                return BuildMissionHistorySummary(query, counts);
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get aggregated mission history")
                .WithDescription("Returns aggregated mission counts by time bucket for dashboard history charts. Counted in the database. The window (fromUtc to toUtc) divided by bucketMinutes may produce at most 1000 buckets; a request that would produce more is rejected with 400 and Data.Code CollectionLimitExceeded.")
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Inclusive UTC start time", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Exclusive UTC end time", false))
                .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket size in minutes", false))
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Optional fleet filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithResponse(200, OpenApiJson.For<MissionHistorySummaryResult>("Mission history summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post<Mission>("/api/v1/missions", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                Mission mission = JsonSerializer.Deserialize<Mission>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Mission.");
                mission.TenantId = ctx.TenantId;
                mission.UserId = ctx.UserId;
                if (mission.WorkerEnvelope != null)
                {
                    // A mission may only tighten its vessel's Worker envelope; a looser or invalid one is a 400 with the reason.
                    Vessel? envelopeVessel = String.IsNullOrEmpty(mission.VesselId) ? null
                        : ctx.IsAdmin || String.IsNullOrEmpty(ctx.TenantId)
                            ? await _database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false)
                            : await _database.Vessels.ReadAsync(ctx.TenantId!, mission.VesselId).ConfigureAwait(false);
                    try { mission.WorkerEnvelope = WorkerEnvelopeRules.ValidateMissionEnvelope(envelopeVessel?.WorkerEnvelope, mission.WorkerEnvelope); }
                    catch (ArgumentException ex) { return RouteErrorMapper.ToResponse(req, ex); }
                }
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                mission.AssignmentBlocker = await ComputeAssignmentBlockerAsync(mission).ConfigureAwait(false);
                mission.Activity = MissionActivityLookup?.Invoke(mission.Id);
                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get a mission")
                .WithDescription("Returns a single mission by ID. A Pending mission carries AssignmentBlocker, the server's explanation of why no captain has taken it yet. A mission whose captain is running carries Activity, the captain's latest tool call, text, or reasoning when its runtime streams structured output.")
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get GitHub pull-request evidence for a mission")
                .WithDescription("Returns normalized GitHub pull-request evidence for the mission pull request: state, review status, the first 100 requested reviewers and labels with their full counts, and ReviewCount, CommentCount, and CheckCount. The reviews, comments, and checks themselves are read one page at a time from POST /api/v1/missions/{id}/github/pull-request/reviews/enumerate, .../comments/enumerate, and .../checks/enumerate.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiJson.For<GitHubPullRequestDetail>("GitHub pull-request evidence"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/missions/{id}/github/pull-request/reviews/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string missionId = req.Parameters["id"];
                EnumerationQuery query = EnumerationRequestReader.Read(req, _jsonOptions);
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
                    return await _gitHub.EnumerateMissionPullRequestReviewsAsync(ctx, mission, query).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Enumerate the reviews of a mission's GitHub pull request")
                .WithDescription("One page of the reviews submitted on the mission pull request, paged by GitHub with an exact TotalRecords. Page size 1 to 100 (GitHub's largest page; a larger value is normalized to 100), default 10. Order CreatedAscending is GitHub's order (oldest first); CreatedDescending (the default) is newest first.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<GitHubPullRequestReview>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/missions/{id}/github/pull-request/comments/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string missionId = req.Parameters["id"];
                EnumerationQuery query = EnumerationRequestReader.Read(req, _jsonOptions);
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
                    return await _gitHub.EnumerateMissionPullRequestCommentsAsync(ctx, mission, query).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Enumerate the comments of a mission's GitHub pull request")
                .WithDescription("One page of the issue comments on the mission pull request, paged by GitHub with an exact TotalRecords. Page size 1 to 100 (GitHub's largest page; a larger value is normalized to 100), default 10. Order CreatedAscending is GitHub's order (oldest first); CreatedDescending (the default) is newest first.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<GitHubPullRequestComment>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/missions/{id}/github/pull-request/checks/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string missionId = req.Parameters["id"];
                EnumerationQuery query = EnumerationRequestReader.Read(req, _jsonOptions);
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
                    return await _gitHub.EnumerateMissionPullRequestChecksAsync(ctx, mission, query).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
                }
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Enumerate the checks of a mission's GitHub pull request")
                .WithDescription("One page of the provider checks on the head commit of the mission pull request, paged by GitHub with an exact TotalRecords. Page size 1 to 100 (GitHub's largest page; a larger value is normalized to 100), default 10. Order CreatedAscending is GitHub's order (oldest first); CreatedDescending (the default) is newest first.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<GitHubPullRequestCheck>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/landing-preview", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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

                // Merge only metadata fields onto the existing record; operational, ownership, and timestamp fields
                // are preserved (shared with the WebSocket update_mission command).
                existing = EntityUpdateMerger.MergeMission(existing, incoming);
                if (incoming.WorkerEnvelope != null)
                {
                    // Omitted or null keeps the stored envelope; {} clears it. A change is validated like dispatch (400)
                    // and refused once the mission has started (409).
                    Vessel? envelopeVessel = String.IsNullOrEmpty(existing.VesselId) ? null
                        : ctx.IsAdmin || String.IsNullOrEmpty(ctx.TenantId)
                            ? await _database.Vessels.ReadAsync(existing.VesselId).ConfigureAwait(false)
                            : await _database.Vessels.ReadAsync(ctx.TenantId!, existing.VesselId).ConfigureAwait(false);
                    try { existing.WorkerEnvelope = WorkerEnvelopeRules.ValidateMissionEnvelopeUpdate(existing, envelopeVessel?.WorkerEnvelope, incoming.WorkerEnvelope); }
                    catch (ArgumentException ex) { return RouteErrorMapper.ToResponse(req, ex); }
                    catch (InvalidOperationException ex) { return RouteErrorMapper.ToResponse(req, ex, 409); }
                }
                existing = await _database.Missions.UpdateAsync(existing).ConfigureAwait(false);
                return (object)existing;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Update a mission")
                .WithDescription("Updates an existing mission by ID. workerEnvelope: omitted or null keeps the stored envelope, {} clears it; a changed envelope may only tighten the vessel default (400 otherwise) and only while the mission is Pending or Assigned and not started (409 otherwise).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Mission>("Updated mission data", true))
                .WithResponse(200, OpenApiJson.For<Mission>("Updated mission"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("workerEnvelope changed on a mission that has started"))
                .WithSecurity("ApiKey"));

            app.Put<StatusTransitionRequest>("/api/v1/missions/{id}/status", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Status is required" }; }

                if (!Enum.TryParse<MissionStatusEnum>(transition.Status, true, out MissionStatusEnum newStatus))
                    { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid status: " + transition.Status }; }

                MissionStatusEnum previousStatus = mission.Status;

                // Validate transitions
                bool valid = IsValidTransition(mission.Status, newStatus);
                if (!valid)
                    { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid transition from " + mission.Status + " to " + newStatus }; }

                string? handoffRefusal = ManualMissionHandoff.Refusal(mission, newStatus);
                if (handoffRefusal != null)
                { req.Http.Response.StatusCode = 409; return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = handoffRefusal }; }
                if ((newStatus == MissionStatusEnum.WorkProduced || newStatus == MissionStatusEnum.Complete)
                    && !MissionModeContract.IsReadOnly(mission.Mode))
                {
                    try { return (object)await ManualMissionHandoff.LandAsync(_database, _admiral, mission, newStatus, _git, _settings).ConfigureAwait(false); }
                    catch (InvalidOperationException ex)
                    { req.Http.Response.StatusCode = 409; return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message }; }
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
                await BlackBoxOperatorCapture.RecordTransitionAsync(BlackBox, mission, previousStatus, "REST").ConfigureAwait(false);

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

                // The black box capture already emitted mission.status_changed for a transition it captured.
                if (!BlackBoxOperatorCapture.EmitsStatusChanged(BlackBox, mission.Status))
                    await _emitMissionStatusChanged(mission, previousStatus, "Mission " + id + " transitioned to " + newStatus).ConfigureAwait(false);

                // Broadcast specific mission change for dashboard toast notifications
                if (_webSocketHub != null)
                {
                    _webSocketHub.BroadcastMissionChange(mission, newStatus.ToString());
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    _webSocketHub?.BroadcastMissionChange(mission, mission.Status.ToString());
                    return (object)mission;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                    _webSocketHub?.BroadcastMissionChange(mission, mission.Status.ToString());
                    return (object)mission;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 400);
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }
                try
                {
                    mission = await _admiral.CancelMissionAsync(mission).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message };
                }
                _webSocketHub?.BroadcastMissionChange(mission, MissionStatusEnum.Cancelled.ToString());
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                if (BlackBoxHooks != null) await BlackBoxHooks.OnMissionDeletedAsync(id).ConfigureAwait(false);

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
                    if (BlackBoxHooks != null) await BlackBoxHooks.OnMissionDeletedAsync(id).ConfigureAwait(false);
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
                .WithDescription("Permanently deletes multiple missions from the database by ID (at most 1000 ids; more is a 400 with Data.Code CollectionLimitExceeded). Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiJson.BodyFor<DeleteMultipleRequest>("List of mission IDs to delete"))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete result summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post<MissionRestartRequest>("/api/v1/missions/{id}/restart", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (mission == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" }; }

                if (mission.Status != MissionStatusEnum.Pending && mission.Status != MissionStatusEnum.Failed
                    && mission.Status != MissionStatusEnum.Cancelled && mission.Status != MissionStatusEnum.LandingFailed)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = "Only Pending, Failed, LandingFailed, or Cancelled missions can be restarted (current: " + mission.Status + ")" };
                }

                MissionRestartRequest? body = null;
                if (!String.IsNullOrWhiteSpace(req.Http.Request.DataAsString))
                {
                    try
                    {
                        body = JsonSerializer.Deserialize<MissionRestartRequest>(req.Http.Request.DataAsString, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        req.Http.Response.StatusCode = 400;
                        return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Request body is not a valid MissionRestartRequest." };
                    }
                }

                mission = await _missionService.RestartAsync(mission, body?.Title, body?.Description).ConfigureAwait(false);

                Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " restarted");
                await _database.Signals.CreateAsync(signal).ConfigureAwait(false);

                await _emitEvent("mission.restarted", "Mission " + id + " restarted",
                    "mission", id, null, id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);

                // Broadcast specific mission change for dashboard toast notifications
                if (_webSocketHub != null)
                {
                    _webSocketHub.BroadcastMissionChange(mission, MissionStatusEnum.Pending.ToString());
                }

                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Restart a failed or cancelled mission, or start a pending one")
                .WithDescription("Resets a Failed, LandingFailed, or Cancelled mission back to Pending (clearing captain assignment, branch, PR URL, and timing fields), or keeps a Pending mission as it is, then dispatches it at once when a captain is available. When no captain is available the mission stays Pending and the regular dispatcher picks it up later. Optionally update the title and description (instructions) before restarting. Any other status returns 409.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<MissionRestartRequest>("Optional updated instructions", false))
                .WithResponse(200, OpenApiJson.For<Mission>("Restarted mission (Pending, Assigned, or InProgress)"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/missions/{id}/retry-landing", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Mission? mission = ctx.IsAdmin
                    ? await _database.Missions.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Missions.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Missions.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
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
                        LandingModeEnum effectiveMode = landingVoyage?.LandingMode ?? landingVessel?.LandingMode ?? _settings.LandingMode ?? LandingModeEnum.MergeAndPush;
                        string branch = String.IsNullOrEmpty(mission.BranchName) ? "(unknown)" : mission.BranchName;

                        if (String.IsNullOrEmpty(mission.BranchName))
                        {
                            reason = "Mission has no branch name -- the branch may have been cleaned up, so there is nothing to land.";
                        }
                        else if (String.IsNullOrEmpty(mission.VesselId))
                        {
                            reason = "Mission has no vessel assigned, so its branch cannot be landed.";
                        }
                        else if (effectiveMode == LandingModeEnum.None)
                        {
                            // Not a failure: there is simply no automatic landing configured. The work is done
                            // and the branch is available for manual integration.
                            reason = "No automatic landing is configured for this vessel (landing mode: None)"
                                + ". The mission's work is complete and its branch '" + branch + "' is available in the repository for manual integration. "
                                + "To land it automatically, set a landing mode (Local Merge, Merge and Push, Pull Request, or Merge Queue) on the vessel or voyage and retry; "
                                + "or merge the branch yourself from the vessel's Manage Branches view.";
                        }
                        else if (mission.Status == MissionStatusEnum.LandingFailed && !String.IsNullOrWhiteSpace(mission.FailureReason))
                        {
                            reason = mission.FailureReason;
                        }
                        else if (mission.Status == MissionStatusEnum.LandingFailed)
                        {
                            reason = "Landing for mode " + effectiveMode + " failed to rebase or merge branch '" + branch
                                + "' onto the target -- the branch likely has conflicts that must be resolved before it can land.";
                        }
                        else if (mission.Status == MissionStatusEnum.WorkProduced)
                        {
                            reason = "Landing for mode " + effectiveMode + " did not complete for branch '" + branch
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
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Conflicts with the current state"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/diff", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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

                if (dock == null || String.IsNullOrEmpty(dock.WorktreePath))
                    { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No diff available: no mission worktree or saved diff exists" }; }

                DockHost? host = null;
                bool remote = DockHostResolver.IsHarborDock(dock);
                try
                {
                    if (remote)
                    {
                        if (DockHosts == null) throw new InvalidOperationException("Harbor delegation is not available.");
                        host = DockHosts.ForDock(dock);
                    }
                    bool exists = host != null
                        ? await host.Files.DirectoryExistsAsync(dock.WorktreePath).ConfigureAwait(false)
                        : Directory.Exists(dock.WorktreePath);
                    if (!exists)
                        { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No diff available: the worktree is not accessible on its owning host and no saved diff exists" }; }
                }
                catch (Exception ex) when (remote && (ex is InvalidOperationException || ex is TimeoutException || ex is IOException))
                {
                    _logging.Warn("Could not read mission diff on Harbor " + dock.HarborId + ": " + ex.Message);
                    req.Http.Response.StatusCode = 503;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.ServiceUnavailable, "The mission worktree is on Harbor " + dock.HarborId + ". Connect that Harbor, ensure it supports mission docks, and try again; its diff is currently unavailable.");
                }

                string baseBranch = "main";
                if (!String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? vessel = await _database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                    if (vessel != null) baseBranch = vessel.DefaultBranch;
                }

                try
                {
                    string diff = await (host?.Git ?? _git).DiffAsync(dock.WorktreePath, baseBranch).ConfigureAwait(false);
                    return (object)new { MissionId = id, Branch = dock.BranchName ?? "", Diff = diff };
                }
                catch (Exception ex) when (remote && (ex is InvalidOperationException || ex is TimeoutException || ex is IOException))
                {
                    _logging.Warn("Could not diff mission worktree on Harbor " + dock.HarborId + ": " + ex.Message);
                    req.Http.Response.StatusCode = 503;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.ServiceUnavailable, "The mission diff could not be read on Harbor " + dock.HarborId + ". Check its connection and mission log, then try again.");
                }
            },
            api => api
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Missions")
                .WithSummary("Get diff for a mission")
                .WithDescription("Returns a saved diff or reads the mission worktree on its owning Admiral or Harbor host. An unavailable Harbor returns 503; a missing worktree without a saved diff returns 404.")
                .WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("The owning Harbor is unavailable"))
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/log", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                        lineCount = Math.Clamp(parsedLines, 1, Constants.MaxLogLinesPerRequest);

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
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
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
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
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
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Missions")
                .WithSummary("Get mission instructions")
                .WithDescription("Returns the runtime-specific instruction file generated for a mission, such as CLAUDE.md, CODEX.md, CURSOR.md, AGENTS.md, GEMINI.md, or MUX.md.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private async Task<MissionAssignmentBlocker?> ComputeAssignmentBlockerAsync(Mission mission)
        {
            try
            {
                return await _missionService.GetAssignmentBlockerAsync(mission).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logging.Warn("[MissionRoutes] could not compute the assignment blocker for mission " + mission.Id + ": " + ex.Message);
                return null;
            }
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
                if (!MuxProtocolEvent.TryParse(raw, out MuxProtocolEvent? evt) || evt == null)
                {
                    FlushAssistantText(output, assistantText);
                    output.Add(raw);
                    continue;
                }

                switch (evt.EventType)
                {
                    case MuxProtocolEvent.AssistantText:
                        if (evt.Text != null) assistantText.Append(evt.Text);
                        break;

                    case MuxProtocolEvent.ToolCallProposed:
                        FlushAssistantText(output, assistantText);
                        if (evt.ToolCall != null)
                        {
                            string name = evt.ToolCall.Name ?? String.Empty;
                            string args = TruncateForLog(evt.ToolCall.Arguments, 400);
                            output.Add("> tool: " + name + (args.Length > 0 ? " " + args : String.Empty));
                        }
                        break;

                    case MuxProtocolEvent.ToolCallCompleted:
                        FlushAssistantText(output, assistantText);
                        {
                            string name = evt.ToolName ?? String.Empty;
                            bool ok = evt.Result?.Success != false;
                            string elapsed = evt.ElapsedMs.HasValue ? " (" + evt.ElapsedMs.Value.ToString("0", CultureInfo.InvariantCulture) + "ms)" : String.Empty;
                            output.Add("  " + (name.Length > 0 ? name + " " : String.Empty) + "-> " + (ok ? "ok" : "failed") + elapsed);
                        }
                        break;

                    case MuxProtocolEvent.RunStarted:
                        FlushAssistantText(output, assistantText);
                        output.Add("-- run started (" + (evt.Model ?? String.Empty) + ") --");
                        break;

                    case MuxProtocolEvent.RunCompleted:
                        FlushAssistantText(output, assistantText);
                        {
                            string dur = evt.DurationMs.HasValue ? evt.DurationMs.Value.ToString("0", CultureInfo.InvariantCulture) : "?";
                            string iters = evt.IterationsCompleted.HasValue ? evt.IterationsCompleted.Value.ToString(CultureInfo.InvariantCulture) : "?";
                            string calls = evt.ToolCallCount.HasValue ? evt.ToolCallCount.Value.ToString(CultureInfo.InvariantCulture) : "?";
                            output.Add("-- run completed: " + dur + "ms, " + iters + " iteration(s), " + calls + " tool call(s) --");
                        }
                        break;

                    // Protocol noise that adds nothing to a human transcript.
                    default:
                        break;
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

        private static string TruncateForLog(string? value, int max)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            string collapsed = value!.Replace("\r", " ").Replace("\n", " ");
            return collapsed.Length <= max ? collapsed : collapsed.Substring(0, max) + "...";
        }

        private static MissionHistorySummaryResult BuildMissionHistorySummary(MissionHistoryQuery query, IEnumerable<MissionHistoryBucketCount> counts)
        {
            DateTime fromUtc = query.FromUtcNormalized;
            DateTime toUtc = query.ToUtcNormalized;
            int bucketMinutes = query.EffectiveBucketMinutes;
            long bucketTicks = TimeSpan.FromMinutes(bucketMinutes).Ticks;

            MissionHistorySummaryResult result = new MissionHistorySummaryResult
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                BucketMinutes = bucketMinutes
            };

            SortedDictionary<long, MissionHistoryBucket> buckets = new SortedDictionary<long, MissionHistoryBucket>();
            long startTicks = query.AlignedStartUtc.Ticks;
            for (long ticks = startTicks; ticks < toUtc.Ticks; ticks += bucketTicks)
            {
                buckets[ticks] = new MissionHistoryBucket { StartUtc = new DateTime(ticks, DateTimeKind.Utc) };
            }

            foreach (MissionHistoryBucketCount count in counts)
            {
                long bucketStartTicks = startTicks + count.BucketIndex * bucketTicks;
                if (!buckets.TryGetValue(bucketStartTicks, out MissionHistoryBucket? bucket)) continue;
                int n = (int)Math.Min(int.MaxValue, count.Count);

                // "Complete" (green) covers every state where the captain has produced work or gone
                // further, not just fully-landed missions -- so with landing off, produced/reviewed work
                // still reads as done. Failed/LandingFailed are red; only genuinely in-flight (Pending/
                // Assigned/InProgress) and Cancelled are grey "Other".
                if (count.Status == MissionStatusEnum.WorkProduced
                    || count.Status == MissionStatusEnum.PullRequestOpen
                    || count.Status == MissionStatusEnum.Testing
                    || count.Status == MissionStatusEnum.Review
                    || count.Status == MissionStatusEnum.Complete)
                {
                    bucket.CompleteCount += n;
                    result.CompleteCount += n;
                }
                else if (count.Status == MissionStatusEnum.Failed || count.Status == MissionStatusEnum.LandingFailed)
                {
                    bucket.FailedCount += n;
                    result.FailedCount += n;
                }
                else
                {
                    bucket.OtherCount += n;
                    result.OtherCount += n;
                }
            }

            result.TotalCount = result.CompleteCount + result.FailedCount + result.OtherCount;
            result.Buckets = buckets.Values.ToList();
            return result;
        }
    }
}
