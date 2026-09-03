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
    using Armada.Runtimes;

    /// <summary>
    /// REST API routes for captain management.
    /// </summary>
    public class CaptainRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly IAdmiralService _admiral;
        private readonly ArmadaSettings _settings;
        private readonly AgentRuntimeFactory _runtimeFactory;
        private readonly AgentLifecycleHandler _agentLifecycle;
        private readonly CaptainToolService _captainTools;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly PlanningSessionCoordinator? _planningSessions;
        private readonly ObjectiveRefinementCoordinator? _objectiveRefinementSessions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral coordination service.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="runtimeFactory">Agent runtime factory.</param>
        /// <param name="agentLifecycle">Agent lifecycle handler used for model validation.</param>
        /// <param name="captainTools">Captain tool availability service.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        /// <param name="planningSessions">Optional planning session coordinator for captain-planning ownership handoff.</param>
        /// <param name="objectiveRefinementSessions">Optional objective refinement coordinator for captain-refinement ownership handoff.</param>
        public CaptainRoutes(
            DatabaseDriver database,
            IAdmiralService admiral,
            ArmadaSettings settings,
            AgentRuntimeFactory runtimeFactory,
            AgentLifecycleHandler agentLifecycle,
            CaptainToolService captainTools,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            JsonSerializerOptions jsonOptions,
            PlanningSessionCoordinator? planningSessions = null,
            ObjectiveRefinementCoordinator? objectiveRefinementSessions = null)
        {
            _database = database;
            _admiral = admiral;
            _settings = settings;
            _runtimeFactory = runtimeFactory;
            _agentLifecycle = agentLifecycle;
            _captainTools = captainTools;
            _emitEvent = emitEvent;
            _jsonOptions = jsonOptions;
            _planningSessions = planningSessions;
            _objectiveRefinementSessions = objectiveRefinementSessions;
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
            // Captains
            app.Get("/api/v1/captains", async (ApiRequest req) =>
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
                EnumerationResult<Captain> result = ctx.IsAdmin
                    ? await _database.Captains.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Captains.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("List all captains")
                .WithDescription("Returns all registered captains (AI agents) with their current state.")
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Captain>>("Paginated captain list"))
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/captains/enumerate", async (ApiRequest req) =>
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
                EnumerationResult<Captain> result = ctx.IsAdmin
                    ? await _database.Captains.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Captains.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Enumerate captains")
                .WithDescription("Paginated enumeration of captains with optional filtering and sorting.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Post<Captain>("/api/v1/captains", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                Captain captain = JsonSerializer.Deserialize<Captain>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Captain.");
                captain.TenantId = ctx.TenantId;
                captain.UserId = ctx.UserId;
                NormalizeCaptainRuntimeOptions(captain);
                string? createValidationError = await _agentLifecycle.ValidateCaptainModelAsync(captain).ConfigureAwait(false);
                if (createValidationError != null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = createValidationError };
                }
                captain = await _database.Captains.CreateAsync(captain).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return captain;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Create a captain")
                .WithDescription("Registers a new captain (AI agent).")
                .WithRequestBody(OpenApiJson.BodyFor<Captain>("Captain data", true))
                .WithResponse(201, OpenApiJson.For<Captain>("Created captain"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/captains/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Captain? captain = ctx.IsAdmin
                    ? await _database.Captains.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (captain == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" }; }
                return (object)captain;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Get a captain")
                .WithDescription("Returns a single captain by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Captain>("Captain details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/captains/{id}/tools", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Captain? captain = ctx.IsAdmin
                    ? await _database.Captains.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (captain == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" }; }
                return await _captainTools.DescribeAsync(captain).ConfigureAwait(false);
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Describe captain tools")
                .WithDescription("Returns the runtime-visible tool sources and named tools available to the selected captain, including runtime-specific availability notes.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithResponse(200, OpenApiJson.For<CaptainToolAccessResult>("Captain runtime tool availability and inventory"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put<Captain>("/api/v1/captains/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Captain? existing = ctx.IsAdmin
                    ? await _database.Captains.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" }; }
                Captain updated = JsonSerializer.Deserialize<Captain>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Captain.");
                updated.Id = id;
                updated.TenantId = existing.TenantId;
                updated.UserId = existing.UserId;
                updated.State = existing.State;
                updated.CurrentMissionId = existing.CurrentMissionId;
                updated.CurrentDockId = existing.CurrentDockId;
                updated.ProcessId = existing.ProcessId;
                updated.RecoveryAttempts = existing.RecoveryAttempts;
                updated.LastHeartbeatUtc = existing.LastHeartbeatUtc;
                updated.CreatedUtc = existing.CreatedUtc;
                updated.LastUpdateUtc = DateTime.UtcNow;
                NormalizeCaptainRuntimeOptions(updated, existing);
                string? updateValidationError = await _agentLifecycle.ValidateCaptainModelAsync(updated).ConfigureAwait(false);
                if (updateValidationError != null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = updateValidationError };
                }
                updated = await _database.Captains.UpdateAsync(updated).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Update a captain")
                .WithDescription("Updates a captain's name, runtime, or max parallelism. Operational fields (state, process, mission) are preserved.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Captain>("Updated captain data", true))
                .WithResponse(200, OpenApiJson.For<Captain>("Updated captain"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/captains/{id}/unquarantine", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string uqId = req.Parameters["id"];
                Captain? uqCaptain = ctx.IsAdmin
                    ? await _database.Captains.ReadAsync(uqId).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.ReadAsync(ctx.TenantId!, uqId).ConfigureAwait(false)
                        : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, uqId).ConfigureAwait(false);
                if (uqCaptain == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" }; }

                if (uqCaptain.State != CaptainStateEnum.Quarantined)
                    return (object)new { Status = "not_quarantined", CaptainId = uqCaptain.Id };

                uqCaptain.State = CaptainStateEnum.Idle;
                uqCaptain.QuarantineUntilUtc = null;
                uqCaptain.QuarantineReason = null;
                uqCaptain.LastUpdateUtc = DateTime.UtcNow;
                uqCaptain = await _database.Captains.UpdateAsync(uqCaptain).ConfigureAwait(false);
                return (object)uqCaptain;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Lift a captain's quarantine")
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/captains/{id}/stop", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Captain? captain = ctx.IsAdmin
                    ? await _database.Captains.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (captain == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" }; }

                if (captain.State == CaptainStateEnum.Planning)
                {
                    PlanningSession? planningSession = (await _database.PlanningSessions.EnumerateByCaptainAsync(captain.Id).ConfigureAwait(false))
                        .Where(s =>
                            s.Status == PlanningSessionStatusEnum.Active ||
                            s.Status == PlanningSessionStatusEnum.Responding ||
                            s.Status == PlanningSessionStatusEnum.Stopping)
                        .OrderByDescending(s => s.LastUpdateUtc)
                        .FirstOrDefault();

                    if (planningSession == null || _planningSessions == null)
                    {
                        req.Http.Response.StatusCode = 409;
                        return (object)new { Error = "Conflict", Message = "Captain is currently reserved by a planning session, but Armada could not resolve that session for coordinated stop." };
                    }

                    PlanningSession stopped = await _planningSessions.StopAsync(planningSession).ConfigureAwait(false);
                    return new { Status = "stopped", PlanningSessionId = stopped.Id };
                }
                else if (captain.State == CaptainStateEnum.Refining)
                {
                    ObjectiveRefinementSession? refinementSession = (await _database.ObjectiveRefinementSessions.EnumerateByCaptainAsync(captain.Id).ConfigureAwait(false))
                        .Where(s =>
                            s.Status == ObjectiveRefinementSessionStatusEnum.Active ||
                            s.Status == ObjectiveRefinementSessionStatusEnum.Responding ||
                            s.Status == ObjectiveRefinementSessionStatusEnum.Stopping)
                        .OrderByDescending(s => s.LastUpdateUtc)
                        .FirstOrDefault();

                    if (refinementSession == null || _objectiveRefinementSessions == null)
                    {
                        req.Http.Response.StatusCode = 409;
                        return (object)new { Error = "Conflict", Message = "Captain is currently reserved by an objective refinement session, but Armada could not resolve that session for coordinated stop." };
                    }

                    ObjectiveRefinementSession stopped = await _objectiveRefinementSessions.StopAsync(refinementSession).ConfigureAwait(false);
                    return new { Status = "stopped", ObjectiveRefinementSessionId = stopped.Id };
                }

                // Kill the process if running
                if (captain.ProcessId.HasValue)
                {
                    Armada.Runtimes.Interfaces.IAgentRuntime runtime = _runtimeFactory.Create(captain.Runtime);
                    await runtime.StopAsync(captain.ProcessId.Value).ConfigureAwait(false);
                }

                await _admiral.RecallCaptainAsync(id).ConfigureAwait(false);
                return new { Status = "stopped" };
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Stop a captain")
                .WithDescription("Stops a running captain agent, killing its process and recalling it to idle state.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/captains/stop-all", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                await _admiral.RecallAllAsync().ConfigureAwait(false);

                if (_planningSessions != null)
                {
                    List<PlanningSession> planningSessions = await _database.PlanningSessions.EnumerateAsync().ConfigureAwait(false);
                    foreach (PlanningSession planningSession in planningSessions.Where(s =>
                        s.Status == PlanningSessionStatusEnum.Active ||
                        s.Status == PlanningSessionStatusEnum.Responding ||
                        s.Status == PlanningSessionStatusEnum.Stopping))
                    {
                        try
                        {
                            await _planningSessions.StopAsync(planningSession).ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                    }
                }

                if (_objectiveRefinementSessions != null)
                {
                    List<ObjectiveRefinementSession> refinementSessions = await _database.ObjectiveRefinementSessions.EnumerateAsync().ConfigureAwait(false);
                    foreach (ObjectiveRefinementSession refinementSession in refinementSessions.Where(s =>
                        s.Status == ObjectiveRefinementSessionStatusEnum.Active ||
                        s.Status == ObjectiveRefinementSessionStatusEnum.Responding ||
                        s.Status == ObjectiveRefinementSessionStatusEnum.Stopping))
                    {
                        try
                        {
                            await _objectiveRefinementSessions.StopAsync(refinementSession).ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                    }
                }

                return (object)new { Status = "all_stopped" };
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Stop all captains")
                .WithDescription("Emergency stop all running captains, recalling them to idle state.")
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/captains/{id}/log", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Captain? captain = ctx.IsAdmin
                    ? await _database.Captains.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (captain == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" }; }

                string pointerPath = Path.Combine(_settings.LogDirectory, "captains", id + ".current");
                string? logPath = null;

                if (File.Exists(pointerPath))
                {
                    string target = (await ReadFileSharedAsync(pointerPath).ConfigureAwait(false)).Trim();
                    if (File.Exists(target))
                        logPath = target;
                }

                if (logPath == null)
                    return (object)new { CaptainId = id, Log = "", Lines = 0, TotalLines = 0 };

                try
                {
                    string[] allLines = await ReadLinesSharedAsync(logPath).ConfigureAwait(false);
                    int totalLines = allLines.Length;

                    int offset = 0;
                    int lineCount = 50;

                    string? offsetParam = req.Query.GetValueOrDefault("offset");
                    if (!String.IsNullOrEmpty(offsetParam) && Int32.TryParse(offsetParam, out int parsedOffset))
                        offset = Math.Max(0, parsedOffset);

                    string? linesParam = req.Query.GetValueOrDefault("lines");
                    if (!String.IsNullOrEmpty(linesParam) && Int32.TryParse(linesParam, out int parsedLines))
                        lineCount = Math.Max(1, parsedLines);

                    string[] slice = allLines.Skip(offset).Take(lineCount).ToArray();

                    // ?formatted=true applies the readable formatter: resolves tool names out of runtime
                    // JSONL, redacts secret-shaped values, truncates oversized payloads, and drops noise.
                    bool formatted = String.Equals(req.Query.GetValueOrDefault("formatted"), "true", StringComparison.OrdinalIgnoreCase);
                    if (formatted)
                    {
                        List<string> formattedLines = new List<string>();
                        List<object> entries = new List<object>();
                        foreach (string raw in slice)
                        {
                            Armada.Core.Services.FormattedLogLine fl = Armada.Core.Services.RuntimeLogFormatter.Format(raw, captain.Runtime);
                            if (fl.Dropped) continue;
                            formattedLines.Add(fl.Text);
                            entries.Add(new { Text = fl.Text, IsToolCall = fl.IsToolCall, ToolName = fl.ToolName, Redacted = fl.Redacted, Truncated = fl.Truncated });
                        }
                        // Log preserves the joined text for existing consumers; Entries carries structured
                        // lines (tool name, redaction/truncation flags) so the dashboard can render chips.
                        return (object)new { CaptainId = id, Log = String.Join("\n", formattedLines), Entries = entries, Lines = formattedLines.Count, TotalLines = totalLines };
                    }

                    string log = String.Join("\n", slice);
                    return (object)new { CaptainId = id, Log = log, Lines = slice.Length, TotalLines = totalLines };
                }
                catch (IOException)
                {
                    return (object)new { CaptainId = id, Log = "", Lines = 0, TotalLines = 0 };
                }
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Get current log for a captain")
                .WithDescription("Returns the current session log for a captain, resolved via the .current pointer file. Supports pagination via ?lines=N (default 50) and ?offset=N.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/captains/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Captain? captain = ctx.IsAdmin
                    ? await _database.Captains.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Captains.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (captain == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" }; }

                // Block deletion of working captains
                if (captain.State == CaptainStateEnum.Working || captain.State == CaptainStateEnum.Planning || captain.State == CaptainStateEnum.Refining)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot delete captain while state is Working, Planning, or Refining. Stop the captain first." };
                }

                // Block deletion if captain has active missions
                List<Mission> captainMissions = ctx.IsAdmin
                    ? await _database.Missions.EnumerateByCaptainAsync(id).ConfigureAwait(false)
                    : await _database.Missions.EnumerateByCaptainAsync(ctx.TenantId!, id).ConfigureAwait(false);
                List<Mission> activeCaptainMissions = captainMissions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress).ToList();
                if (activeCaptainMissions.Count > 0)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot delete captain with " + activeCaptainMissions.Count + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };
                }

                if (ctx.IsAdmin)
                    await _database.Captains.DeleteAsync(id).ConfigureAwait(false);
                else if (ctx.IsTenantAdmin)
                    await _database.Captains.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                else
                    await _database.Captains.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);

                // Remove dependents (telemetry events + planning sessions) that referenced this captain.
                await CascadeCleanup.RemoveDependentsForCaptainAsync(_database, id).ConfigureAwait(false);

                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Delete a captain")
                .WithDescription("Deletes a captain. Blocked if captain is Working or has active missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<object>("Captain cannot be deleted while active"))
                .WithSecurity("ApiKey"));

            app.Post<DeleteMultipleRequest>("/api/v1/captains/delete/multiple", async (ApiRequest req) =>
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
                    Captain? captain = ctx.IsAdmin
                        ? await _database.Captains.ReadAsync(id).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Captains.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                            : await _database.Captains.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    if (captain == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    if (captain.State == CaptainStateEnum.Working || captain.State == CaptainStateEnum.Planning || captain.State == CaptainStateEnum.Refining)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete captain while state is Working, Planning, or Refining. Stop the captain first."));
                        continue;
                    }
                    List<Mission> captainMissions = ctx.IsAdmin
                        ? await _database.Missions.EnumerateByCaptainAsync(id).ConfigureAwait(false)
                        : await _database.Missions.EnumerateByCaptainAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    List<Mission> activeCaptainMissions = captainMissions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress).ToList();
                    if (activeCaptainMissions.Count > 0)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete captain with " + activeCaptainMissions.Count + " active mission(s). Cancel or complete them first."));
                        continue;
                    }
                    if (ctx.IsAdmin)
                        await _database.Captains.DeleteAsync(id).ConfigureAwait(false);
                    else if (ctx.IsTenantAdmin)
                        await _database.Captains.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    else
                        await _database.Captains.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await _emitEvent("captain.batch_deleted", "Batch deleted " + result.Deleted + " captains",
                    "captain", null, null, null, null, null).ConfigureAwait(false);

                result.ResolveStatus();
                return (object)result;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Batch delete multiple captains")
                .WithDescription("Permanently deletes multiple captains from the database by ID. Captains that are Working or have active missions are skipped. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiJson.BodyFor<DeleteMultipleRequest>("List of captain IDs to delete"))
                .WithResponse(200, OpenApiJson.For<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));
        }

        private static void NormalizeCaptainRuntimeOptions(Captain captain, Captain? existing = null)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            if (captain.Runtime != AgentRuntimeEnum.Mux)
            {
                captain.RuntimeOptionsJson = null;
                return;
            }

            if (String.IsNullOrWhiteSpace(captain.RuntimeOptionsJson) &&
                existing != null &&
                existing.Runtime == AgentRuntimeEnum.Mux &&
                !String.IsNullOrWhiteSpace(existing.RuntimeOptionsJson))
            {
                captain.RuntimeOptionsJson = existing.RuntimeOptionsJson;
                return;
            }

            if (String.IsNullOrWhiteSpace(captain.RuntimeOptionsJson))
            {
                captain.RuntimeOptionsJson = null;
            }
        }
    }
}
