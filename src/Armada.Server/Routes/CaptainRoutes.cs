namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using SwiftStack;
    using SwiftStack.Rest;
    using SwiftStack.Rest.OpenApi;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
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
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral coordination service.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="runtimeFactory">Agent runtime factory.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public CaptainRoutes(
            DatabaseDriver database,
            IAdmiralService admiral,
            ArmadaSettings settings,
            AgentRuntimeFactory runtimeFactory,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            JsonSerializerOptions jsonOptions)
        {
            _database = database;
            _admiral = admiral;
            _settings = settings;
            _runtimeFactory = runtimeFactory;
            _emitEvent = emitEvent;
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

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">SwiftStack application.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            SwiftStackApp app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            // Captains
            app.Rest.Get("/api/v1/captains", async (AppRequest req) =>
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
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Captain>>("Paginated captain list"))
                .WithSecurity("ApiKey"));

            app.Rest.Post<EnumerationQuery>("/api/v1/captains/enumerate", async (AppRequest req) =>
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
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Rest.Post<Captain>("/api/v1/captains", async (AppRequest req) =>
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
                captain = await _database.Captains.CreateAsync(captain).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return captain;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Create a captain")
                .WithDescription("Registers a new captain (AI agent).")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Captain>("Captain data", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Captain>("Created captain"))
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/captains/{id}", async (AppRequest req) =>
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
                .WithResponse(200, OpenApiResponseMetadata.Json<Captain>("Captain details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Put<Captain>("/api/v1/captains/{id}", async (AppRequest req) =>
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
                updated.State = existing.State;
                updated.CurrentMissionId = existing.CurrentMissionId;
                updated.CurrentDockId = existing.CurrentDockId;
                updated.ProcessId = existing.ProcessId;
                updated.RecoveryAttempts = existing.RecoveryAttempts;
                updated.LastHeartbeatUtc = existing.LastHeartbeatUtc;
                updated.CreatedUtc = existing.CreatedUtc;
                updated.LastUpdateUtc = DateTime.UtcNow;
                updated = await _database.Captains.UpdateAsync(updated).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Update a captain")
                .WithDescription("Updates a captain's name, runtime, or max parallelism. Operational fields (state, process, mission) are preserved.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Captain>("Updated captain data", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Captain>("Updated captain"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Post("/api/v1/captains/{id}/stop", async (AppRequest req) =>
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

            app.Rest.Post("/api/v1/captains/stop-all", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                await _admiral.RecallAllAsync().ConfigureAwait(false);
                return (object)new { Status = "all_stopped" };
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Stop all captains")
                .WithDescription("Emergency stop all running captains, recalling them to idle state.")
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/captains/{id}/log", async (AppRequest req) =>
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

            app.Rest.Delete("/api/v1/captains/{id}", async (AppRequest req) =>
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
                if (captain.State == CaptainStateEnum.Working)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot delete captain while state is Working. Stop the captain first." };
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
                .WithResponse(409, OpenApiResponseMetadata.Json<object>("Captain cannot be deleted while active"))
                .WithSecurity("ApiKey"));

            app.Rest.Post<DeleteMultipleRequest>("/api/v1/captains/delete/multiple", async (AppRequest req) =>
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
                    if (captain.State == CaptainStateEnum.Working)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete captain while state is Working. Stop the captain first."));
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
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of captain IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));
        }
    }
}
