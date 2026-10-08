namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Server;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// REST API routes for vessel management.
    /// </summary>
    public class VesselRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly VesselReadinessService _readiness;
        private readonly LandingPreviewService _landingPreview;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IDockService? _dockService;
        private readonly VesselContextService? _contextService;
        private readonly IGitService? _git;
        private readonly ArmadaSettings? _settings;
        private readonly IVesselService _VesselService;
        private readonly ManualLandingReconciler? _ManualLandingReconciler;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="readiness">Readiness evaluation service.</param>
        /// <param name="landingPreview">Landing-preview evaluation service.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        /// <param name="dockService">Optional dock service for worktree cleanup during vessel deletion.</param>
        /// <param name="contextService">Optional service that builds/refines a vessel's Model Context.</param>
        /// <param name="git">Optional git service for branch management operations.</param>
        /// <param name="settings">Optional application settings for repository path resolution.</param>
        /// <param name="vesselService">Optional shared vessel creation service. Defaults to a new <see cref="VesselService"/>.</param>
        /// <param name="manualLandingReconciler">Optional reconciler that completes manual-landing missions after a branch merge.</param>
        public VesselRoutes(
            DatabaseDriver database,
            VesselReadinessService readiness,
            LandingPreviewService landingPreview,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            JsonSerializerOptions jsonOptions,
            IDockService? dockService = null,
            VesselContextService? contextService = null,
            IGitService? git = null,
            ArmadaSettings? settings = null,
            IVesselService? vesselService = null,
            ManualLandingReconciler? manualLandingReconciler = null)
        {
            _ManualLandingReconciler = manualLandingReconciler;
            _database = database;
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _landingPreview = landingPreview ?? throw new ArgumentNullException(nameof(landingPreview));
            _emitEvent = emitEvent;
            _jsonOptions = jsonOptions;
            _dockService = dockService;
            _contextService = contextService;
            _git = git;
            _settings = settings;
            _VesselService = vesselService ?? new VesselService(database);
        }

        /// <summary>
        /// Resolve the bare repository path for a vessel, where all mission branches live.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>The bare repository path, or null when it cannot be resolved or does not exist.</returns>
        private string? ResolveRepoPath(Vessel vessel)
        {
            if (!String.IsNullOrEmpty(vessel.LocalPath) && Directory.Exists(vessel.LocalPath)) return vessel.LocalPath;
            // A local-clone vessel (e.g. one created from a file:// URL, as the Armada self-rebuild vessel is)
            // carries only a WorkingDirectory -- the operator's own clone. Fall back to it so git operations
            // such as branch listing work, which is what populates the Rebuild page's branch picker.
            if (!String.IsNullOrEmpty(vessel.WorkingDirectory) && Directory.Exists(vessel.WorkingDirectory)) return vessel.WorkingDirectory;
            if (_settings != null && !String.IsNullOrEmpty(vessel.Name))
            {
                string candidate = Path.Combine(_settings.ReposDirectory, vessel.Name + ".git");
                if (Directory.Exists(candidate)) return candidate;
            }

            return null;
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
            // Vessels
            app.Get("/api/v1/vessels", async (ApiRequest req) =>
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
                EnumerationResult<Vessel> result = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(ctx, query, q => _database.Vessels.EnumerateAsync(q), (t, q) => _database.Vessels.EnumerateAsync(t, q), (t, u, q) => _database.Vessels.EnumerateAsync(t, u, q)).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("List all vessels")
                .WithDescription("Returns all registered vessels (git repositories), optionally filtered by fleet.")
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Filter by fleet ID", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Vessel>>("Paginated vessel list"))
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/vessels/enumerate", async (ApiRequest req) =>
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
                EnumerationResult<Vessel> result = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(ctx, query, q => _database.Vessels.EnumerateAsync(q), (t, q) => _database.Vessels.EnumerateAsync(t, q), (t, u, q) => _database.Vessels.EnumerateAsync(t, u, q)).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Enumerate vessels")
                .WithDescription("Paginated enumeration of vessels with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Post<Vessel>("/api/v1/vessels", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                Vessel vessel = JsonSerializer.Deserialize<Vessel>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Vessel.");
                if (String.IsNullOrEmpty(vessel.RepoUrl))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "repoUrl is required when creating a vessel" };
                }
                vessel.TenantId = ctx.TenantId;
                vessel.UserId = ctx.UserId;
                vessel = await _VesselService.CreateAsync(vessel, false).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return vessel;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Create a vessel")
                .WithDescription("Registers a new vessel (git repository) and returns it with an assigned ID.")
                .WithRequestBody(OpenApiJson.BodyFor<Vessel>("Vessel data", true))
                .WithResponse(201, OpenApiJson.For<Vessel>("Created vessel"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("A vessel with that name already exists (Data: DuplicateEntityErrorDetail)"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                return (object)vessel;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Get a vessel")
                .WithDescription("Returns a single vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Vessel>("Vessel details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put<Vessel>("/api/v1/vessels/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? existing = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                Vessel updated = JsonSerializer.Deserialize<Vessel>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Vessel.");
                updated = EntityUpdateMerger.MergeVessel(existing, updated);
                await DuplicateEntityGuard.EnsureVesselNameAvailableAsync(_database, updated).ConfigureAwait(false);
                updated = await _database.Vessels.UpdateAsync(updated).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Update a vessel")
                .WithDescription("Updates an existing vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Vessel>("Updated vessel data", true))
                .WithResponse(200, OpenApiJson.For<Vessel>("Updated vessel"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("A vessel with that name already exists (Data: DuplicateEntityErrorDetail)"))
                .WithSecurity("ApiKey"));

            app.Patch<Vessel>("/api/v1/vessels/{id}/context", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? existing = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                Vessel patch = JsonSerializer.Deserialize<Vessel>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Vessel.");
                if (patch.ProjectContext != null)
                    existing.ProjectContext = patch.ProjectContext;
                if (patch.StyleGuide != null)
                    existing.StyleGuide = patch.StyleGuide;
                if (patch.ModelContext != null)
                    existing.ModelContext = patch.ModelContext;
                existing = await _database.Vessels.UpdateAsync(existing).ConfigureAwait(false);
                return (object)existing;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Update vessel context")
                .WithDescription("Updates only the ProjectContext and StyleGuide fields of a vessel.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Vessel>("Vessel context data (projectContext, styleGuide)", true))
                .WithResponse(200, OpenApiJson.For<Vessel>("Updated vessel"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/git-status", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                if (String.IsNullOrEmpty(vessel.WorkingDirectory) || !Directory.Exists(vessel.WorkingDirectory))
                    return (object)new { VesselId = id, CommitsAhead = (int?)null, CommitsBehind = (int?)null, Error = "No working directory configured or directory does not exist" };

                try
                {
                    string baseBranch = vessel.DefaultBranch ?? "main";

                    // Fetch latest from remote (silent, best-effort)
                    try { await RunGitCommandAsync(vessel.WorkingDirectory, "fetch", "origin", "--quiet").ConfigureAwait(false); }
                    catch { /* ignore fetch failures -- offline or no remote */ }

                    string aheadStr = await RunGitCommandAsync(vessel.WorkingDirectory, "rev-list", "--count", "origin/" + baseBranch + "..HEAD").ConfigureAwait(false);
                    string behindStr = await RunGitCommandAsync(vessel.WorkingDirectory, "rev-list", "--count", "HEAD..origin/" + baseBranch).ConfigureAwait(false);

                    int.TryParse(aheadStr.Trim(), out int ahead);
                    int.TryParse(behindStr.Trim(), out int behind);

                    return (object)new { VesselId = id, CommitsAhead = ahead, CommitsBehind = behind };
                }
                catch (Exception ex)
                {
                    return (object)new { VesselId = id, CommitsAhead = (int?)null, CommitsBehind = (int?)null, Error = "Git error: " + ex.Message };
                }
            },
            api => api
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Vessels")
                .WithSummary("Get vessel git status")
                .WithDescription("Returns commits ahead/behind the remote default branch for the vessel's working directory.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/branches", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                if (_git == null) { req.Http.Response.StatusCode = 503; return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.ServiceUnavailable, "Git service is not available"); }

                string? repoPath = ResolveRepoPath(vessel);
                if (repoPath == null)
                    return new BranchListResponse { VesselId = id, DefaultBranch = vessel.DefaultBranch ?? "main", Error = "No repository found for this vessel" };

                try
                {
                    IReadOnlyList<BranchInfo> branches = await _git.ListBranchesAsync(repoPath, vessel.DefaultBranch ?? "main").ConfigureAwait(false);
                    return new BranchListResponse
                    {
                        VesselId = id,
                        DefaultBranch = vessel.DefaultBranch ?? "main",
                        Branches = new List<BranchInfo>(branches),
                        BranchCount = branches.Count
                    };
                }
                catch (Exception ex)
                {
                    return new BranchListResponse { VesselId = id, DefaultBranch = vessel.DefaultBranch ?? "main", Error = "Git error: " + ex.Message };
                }
            },
            api => api
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Vessels")
                .WithSummary("List vessel branches")
                .WithDescription("Returns the vessel repository's branches with current flag and ahead/behind counts relative to the default branch.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("Git service is not available (Error ServiceUnavailable)"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/history/activity", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                if (_git == null) { req.Http.Response.StatusCode = 503; return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.ServiceUnavailable, "Git service is not available"); }

                string branch = NormalizeEmpty(UnescapeQueryValue(req.Query.GetValueOrDefault("branch"))) ?? DefaultBranchOf(vessel);
                if (!GitRevisionNames.IsSafeBranchName(branch)) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "branch is not a valid branch name" }; }

                int offset = 0;
                string? offsetRaw = NormalizeEmpty(req.Query.GetValueOrDefault("utcOffsetMinutes"));
                if (offsetRaw != null && !Int32.TryParse(offsetRaw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out offset))
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "utcOffsetMinutes must be an integer" }; }
                if (Math.Abs(offset) > Constants.VesselHistoryMaxUtcOffsetMinutes)
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "utcOffsetMinutes must be between -" + Constants.VesselHistoryMaxUtcOffsetMinutes + " and " + Constants.VesselHistoryMaxUtcOffsetMinutes }; }

                string? fromRaw = NormalizeEmpty(req.Query.GetValueOrDefault("from"));
                string? toRaw = NormalizeEmpty(req.Query.GetValueOrDefault("to"));
                DateTime to = DateTime.UtcNow.AddMinutes(offset).Date;
                if (toRaw != null && !TryParseDay(toRaw, out to))
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "to must be a date in yyyy-MM-dd form" }; }
                DateTime from = to.AddDays(-364);
                if (fromRaw != null && !TryParseDay(fromRaw, out from))
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "from must be a date in yyyy-MM-dd form" }; }
                if (from > to)
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "from must not be after to" }; }
                if ((to - from).TotalDays > Constants.VesselHistoryMaxActivityRangeDays)
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "The range from..to must be at most " + Constants.VesselHistoryMaxActivityRangeDays + " days" }; }

                string? repoPath = ResolveRepoPath(vessel);
                if (repoPath == null)
                    return EmptyActivity(id, branch, from, to, offset, "No repository found for this vessel");

                try
                {
                    VesselCommitActivity activity = await _git.GetCommitActivityAsync(repoPath, branch, from, to, offset).ConfigureAwait(false);
                    activity.VesselId = id;
                    return activity;
                }
                catch (Exception ex)
                {
                    return EmptyActivity(id, branch, from, to, offset, "Git error: " + ex.Message);
                }
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Get vessel commit activity")
                .WithDescription("Returns per-day commit counts on a vessel branch for the history heatmap: one zero-filled entry per day from 'from' to 'to' inclusive, bucketed by committer date shifted by utcOffsetMinutes, plus the range total, the largest day count, and the committer dates of the first and last commits on the branch. Repository problems (no local clone yet, unknown branch, git failure) return 200 with Error set.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("branch", "Branch to read (default: the vessel's default branch, else main)", false))
                .WithParameter(OpenApiParameterMetadata.Query("from", "First day, yyyy-MM-dd (default: 364 days before 'to')", false))
                .WithParameter(OpenApiParameterMetadata.Query("to", "Last day, yyyy-MM-dd, inclusive (default: today in the offset); at most 1830 days after 'from'", false))
                .WithParameter(OpenApiParameterMetadata.Query("utcOffsetMinutes", "UTC offset in minutes to bucket days in, -840 to 840 (default 0)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<VesselCommitActivity>("Per-day commit counts (Error set for repository problems)"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("Invalid branch, dates, range, or offset (Error BadRequest)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("Git service is not available (Error ServiceUnavailable)"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/history/commits", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                if (_git == null) { req.Http.Response.StatusCode = 503; return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.ServiceUnavailable, "Git service is not available"); }

                int limit = Constants.VesselHistoryDefaultPageSize;
                string? limitRaw = NormalizeEmpty(req.Query.GetValueOrDefault("limit"));
                if (limitRaw != null && !Int32.TryParse(limitRaw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out limit))
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "limit must be an integer" }; }
                if (limit < 1 || limit > Constants.VesselHistoryMaxPageSize)
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "limit must be between 1 and " + Constants.VesselHistoryMaxPageSize }; }

                VesselCommitCursor? cursor = null;
                string? cursorRaw = NormalizeEmpty(UnescapeQueryValue(req.Query.GetValueOrDefault("cursor")));
                string branch;
                long? until = null;
                if (cursorRaw != null)
                {
                    if (!VesselCommitCursor.TryDecode(cursorRaw, out cursor) || cursor == null)
                    { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "cursor is not valid; pass a NextCursor value unchanged" }; }
                    branch = cursor.Branch;
                    until = cursor.Until;
                }
                else
                {
                    branch = NormalizeEmpty(UnescapeQueryValue(req.Query.GetValueOrDefault("branch"))) ?? DefaultBranchOf(vessel);
                    if (!GitRevisionNames.IsSafeBranchName(branch)) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "branch is not a valid branch name" }; }
                    string? beforeRaw = NormalizeEmpty(UnescapeQueryValue(req.Query.GetValueOrDefault("before")));
                    if (beforeRaw != null)
                    {
                        if (!TryParseBefore(beforeRaw, out long beforeUntil))
                        { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "before must be an ISO 8601 date or date-time (yyyy-MM-dd means the start of that day in UTC)" }; }
                        until = beforeUntil;
                    }
                }

                VesselCommitPage page = new VesselCommitPage { VesselId = id, Branch = branch };
                string? repoPath = ResolveRepoPath(vessel);
                if (repoPath == null)
                {
                    page.Error = "No repository found for this vessel";
                    return page;
                }

                try
                {
                    string? tip = cursor != null ? cursor.Tip : await _git.ResolveBranchTipAsync(repoPath, branch).ConfigureAwait(false);
                    if (tip == null)
                    {
                        page.Error = "Branch " + branch + " was not found or has no commits";
                        return page;
                    }

                    int skip = cursor != null ? cursor.Skip : 0;
                    GitCommitLogPage log = await _git.GetCommitLogAsync(repoPath, tip, until, skip, limit).ConfigureAwait(false);
                    page.Commits = log.Commits;
                    if (log.HasMore)
                    {
                        VesselCommitCursor next = new VesselCommitCursor { Branch = branch, Tip = tip, Until = until, Skip = skip + log.Commits.Count };
                        page.NextCursor = next.Encode();
                    }
                    return page;
                }
                catch (Exception ex)
                {
                    page.Error = "Git error: " + ex.Message;
                    return page;
                }
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("List vessel commits")
                .WithDescription("Returns one page of a vessel branch's commit history, newest commit date first (git log --date-order), each commit with its author, committer, dates, message, and changed files with added and deleted lines (merge commits against their first parent; at most 200 files listed per commit, FilesTruncated set beyond that). Follow NextCursor for older pages: the cursor pins the branch tip the first page resolved, so commits that land later do not shift paging; pass it alone (branch and before are then ignored). NextCursor is null on the last page. Repository problems return 200 with Error set.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("branch", "Branch to read (default: the vessel's default branch, else main); ignored with cursor", false))
                .WithParameter(OpenApiParameterMetadata.Query("before", "Only commits with a commit date strictly before this instant (ISO 8601; a bare yyyy-MM-dd means the start of that day in UTC); ignored with cursor", false))
                .WithParameter(OpenApiParameterMetadata.Query("cursor", "Opaque cursor from the previous page's NextCursor", false))
                .WithParameter(OpenApiParameterMetadata.Query("limit", "Page size, 1-200 (default 50)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<VesselCommitPage>("One page of commits (Error set for repository problems)"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("Invalid branch, before, cursor, or limit (Error BadRequest)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("Git service is not available (Error ServiceUnavailable)"))
                .WithSecurity("ApiKey"));

            app.Post<BranchActionRequest>("/api/v1/vessels/{id}/branches/push", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                if (_git == null) { req.Http.Response.StatusCode = 503; return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.ServiceUnavailable, "Git service is not available"); }
                BranchActionRequest pushBody = JsonSerializer.Deserialize<BranchActionRequest>(req.Http.Request.DataAsString, _jsonOptions) ?? new BranchActionRequest();
                if (String.IsNullOrWhiteSpace(pushBody.Branch)) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Branch is required" }; }

                string? repoPath = ResolveRepoPath(vessel);
                if (repoPath == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No repository found for this vessel" }; }

                try
                {
                    await _git.PushLocalBranchAsync(repoPath, pushBody.Branch!).ConfigureAwait(false);
                    return new BranchPushResponse { VesselId = id, Branch = pushBody.Branch!, Pushed = true };
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 422;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.UnprocessableEntity, "Push failed: " + ex.Message);
                }
            },
            api => api
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Vessels")
                .WithSummary("Push a vessel branch")
                .WithDescription("Pushes the named local branch to the vessel's remote.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(422, OpenApiJson.For<ApiStatusErrorResponse>("Rejected by the remote (Error UnprocessableEntity)"))
                .WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("Git service is not available (Error ServiceUnavailable)"))
                .WithSecurity("ApiKey"));

            app.Post<BranchMergeRequest>("/api/v1/vessels/{id}/branches/merge", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                if (_git == null) { req.Http.Response.StatusCode = 503; return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.ServiceUnavailable, "Git service is not available"); }
                BranchMergeRequest mergeBody = JsonSerializer.Deserialize<BranchMergeRequest>(req.Http.Request.DataAsString, _jsonOptions) ?? new BranchMergeRequest();
                if (String.IsNullOrWhiteSpace(mergeBody.Source) || String.IsNullOrWhiteSpace(mergeBody.Target))
                { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Source and target are required" }; }

                string? repoPath = ResolveRepoPath(vessel);
                if (repoPath == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No repository found for this vessel" }; }

                try
                {
                    await _git.MergeBranchesAsync(repoPath, mergeBody.Source!, mergeBody.Target!, mergeBody.Push).ConfigureAwait(false);

                    // A manual-landing mission whose branch was just merged is done: complete it now rather than on the
                    // next health check, so its page shows Complete when the operator returns to it.
                    if (_ManualLandingReconciler != null)
                    {
                        try { await _ManualLandingReconciler.ReconcileAsync(vessel.Id).ConfigureAwait(false); }
                        catch (Exception)
                        {
                            // The merge itself succeeded; the reconciler logs per-mission problems and the health check retries.
                        }
                    }

                    return new BranchMergeResponse { VesselId = id, Source = mergeBody.Source!, Target = mergeBody.Target!, Merged = true, Pushed = mergeBody.Push };
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 422;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.UnprocessableEntity, "Merge failed: " + ex.Message);
                }
            },
            api => api
                .WithResponse(200, OpenApiResponseMetadata.Create("Successful response"))
                .WithTag("Vessels")
                .WithSummary("Merge a vessel branch into another")
                .WithDescription("Merges the source branch into the target branch, optionally pushing the target.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(422, OpenApiJson.For<ApiStatusErrorResponse>("Rejected by the remote (Error UnprocessableEntity)"))
                .WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("Git service is not available (Error ServiceUnavailable)"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/readiness", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                string? explicitProfileId = NormalizeEmpty(req.Query.GetValueOrDefault("workflowProfileId"));
                string? environmentName = NormalizeEmpty(req.Query.GetValueOrDefault("environmentName"));
                bool includeWorkflowRequirements = ParseBoolean(req.Query.GetValueOrDefault("includeWorkflowRequirements"), true);

                CheckRunTypeEnum? checkType = null;
                string? checkTypeRaw = NormalizeEmpty(req.Query.GetValueOrDefault("checkType"));
                if (!String.IsNullOrWhiteSpace(checkTypeRaw))
                {
                    if (!Enum.TryParse(checkTypeRaw, true, out CheckRunTypeEnum parsedCheckType))
                    {
                        req.Http.Response.StatusCode = 400;
                        return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid checkType" };
                    }

                    checkType = parsedCheckType;
                }

                return await _readiness.EvaluateAsync(
                    ctx,
                    vessel,
                    explicitProfileId,
                    checkType,
                    environmentName,
                    includeWorkflowRequirements).ConfigureAwait(false);
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Get vessel readiness")
                .WithDescription("Returns readiness warnings and blocking issues for a vessel, optionally scoped to a requested workflow check.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("workflowProfileId", "Optional explicit workflow-profile override", false))
                .WithParameter(OpenApiParameterMetadata.Query("checkType", "Optional check type to evaluate as a preflight", false))
                .WithParameter(OpenApiParameterMetadata.Query("environmentName", "Optional environment name for deploy, rollback, smoke-test, or health-check readiness", false))
                .WithParameter(OpenApiParameterMetadata.Query("includeWorkflowRequirements", "When false, only vessel and repository basics are evaluated", false, OpenApiSchemaMetadata.Boolean()))
                .WithResponse(200, OpenApiJson.For<VesselReadinessResult>("Vessel readiness summary"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/{id}/landing-preview", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                string? sourceBranch = NormalizeEmpty(UnescapeQueryValue(req.Query.GetValueOrDefault("sourceBranch")));
                return await _landingPreview.PreviewForVesselAsync(ctx, vessel, sourceBranch).ConfigureAwait(false);
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Preview landing readiness")
                .WithDescription("Predicts how Armada would land a branch for this vessel, including branch policy, check requirements, and likely blockers.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("sourceBranch", "Optional branch name to preview", false))
                .WithResponse(200, OpenApiJson.For<LandingPreviewResult>("Landing preview"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<VesselBuildContextRequest>("/api/v1/vessels/{id}/build-context", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                if (_contextService == null)
                {
                    req.Http.Response.StatusCode = 501;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.NotImplemented, "Model Context building is not available on this server.");
                }

                string buildId = req.Parameters["id"];
                Vessel? buildVessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(buildId).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, buildId).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, buildId).ConfigureAwait(false);
                if (buildVessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                VesselBuildContextRequest request = JsonSerializer.Deserialize<VesselBuildContextRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? new VesselBuildContextRequest();
                if (String.IsNullOrWhiteSpace(request.CaptainId))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "captainId is required." };
                }

                try
                {
                    Vessel updated = await _contextService.BuildAsync(buildVessel.Id, request.CaptainId, request.Notes, ctx.UserId).ConfigureAwait(false);
                    return (object)updated;
                }
                catch (TimeoutException ex)
                {
                    req.Http.Response.StatusCode = 504;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.GatewayTimeout, ex.Message);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Build or refine the vessel Model Context")
                .WithDescription("Launches the chosen captain in a worktree of the vessel repository to analyze it and write a Model Context document. Refines the existing context when one is present. Runs synchronously and can take several minutes.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<VesselBuildContextRequest>("Build context request", true))
                .WithResponse(200, OpenApiJson.For<Vessel>("Updated vessel with new Model Context"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("Model context building is not available on this server (Error NotImplemented)"))
                .WithResponse(504, OpenApiJson.For<ApiStatusErrorResponse>("Model context build timed out (Error GatewayTimeout)"))
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/vessels/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }

                await CleanupVesselResourcesAsync(vessel).ConfigureAwait(false);

                if (ctx.IsAdmin)
                    await _database.Vessels.DeleteAsync(id).ConfigureAwait(false);
                else if (ctx.IsTenantAdmin)
                    await _database.Vessels.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                else
                    await _database.Vessels.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Delete a vessel")
                .WithDescription("Deletes a vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<DeleteMultipleRequest>("/api/v1/vessels/delete/multiple", async (ApiRequest req) =>
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

                DeleteMultipleResult result = new DeleteMultipleResult();
                foreach (string id in body.Ids)
                {
                    if (String.IsNullOrEmpty(id))
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                        continue;
                    }
                    Vessel? existing = ctx.IsAdmin
                        ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                            : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    if (existing == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }

                    await CleanupVesselResourcesAsync(existing).ConfigureAwait(false);

                    if (ctx.IsAdmin)
                        await _database.Vessels.DeleteAsync(id).ConfigureAwait(false);
                    else if (ctx.IsTenantAdmin)
                        await _database.Vessels.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    else
                        await _database.Vessels.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await _emitEvent("vessel.batch_deleted", "Batch deleted " + result.Deleted + " vessels",
                    "vessel", null, null, null, null, null).ConfigureAwait(false);

                result.ResolveStatus();
                return (object)result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Batch delete multiple vessels")
                .WithDescription("Permanently deletes multiple vessels from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiJson.BodyFor<DeleteMultipleRequest>("List of vessel IDs to delete"))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete result summary"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));
        }

        /// <summary>
        /// Cleans up filesystem and database resources associated with a vessel before deletion.
        /// Removes docks/worktrees, the bare repository, and cancels active missions.
        /// Cleanup failures are silently caught to avoid blocking the vessel delete.
        /// </summary>
        /// <summary>
        /// Cleans up ALL resources associated with a vessel. Throws on failure.
        /// </summary>
        private async Task CleanupVesselResourcesAsync(Vessel vessel)
        {
            List<string> errors = new List<string>();

            // Cancel and delete all missions
            List<Mission> missions = await _database.Missions.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
            foreach (Mission mission in missions)
            {
                if (mission.Status == Armada.Core.Enums.MissionStatusEnum.Pending
                    || mission.Status == Armada.Core.Enums.MissionStatusEnum.Assigned
                    || mission.Status == Armada.Core.Enums.MissionStatusEnum.InProgress
                    || mission.Status == Armada.Core.Enums.MissionStatusEnum.Review
                    || mission.Status == Armada.Core.Enums.MissionStatusEnum.Testing)
                {
                    mission.Status = Armada.Core.Enums.MissionStatusEnum.Cancelled;
                    mission.FailureReason = "Vessel deleted";
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                }
                try { await _database.Missions.DeleteAsync(mission.Id).ConfigureAwait(false); }
                catch (Exception ex) { errors.Add("Mission " + mission.Id + ": " + ex.Message); }
                try { await CascadeCleanup.RemoveEventsForMissionAsync(_database, mission.Id).ConfigureAwait(false); }
                catch (Exception ex) { errors.Add("Mission events " + mission.Id + ": " + ex.Message); }
            }

            // Remove telemetry events that referenced this vessel so they do not dangle.
            try { await CascadeCleanup.RemoveEventsForVesselAsync(_database, vessel.Id).ConfigureAwait(false); }
            catch (Exception ex) { errors.Add("Vessel events " + vessel.Id + ": " + ex.Message); }

            // Purge docks
            List<Dock> docks = await _database.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
            foreach (Dock dock in docks)
            {
                try
                {
                    if (_dockService != null)
                        await _dockService.PurgeAsync(dock.Id).ConfigureAwait(false);
                    else
                    {
                        if (!String.IsNullOrEmpty(dock.WorktreePath) && Directory.Exists(dock.WorktreePath))
                            Directory.Delete(dock.WorktreePath, true);
                        await _database.Docks.DeleteAsync(dock.Id).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) { errors.Add("Dock " + dock.Id + ": " + ex.Message); }
            }

            // Delete vessel dock directory
            // Only directories inside the managed docks and repos roots are ever removed (LocalPath and Name are
            // caller-controlled).
            string docksRoot = _settings?.DocksDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".armada", "docks");
            string reposRoot = _settings?.ReposDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".armada", "repos");
            string vesselDockDir = Path.Combine(docksRoot, vessel.Name);
            if (Directory.Exists(vesselDockDir))
            {
                if (!ManagedPaths.IsStrictlyUnder(vesselDockDir, docksRoot))
                    errors.Add("Dock dir left in place (outside the managed docks directory): " + vesselDockDir);
                else
                {
                    try { Directory.Delete(vesselDockDir, true); }
                    catch (Exception ex) { errors.Add("Dock dir: " + ex.Message); }
                }
            }

            // Delete bare repo
            if (!String.IsNullOrEmpty(vessel.LocalPath) && Directory.Exists(vessel.LocalPath))
            {
                if (!ManagedPaths.IsStrictlyUnder(vessel.LocalPath, reposRoot))
                    errors.Add("Bare repo left in place (outside the managed repos directory): " + vessel.LocalPath);
                else
                {
                    try { Directory.Delete(vessel.LocalPath, true); }
                    catch (Exception ex) { errors.Add("Bare repo: " + ex.Message); }

                    if (Directory.Exists(vessel.LocalPath))
                        errors.Add("Bare repo still exists after deletion: " + vessel.LocalPath);
                }
            }

            // Log warnings but don't block deletion -- orphan filesystem cleanup
            // can happen on next server restart. The vessel DB record must be deleted.
        }

        /// <summary>
        /// Run a git command and return stdout.
        /// </summary>
        private static async Task<string> RunGitCommandAsync(string workingDirectory, params string[] args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in args) psi.ArgumentList.Add(arg);
            Armada.Core.Services.GitProcessEnvironment.Apply(psi);

            using (Process process = Process.Start(psi)!)
            {
                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    throw new InvalidOperationException("git exited with code " + process.ExitCode + ": " + error.Trim());
                }
                return output;
            }
        }

        private static string? UnescapeQueryValue(string? value)
        {
            // Query values arrive percent-encoded (a branch such as feature/x is sent as feature%2Fx).
            if (String.IsNullOrEmpty(value)) return value;
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return value;
            }
        }

        private static string DefaultBranchOf(Vessel vessel)
        {
            return String.IsNullOrWhiteSpace(vessel.DefaultBranch) ? "main" : vessel.DefaultBranch.Trim();
        }

        private static bool TryParseDay(string value, out DateTime day)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }

        /// <summary>
        /// Parse the history before filter (an exclusive instant) into git's inclusive --until bound in Unix seconds:
        /// commit dates are whole seconds, so "strictly before T" is "at or before ceil(T) - 1".
        /// </summary>
        private static bool TryParseBefore(string value, out long untilUnixSeconds)
        {
            untilUnixSeconds = 0;
            DateTimeOffset instant;
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
            {
                instant = new DateTimeOffset(day, TimeSpan.Zero);
            }
            else if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out instant))
            {
                return false;
            }

            long milliseconds = instant.ToUnixTimeMilliseconds();
            long ceilingSeconds = milliseconds / 1000L + (milliseconds % 1000L > 0 ? 1L : 0L);
            untilUnixSeconds = ceilingSeconds - 1L;
            return true;
        }

        private static VesselCommitActivity EmptyActivity(string vesselId, string branch, DateTime from, DateTime to, int offset, string error)
        {
            VesselCommitActivity activity = new VesselCommitActivity();
            activity.VesselId = vesselId;
            activity.Branch = branch;
            activity.From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            activity.To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            activity.UtcOffsetMinutes = offset;
            for (DateTime day = from; day <= to; day = day.AddDays(1))
                activity.Days.Add(new VesselCommitActivityDay { Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            activity.Error = error;
            return activity;
        }

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool ParseBoolean(string? value, bool defaultValue)
        {
            if (String.IsNullOrWhiteSpace(value)) return defaultValue;
            return Boolean.TryParse(value, out bool parsed) ? parsed : defaultValue;
        }
    }
}
