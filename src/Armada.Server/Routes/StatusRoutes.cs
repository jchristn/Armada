namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Server;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// REST API routes for status management.
    /// </summary>
    public class StatusRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly ArmadaSettings _settings;
        private readonly IAdmiralService _admiral;
        private readonly Action _stopCallback;
        private readonly DateTime _startUtc;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly LoggingModule _logging;
        private readonly ISlotManager _slots;
        private readonly ServerRebuildService _rebuild;
        private readonly Func<RemoteTunnelStatus>? _getRemoteTunnelStatus;
        private readonly Func<Task>? _onRemoteControlSettingsChanged;
        private const string _HeaderRestart = "[ArmadaServer] restart: ";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="admiral">Admiral coordination service.</param>
        /// <param name="stopCallback">Server shutdown callback.</param>
        /// <param name="startUtc">Server start timestamp.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="slots">Slot manager for the self-rebuild slot layout and pointer.</param>
        /// <param name="rebuild">Server self-rebuild service.</param>
        /// <param name="getRemoteTunnelStatus">Optional callback that returns the current remote tunnel status.</param>
        /// <param name="onRemoteControlSettingsChanged">Optional callback invoked after remote-control settings are updated.</param>
        public StatusRoutes(
            DatabaseDriver database,
            ArmadaSettings settings,
            IAdmiralService admiral,
            Action stopCallback,
            DateTime startUtc,
            JsonSerializerOptions jsonOptions,
            LoggingModule logging,
            ISlotManager slots,
            ServerRebuildService rebuild,
            Func<RemoteTunnelStatus>? getRemoteTunnelStatus = null,
            Func<Task>? onRemoteControlSettingsChanged = null)
        {
            _database = database;
            _settings = settings;
            _admiral = admiral;
            _stopCallback = stopCallback;
            _startUtc = startUtc;
            _jsonOptions = jsonOptions;
            _logging = logging;
            _slots = slots;
            _rebuild = rebuild;
            _getRemoteTunnelStatus = getRemoteTunnelStatus;
            _onRemoteControlSettingsChanged = onRemoteControlSettingsChanged;
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

            // Status
            app.Get("/api/v1/status", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                ArmadaStatus status = await _admiral.GetStatusAsync().ConfigureAwait(false);
                return status;
            },
            api => api
                .WithTag("Status")
                .WithSummary("Get Armada status")
                .WithDescription("Returns aggregate status including captain counts, mission breakdown, active voyages, and recent signals.")
                .WithResponse(200, OpenApiJson.For<ArmadaStatus>("Armada status dashboard"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/status/health", (ApiRequest req) =>
            {
                TimeSpan uptime = DateTime.UtcNow - _startUtc;
                return Task.FromResult<object>(new
                {
                    Status = "healthy",
                    Timestamp = DateTime.UtcNow,
                    StartUtc = _startUtc,
                    Uptime = uptime.ToString(@"d\.hh\:mm\:ss"),
                    Version = ArmadaConstants.ProductVersion,
                    Ports = new
                    {
                        Admiral = _settings.AdmiralPort,
                        Mcp = _settings.McpPort
                    },
                    RemoteTunnel = BuildRemoteTunnelStatus()
                });
            },
            api => api
                .WithTag("Status")
                .WithSummary("Health check")
                .WithDescription("Returns health status. Does not require authentication."));

            app.Get("/api/v1/doctor", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                List<object> results = new List<object>();

                // 1. Settings File
                try
                {
                    string settingsPath = ArmadaSettings.DefaultSettingsPath;
                    if (File.Exists(settingsPath))
                        results.Add(new { Name = "Settings", Status = "Pass", Message = "Settings loaded from " + settingsPath });
                    else
                        results.Add(new { Name = "Settings", Status = "Fail", Message = "Settings file not found at " + settingsPath });
                }
                catch (Exception ex)
                {
                    results.Add(new { Name = "Settings", Status = "Fail", Message = "Error checking settings: " + ex.Message });
                }

                // 2. Git Availability
                try
                {
                    ProcessStartInfo gitPsi = new ProcessStartInfo("git", "--version")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (Process? gitProc = Process.Start(gitPsi))
                    {
                        if (gitProc != null)
                        {
                            string gitOutput = gitProc.StandardOutput.ReadToEnd().Trim();
                            gitProc.WaitForExit(5000);
                            results.Add(new { Name = "Git", Status = "Pass", Message = gitOutput });
                        }
                        else
                        {
                            results.Add(new { Name = "Git", Status = "Fail", Message = "Could not start git process" });
                        }
                    }
                }
                catch
                {
                    results.Add(new { Name = "Git", Status = "Fail", Message = "Git not found on PATH" });
                }

                // 3. Database
                try
                {
                    string dbPath = _settings.DatabasePath;
                    if (File.Exists(dbPath))
                    {
                        FileInfo fi = new FileInfo(dbPath);
                        results.Add(new { Name = "Database", Status = "Pass", Message = $"Database exists ({fi.Length / 1024} KB) at {dbPath}" });
                    }
                    else
                    {
                        results.Add(new { Name = "Database", Status = "Warn", Message = "Database not found at " + dbPath });
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new { Name = "Database", Status = "Fail", Message = "Error checking database: " + ex.Message });
                }

                // 4. Admiral Server (self-check — always passes if we reach here)
                results.Add(new { Name = "Admiral Server", Status = "Pass", Message = "Server is healthy" });

                // 5. Stalled Captains
                try
                {
                    List<Captain> stalledCaptains = ctx.IsAdmin
                        ? await _database.Captains.EnumerateByStateAsync(CaptainStateEnum.Stalled).ConfigureAwait(false)
                        : await _database.Captains.EnumerateByStateAsync(ctx.TenantId!, CaptainStateEnum.Stalled).ConfigureAwait(false);
                    int stalledCount = stalledCaptains.Count;
                    if (stalledCount == 0)
                        results.Add(new { Name = "Stalled Captains", Status = "Pass", Message = "No stalled captains" });
                    else
                        results.Add(new { Name = "Stalled Captains", Status = "Warn", Message = $"{stalledCount} captain(s) are stalled" });
                }
                catch (Exception ex)
                {
                    results.Add(new { Name = "Stalled Captains", Status = "Fail", Message = "Error checking captains: " + ex.Message });
                }

                // 6. Failed Missions
                try
                {
                    List<Mission> failedMissions = ctx.IsAdmin
                        ? await _database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Failed).ConfigureAwait(false)
                        : await _database.Missions.EnumerateByStatusAsync(ctx.TenantId!, MissionStatusEnum.Failed).ConfigureAwait(false);
                    int failedCount = failedMissions.Count;
                    if (failedCount == 0)
                        results.Add(new { Name = "Failed Missions", Status = "Pass", Message = "No failed missions" });
                    else
                        results.Add(new { Name = "Failed Missions", Status = "Warn", Message = $"{failedCount} mission(s) have failed" });
                }
                catch (Exception ex)
                {
                    results.Add(new { Name = "Failed Missions", Status = "Fail", Message = "Error checking missions: " + ex.Message });
                }

                // 7. Agent Runtimes
                string[] runtimeCommands = new string[] { "claude", "codex", "gemini", "cursor-agent", "mux" };
                string[] runtimeNames = new string[] { "Claude Code", "Codex", "Gemini CLI", "Cursor", "Mux" };
                bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                for (int i = 0; i < runtimeCommands.Length; i++)
                {
                    try
                    {
                        // Use 'where' on Windows, 'command -v' (POSIX standard) on Unix.
                        // 'which' is deprecated on Debian and missing on some distros.
                        ProcessStartInfo rtPsi;
                        if (isWindows)
                        {
                            rtPsi = new ProcessStartInfo("where");
                            rtPsi.ArgumentList.Add(runtimeCommands[i]);
                        }
                        else
                        {
                            rtPsi = new ProcessStartInfo("/bin/sh");
                            rtPsi.ArgumentList.Add("-c");
                            rtPsi.ArgumentList.Add("command -v " + runtimeCommands[i]);
                        }
                        rtPsi.RedirectStandardOutput = true;
                        rtPsi.RedirectStandardError = true;
                        rtPsi.UseShellExecute = false;
                        rtPsi.CreateNoWindow = true;
                        using (Process? rtProc = Process.Start(rtPsi))
                        {
                            if (rtProc != null)
                            {
                                string rtOutput = rtProc.StandardOutput.ReadToEnd().Trim();
                                rtProc.WaitForExit(5000);
                                if (rtProc.ExitCode == 0 && !string.IsNullOrEmpty(rtOutput))
                                {
                                    string path = rtOutput.Split('\n')[0].Trim();
                                    results.Add(new { Name = runtimeNames[i], Status = "Pass", Message = runtimeNames[i] + " found at " + path });
                                }
                                else
                                {
                                    results.Add(new { Name = runtimeNames[i], Status = "Warn", Message = runtimeNames[i] + " not found on PATH (optional)" });
                                }
                            }
                            else
                            {
                                results.Add(new { Name = runtimeNames[i], Status = "Warn", Message = runtimeNames[i] + " not found (optional)" });
                            }
                        }
                    }
                    catch
                    {
                        results.Add(new { Name = runtimeNames[i], Status = "Warn", Message = runtimeNames[i] + " not found (optional)" });
                    }
                }

                return results;
            },
            api => api
                .WithTag("Status")
                .WithSummary("Run system health diagnostics")
                .WithDescription("Runs 7 system health checks and returns results as a JSON array.")
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/server/stop", async (ApiRequest req) =>
            {
                if (_settings.RequireAuthForShutdown)
                {
                    AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                    if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                    {
                        req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                        return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                    }
                }
                _logging.Info(_Header + "shutdown requested via API");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    _stopCallback();
                });
                return new { Status = "shutting_down" };
            },
            api => api
                .WithTag("Status")
                .WithSummary("Stop the Admiral server")
                .WithDescription("Initiates a graceful shutdown of the Admiral server.")
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/server/restart", async (ApiRequest req) =>
            {
                if (_settings.RequireAuthForShutdown)
                {
                    AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                    if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                    {
                        req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                        return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                    }
                }

                string replacementExe = await ResolveCurrentExecutableAsync().ConfigureAwait(false);
                if (!ReplacementProcessLauncher.Launch(replacementExe, _logging, _HeaderRestart))
                {
                    req.Http.Response.StatusCode = 500;
                    return new ApiErrorResponse { Error = ApiResultEnum.InternalError, Message = "Unable to launch a replacement Admiral process; server was not restarted." };
                }

                _logging.Info(_Header + "restart requested via API; replacement process launched");
                _ = Task.Run(async () =>
                {
                    // Give the replacement a moment to spawn and register the predecessor wait, then stop
                    // this instance so the port frees for the replacement to bind.
                    await Task.Delay(500).ConfigureAwait(false);
                    _stopCallback();
                });
                return new { Status = "restarting" };
            },
            api => api
                .WithTag("Status")
                .WithSummary("Restart the Admiral server")
                .WithDescription("Launches a replacement Admiral process that waits for this instance to exit, then gracefully stops this instance so the replacement can bind the listening port.")
                .WithSecurity("ApiKey"));

            app.Post<ServerRebuildRequest>("/api/v1/server/rebuild", async (ApiRequest req) =>
            {
                if (_settings.RequireAuthForShutdown)
                {
                    AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                    if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                    {
                        req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                        return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                    }
                }

                ServerRebuildRequest body = new ServerRebuildRequest();
                if (!String.IsNullOrWhiteSpace(req.Http.Request.DataAsString))
                {
                    body = JsonSerializer.Deserialize<ServerRebuildRequest>(req.Http.Request.DataAsString, _jsonOptions) ?? new ServerRebuildRequest();
                }

                try
                {
                    ServerRebuildStatus status = _rebuild.StartRebuild(body);
                    _logging.Info(_Header + "rebuild requested via API; building slot " + (status.Slot ?? "(pending)"));
                    return status;
                }
                catch (InvalidOperationException e)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = e.Message };
                }
            },
            api => api
                .WithTag("Status")
                .WithSummary("Rebuild the Admiral server")
                .WithDescription("Publishes the Armada source into a new slot from a detached worktree at the chosen ref, backs up the database, flips the slot pointer, and cuts over to the new build. Returns the initial rebuild status; poll GET /api/v1/server/rebuild/status for progress.")
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/server/rebuild/status", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                ServerRebuildStatus? status = _rebuild.Latest;
                if (status == null) return new { Status = "none" };
                return status;
            },
            api => api
                .WithTag("Status")
                .WithSummary("Get the latest rebuild status")
                .WithDescription("Returns the most recent Admiral rebuild status and its accumulated build log, or {\"status\":\"none\"} when no rebuild has run.")
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/server/rollback", async (ApiRequest req) =>
            {
                if (_settings.RequireAuthForShutdown)
                {
                    AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                    if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                    {
                        req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                        return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                    }
                }

                try
                {
                    ServerRebuildStatus status = await _rebuild.RollbackAsync().ConfigureAwait(false);
                    _logging.Info(_Header + "rollback requested via API; reverting to slot " + (status.PreviousSlot ?? "(unknown)"));
                    return status;
                }
                catch (RebuildException e)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = e.Message };
                }
            },
            api => api
                .WithTag("Status")
                .WithSummary("Roll back the last rebuild")
                .WithDescription("Reverts to the previous slot after a successful rebuild. When the rebuild migrated the database schema, the pre-rebuild backup is restored first (discarding data written since cutover); otherwise the previous slot is relaunched with no restore. Then this instance stops so the previous slot binds.")
                .WithSecurity("ApiKey"));

            // Settings
            app.Get("/api/v1/settings", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                return BuildSettingsResponse();
            },
            api => api
                .WithTag("Settings")
                .WithSummary("Get server settings")
                .WithDescription("Returns current server settings including ports, agent configuration, and system paths.")
                .WithSecurity("ApiKey"));

            app.Put<SettingsUpdateRequest>("/api/v1/settings", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                SettingsUpdateRequest body = JsonSerializer.Deserialize<SettingsUpdateRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as SettingsUpdateRequest.");

                if (body.AdmiralPort.HasValue)
                    _settings.AdmiralPort = body.AdmiralPort.Value;

                if (body.McpPort.HasValue)
                    _settings.McpPort = body.McpPort.Value;

                if (body.MaxCaptains.HasValue)
                    _settings.MaxCaptains = body.MaxCaptains.Value;

                if (body.HeartbeatIntervalSeconds.HasValue)
                    _settings.HeartbeatIntervalSeconds = body.HeartbeatIntervalSeconds.Value;

                if (body.StallThresholdMinutes.HasValue)
                    _settings.StallThresholdMinutes = body.StallThresholdMinutes.Value;

                if (body.IdleCaptainTimeoutSeconds.HasValue)
                    _settings.IdleCaptainTimeoutSeconds = body.IdleCaptainTimeoutSeconds.Value;

                if (body.PlanningSessionInactivityTimeoutMinutes.HasValue)
                    _settings.PlanningSessionInactivityTimeoutMinutes = body.PlanningSessionInactivityTimeoutMinutes.Value;

                if (body.PlanningSessionAbandonmentTimeoutMinutes.HasValue)
                    _settings.PlanningSessionAbandonmentTimeoutMinutes = body.PlanningSessionAbandonmentTimeoutMinutes.Value;

                if (body.PlanningSessionRetentionDays.HasValue)
                    _settings.PlanningSessionRetentionDays = body.PlanningSessionRetentionDays.Value;

                if (body.AutoCreatePr.HasValue)
                    _settings.AutoCreatePullRequests = body.AutoCreatePr.Value;

                if (body.SelfVesselId != null)
                    _settings.SelfVesselId = String.IsNullOrWhiteSpace(body.SelfVesselId) ? null : body.SelfVesselId.Trim();

                if (body.RebuildSlotRetentionCount.HasValue)
                    _settings.RebuildSlotRetentionCount = body.RebuildSlotRetentionCount.Value;

                bool remoteControlChanged = body.RemoteControl != null;
                if (remoteControlChanged)
                    _settings.RemoteControl = body.RemoteControl!;

                await _settings.SaveAsync().ConfigureAwait(false);

                if (remoteControlChanged && _onRemoteControlSettingsChanged != null)
                    await _onRemoteControlSettingsChanged().ConfigureAwait(false);

                _logging.Info(_Header + "settings updated via API");

                return BuildSettingsResponse();
            },
            api => api
                .WithTag("Settings")
                .WithSummary("Update server settings")
                .WithDescription("Accepts partial update of editable settings. Validates values and persists to settings file.")
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/server/reset", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                _logging.Warn(_Header + "factory reset requested via API");

                List<string> deleted = new List<string>();

                if (Directory.Exists(_settings.LogDirectory))
                {
                    Directory.Delete(_settings.LogDirectory, true);
                    deleted.Add("logs");
                }

                if (Directory.Exists(_settings.DocksDirectory))
                {
                    Directory.Delete(_settings.DocksDirectory, true);
                    deleted.Add("docks");
                }

                if (Directory.Exists(_settings.ReposDirectory))
                {
                    Directory.Delete(_settings.ReposDirectory, true);
                    deleted.Add("repos");
                }

                if (File.Exists(_settings.DatabasePath))
                {
                    File.Delete(_settings.DatabasePath);
                    deleted.Add("database");
                }

                _settings.InitializeDirectories();

                return new
                {
                    Status = "reset_complete",
                    Message = "Factory reset complete. Deleted: " + String.Join(", ", deleted) + ". Settings file preserved.",
                    Deleted = deleted
                };
            },
            api => api
                .WithTag("Settings")
                .WithSummary("Factory reset")
                .WithDescription("Deletes database, logs, docks, and repos directories. Preserves settings file.")
                .WithSecurity("ApiKey"));
        }

        private async Task<string> ResolveCurrentExecutableAsync()
        {
            // Prefer the executable named by the active slot pointer so a restart after a rebuild comes up on
            // the new build. Fall back to the currently running executable when no slot pointer is present
            // (e.g. a classic non-slot install), preserving the original restart behavior.
            try
            {
                string? currentSlot = await _slots.ReadCurrentAsync().ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(currentSlot))
                {
                    string slotExe = _slots.GetSlotExecutablePath(currentSlot!);
                    if (File.Exists(slotExe)) return slotExe;
                    _logging.Warn(_HeaderRestart + "current slot '" + currentSlot + "' executable not found at " + slotExe + "; falling back to the running executable");
                }
            }
            catch (Exception e)
            {
                _logging.Warn(_HeaderRestart + "could not resolve the current slot; falling back to the running executable: " + e.Message);
            }

            return Environment.ProcessPath ?? String.Empty;
        }

        private object BuildSettingsResponse()
        {
            return new
            {
                AdmiralPort = _settings.AdmiralPort,
                McpPort = _settings.McpPort,
                MaxCaptains = _settings.MaxCaptains,
                HeartbeatIntervalSeconds = _settings.HeartbeatIntervalSeconds,
                StallThresholdMinutes = _settings.StallThresholdMinutes,
                IdleCaptainTimeoutSeconds = _settings.IdleCaptainTimeoutSeconds,
                PlanningSessionInactivityTimeoutMinutes = _settings.PlanningSessionInactivityTimeoutMinutes,
                PlanningSessionAbandonmentTimeoutMinutes = _settings.PlanningSessionAbandonmentTimeoutMinutes,
                PlanningSessionRetentionDays = _settings.PlanningSessionRetentionDays,
                AutoCreatePr = _settings.AutoCreatePullRequests,
                DataDirectory = _settings.DataDirectory,
                DatabasePath = _settings.DatabasePath,
                LogDirectory = _settings.LogDirectory,
                DocksDirectory = _settings.DocksDirectory,
                ReposDirectory = _settings.ReposDirectory,
                SelfVesselId = _settings.SelfVesselId,
                RebuildSlotRetentionCount = _settings.RebuildSlotRetentionCount,
                RemoteControl = _settings.RemoteControl
            };
        }

        private RemoteTunnelStatus BuildRemoteTunnelStatus()
        {
            return _getRemoteTunnelStatus?.Invoke() ?? new RemoteTunnelStatus
            {
                Enabled = _settings.RemoteControl.Enabled,
                State = _settings.RemoteControl.Enabled ? RemoteTunnelStateEnum.Disconnected : RemoteTunnelStateEnum.Disabled,
                TunnelUrl = _settings.RemoteControl.TunnelUrl
            };
        }
    }
}
