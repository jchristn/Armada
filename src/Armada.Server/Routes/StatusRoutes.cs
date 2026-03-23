namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using SwiftStack;
    using SwiftStack.Rest;
    using SwiftStack.Rest.OpenApi;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
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
        public StatusRoutes(
            DatabaseDriver database,
            ArmadaSettings settings,
            IAdmiralService admiral,
            Action stopCallback,
            DateTime startUtc,
            JsonSerializerOptions jsonOptions,
            LoggingModule logging)
        {
            _database = database;
            _settings = settings;
            _admiral = admiral;
            _stopCallback = stopCallback;
            _startUtc = startUtc;
            _jsonOptions = jsonOptions;
            _logging = logging;
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
            string _Header = "[ArmadaServer] ";

            // Status
            app.Rest.Get("/api/v1/status", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                ArmadaStatus status = await _admiral.GetStatusAsync().ConfigureAwait(false);
                return status;
            },
            api => api
                .WithTag("Status")
                .WithSummary("Get Armada status")
                .WithDescription("Returns aggregate status including captain counts, mission breakdown, active voyages, and recent signals.")
                .WithResponse(200, OpenApiResponseMetadata.Json<ArmadaStatus>("Armada status dashboard"))
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/status/health", async (AppRequest req) =>
            {
                TimeSpan uptime = DateTime.UtcNow - _startUtc;
                return new
                {
                    Status = "healthy",
                    Timestamp = DateTime.UtcNow,
                    StartUtc = _startUtc,
                    Uptime = uptime.ToString(@"d\.hh\:mm\:ss"),
                    Version = ArmadaConstants.ProductVersion,
                    Ports = new
                    {
                        Admiral = _settings.AdmiralPort,
                        Mcp = _settings.McpPort,
                        WebSocket = _settings.WebSocketPort
                    }
                };
            },
            api => api
                .WithTag("Status")
                .WithSummary("Health check")
                .WithDescription("Returns health status. Does not require authentication."));

            app.Rest.Get("/api/v1/doctor", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
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
                string[] runtimeCommands = new string[] { "claude", "codex" };
                string[] runtimeNames = new string[] { "Claude Code", "Codex" };
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

            app.Rest.Post("/api/v1/server/stop", async (AppRequest req) =>
            {
                if (_settings.RequireAuthForShutdown)
                {
                    AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                    if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                    {
                        req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                        return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
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

            // Settings
            app.Rest.Get("/api/v1/settings", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                return new
                {
                    AdmiralPort = _settings.AdmiralPort,
                    McpPort = _settings.McpPort,
                    MaxCaptains = _settings.MaxCaptains,
                    HeartbeatIntervalSeconds = _settings.HeartbeatIntervalSeconds,
                    StallThresholdMinutes = _settings.StallThresholdMinutes,
                    IdleCaptainTimeoutSeconds = _settings.IdleCaptainTimeoutSeconds,
                    AutoCreatePr = _settings.AutoCreatePullRequests,
                    DataDirectory = _settings.DataDirectory,
                    DatabasePath = _settings.DatabasePath,
                    LogDirectory = _settings.LogDirectory,
                    DocksDirectory = _settings.DocksDirectory,
                    ReposDirectory = _settings.ReposDirectory
                };
            },
            api => api
                .WithTag("Settings")
                .WithSummary("Get server settings")
                .WithDescription("Returns current server settings including ports, agent configuration, and system paths.")
                .WithSecurity("ApiKey"));

            app.Rest.Put<SettingsUpdateRequest>("/api/v1/settings", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
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

                if (body.AutoCreatePr.HasValue)
                    _settings.AutoCreatePullRequests = body.AutoCreatePr.Value;

                await _settings.SaveAsync().ConfigureAwait(false);
                _logging.Info(_Header + "settings updated via API");

                return new
                {
                    AdmiralPort = _settings.AdmiralPort,
                    McpPort = _settings.McpPort,
                    MaxCaptains = _settings.MaxCaptains,
                    HeartbeatIntervalSeconds = _settings.HeartbeatIntervalSeconds,
                    StallThresholdMinutes = _settings.StallThresholdMinutes,
                    IdleCaptainTimeoutSeconds = _settings.IdleCaptainTimeoutSeconds,
                    AutoCreatePr = _settings.AutoCreatePullRequests,
                    DataDirectory = _settings.DataDirectory,
                    DatabasePath = _settings.DatabasePath,
                    LogDirectory = _settings.LogDirectory,
                    DocksDirectory = _settings.DocksDirectory,
                    ReposDirectory = _settings.ReposDirectory
                };
            },
            api => api
                .WithTag("Settings")
                .WithSummary("Update server settings")
                .WithDescription("Accepts partial update of editable settings. Validates values and persists to settings file.")
                .WithSecurity("ApiKey"));

            app.Rest.Post("/api/v1/server/reset", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
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
    }
}
