namespace Armada.Server
{
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using SyslogLogging;
    using SwiftStack;
    using SwiftStack.Rest;
    using SwiftStack.Rest.OpenApi;
    using Voltaic;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server.Mcp;
    using Armada.Server.WebSocket;

    /// <summary>
    /// Admiral server orchestrating REST API, MCP server, and agent coordination.
    /// </summary>
    public class ArmadaServer
    {
        #region Public-Members

        /// <summary>
        /// Callback invoked when the server is stopping, allowing the host to unblock.
        /// </summary>
        public Action? OnStopping { get; set; }

        #endregion

        #region Private-Members

        private string _Header = "[ArmadaServer] ";
        private LoggingModule _Logging;
        private ArmadaSettings _Settings;
        private bool _Quiet;

        private DatabaseDriver _Database = null!;
        private IGitService _Git = null!;
        private IDockService _Docks = null!;
        private IAdmiralService _Admiral = null!;
        private AgentRuntimeFactory _RuntimeFactory = null!;

        private SwiftStackApp _App = null!;
        private McpHttpServer _McpServer = null!;
        private ArmadaWebSocketHub _WebSocketHub = null!;

        private IMergeQueueService _MergeQueue = null!;
        private LandingService _LandingService = null!;
        private IMessageTemplateService _TemplateService = null!;
        private LogRotationService _LogRotation = null!;
        private DataExpiryService _DataExpiry = null!;

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private Task _HealthCheckTask = null!;
        private int _HealthCheckCycles = 0;
        private DateTime _StartUtc = DateTime.UtcNow;

        /// <summary>
        /// Maps process IDs to captain IDs for progress tracking.
        /// </summary>
        private Dictionary<int, string> _ProcessToCaptain = new Dictionary<int, string>();

        /// <summary>
        /// Maps process IDs to mission IDs for per-mission progress tracking.
        /// </summary>
        private Dictionary<int, string> _ProcessToMission = new Dictionary<int, string>();

        /// <summary>
        /// Per-vessel semaphores to prevent concurrent merge operations on the same repository.
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _VesselMergeLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="quiet">Suppress startup console output.</param>
        public ArmadaServer(LoggingModule logging, ArmadaSettings settings, bool quiet = false)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Quiet = quiet;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the Admiral server.
        /// </summary>
        public async Task StartAsync()
        {
            // Initialize database
            string connectionString = "Data Source=" + _Settings.DatabasePath;
            _Database = new SqliteDatabaseDriver(connectionString, _Logging);
            await _Database.InitializeAsync().ConfigureAwait(false);
            _Logging.Info(_Header + "database initialized");

            // Initialize services
            _Git = new GitService(_Logging);
            IDockService dockService = new DockService(_Logging, _Database, _Settings, _Git);
            _Docks = dockService;
            ICaptainService captainService = new CaptainService(_Logging, _Database, _Settings, _Git, dockService);
            IMissionService missionService = new MissionService(_Logging, _Database, _Settings, dockService, captainService);
            IVoyageService voyageService = new VoyageService(_Logging, _Database);
            IEscalationService escalationService = new EscalationService(_Logging, _Database, _Settings);
            _Admiral = new AdmiralService(_Logging, _Database, _Settings, captainService, missionService, voyageService, dockService, escalationService);
            _MergeQueue = new MergeQueueService(_Logging, _Database, _Settings, _Git);
            _LandingService = new LandingService(_Logging, _Database, _Settings, _Git);
            _TemplateService = new MessageTemplateService(_Logging);
            _RuntimeFactory = new AgentRuntimeFactory(_Logging);

            // Initialize log rotation and data expiry
            _LogRotation = new LogRotationService(_Logging, _Settings.MaxLogFileSizeBytes, _Settings.MaxLogFileCount);
            _DataExpiry = new DataExpiryService(_Logging, connectionString, _Settings.DataRetentionDays);

            // Wire up agent lifecycle events
            _Admiral.OnLaunchAgent = HandleLaunchAgentAsync;
            _Admiral.OnStopAgent = HandleStopAgentAsync;
            _Admiral.OnCaptureDiff = HandleCaptureDiffAsync;
            _Admiral.OnMissionComplete = HandleMissionCompleteAsync;
            _Admiral.OnVoyageComplete = HandleVoyageCompleteAsync;
            _Admiral.OnReconcilePullRequest = HandleReconcilePullRequestAsync;
            _LandingService.OnPerformLanding = HandleMissionCompleteAsync;

            // Initialize REST API
            _App = new SwiftStackApp(ArmadaConstants.ProductName, _Quiet);
            _App.Logging = _Logging;

            _App.Rest.WebserverSettings.Hostname = _Settings.Rest.Hostname;
            _App.Rest.WebserverSettings.Port = _Settings.AdmiralPort;
            _App.Rest.WebserverSettings.Ssl.Enable = _Settings.Rest.Ssl;

            _App.Rest.UseOpenApi(openApi =>
            {
                openApi.Info.Title = ArmadaConstants.ProductName + " API";
                openApi.Info.Version = ArmadaConstants.ProductVersion;
                openApi.Info.Description = "Multi-agent orchestration API for scaling human developers with AI captains across git worktrees.";

                // Tags for route grouping
                openApi.Tags.Add(new OpenApiTag("Status", "Health check and system status"));
                openApi.Tags.Add(new OpenApiTag("Fleets", "Fleet (repository collection) management"));
                openApi.Tags.Add(new OpenApiTag("Vessels", "Vessel (git repository) management"));
                openApi.Tags.Add(new OpenApiTag("Voyages", "Voyage (mission batch) management"));
                openApi.Tags.Add(new OpenApiTag("Missions", "Mission (atomic work unit) management"));
                openApi.Tags.Add(new OpenApiTag("Captains", "Captain (AI agent) management"));
                openApi.Tags.Add(new OpenApiTag("Signals", "Signal (inter-agent messaging) management"));
                openApi.Tags.Add(new OpenApiTag("Events", "System event log"));
                openApi.Tags.Add(new OpenApiTag("MergeQueue", "Bors-style merge queue with batch testing"));

                // API key security scheme
                openApi.SecuritySchemes["ApiKey"] = OpenApiSecurityScheme.ApiKey(
                    "X-Api-Key",
                    "header",
                    "API key for authenticating requests. Configure via ArmadaSettings.ApiKey.");
            });

            // Set timestamp on request start
            _App.Rest.PreRoutingRoute = async (WatsonWebserver.Core.HttpContextBase ctx) =>
            {
                ctx.Timestamp.Start = DateTime.UtcNow;
                ctx.Response.ContentType = "application/json";
            };

            // Log every API call and enable CORS
            _App.Rest.PostRoutingRoute = async (WatsonWebserver.Core.HttpContextBase ctx) =>
            {
                ctx.Timestamp.End = DateTime.UtcNow;
                _Logging.Debug(
                    _Header +
                    ctx.Request.Method + " " +
                    ctx.Request.Url.RawWithQuery + " " +
                    ctx.Response.StatusCode + " " +
                    "(" + (ctx.Timestamp.TotalMs.HasValue ? ctx.Timestamp.TotalMs.Value.ToString("F2") : "?") + "ms)");

                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Api-Key");
            };

            // Configure API key authentication
            if (!String.IsNullOrEmpty(_Settings.ApiKey))
            {
                _App.Rest.AuthenticationRoute = (WatsonWebserver.Core.HttpContextBase ctx) =>
                {
                    // Allow health check and dashboard without auth
                    string requestPath = ctx.Request.Url.RawWithoutQuery;
                    if (requestPath.EndsWith("/status/health") ||
                        requestPath.StartsWith("/dashboard") ||
                        requestPath == "/")
                    {
                        return Task.FromResult(new AuthResult
                        {
                            AuthenticationResult = AuthenticationResultEnum.Success
                        });
                    }

                    string? apiKey = ctx.Request.Headers.Get("X-Api-Key");

                    if (!String.IsNullOrEmpty(apiKey) && apiKey == _Settings.ApiKey)
                    {
                        return Task.FromResult(new AuthResult
                        {
                            AuthenticationResult = AuthenticationResultEnum.Success
                        });
                    }

                    return Task.FromResult(new AuthResult
                    {
                        AuthenticationResult = AuthenticationResultEnum.Invalid
                    });
                };

                _Logging.Info(_Header + "API key authentication enabled");
            }

            RegisterRoutes();
            RegisterDashboardRoutes();

            // Start REST API (background)
            Task restTask = Task.Run(() => _App.Rest.Run(_TokenSource.Token));
            _Logging.Info(_Header + "REST API started on port " + _Settings.AdmiralPort);

            // Initialize MCP server
            _McpServer = new McpHttpServer(_Settings.Rest.Hostname, _Settings.McpPort);
            _McpServer.ServerName = ArmadaConstants.ProductName;
            _McpServer.ServerVersion = ArmadaConstants.ProductVersion;
            RegisterMcpTools();

            Task mcpTask = Task.Run(() => _McpServer.StartAsync(_TokenSource.Token));
            _Logging.Info(_Header + "MCP server started on port " + _Settings.McpPort);

            // Initialize WebSocket hub
            _WebSocketHub = new ArmadaWebSocketHub(_Logging, _App, _Settings.WebSocketPort, _Settings.Rest.Ssl, _Admiral, _Database, _MergeQueue, _Settings, _Git, () => { OnStopping?.Invoke(); _TokenSource.Cancel(); });
            Task wsTask = Task.Run(() => _WebSocketHub.StartAsync(_TokenSource.Token));
            _Logging.Info(_Header + "WebSocket hub started on port " + _Settings.WebSocketPort);

            // Start health check loop
            _HealthCheckTask = HealthCheckLoopAsync(_TokenSource.Token);
        }

        /// <summary>
        /// Stop the Admiral server.
        /// </summary>
        public void Stop()
        {
            _Logging.Info(_Header + "stopping");
            _TokenSource.Cancel();
            _McpServer?.Stop();
            _Database?.Dispose();
            OnStopping?.Invoke();
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Reads all text from a file using FileShare.ReadWrite to avoid locking conflicts with writer processes.
        /// </summary>
        private async Task<string> ReadFileSharedAsync(string path)
        {
            using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(fs);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads all lines from a file using FileShare.ReadWrite to avoid locking conflicts with writer processes.
        /// </summary>
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

        private void RegisterRoutes()
        {
            // Status
            _App.Rest.Get("/api/v1/status", async (AppRequest req) =>
            {
                ArmadaStatus status = await _Admiral.GetStatusAsync().ConfigureAwait(false);
                return status;
            },
            api => api
                .WithTag("Status")
                .WithSummary("Get Armada status")
                .WithDescription("Returns aggregate status including captain counts, mission breakdown, active voyages, and recent signals.")
                .WithResponse(200, OpenApiResponseMetadata.Json<ArmadaStatus>("Armada status dashboard"))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/status/health", async (AppRequest req) =>
            {
                TimeSpan uptime = DateTime.UtcNow - _StartUtc;
                return new
                {
                    Status = "healthy",
                    Timestamp = DateTime.UtcNow,
                    StartUtc = _StartUtc,
                    Uptime = uptime.ToString(@"d\.hh\:mm\:ss"),
                    Version = ArmadaConstants.ProductVersion,
                    Ports = new
                    {
                        Admiral = _Settings.AdmiralPort,
                        Mcp = _Settings.McpPort,
                        WebSocket = _Settings.WebSocketPort
                    }
                };
            },
            api => api
                .WithTag("Status")
                .WithSummary("Health check")
                .WithDescription("Returns health status. Does not require authentication."));

            _App.Rest.Get("/api/v1/doctor", async (AppRequest req) =>
            {
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
                    string dbPath = _Settings.DatabasePath;
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
                    List<Captain> stalledCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Stalled).ConfigureAwait(false);
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
                    List<Mission> failedMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Failed).ConfigureAwait(false);
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

            _App.Rest.Post("/api/v1/server/stop", async (AppRequest req) =>
            {
                _Logging.Info(_Header + "shutdown requested via API");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    Stop();
                });
                return new { Status = "shutting_down" };
            },
            api => api
                .WithTag("Status")
                .WithSummary("Stop the Admiral server")
                .WithDescription("Initiates a graceful shutdown of the Admiral server.")
                .WithSecurity("ApiKey"));

            // Settings
            _App.Rest.Get("/api/v1/settings", async (AppRequest req) =>
            {
                return new
                {
                    AdmiralPort = _Settings.AdmiralPort,
                    McpPort = _Settings.McpPort,
                    MaxCaptains = _Settings.MaxCaptains,
                    HeartbeatIntervalSeconds = _Settings.HeartbeatIntervalSeconds,
                    StallThresholdMinutes = _Settings.StallThresholdMinutes,
                    IdleCaptainTimeoutSeconds = _Settings.IdleCaptainTimeoutSeconds,
                    AutoCreatePr = _Settings.AutoCreatePullRequests,
                    DataDirectory = _Settings.DataDirectory,
                    DatabasePath = _Settings.DatabasePath,
                    LogDirectory = _Settings.LogDirectory,
                    DocksDirectory = _Settings.DocksDirectory,
                    ReposDirectory = _Settings.ReposDirectory
                };
            },
            api => api
                .WithTag("Settings")
                .WithSummary("Get server settings")
                .WithDescription("Returns current server settings including ports, agent configuration, and system paths.")
                .WithSecurity("ApiKey"));

            _App.Rest.Put<SettingsUpdateRequest>("/api/v1/settings", async (AppRequest req) =>
            {
                SettingsUpdateRequest body = req.GetData<SettingsUpdateRequest>();

                if (body.AdmiralPort.HasValue)
                    _Settings.AdmiralPort = body.AdmiralPort.Value;

                if (body.McpPort.HasValue)
                    _Settings.McpPort = body.McpPort.Value;

                if (body.MaxCaptains.HasValue)
                    _Settings.MaxCaptains = body.MaxCaptains.Value;

                if (body.HeartbeatIntervalSeconds.HasValue)
                    _Settings.HeartbeatIntervalSeconds = body.HeartbeatIntervalSeconds.Value;

                if (body.StallThresholdMinutes.HasValue)
                    _Settings.StallThresholdMinutes = body.StallThresholdMinutes.Value;

                if (body.IdleCaptainTimeoutSeconds.HasValue)
                    _Settings.IdleCaptainTimeoutSeconds = body.IdleCaptainTimeoutSeconds.Value;

                if (body.AutoCreatePr.HasValue)
                    _Settings.AutoCreatePullRequests = body.AutoCreatePr.Value;

                await _Settings.SaveAsync().ConfigureAwait(false);
                _Logging.Info(_Header + "settings updated via API");

                return new
                {
                    AdmiralPort = _Settings.AdmiralPort,
                    McpPort = _Settings.McpPort,
                    MaxCaptains = _Settings.MaxCaptains,
                    HeartbeatIntervalSeconds = _Settings.HeartbeatIntervalSeconds,
                    StallThresholdMinutes = _Settings.StallThresholdMinutes,
                    IdleCaptainTimeoutSeconds = _Settings.IdleCaptainTimeoutSeconds,
                    AutoCreatePr = _Settings.AutoCreatePullRequests,
                    DataDirectory = _Settings.DataDirectory,
                    DatabasePath = _Settings.DatabasePath,
                    LogDirectory = _Settings.LogDirectory,
                    DocksDirectory = _Settings.DocksDirectory,
                    ReposDirectory = _Settings.ReposDirectory
                };
            },
            api => api
                .WithTag("Settings")
                .WithSummary("Update server settings")
                .WithDescription("Accepts partial update of editable settings. Validates values and persists to settings file.")
                .WithSecurity("ApiKey"));

            _App.Rest.Post("/api/v1/server/reset", async (AppRequest req) =>
            {
                _Logging.Warn(_Header + "factory reset requested via API");

                List<string> deleted = new List<string>();

                if (Directory.Exists(_Settings.LogDirectory))
                {
                    Directory.Delete(_Settings.LogDirectory, true);
                    deleted.Add("logs");
                }

                if (Directory.Exists(_Settings.DocksDirectory))
                {
                    Directory.Delete(_Settings.DocksDirectory, true);
                    deleted.Add("docks");
                }

                if (Directory.Exists(_Settings.ReposDirectory))
                {
                    Directory.Delete(_Settings.ReposDirectory, true);
                    deleted.Add("repos");
                }

                if (File.Exists(_Settings.DatabasePath))
                {
                    File.Delete(_Settings.DatabasePath);
                    deleted.Add("database");
                }

                _Settings.InitializeDirectories();

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

            // Fleets
            _App.Rest.Get("/api/v1/fleets", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Fleet> result = await _Database.Fleets.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("List all fleets")
                .WithDescription("Returns all registered fleets (repository collections).")
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Fleet>>("Paginated fleet list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/fleets/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Fleet> result = await _Database.Fleets.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Enumerate fleets")
                .WithDescription("Paginated enumeration of fleets with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<Fleet>("/api/v1/fleets", async (AppRequest req) =>
            {
                Fleet fleet = req.GetData<Fleet>();
                fleet = await _Database.Fleets.CreateAsync(fleet).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return fleet;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Create a fleet")
                .WithDescription("Creates a new fleet and returns it with an assigned ID.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Fleet>("Fleet data", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Fleet>("Created fleet"))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/fleets/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Fleet? fleet = await _Database.Fleets.ReadAsync(id).ConfigureAwait(false);
                if (fleet == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Fleet not found" };
                List<Vessel> vessels = await _Database.Vessels.EnumerateByFleetAsync(id).ConfigureAwait(false);
                return (object)new { Fleet = fleet, Vessels = vessels };
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Get a fleet")
                .WithDescription("Returns a single fleet by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet ID (flt_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Fleet>("Fleet details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Put<Fleet>("/api/v1/fleets/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Fleet? existing = await _Database.Fleets.ReadAsync(id).ConfigureAwait(false);
                if (existing == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Fleet not found" };
                Fleet updated = req.GetData<Fleet>();
                updated.Id = id;
                updated = await _Database.Fleets.UpdateAsync(updated).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Update a fleet")
                .WithDescription("Updates an existing fleet by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet ID (flt_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Fleet>("Updated fleet data", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Fleet>("Updated fleet"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/fleets/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                await _Database.Fleets.DeleteAsync(id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Delete a fleet")
                .WithDescription("Deletes a fleet by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Fleet ID (flt_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithSecurity("ApiKey"));

            _App.Rest.Post<DeleteMultipleRequest>("/api/v1/fleets/delete/multiple", async (AppRequest req) =>
            {
                DeleteMultipleRequest body = req.GetData<DeleteMultipleRequest>();
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
                    bool exists = await _Database.Fleets.ExistsAsync(id).ConfigureAwait(false);
                    if (!exists)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    await _Database.Fleets.DeleteAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await EmitEventAsync("fleet.batch_deleted", "Batch deleted " + result.Deleted + " fleets",
                    entityType: "fleet").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("Fleets")
                .WithSummary("Batch delete multiple fleets")
                .WithDescription("Permanently deletes multiple fleets from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of fleet IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            // Vessels
            _App.Rest.Get("/api/v1/vessels", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Vessel> result = await _Database.Vessels.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("List all vessels")
                .WithDescription("Returns all registered vessels (git repositories), optionally filtered by fleet.")
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Filter by fleet ID", false))
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Vessel>>("Paginated vessel list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/vessels/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Vessel> result = await _Database.Vessels.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Enumerate vessels")
                .WithDescription("Paginated enumeration of vessels with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<Vessel>("/api/v1/vessels", async (AppRequest req) =>
            {
                Vessel vessel = req.GetData<Vessel>();
                vessel = await _Database.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return vessel;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Create a vessel")
                .WithDescription("Registers a new vessel (git repository) and returns it with an assigned ID.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Vessel>("Vessel data", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Vessel>("Created vessel"))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/vessels/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Vessel? vessel = await _Database.Vessels.ReadAsync(id).ConfigureAwait(false);
                if (vessel == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                return (object)vessel;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Get a vessel")
                .WithDescription("Returns a single vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Vessel>("Vessel details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Put<Vessel>("/api/v1/vessels/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Vessel? existing = await _Database.Vessels.ReadAsync(id).ConfigureAwait(false);
                if (existing == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                Vessel updated = req.GetData<Vessel>();
                updated.Id = id;
                updated = await _Database.Vessels.UpdateAsync(updated).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Update a vessel")
                .WithDescription("Updates an existing vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Vessel>("Updated vessel data", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Vessel>("Updated vessel"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Patch<Vessel>("/api/v1/vessels/{id}/context", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Vessel? existing = await _Database.Vessels.ReadAsync(id).ConfigureAwait(false);
                if (existing == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                Vessel patch = req.GetData<Vessel>();
                if (patch.ProjectContext != null)
                    existing.ProjectContext = patch.ProjectContext;
                if (patch.StyleGuide != null)
                    existing.StyleGuide = patch.StyleGuide;
                existing = await _Database.Vessels.UpdateAsync(existing).ConfigureAwait(false);
                return (object)existing;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Update vessel context")
                .WithDescription("Updates only the ProjectContext and StyleGuide fields of a vessel.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Vessel>("Vessel context data (projectContext, styleGuide)", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Vessel>("Updated vessel"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/vessels/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                await _Database.Vessels.DeleteAsync(id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Delete a vessel")
                .WithDescription("Deletes a vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithSecurity("ApiKey"));

            _App.Rest.Post<DeleteMultipleRequest>("/api/v1/vessels/delete/multiple", async (AppRequest req) =>
            {
                DeleteMultipleRequest body = req.GetData<DeleteMultipleRequest>();
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
                    bool exists = await _Database.Vessels.ExistsAsync(id).ConfigureAwait(false);
                    if (!exists)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    await _Database.Vessels.DeleteAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await EmitEventAsync("vessel.batch_deleted", "Batch deleted " + result.Deleted + " vessels",
                    entityType: "vessel").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Batch delete multiple vessels")
                .WithDescription("Permanently deletes multiple vessels from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of vessel IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            // Voyages
            _App.Rest.Get("/api/v1/voyages", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Voyage> result = await _Database.Voyages.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("List all voyages")
                .WithDescription("Returns all voyages, optionally filtered by status (Active, Complete, Cancelled).")
                .WithParameter(OpenApiParameterMetadata.Query("status", "Filter by voyage status", false))
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Voyage>>("Paginated voyage list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/voyages/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Voyage> result = await _Database.Voyages.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Enumerate voyages")
                .WithDescription("Paginated enumeration of voyages with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<VoyageRequest>("/api/v1/voyages", async (AppRequest req) =>
            {
                VoyageRequest voyageReq = req.GetData<VoyageRequest>();
                List<MissionDescription> missions = new List<MissionDescription>();
                if (voyageReq.Missions != null)
                {
                    foreach (MissionRequest m in voyageReq.Missions)
                    {
                        missions.Add(new MissionDescription(m.Title, m.Description));
                    }
                }

                Voyage voyage;
                if (String.IsNullOrEmpty(voyageReq.VesselId) || missions.Count == 0)
                {
                    // Bare voyage creation (missions added separately)
                    voyage = new Voyage(voyageReq.Title, voyageReq.Description);
                    voyage = await _Database.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                    _Logging.Info(_Header + "created bare voyage " + voyage.Id + ": " + voyageReq.Title);
                }
                else
                {
                    voyage = await _Admiral.DispatchVoyageAsync(
                        voyageReq.Title, voyageReq.Description, voyageReq.VesselId, missions).ConfigureAwait(false);
                }

                req.Http.Response.StatusCode = 201;
                return voyage;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Create a voyage")
                .WithDescription("Creates a new voyage with optional missions. Missions are automatically dispatched to the target vessel.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<VoyageRequest>("Voyage with missions", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Voyage>("Created voyage"))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/voyages/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Voyage? voyage = await _Database.Voyages.ReadAsync(id).ConfigureAwait(false);
                if (voyage == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" };
                List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false);
                return (object)new { Voyage = voyage, Missions = missions };
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Get a voyage")
                .WithDescription("Returns a voyage and all its associated missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/voyages/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Voyage? voyage = await _Database.Voyages.ReadAsync(id).ConfigureAwait(false);
                if (voyage == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" };
                voyage.Status = VoyageStatusEnum.Cancelled;
                voyage.CompletedUtc = DateTime.UtcNow;
                voyage.LastUpdateUtc = DateTime.UtcNow;
                voyage = await _Database.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                // Cancel all pending/assigned missions in the voyage
                List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false);
                int cancelledCount = 0;
                foreach (Mission m in missions)
                {
                    if (m.Status == MissionStatusEnum.Pending || m.Status == MissionStatusEnum.Assigned)
                    {
                        // Release the captain if this mission was assigned to one
                        if (!String.IsNullOrEmpty(m.CaptainId))
                        {
                            Captain? captain = await _Database.Captains.ReadAsync(m.CaptainId).ConfigureAwait(false);
                            if (captain != null && captain.CurrentMissionId == m.Id)
                            {
                                List<Mission> otherMissions = (await _Database.Missions.EnumerateByCaptainAsync(captain.Id).ConfigureAwait(false))
                                    .Where(om => om.Id != m.Id && (om.Status == MissionStatusEnum.InProgress || om.Status == MissionStatusEnum.Assigned)).ToList();
                                if (otherMissions.Count == 0)
                                {
                                    captain.State = CaptainStateEnum.Idle;
                                    captain.CurrentMissionId = null;
                                    captain.CurrentDockId = null;
                                    captain.ProcessId = null;
                                    captain.RecoveryAttempts = 0;
                                    captain.LastUpdateUtc = DateTime.UtcNow;
                                    await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                                }
                            }
                        }

                        m.Status = MissionStatusEnum.Cancelled;
                        m.CompletedUtc = DateTime.UtcNow;
                        m.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(m).ConfigureAwait(false);
                        cancelledCount++;
                    }
                }
                // Broadcast voyage and mission cancellations for dashboard toast notifications
                if (_WebSocketHub != null)
                {
                    _WebSocketHub.BroadcastVoyageChange(id, VoyageStatusEnum.Cancelled.ToString(), voyage.Title);
                    foreach (Mission cm in missions)
                    {
                        if (cm.Status == MissionStatusEnum.Cancelled)
                        {
                            _WebSocketHub.BroadcastMissionChange(cm.Id, MissionStatusEnum.Cancelled.ToString(), cm.Title);
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

            _App.Rest.Delete("/api/v1/voyages/{id}/purge", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Voyage? voyage = await _Database.Voyages.ReadAsync(id).ConfigureAwait(false);
                if (voyage == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Voyage not found" };

                // Block deletion of active voyages
                if (voyage.Status == VoyageStatusEnum.Open || voyage.Status == VoyageStatusEnum.InProgress)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot delete voyage while status is " + voyage.Status + ". Cancel the voyage first." };
                }

                List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false);

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
                    await _Database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                }

                // Delete the voyage itself
                await _Database.Voyages.DeleteAsync(id).ConfigureAwait(false);

                await EmitEventAsync("voyage.deleted", "Voyage " + id + " permanently deleted with " + missions.Count + " missions",
                    entityType: "voyage", entityId: id).ConfigureAwait(false);

                return (object)new { Status = "deleted", VoyageId = id, MissionsDeleted = missions.Count };
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Permanently delete a voyage")
                .WithDescription("Permanently deletes a voyage and all its associated missions from the database. This cannot be undone. Blocked if voyage is Open/InProgress or has active missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<object>("Deleted voyage and missions"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiResponseMetadata.Json<object>("Voyage cannot be deleted while active"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<DeleteMultipleRequest>("/api/v1/voyages/delete/multiple", async (AppRequest req) =>
            {
                DeleteMultipleRequest body = req.GetData<DeleteMultipleRequest>();
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
                    Voyage? voyage = await _Database.Voyages.ReadAsync(id).ConfigureAwait(false);
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
                    List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false);
                    List<Mission> activeMissions = missions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress).ToList();
                    if (activeMissions.Count > 0)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete voyage with " + activeMissions.Count + " active mission(s). Cancel or complete them first."));
                        continue;
                    }
                    foreach (Mission m in missions)
                    {
                        await _Database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                    }
                    await _Database.Voyages.DeleteAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await EmitEventAsync("voyage.batch_deleted", "Batch deleted " + result.Deleted + " voyages",
                    entityType: "voyage").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("Voyages")
                .WithSummary("Batch delete multiple voyages")
                .WithDescription("Permanently deletes multiple voyages and their associated missions from the database by ID. Voyages that are Open/InProgress or have active missions are skipped. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of voyage IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            // Missions
            _App.Rest.Get("/api/v1/missions", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Mission> result = await _Database.Missions.EnumerateAsync(query).ConfigureAwait(false);
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
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Mission>>("Paginated mission list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/missions/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Mission> result = await _Database.Missions.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                foreach (Mission m in result.Objects) m.DiffSnapshot = null;
                return result;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Enumerate missions")
                .WithDescription("Paginated enumeration of missions with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<Mission>("/api/v1/missions", async (AppRequest req) =>
            {
                Mission mission = req.GetData<Mission>();
                mission = await _Admiral.DispatchMissionAsync(mission).ConfigureAwait(false);
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
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Mission>("Mission data", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Mission>("Created mission"))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/missions/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Mission? mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (mission == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };
                mission.DiffSnapshot = null;
                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get a mission")
                .WithDescription("Returns a single mission by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Mission>("Mission details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Put<Mission>("/api/v1/missions/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Mission? existing = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (existing == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };
                Mission incoming = req.GetData<Mission>();

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

                existing = await _Database.Missions.UpdateAsync(existing).ConfigureAwait(false);
                return (object)existing;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Update a mission")
                .WithDescription("Updates an existing mission by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Mission>("Updated mission data", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Mission>("Updated mission"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Put<StatusTransitionRequest>("/api/v1/missions/{id}/status", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Mission? mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (mission == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };

                StatusTransitionRequest transition = req.GetData<StatusTransitionRequest>();
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
                    Dock? landingDock = await _Database.Docks.ReadAsync(mission.DockId).ConfigureAwait(false);
                    if (landingDock != null && landingDock.Active)
                    {
                        // Capture diff before landing
                        if (_Admiral.OnCaptureDiff != null)
                        {
                            try
                            {
                                await _Admiral.OnCaptureDiff.Invoke(mission, landingDock).ConfigureAwait(false);
                            }
                            catch (Exception diffEx)
                            {
                                _Logging.Warn(_Header + "error capturing diff during manual completion of " + id + ": " + diffEx.Message);
                            }
                        }

                        // Set to WorkProduced first so the landing handler can process it
                        mission.Status = MissionStatusEnum.WorkProduced;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                        _Logging.Info(_Header + "manual Complete transition for " + id + " — routing through landing pipeline");

                        // Invoke the full landing pipeline (same as agent-driven completion)
                        await HandleMissionCompleteAsync(mission, landingDock).ConfigureAwait(false);

                        // Re-read the mission to get the final state after landing
                        mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                        if (mission == null)
                            return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found after landing" };

                        Signal landingSignal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " manual completion — landed as " + mission.Status);
                        if (!String.IsNullOrEmpty(mission.CaptainId)) landingSignal.FromCaptainId = mission.CaptainId;
                        await _Database.Signals.CreateAsync(landingSignal).ConfigureAwait(false);

                        await EmitEventAsync("mission.status_changed", "Mission " + id + " manually completed — landed as " + mission.Status,
                            entityType: "mission", entityId: id,
                            captainId: mission.CaptainId, missionId: id, vesselId: mission.VesselId, voyageId: mission.VoyageId).ConfigureAwait(false);

                        if (_WebSocketHub != null)
                            _WebSocketHub.BroadcastMissionChange(id, mission.Status.ToString(), mission.Title);

                        return (object)mission;
                    }
                }

                // Standard transition: no dock available or not transitioning to Complete
                mission.Status = newStatus;
                mission.LastUpdateUtc = DateTime.UtcNow;

                if (newStatus == MissionStatusEnum.Complete || newStatus == MissionStatusEnum.Failed ||
                    newStatus == MissionStatusEnum.LandingFailed || newStatus == MissionStatusEnum.Cancelled)
                {
                    mission.CompletedUtc = DateTime.UtcNow;
                }

                await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                // Audit event: manual Complete without an active dock bypasses the landing pipeline.
                // This is allowed (operators may need it after restarts/cleanup) but should be visible.
                if (newStatus == MissionStatusEnum.Complete)
                {
                    _Logging.Warn(_Header + "mission " + id + " manually completed without active dock — landing pipeline was skipped");
                    await EmitEventAsync("mission.manual_complete_no_dock",
                        "Mission " + id + " manually marked Complete without an active dock (landing pipeline skipped)",
                        entityType: "mission", entityId: id,
                        captainId: mission.CaptainId, missionId: id, vesselId: mission.VesselId, voyageId: mission.VoyageId).ConfigureAwait(false);
                }

                Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " transitioned to " + newStatus);
                if (!String.IsNullOrEmpty(mission.CaptainId)) signal.FromCaptainId = mission.CaptainId;
                await _Database.Signals.CreateAsync(signal).ConfigureAwait(false);

                await EmitEventAsync("mission.status_changed", "Mission " + id + " transitioned to " + newStatus,
                    entityType: "mission", entityId: id,
                    captainId: mission.CaptainId, missionId: id, vesselId: mission.VesselId, voyageId: mission.VoyageId).ConfigureAwait(false);

                // Broadcast specific mission change for dashboard toast notifications
                if (_WebSocketHub != null)
                {
                    _WebSocketHub.BroadcastMissionChange(id, newStatus.ToString(), mission.Title);
                }

                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Transition mission status")
                .WithDescription("Transitions a mission to a new status. Valid transitions: Pending→Assigned, Assigned→InProgress, InProgress→Testing/Review/Complete/Failed, Testing→Review/InProgress/Complete/Failed, Review→Complete/InProgress/Failed. Most states allow →Cancelled.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<StatusTransitionRequest>("Target status", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Mission>("Updated mission"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/missions/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Mission? mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (mission == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };
                mission.Status = MissionStatusEnum.Cancelled;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                mission = await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Cancel a mission")
                .WithDescription("Cancels a mission by setting its status to Cancelled. Returns the full updated mission.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Mission>("Cancelled mission"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/missions/{id}/purge", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Mission? mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (mission == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };

                await _Database.Missions.DeleteAsync(id).ConfigureAwait(false);

                await EmitEventAsync("mission.deleted", "Mission " + id + " permanently deleted",
                    entityType: "mission", entityId: id).ConfigureAwait(false);

                return (object)new { Status = "deleted", MissionId = id };
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Permanently delete a mission")
                .WithDescription("Permanently deletes a mission from the database. This cannot be undone.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<object>("Deleted mission"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Post<DeleteMultipleRequest>("/api/v1/missions/delete/multiple", async (AppRequest req) =>
            {
                DeleteMultipleRequest body = req.GetData<DeleteMultipleRequest>();
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
                    Mission? mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                    if (mission == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    await _Database.Missions.DeleteAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await EmitEventAsync("mission.batch_deleted", "Batch deleted " + result.Deleted + " missions",
                    entityType: "mission").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Batch delete multiple missions")
                .WithDescription("Permanently deletes multiple missions from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of mission IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<MissionRestartRequest>("/api/v1/missions/{id}/restart", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Mission? mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (mission == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };

                if (mission.Status != MissionStatusEnum.Failed && mission.Status != MissionStatusEnum.Cancelled)
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Only Failed or Cancelled missions can be restarted" };

                try
                {
                    MissionRestartRequest body = req.GetData<MissionRestartRequest>();
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
                mission = await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + id + " restarted");
                await _Database.Signals.CreateAsync(signal).ConfigureAwait(false);

                await EmitEventAsync("mission.restarted", "Mission " + id + " restarted",
                    entityType: "mission", entityId: id,
                    missionId: id, vesselId: mission.VesselId, voyageId: mission.VoyageId).ConfigureAwait(false);

                // Broadcast specific mission change for dashboard toast notifications
                if (_WebSocketHub != null)
                {
                    _WebSocketHub.BroadcastMissionChange(id, MissionStatusEnum.Pending.ToString(), mission.Title);
                }

                return (object)mission;
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Restart a failed or cancelled mission")
                .WithDescription("Resets a Failed or Cancelled mission back to Pending so it can be re-dispatched. Optionally update the title and description (instructions) before restarting. Clears captain assignment, branch, PR URL, and timing fields.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<MissionRestartRequest>("Optional updated instructions", false))
                .WithResponse(200, OpenApiResponseMetadata.Json<Mission>("Restarted mission"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/missions/{id}/diff", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Mission? mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (mission == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };

                // Check for a saved diff file first (captured at completion time)
                string savedDiffPath = Path.Combine(_Settings.LogDirectory, "diffs", id + ".diff");
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
                    dock = await _Database.Docks.ReadAsync(mission.DockId).ConfigureAwait(false);
                }

                if (dock == null && !String.IsNullOrEmpty(mission.CaptainId))
                {
                    Captain? captain = await _Database.Captains.ReadAsync(mission.CaptainId).ConfigureAwait(false);
                    if (captain != null && !String.IsNullOrEmpty(captain.CurrentDockId))
                    {
                        dock = await _Database.Docks.ReadAsync(captain.CurrentDockId).ConfigureAwait(false);
                    }
                }

                if (dock == null && !String.IsNullOrEmpty(mission.BranchName) && !String.IsNullOrEmpty(mission.VesselId))
                {
                    List<Dock> docks = await _Database.Docks.EnumerateByVesselAsync(mission.VesselId).ConfigureAwait(false);
                    dock = docks.FirstOrDefault(d => d.BranchName == mission.BranchName && d.Active);
                }

                if (dock == null || String.IsNullOrEmpty(dock.WorktreePath) || !Directory.Exists(dock.WorktreePath))
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "No diff available — worktree was already reclaimed and no saved diff exists" };

                string baseBranch = "main";
                if (!String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                    if (vessel != null) baseBranch = vessel.DefaultBranch;
                }

                string diff = await _Git.DiffAsync(dock.WorktreePath, baseBranch).ConfigureAwait(false);
                return (object)new { MissionId = id, Branch = dock.BranchName ?? "", Diff = diff };
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get diff for a mission")
                .WithDescription("Returns the git diff of changes made by a captain in the mission's worktree.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/missions/{id}/log", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Mission? mission = await _Database.Missions.ReadAsync(id).ConfigureAwait(false);
                if (mission == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Mission not found" };

                string logPath = Path.Combine(_Settings.LogDirectory, "missions", id + ".log");
                if (!File.Exists(logPath))
                    return (object)new { MissionId = id, Log = "", Lines = 0, TotalLines = 0 };

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
            },
            api => api
                .WithTag("Missions")
                .WithSummary("Get log for a mission")
                .WithDescription("Returns the session log for a mission. Supports pagination via ?lines=N (default 200) and ?offset=N query parameters.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            // Captains
            _App.Rest.Get("/api/v1/captains", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Captain> result = await _Database.Captains.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("List all captains")
                .WithDescription("Returns all registered captains (AI agents) with their current state.")
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Captain>>("Paginated captain list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/captains/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Captain> result = await _Database.Captains.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Enumerate captains")
                .WithDescription("Paginated enumeration of captains with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<Captain>("/api/v1/captains", async (AppRequest req) =>
            {
                Captain captain = req.GetData<Captain>();
                captain = await _Database.Captains.CreateAsync(captain).ConfigureAwait(false);
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

            _App.Rest.Get("/api/v1/captains/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Captain? captain = await _Database.Captains.ReadAsync(id).ConfigureAwait(false);
                if (captain == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" };
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

            _App.Rest.Put<Captain>("/api/v1/captains/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Captain? existing = await _Database.Captains.ReadAsync(id).ConfigureAwait(false);
                if (existing == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" };
                Captain updated = req.GetData<Captain>();
                updated.Id = id;
                updated.State = existing.State;
                updated.CurrentMissionId = existing.CurrentMissionId;
                updated.CurrentDockId = existing.CurrentDockId;
                updated.ProcessId = existing.ProcessId;
                updated.RecoveryAttempts = existing.RecoveryAttempts;
                updated.LastHeartbeatUtc = existing.LastHeartbeatUtc;
                updated.CreatedUtc = existing.CreatedUtc;
                updated.LastUpdateUtc = DateTime.UtcNow;
                updated = await _Database.Captains.UpdateAsync(updated).ConfigureAwait(false);
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

            _App.Rest.Post("/api/v1/captains/{id}/stop", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Captain? captain = await _Database.Captains.ReadAsync(id).ConfigureAwait(false);
                if (captain == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" };

                // Kill the process if running
                if (captain.ProcessId.HasValue)
                {
                    Armada.Runtimes.Interfaces.IAgentRuntime runtime = _RuntimeFactory.Create(captain.Runtime);
                    await runtime.StopAsync(captain.ProcessId.Value).ConfigureAwait(false);
                }

                await _Admiral.RecallCaptainAsync(id).ConfigureAwait(false);
                return new { Status = "stopped" };
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Stop a captain")
                .WithDescription("Stops a running captain agent, killing its process and recalling it to idle state.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Post("/api/v1/captains/stop-all", async (AppRequest req) =>
            {
                await _Admiral.RecallAllAsync().ConfigureAwait(false);
                return (object)new { Status = "all_stopped" };
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Stop all captains")
                .WithDescription("Emergency stop all running captains, recalling them to idle state.")
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/captains/{id}/log", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Captain? captain = await _Database.Captains.ReadAsync(id).ConfigureAwait(false);
                if (captain == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" };

                string pointerPath = Path.Combine(_Settings.LogDirectory, "captains", id + ".current");
                string? logPath = null;

                if (File.Exists(pointerPath))
                {
                    string target = (await ReadFileSharedAsync(pointerPath).ConfigureAwait(false)).Trim();
                    if (File.Exists(target))
                        logPath = target;
                }

                if (logPath == null)
                    return (object)new { CaptainId = id, Log = "", Lines = 0, TotalLines = 0 };

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
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Get current log for a captain")
                .WithDescription("Returns the current session log for a captain, resolved via the .current pointer file. Supports pagination via ?lines=N (default 50) and ?offset=N.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/captains/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Captain? captain = await _Database.Captains.ReadAsync(id).ConfigureAwait(false);
                if (captain == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Captain not found" };

                // Block deletion of working captains
                if (captain.State == CaptainStateEnum.Working)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot delete captain while state is Working. Stop the captain first." };
                }

                // Block deletion if captain has active missions
                List<Mission> captainMissions = await _Database.Missions.EnumerateByCaptainAsync(id).ConfigureAwait(false);
                List<Mission> activeCaptainMissions = captainMissions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress).ToList();
                if (activeCaptainMissions.Count > 0)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot delete captain with " + activeCaptainMissions.Count + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };
                }

                await _Database.Captains.DeleteAsync(id).ConfigureAwait(false);
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

            _App.Rest.Post<DeleteMultipleRequest>("/api/v1/captains/delete/multiple", async (AppRequest req) =>
            {
                DeleteMultipleRequest body = req.GetData<DeleteMultipleRequest>();
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
                    Captain? captain = await _Database.Captains.ReadAsync(id).ConfigureAwait(false);
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
                    List<Mission> captainMissions = await _Database.Missions.EnumerateByCaptainAsync(id).ConfigureAwait(false);
                    List<Mission> activeCaptainMissions = captainMissions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress).ToList();
                    if (activeCaptainMissions.Count > 0)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete captain with " + activeCaptainMissions.Count + " active mission(s). Cancel or complete them first."));
                        continue;
                    }
                    await _Database.Captains.DeleteAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await EmitEventAsync("captain.batch_deleted", "Batch deleted " + result.Deleted + " captains",
                    entityType: "captain").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Batch delete multiple captains")
                .WithDescription("Permanently deletes multiple captains from the database by ID. Captains that are Working or have active missions are skipped. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of captain IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            // Docks
            _App.Rest.Get("/api/v1/docks", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Dock> result = await _Database.Docks.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Docks")
                .WithSummary("List docks")
                .WithDescription("Returns all docks (git worktrees) with optional filtering by vesselId.")
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Filter by vessel ID", false))
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Dock>>("Paginated dock list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/docks/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Dock> result = await _Database.Docks.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Docks")
                .WithSummary("Enumerate docks")
                .WithDescription("Paginated enumeration of docks with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/docks/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Dock? dock = await _Database.Docks.ReadAsync(id).ConfigureAwait(false);
                if (dock == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Dock not found" };
                return (object)dock;
            },
            api => api
                .WithTag("Docks")
                .WithSummary("Get a dock")
                .WithDescription("Returns a single dock by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Dock ID (dck_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Dock>("Dock details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/docks/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Dock? dock = await _Database.Docks.ReadAsync(id).ConfigureAwait(false);
                if (dock == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Dock not found" };

                bool deleted = await _Docks.DeleteAsync(id).ConfigureAwait(false);
                if (!deleted)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot delete dock while it is actively in use by a captain" };
                }

                await EmitEventAsync("dock.deleted", "Dock " + id + " deleted",
                    entityType: "dock", entityId: id).ConfigureAwait(false);

                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Docks")
                .WithSummary("Delete a dock")
                .WithDescription("Deletes a dock and cleans up its git worktree. Blocked if the dock is actively in use by a captain.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Dock ID (dck_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiResponseMetadata.Json<object>("Dock is actively in use"))
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/docks/{id}/purge", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Dock? dock = await _Database.Docks.ReadAsync(id).ConfigureAwait(false);
                if (dock == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Dock not found" };

                await _Docks.PurgeAsync(id).ConfigureAwait(false);

                await EmitEventAsync("dock.purged", "Dock " + id + " force purged",
                    entityType: "dock", entityId: id).ConfigureAwait(false);

                return (object)new { Status = "purged", DockId = id };
            },
            api => api
                .WithTag("Docks")
                .WithSummary("Force purge a dock")
                .WithDescription("Force purges a dock and its git worktree, even if a mission references it. This cannot be undone.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Dock ID (dck_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<object>("Purged dock"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Post<DeleteMultipleRequest>("/api/v1/docks/delete/multiple", async (AppRequest req) =>
            {
                DeleteMultipleRequest body = req.GetData<DeleteMultipleRequest>();
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
                    Dock? dock = await _Database.Docks.ReadAsync(id).ConfigureAwait(false);
                    if (dock == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    await _Docks.PurgeAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await EmitEventAsync("dock.batch_deleted", "Batch deleted " + result.Deleted + " docks",
                    entityType: "dock").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("Docks")
                .WithSummary("Batch delete multiple docks")
                .WithDescription("Permanently deletes multiple docks and their git worktrees from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of dock IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            // Signals
            _App.Rest.Get("/api/v1/signals", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Signal> result = await _Database.Signals.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("List recent signals")
                .WithDescription("Returns the 50 most recent signals (inter-agent messages).")
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Signal>>("Paginated signal list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/signals/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Signal> result = await _Database.Signals.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Enumerate signals")
                .WithDescription("Paginated enumeration of signals with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<Signal>("/api/v1/signals", async (AppRequest req) =>
            {
                Signal signal = req.GetData<Signal>();
                signal = await _Database.Signals.CreateAsync(signal).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return signal;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Send a signal")
                .WithDescription("Creates a new signal (message between admiral and captains).")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Signal>("Signal data", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Signal>("Created signal"))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/signals/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Signal? signal = await _Database.Signals.ReadAsync(id).ConfigureAwait(false);
                if (signal == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Signal not found" };
                return (object)signal;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Get a signal")
                .WithDescription("Returns a single signal by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Signal ID (sig_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Signal>("Signal details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Put("/api/v1/signals/{id}/read", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Signal? signal = await _Database.Signals.ReadAsync(id).ConfigureAwait(false);
                if (signal == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Signal not found" };
                await _Database.Signals.MarkReadAsync(id).ConfigureAwait(false);
                Signal? updated = await _Database.Signals.ReadAsync(id).ConfigureAwait(false);
                return (object)updated!;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Mark a signal as read")
                .WithDescription("Marks a signal as read by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Signal ID (sig_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Signal>("Updated signal"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/signals/recipient/{captainId}", async (AppRequest req) =>
            {
                string captainId = req.Parameters["captainId"];
                string unreadOnlyStr = req.Query.GetValueOrDefault("unreadOnly");
                bool unreadOnly = String.IsNullOrEmpty(unreadOnlyStr) || bool.Parse(unreadOnlyStr);
                List<Signal> signals = await _Database.Signals.EnumerateByRecipientAsync(captainId, unreadOnly).ConfigureAwait(false);
                return signals;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Enumerate signals by recipient")
                .WithDescription("Returns signals addressed to a specific captain. Defaults to unread only.")
                .WithParameter(OpenApiParameterMetadata.Path("captainId", "Captain ID (cpt_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("unreadOnly", "Filter to unread signals only (default: true)", false, OpenApiSchemaMetadata.Boolean()))
                .WithResponse(200, OpenApiResponseMetadata.Json<List<Signal>>("Signal list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/signals/recent", async (AppRequest req) =>
            {
                string countStr = req.Query.GetValueOrDefault("count");
                int count = 50;
                if (!String.IsNullOrEmpty(countStr) && int.TryParse(countStr, out int parsedCount)) count = parsedCount;
                List<Signal> signals = await _Database.Signals.EnumerateRecentAsync(count).ConfigureAwait(false);
                return signals;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Get recent signals")
                .WithDescription("Returns the most recent signals, ordered by creation time descending.")
                .WithParameter(OpenApiParameterMetadata.Query("count", "Maximum number of signals to return (default: 50)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiResponseMetadata.Json<List<Signal>>("Signal list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/signals/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                Signal? signal = await _Database.Signals.ReadAsync(id).ConfigureAwait(false);
                if (signal == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Signal not found" };
                // Mark as read to "soft delete" since no hard delete exists
                await _Database.Signals.MarkReadAsync(id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Delete a signal")
                .WithDescription("Soft-deletes a signal by marking it as read.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Signal ID (sig_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Post<DeleteMultipleRequest>("/api/v1/signals/delete/multiple", async (AppRequest req) =>
            {
                DeleteMultipleRequest body = req.GetData<DeleteMultipleRequest>();
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
                    Signal? signal = await _Database.Signals.ReadAsync(id).ConfigureAwait(false);
                    if (signal == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    await _Database.Signals.MarkReadAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await EmitEventAsync("signal.batch_deleted", "Batch deleted " + result.Deleted + " signals",
                    entityType: "signal").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("Signals")
                .WithSummary("Batch delete multiple signals")
                .WithDescription("Soft-deletes multiple signals by marking them as read. Returns a summary of deleted and skipped entries.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of signal IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            // Events
            _App.Rest.Get("/api/v1/events", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                string limitStr = req.Query.GetValueOrDefault("limit");
                if (!String.IsNullOrEmpty(limitStr) && int.TryParse(limitStr, out int limit)) query.PageSize = limit;
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<ArmadaEvent> result = await _Database.Events.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Events")
                .WithSummary("List events")
                .WithDescription("Returns system events, filterable by type, captainId, missionId, vesselId, or voyageId. Default limit is 50.")
                .WithParameter(OpenApiParameterMetadata.Query("type", "Filter by event type (e.g. mission.status_changed, captain.launched)", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Filter by captain ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Filter by mission ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Filter by vessel ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Filter by voyage ID", false))
                .WithParameter(OpenApiParameterMetadata.Query("limit", "Maximum number of events to return (default: 50)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<ArmadaEvent>>("Paginated event list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/events/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                string limitStr = req.Query.GetValueOrDefault("limit");
                if (!String.IsNullOrEmpty(limitStr) && int.TryParse(limitStr, out int limit)) query.PageSize = limit;
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<ArmadaEvent> result = await _Database.Events.EnumerateAsync(query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Events")
                .WithSummary("Enumerate events")
                .WithDescription("Paginated enumeration of events with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/events/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                ArmadaEvent? evt = await _Database.Events.ReadAsync(id).ConfigureAwait(false);
                if (evt == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Event not found" };
                await _Database.Events.DeleteAsync(id).ConfigureAwait(false);
                await EmitEventAsync("event.deleted", "Deleted event " + id,
                    entityType: "event", entityId: id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Events")
                .WithSummary("Delete an event")
                .WithDescription("Permanently deletes an event by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Event ID (evt_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Post<DeleteMultipleRequest>("/api/v1/events/delete/multiple", async (AppRequest req) =>
            {
                DeleteMultipleRequest body = req.GetData<DeleteMultipleRequest>();
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
                    ArmadaEvent? evt = await _Database.Events.ReadAsync(id).ConfigureAwait(false);
                    if (evt == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    await _Database.Events.DeleteAsync(id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await EmitEventAsync("event.batch_deleted", "Batch deleted " + result.Deleted + " events",
                    entityType: "event").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("Events")
                .WithSummary("Batch delete multiple events")
                .WithDescription("Permanently deletes multiple events by ID. Returns a summary of deleted and skipped entries.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of event IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));

            // Merge Queue
            _App.Rest.Get("/api/v1/merge-queue", async (AppRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                List<MergeEntry> all = await _MergeQueue.ListAsync().ConfigureAwait(false);
                int totalCount = all.Count;
                List<MergeEntry> page = all.Skip(query.Offset).Take(query.PageSize).ToList();
                EnumerationResult<MergeEntry> result = EnumerationResult<MergeEntry>.Create(query, page, totalCount);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("List merge queue entries")
                .WithDescription("Returns all entries in the merge queue.")
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<MergeEntry>>("Paginated merge queue list"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<EnumerationQuery>("/api/v1/merge-queue/enumerate", async (AppRequest req) =>
            {
                EnumerationQuery query = req.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                List<MergeEntry> all = await _MergeQueue.ListAsync().ConfigureAwait(false);
                int totalCount = all.Count;
                List<MergeEntry> page = all.Skip(query.Offset).Take(query.PageSize).ToList();
                EnumerationResult<MergeEntry> result = EnumerationResult<MergeEntry>.Create(query, page, totalCount);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Enumerate merge queue entries")
                .WithDescription("Paginated enumeration of merge queue entries with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<MergeEntry>("/api/v1/merge-queue", async (AppRequest req) =>
            {
                MergeEntry entry = req.GetData<MergeEntry>();
                entry = await _MergeQueue.EnqueueAsync(entry).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return entry;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Enqueue a branch for merge")
                .WithDescription("Adds a branch to the merge queue for testing and merging into the target branch.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<MergeEntry>("Merge entry", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<MergeEntry>("Enqueued entry"))
                .WithSecurity("ApiKey"));

            _App.Rest.Get("/api/v1/merge-queue/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                MergeEntry? entry = await _MergeQueue.GetAsync(id).ConfigureAwait(false);
                if (entry == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Merge entry not found" };
                return (object)entry;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Get a merge queue entry")
                .WithDescription("Returns a single merge queue entry by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Merge entry ID (mrg_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<MergeEntry>("Merge entry details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/merge-queue/{id}", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                bool deleted = await _MergeQueue.DeleteAsync(id).ConfigureAwait(false);
                if (!deleted)
                {
                    // Fall back to cancel if not in a terminal state
                    await _MergeQueue.CancelAsync(id).ConfigureAwait(false);
                }
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Delete or cancel a merge queue entry")
                .WithDescription("Permanently deletes a terminal merge entry (Cancelled, Landed, Failed) or cancels an active one.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Merge entry ID (mrg_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithSecurity("ApiKey"));

            _App.Rest.Post("/api/v1/merge-queue/{id}/process", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                MergeEntry? entry = await _MergeQueue.ProcessSingleAsync(id).ConfigureAwait(false);
                if (entry == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Merge entry not found or not in Queued status" };
                return (object)entry;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Process a single merge queue entry")
                .WithDescription("Processes a single merge queue entry by ID: creates integration branch, runs tests, and lands if passing.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Merge entry ID (mrg_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<MergeEntry>("Processed merge entry"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            _App.Rest.Post("/api/v1/merge-queue/process", async (AppRequest req) =>
            {
                await _MergeQueue.ProcessQueueAsync().ConfigureAwait(false);
                return new { Status = "processed" };
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Process the merge queue")
                .WithDescription("Triggers processing of all queued entries: creates integration branches, runs tests, and lands passing batches.")
                .WithSecurity("ApiKey"));

            _App.Rest.Delete("/api/v1/merge-queue/{id}/purge", async (AppRequest req) =>
            {
                string id = req.Parameters["id"];
                MergeEntry? entry = await _MergeQueue.GetAsync(id).ConfigureAwait(false);
                if (entry == null) return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Merge entry not found" };

                bool deleted = await _MergeQueue.DeleteAsync(id).ConfigureAwait(false);
                if (!deleted)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new { Error = "Conflict", Message = "Cannot purge merge entry in non-terminal status " + entry.Status + ". Only Landed, Failed, or Cancelled entries can be purged." };
                }

                await EmitEventAsync("merge.purged", "Merge entry " + id + " purged",
                    entityType: "merge_entry", entityId: id).ConfigureAwait(false);

                return (object)new { Status = "purged", EntryId = id };
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Purge a single merge queue entry")
                .WithDescription("Permanently deletes a terminal merge queue entry from the database. Only entries in Landed, Failed, or Cancelled status can be purged. This cannot be undone.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Merge entry ID (mrg_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<object>("Purged merge entry"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiResponseMetadata.Json<object>("Entry is not in a terminal state"))
                .WithSecurity("ApiKey"));

            _App.Rest.Post<PurgeMergeEntriesRequest>("/api/v1/merge-queue/purge", async (AppRequest req) =>
            {
                PurgeMergeEntriesRequest body = req.GetData<PurgeMergeEntriesRequest>();
                if (body == null || body.EntryIds == null || body.EntryIds.Count == 0)
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "EntryIds is required and must not be empty" };

                MergeQueuePurgeResult result = await _MergeQueue.DeleteMultipleAsync(body.EntryIds).ConfigureAwait(false);

                await EmitEventAsync("merge.batch_purged", "Batch purged " + result.EntriesPurged + " merge entries",
                    entityType: "merge_entry").ConfigureAwait(false);

                return (object)result;
            },
            api => api
                .WithTag("MergeQueue")
                .WithSummary("Batch purge merge queue entries")
                .WithDescription("Permanently deletes multiple terminal merge queue entries from the database by ID. Returns a summary of purged and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<PurgeMergeEntriesRequest>("List of merge entry IDs to purge"))
                .WithResponse(200, OpenApiResponseMetadata.Json<MergeQueuePurgeResult>("Purge result summary"))
                .WithSecurity("ApiKey"));

            // Backup & Restore
            _App.Rest.Get("/api/v1/backup", async (AppRequest req) =>
            {
                object backupResult = await McpToolRegistrar.PerformBackupAsync(_Database, _Settings, null).ConfigureAwait(false);
                string zipPath = (string)backupResult.GetType().GetProperty("Path")!.GetValue(backupResult)!;
                byte[] fileBytes = await File.ReadAllBytesAsync(zipPath).ConfigureAwait(false);
                string filename = Path.GetFileName(zipPath);
                req.Http.Response.ContentType = "application/zip";
                req.Http.Response.Headers.Add("Content-Disposition", "attachment; filename=\"" + filename + "\"");
                await req.Http.Response.Send(fileBytes).ConfigureAwait(false);
                return null;
            },
            api => api
                .WithTag("Backup")
                .WithSummary("Download backup")
                .WithDescription("Creates and streams a ZIP backup of the database and settings.")
                .WithSecurity("ApiKey"));

            _App.Rest.Post("/api/v1/restore", async (AppRequest req) =>
            {
                byte[] body = req.Http.Request.DataAsBytes;
                if (body == null || body.Length == 0)
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Request body must contain the ZIP file" };

                string tempZipPath = Path.Combine(Path.GetTempPath(), "armada-upload-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    await File.WriteAllBytesAsync(tempZipPath, body).ConfigureAwait(false);
                    string? originalFilename = req.Http.Request.Headers.Get("X-Original-Filename");
                    object result = await McpToolRegistrar.PerformRestoreAsync(_Database, _Settings, tempZipPath, originalFilename).ConfigureAwait(false);
                    return result;
                }
                finally
                {
                    if (File.Exists(tempZipPath))
                    {
                        try { File.Delete(tempZipPath); }
                        catch { /* best effort */ }
                    }
                }
            },
            api => api
                .WithTag("Backup")
                .WithSummary("Restore from backup")
                .WithDescription("Accepts a ZIP backup file in the request body and restores the database and settings. Server restart recommended after restore.")
                .WithSecurity("ApiKey"));
        }

        private void RegisterDashboardRoutes()
        {
            // Serve embedded static files for the web dashboard
            _App.Rest.DefaultRoute = async (WatsonWebserver.Core.HttpContextBase ctx) =>
            {
                string path = ctx.Request.Url.RawWithoutQuery;

                // Redirect root to dashboard
                if (path == "/" || path == "")
                {
                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers.Add("Location", "/dashboard");
                    await ctx.Response.Send().ConfigureAwait(false);
                    return;
                }

                // Serve dashboard static files
                if (path.StartsWith("/dashboard"))
                {
                    if (Dashboard.StaticFileHandler.TryGetFile(path, out byte[] content, out string contentType))
                    {
                        ctx.Response.ContentType = contentType;
                        await ctx.Response.Send(content).ConfigureAwait(false);
                        return;
                    }

                    // SPA fallback: serve index.html for unmatched dashboard routes
                    if (Dashboard.StaticFileHandler.TryGetFile("/dashboard/index.html", out byte[] indexContent, out string indexType))
                    {
                        ctx.Response.ContentType = indexType;
                        await ctx.Response.Send(indexContent).ConfigureAwait(false);
                        return;
                    }
                }

                // 404 for everything else
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send("{\"error\":\"Not found\"}").ConfigureAwait(false);
            };

            _Logging.Info(_Header + "dashboard registered at /dashboard");
        }

        private void RegisterMcpTools()
        {
            McpToolRegistrar.RegisterAll(
                _McpServer.RegisterTool,
                _Database,
                _Admiral,
                _Settings,
                _Git,
                _MergeQueue,
                _Docks,
                _LandingService,
                () => Stop(),
                async (captainId) =>
                {
                    Captain? captain = await _Database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (captain != null)
                        await HandleStopAgentAsync(captain).ConfigureAwait(false);
                });
        }

        private async Task<int> HandleLaunchAgentAsync(Captain captain, Mission mission, Dock dock)
        {
            _Logging.Info(_Header + "launching " + captain.Runtime + " agent for captain " + captain.Id);

            Armada.Runtimes.Interfaces.IAgentRuntime runtime = _RuntimeFactory.Create(captain.Runtime);

            // Wire up progress tracking, heartbeat, and process exit detection
            runtime.OnOutputReceived += HandleAgentOutput;
            runtime.OnOutputReceived += HandleAgentHeartbeat;
            runtime.OnProcessExited += HandleAgentProcessExited;

            string prompt = "Mission: " + mission.Title + "\n\n" + (mission.Description ?? "");

            // Append commit message template instructions to the agent prompt
            if (_Settings.MessageTemplates.EnableCommitMetadata)
            {
                Dictionary<string, string> templateContext = _TemplateService.BuildContext(mission, captain, null, null, dock);
                string commitInstructions = _TemplateService.RenderCommitInstructions(_Settings.MessageTemplates, templateContext);
                if (!String.IsNullOrEmpty(commitInstructions))
                {
                    prompt += "\n\n" + commitInstructions;
                }
            }

            // Per-mission log file (also symlinked as captain's current log)
            string missionLogDir = Path.Combine(_Settings.LogDirectory, "missions");
            string logFilePath = Path.Combine(missionLogDir, mission.Id + ".log");

            // Also write a captain-level pointer for `armada log captain-1`
            string captainLogDir = Path.Combine(_Settings.LogDirectory, "captains");
            Directory.CreateDirectory(captainLogDir);
            string captainLogPointer = Path.Combine(captainLogDir, captain.Id + ".current");
            File.WriteAllText(captainLogPointer, logFilePath);

            int processId = await runtime.StartAsync(
                dock.WorktreePath ?? throw new InvalidOperationException("Dock worktree path is null"),
                prompt,
                logFilePath: logFilePath).ConfigureAwait(false);

            // Track process-to-captain and process-to-mission mappings
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain[processId] = captain.Id;
                _ProcessToMission[processId] = mission.Id;
            }

            _Logging.Info(_Header + "agent process " + processId + " started for captain " + captain.Id + " (log: " + logFilePath + ")");

            await EmitEventAsync("captain.launched", "Agent process started for captain " + captain.Name,
                entityType: "captain", entityId: captain.Id,
                captainId: captain.Id, missionId: mission.Id, vesselId: mission.VesselId, voyageId: mission.VoyageId).ConfigureAwait(false);

            // Broadcast captain and mission state changes for dashboard toast notifications
            if (_WebSocketHub != null)
            {
                _WebSocketHub.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);
                _WebSocketHub.BroadcastMissionChange(mission.Id, mission.Status.ToString(), mission.Title);
            }

            return processId;
        }

        /// <summary>
        /// Update captain heartbeat on any agent output line.
        /// This is the ONLY place heartbeat should be updated — not the health check loop.
        /// Stall detection relies on this to distinguish alive-but-hung from alive-and-working.
        /// </summary>
        private void HandleAgentHeartbeat(int processId, string line)
        {
            string? captainId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.TryGetValue(processId, out captainId);
            }

            if (String.IsNullOrEmpty(captainId)) return;

            // Fire and forget heartbeat update
            _ = Task.Run(async () =>
            {
                try
                {
                    await _Database.Captains.UpdateHeartbeatAsync(captainId).ConfigureAwait(false);
                }
                catch { }
            });
        }

        private void HandleAgentOutput(int processId, string line)
        {
            ProgressParser.ProgressSignal? signal = ProgressParser.TryParse(line);
            if (signal == null) return;

            string? captainId = null;
            string? missionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.TryGetValue(processId, out captainId);
                _ProcessToMission.TryGetValue(processId, out missionId);
            }

            if (String.IsNullOrEmpty(captainId)) return;

            _Logging.Info(_Header + "progress signal from captain " + captainId + ": [" + signal.Type + "] " + signal.Value);

            // Fire and forget — update mission status in background
            string capturedCaptainId = captainId;
            string? capturedMissionId = missionId;
            _ = Task.Run(async () =>
            {
                try
                {
                    // Use mission ID from process mapping (supports parallelism), fall back to captain's current mission
                    string? targetMissionId = capturedMissionId;
                    if (String.IsNullOrEmpty(targetMissionId))
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(capturedCaptainId).ConfigureAwait(false);
                        targetMissionId = captain?.CurrentMissionId;
                    }

                    if (String.IsNullOrEmpty(targetMissionId)) return;

                    if (signal.Type == "status" && signal.MissionStatus.HasValue)
                    {
                        Mission? mission = await _Database.Missions.ReadAsync(targetMissionId).ConfigureAwait(false);
                        if (mission != null && IsValidTransition(mission.Status, signal.MissionStatus.Value))
                        {
                            mission.Status = signal.MissionStatus.Value;
                            mission.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                            _Logging.Info(_Header + "mission " + mission.Id + " transitioned to " + signal.MissionStatus.Value + " via agent signal");
                        }
                    }

                    // Log all progress signals
                    Signal dbSignal = new Signal(SignalTypeEnum.Progress, "[" + signal.Type + "] " + signal.Value);
                    dbSignal.FromCaptainId = capturedCaptainId;
                    await _Database.Signals.CreateAsync(dbSignal).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error processing progress signal: " + ex.Message);
                }
            });
        }

        private void HandleAgentProcessExited(int processId, int? exitCode)
        {
            // Look up captain and mission from process tracking maps
            string? captainId = null;
            string? missionId = null;
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.TryGetValue(processId, out captainId);
                _ProcessToMission.TryGetValue(processId, out missionId);
            }

            if (String.IsNullOrEmpty(captainId) || String.IsNullOrEmpty(missionId))
            {
                _Logging.Debug(_Header + "process " + processId + " exited (code " + (exitCode?.ToString() ?? "unknown") + ") but no captain/mission mapping found — likely already handled");
                return;
            }

            _Logging.Info(_Header + "process " + processId + " exited (code " + (exitCode?.ToString() ?? "unknown") + ") for captain " + captainId + " mission " + missionId);

            // Clean up process tracking immediately to prevent duplicate handling
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.Remove(processId);
                _ProcessToMission.Remove(processId);
            }

            // Handle the exit asynchronously — fire-and-forget since we're in an event handler
            string capturedCaptainId = captainId;
            string capturedMissionId = missionId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAgentProcessExitedAsync(processId, exitCode, capturedCaptainId, capturedMissionId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error handling process exit for captain " + capturedCaptainId + " mission " + capturedMissionId + ": " + ex.Message);
                }
            });
        }

        private async Task HandleAgentProcessExitedAsync(int processId, int? exitCode, string captainId, string missionId)
        {
            await _Admiral.HandleProcessExitAsync(processId, exitCode, captainId, missionId).ConfigureAwait(false);
        }

        private async Task HandleStopAgentAsync(Captain captain)
        {
            if (!captain.ProcessId.HasValue) return;

            _Logging.Info(_Header + "stopping agent process " + captain.ProcessId.Value + " for captain " + captain.Id);

            // Clean up process tracking
            lock (_ProcessToCaptain)
            {
                _ProcessToCaptain.Remove(captain.ProcessId.Value);
                _ProcessToMission.Remove(captain.ProcessId.Value);
            }

            Armada.Runtimes.Interfaces.IAgentRuntime runtime = _RuntimeFactory.Create(captain.Runtime);
            await runtime.StopAsync(captain.ProcessId.Value).ConfigureAwait(false);
        }

        private async Task HandleCaptureDiffAsync(Mission mission, Dock dock)
        {
            if (String.IsNullOrEmpty(dock.WorktreePath) || String.IsNullOrEmpty(dock.BranchName))
                return;

            _Logging.Info(_Header + "capturing diff for mission " + mission.Id + " before worktree reclamation");

            // Capture diff and persist to database + file
            string baseBranch = "main";
            try
            {
                if (!String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? diffVessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                    if (diffVessel != null) baseBranch = diffVessel.DefaultBranch;
                }

                string diff = await _Git.DiffAsync(dock.WorktreePath, baseBranch).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(diff))
                {
                    // Persist to database so it survives worktree reclamation
                    mission.DiffSnapshot = diff;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "persisted diff snapshot to database for mission " + mission.Id + " (" + diff.Length + " chars)");

                    // Also save to file for backwards compatibility
                    string diffDir = Path.Combine(_Settings.LogDirectory, "diffs");
                    Directory.CreateDirectory(diffDir);
                    string diffPath = Path.Combine(diffDir, mission.Id + ".diff");
                    await File.WriteAllTextAsync(diffPath, diff).ConfigureAwait(false);
                }
            }
            catch (Exception diffEx)
            {
                _Logging.Debug(_Header + "could not capture diff for mission " + mission.Id + ": " + diffEx.Message);
            }

            // Capture the HEAD commit hash before any merge/reclaim
            try
            {
                string? commitHash = await _Git.GetHeadCommitHashAsync(dock.WorktreePath).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(commitHash))
                {
                    mission.CommitHash = commitHash;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "captured commit hash " + commitHash + " for mission " + mission.Id);
                }
            }
            catch (Exception commitEx)
            {
                _Logging.Debug(_Header + "could not capture commit hash for mission " + mission.Id + ": " + commitEx.Message);
            }
        }

        private async Task HandleMissionCompleteAsync(Mission mission, Dock dock)
        {
            if (String.IsNullOrEmpty(dock.WorktreePath) || String.IsNullOrEmpty(dock.BranchName))
                return;

            _Logging.Info(_Header + "handling landing for mission " + mission.Id);

            // Look up the vessel and voyage for settings resolution
            Vessel? vessel = null;
            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                vessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
            }

            Voyage? voyage = null;
            if (!String.IsNullOrEmpty(mission.VoyageId))
            {
                voyage = await _Database.Voyages.ReadAsync(mission.VoyageId).ConfigureAwait(false);
            }

            // Resolve landing mode: voyage > vessel > global > derive from legacy booleans
            LandingModeEnum? resolvedLandingMode = voyage?.LandingMode ?? vessel?.LandingMode ?? _Settings.LandingMode;

            // Resolve effective settings from landing mode or legacy booleans
            bool effectivePush;
            bool effectivePr;
            bool effectiveMerge;

            if (resolvedLandingMode.HasValue)
            {
                // Explicit landing mode takes precedence over boolean flags
                effectivePr = resolvedLandingMode.Value == LandingModeEnum.PullRequest;
                effectivePush = effectivePr || resolvedLandingMode.Value == LandingModeEnum.LocalMerge;
                effectiveMerge = effectivePr && (voyage?.AutoMergePullRequests ?? _Settings.AutoMergePullRequests);
            }
            else
            {
                // Legacy boolean resolution: per-voyage override > global setting
                effectivePush = voyage?.AutoPush ?? _Settings.AutoPush;
                effectivePr = voyage?.AutoCreatePullRequests ?? _Settings.AutoCreatePullRequests;
                effectiveMerge = voyage?.AutoMergePullRequests ?? _Settings.AutoMergePullRequests;
            }

            bool landingModeIsNone = resolvedLandingMode == LandingModeEnum.None;
            bool landingModeIsMergeQueue = resolvedLandingMode == LandingModeEnum.MergeQueue;

            // Resolve branch cleanup policy: vessel > global setting
            BranchCleanupPolicyEnum cleanupPolicy = vessel?.BranchCleanupPolicy ?? _Settings.BranchCleanupPolicy;

            // Acquire per-vessel merge lock to prevent concurrent git operations on the same repo
            string vesselLockKey = mission.VesselId ?? dock.VesselId ?? "unknown";
            SemaphoreSlim vesselLock = _VesselMergeLocks.GetOrAdd(vesselLockKey, _ => new SemaphoreSlim(1, 1));

            _Logging.Info(_Header + "acquiring merge lock for vessel " + vesselLockKey + " (mission " + mission.Id + ")");
            await vesselLock.WaitAsync().ConfigureAwait(false);

            bool landingSucceeded = false;
            bool landingAttempted = false;

            try
            {
                _Logging.Info(_Header + "merge lock acquired for vessel " + vesselLockKey + " (mission " + mission.Id + ")");

                if (effectivePr)
                {
                    // Push + PR flow
                    landingAttempted = true;
                    try
                    {
                        await _Git.PushBranchAsync(dock.WorktreePath).ConfigureAwait(false);
                        _Logging.Info(_Header + "pushed branch " + dock.BranchName);

                        string prBody = "## Mission\n" +
                            "**" + mission.Title + "**\n\n" +
                            (mission.Description ?? "");

                        // Append PR metadata template
                        Dictionary<string, string> prContext = _TemplateService.BuildContext(mission, null, vessel, voyage, dock);
                        prBody = _TemplateService.RenderPrDescription(_Settings.MessageTemplates, prBody, prContext);

                        string prUrl = await _Git.CreatePullRequestAsync(
                            dock.WorktreePath,
                            mission.Title,
                            prBody).ConfigureAwait(false);

                        mission.PrUrl = prUrl;
                        await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                        _Logging.Info(_Header + "created PR: " + prUrl);

                        // PR created — transition to PullRequestOpen (not Complete).
                        // Complete is reserved for when the PR is actually merged.
                        mission.Status = MissionStatusEnum.PullRequestOpen;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                        _Logging.Info(_Header + "mission " + mission.Id + " PR created, status set to PullRequestOpen");

                        // Emit mission.pull_request_open event
                        try
                        {
                            ArmadaEvent prEvent = new ArmadaEvent("mission.pull_request_open", "Pull request opened: " + mission.Title);
                            prEvent.EntityType = "mission";
                            prEvent.EntityId = mission.Id;
                            prEvent.CaptainId = mission.CaptainId;
                            prEvent.MissionId = mission.Id;
                            prEvent.VesselId = mission.VesselId;
                            prEvent.VoyageId = mission.VoyageId;
                            await _Database.Events.CreateAsync(prEvent).ConfigureAwait(false);
                        }
                        catch (Exception evtEx)
                        {
                            _Logging.Warn(_Header + "error emitting mission.pull_request_open event for " + mission.Id + ": " + evtEx.Message);
                        }

                        // Broadcast PullRequestOpen via WebSocket
                        if (_WebSocketHub != null)
                        {
                            _WebSocketHub.BroadcastEvent("mission.pull_request_open", "Pull request opened: " + mission.Title, new
                            {
                                entityType = "mission",
                                entityId = mission.Id,
                                captainId = mission.CaptainId,
                                missionId = mission.Id,
                                vesselId = mission.VesselId,
                                voyageId = mission.VoyageId
                            });
                            _WebSocketHub.BroadcastMissionChange(mission.Id, MissionStatusEnum.PullRequestOpen.ToString(), mission.Title);
                        }

                        // PR path handles its own status — skip the generic landing result block below
                        landingSucceeded = false;
                        landingAttempted = false;

                        // Auto-merge if enabled
                        if (effectiveMerge && !String.IsNullOrEmpty(prUrl))
                        {
                            try
                            {
                                await _Git.EnableAutoMergeAsync(dock.WorktreePath, prUrl).ConfigureAwait(false);
                                _Logging.Info(_Header + "enabled auto-merge for PR: " + prUrl);

                                // Poll for merge completion, then transition to Complete
                                if (vessel != null && !String.IsNullOrEmpty(vessel.WorkingDirectory) && !String.IsNullOrEmpty(vessel.LocalPath) && !String.IsNullOrEmpty(dock.BranchName))
                                {
                                    _ = PollAndPullAfterMergeAsync(vessel.WorkingDirectory, vessel.LocalPath, dock.BranchName, prUrl, mission.Id, cleanupPolicy);
                                }
                            }
                            catch (Exception mergeEx)
                            {
                                _Logging.Warn(_Header + "failed to enable auto-merge for " + prUrl + ": " + mergeEx.Message);
                                // PR was created, so mission stays PullRequestOpen even if auto-merge enablement fails
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error pushing/creating PR for mission " + mission.Id + ": " + ex.Message);
                        landingSucceeded = false;
                    }
                }
                else if (vessel != null && !String.IsNullOrEmpty(vessel.WorkingDirectory) && !String.IsNullOrEmpty(vessel.LocalPath))
                {
                    // Local merge flow: fetch captain's branch from bare repo and merge into user's working directory
                    landingAttempted = true;
                    try
                    {
                        // Render merge commit message from template
                        string? mergeMessage = null;
                        Dictionary<string, string> mergeContext = _TemplateService.BuildContext(mission, null, vessel, voyage, dock);
                        mergeMessage = _TemplateService.RenderMergeCommitMessage(_Settings.MessageTemplates, mergeContext);

                        await _Git.MergeBranchLocalAsync(vessel.WorkingDirectory, vessel.LocalPath, dock.BranchName, vessel.DefaultBranch, mergeMessage).ConfigureAwait(false);
                        _Logging.Info(_Header + "merged branch " + dock.BranchName + " into " + vessel.WorkingDirectory);

                        landingSucceeded = true;

                        // Push the merged changes to the remote BEFORE branch cleanup,
                        // so the branch is preserved for retry if push fails.
                        if (effectivePush)
                        {
                            try
                            {
                                await _Git.PushBranchAsync(vessel.WorkingDirectory).ConfigureAwait(false);
                                _Logging.Info(_Header + "pushed merged changes from " + vessel.WorkingDirectory);
                            }
                            catch (Exception pushEx)
                            {
                                _Logging.Warn(_Header + "local merge succeeded but push failed for mission " + mission.Id + ": " + pushEx.Message);
                                landingSucceeded = false;
                            }
                        }

                        // Only clean up the mission branch after confirmed success (merge + push).
                        // Preserve branch on failure for retry.
                        if (landingSucceeded && cleanupPolicy != BranchCleanupPolicyEnum.None)
                        {
                            try
                            {
                                await _Git.DeleteLocalBranchAsync(vessel.LocalPath, dock.BranchName).ConfigureAwait(false);
                                _Logging.Info(_Header + "deleted branch " + dock.BranchName + " from bare repo after successful landing");
                            }
                            catch (Exception branchEx)
                            {
                                _Logging.Warn(_Header + "failed to delete branch " + dock.BranchName + " from bare repo: " + branchEx.Message);
                            }

                            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
                            {
                                try
                                {
                                    await _Git.DeleteRemoteBranchAsync(vessel.WorkingDirectory, dock.BranchName).ConfigureAwait(false);
                                    _Logging.Info(_Header + "deleted remote branch " + dock.BranchName + " after successful landing");
                                }
                                catch (Exception remoteBranchEx)
                                {
                                    _Logging.Warn(_Header + "failed to delete remote branch " + dock.BranchName + ": " + remoteBranchEx.Message);
                                }
                            }
                        }
                        else if (!landingSucceeded)
                        {
                            _Logging.Info(_Header + "preserving branch " + dock.BranchName + " for retry (landing failed)");
                        }
                        else
                        {
                            _Logging.Info(_Header + "branch cleanup policy is None — retaining branch " + dock.BranchName + " for inspection");
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error merging locally for mission " + mission.Id + ": " + ex.Message + " — branch " + dock.BranchName + " is still available in the bare repo");
                        landingSucceeded = false;
                    }
                }
                else if (landingModeIsMergeQueue)
                {
                    // MergeQueue mode: auto-enqueue the branch into the merge queue.
                    // Processing (test-and-land) remains a separate trigger via armada_process_merge_queue.
                    try
                    {
                        string targetBranch = vessel?.DefaultBranch ?? "main";
                        MergeEntry entry = new MergeEntry(dock.BranchName, targetBranch);
                        entry.MissionId = mission.Id;
                        entry.VesselId = mission.VesselId;
                        entry = await _MergeQueue.EnqueueAsync(entry).ConfigureAwait(false);
                        _Logging.Info(_Header + "mission " + mission.Id + " auto-enqueued as merge entry " + entry.Id + " (branch " + dock.BranchName + " -> " + targetBranch + ")");

                        // Emit merge_queue.enqueued event
                        try
                        {
                            ArmadaEvent mqEvent = new ArmadaEvent("merge_queue.enqueued", "Mission " + mission.Id + " auto-enqueued for merge queue: " + dock.BranchName + " -> " + targetBranch);
                            mqEvent.EntityType = "merge_entry";
                            mqEvent.EntityId = entry.Id;
                            mqEvent.MissionId = mission.Id;
                            mqEvent.VesselId = mission.VesselId;
                            mqEvent.VoyageId = mission.VoyageId;
                            mqEvent.CaptainId = mission.CaptainId;
                            await _Database.Events.CreateAsync(mqEvent).ConfigureAwait(false);
                        }
                        catch (Exception evtEx)
                        {
                            _Logging.Warn(_Header + "error emitting merge_queue.enqueued event for " + mission.Id + ": " + evtEx.Message);
                        }
                    }
                    catch (Exception mqEx)
                    {
                        _Logging.Warn(_Header + "error auto-enqueuing merge entry for mission " + mission.Id + ": " + mqEx.Message + " — branch " + dock.BranchName + " is still available for manual enqueue");
                    }
                    // Mission stays as WorkProduced; merge queue processing will land it
                }
                else
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " work produced — branch " + dock.BranchName + " available in bare repo (landing mode: " + (resolvedLandingMode?.ToString() ?? "not configured") + ")");
                    // No landing configured or LandingMode.None — mission stays as WorkProduced
                }
            }
            finally
            {
                vesselLock.Release();
                _Logging.Info(_Header + "merge lock released for vessel " + vesselLockKey + " (mission " + mission.Id + ")");
            }

            // Transition mission status based on landing result
            if (landingAttempted)
            {
                if (landingSucceeded)
                {
                    mission.Status = MissionStatusEnum.Complete;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "mission " + mission.Id + " landed successfully, status set to Complete");

                    // Emit mission.completed event
                    try
                    {
                        ArmadaEvent completedEvent = new ArmadaEvent("mission.completed", "Mission completed: " + mission.Title);
                        completedEvent.EntityType = "mission";
                        completedEvent.EntityId = mission.Id;
                        completedEvent.CaptainId = mission.CaptainId;
                        completedEvent.MissionId = mission.Id;
                        completedEvent.VesselId = mission.VesselId;
                        completedEvent.VoyageId = mission.VoyageId;
                        await _Database.Events.CreateAsync(completedEvent).ConfigureAwait(false);
                    }
                    catch (Exception evtEx)
                    {
                        _Logging.Warn(_Header + "error emitting mission.completed event for " + mission.Id + ": " + evtEx.Message);
                    }
                }
                else
                {
                    mission.Status = MissionStatusEnum.LandingFailed;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Warn(_Header + "mission " + mission.Id + " landing failed, status set to LandingFailed");

                    // Emit mission.landing_failed event
                    try
                    {
                        ArmadaEvent failedEvent = new ArmadaEvent("mission.landing_failed", "Landing failed: " + mission.Title);
                        failedEvent.EntityType = "mission";
                        failedEvent.EntityId = mission.Id;
                        failedEvent.CaptainId = mission.CaptainId;
                        failedEvent.MissionId = mission.Id;
                        failedEvent.VesselId = mission.VesselId;
                        failedEvent.VoyageId = mission.VoyageId;
                        await _Database.Events.CreateAsync(failedEvent).ConfigureAwait(false);
                    }
                    catch (Exception evtEx)
                    {
                        _Logging.Warn(_Header + "error emitting mission.landing_failed event for " + mission.Id + ": " + evtEx.Message);
                    }
                }
            }

            // Broadcast via WebSocket for real-time UI updates.
            // Derive event type from mission.Status (not from boolean flags) so the broadcast
            // accurately reflects the current state. This prevents the PR path from emitting
            // a misleading "mission.work_produced" broadcast when the mission is already PullRequestOpen.
            if (_WebSocketHub != null)
            {
                string eventType;
                string eventMessage;

                switch (mission.Status)
                {
                    case MissionStatusEnum.Complete:
                        eventType = "mission.completed";
                        eventMessage = "Mission completed: " + mission.Title;
                        break;
                    case MissionStatusEnum.LandingFailed:
                        eventType = "mission.landing_failed";
                        eventMessage = "Landing failed: " + mission.Title;
                        break;
                    case MissionStatusEnum.PullRequestOpen:
                        eventType = "mission.pull_request_open";
                        eventMessage = "Pull request open: " + mission.Title;
                        break;
                    default:
                        eventType = "mission.work_produced";
                        eventMessage = "Work produced: " + mission.Title;
                        break;
                }

                _WebSocketHub.BroadcastEvent(eventType, eventMessage, new
                {
                    entityType = "mission",
                    entityId = mission.Id,
                    captainId = mission.CaptainId,
                    missionId = mission.Id,
                    vesselId = mission.VesselId,
                    voyageId = mission.VoyageId
                });

                // Broadcast specific mission change for dashboard toast notifications
                _WebSocketHub.BroadcastMissionChange(mission.Id, mission.Status.ToString(), mission.Title);
            }

            // Reclaim dock through DockService (single owner for worktree teardown)
            try
            {
                await _Docks.ReclaimAsync(dock.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error reclaiming dock " + dock.Id + ": " + ex.Message);
            }
        }

        private Task HandleVoyageCompleteAsync(Voyage voyage)
        {
            _Logging.Info(_Header + "voyage " + voyage.Id + " completed, broadcasting to dashboard");

            if (_WebSocketHub != null)
            {
                _WebSocketHub.BroadcastVoyageChange(voyage.Id, VoyageStatusEnum.Complete.ToString(), voyage.Title);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Persistent PR reconciler: checks if a PullRequestOpen mission's PR has been merged.
        /// Called from the health-check loop via delegate. Returns true if the mission was reconciled.
        /// </summary>
        private async Task<bool> HandleReconcilePullRequestAsync(Mission mission)
        {
            if (mission == null || String.IsNullOrEmpty(mission.PrUrl)) return false;
            if (mission.Status != MissionStatusEnum.PullRequestOpen) return false;

            // Find the vessel to get working directory context for gh CLI
            Vessel? vessel = null;
            if (!String.IsNullOrEmpty(mission.VesselId))
                vessel = await _Database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);

            string? workingDir = vessel?.WorkingDirectory ?? vessel?.LocalPath;
            if (String.IsNullOrEmpty(workingDir)) return false;

            try
            {
                bool merged = await _Git.IsPrMergedAsync(workingDir, mission.PrUrl).ConfigureAwait(false);
                if (merged)
                {
                    mission.Status = MissionStatusEnum.Complete;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    _Logging.Info(_Header + "PR reconciler: mission " + mission.Id + " PR merged, status set to Complete");

                    // Pull latest into working directory if available
                    if (!String.IsNullOrEmpty(vessel?.WorkingDirectory))
                    {
                        try
                        {
                            await _Git.PullAsync(vessel.WorkingDirectory).ConfigureAwait(false);
                        }
                        catch (Exception pullEx)
                        {
                            _Logging.Warn(_Header + "PR reconciler: pull failed for " + vessel.WorkingDirectory + ": " + pullEx.Message);
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "PR reconciler: error checking PR status for mission " + mission.Id + ": " + ex.Message);
            }

            return false;
        }

        private async Task PollAndPullAfterMergeAsync(string workingDirectory, string bareRepoPath, string branchName, string prUrl, string missionId, BranchCleanupPolicyEnum cleanupPolicy = BranchCleanupPolicyEnum.LocalOnly)
        {
            try
            {
                // Poll for up to 5 minutes (30 attempts, 10 seconds apart)
                // Uses the vessel working directory (not the mission dock) for gh CLI context,
                // since the dock worktree may be reclaimed before the PR merges.
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                    bool merged = await _Git.IsPrMergedAsync(workingDirectory, prUrl).ConfigureAwait(false);
                    if (merged)
                    {
                        _Logging.Info(_Header + "PR " + prUrl + " merged, pulling into " + workingDirectory);
                        await _Git.PullAsync(workingDirectory).ConfigureAwait(false);
                        _Logging.Info(_Header + "pulled latest into " + workingDirectory + " after PR merge");

                        // Transition mission from PullRequestOpen to Complete
                        try
                        {
                            Mission? mission = await _Database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                            if (mission != null && mission.Status == MissionStatusEnum.PullRequestOpen)
                            {
                                mission.Status = MissionStatusEnum.Complete;
                                mission.CompletedUtc = DateTime.UtcNow;
                                mission.LastUpdateUtc = DateTime.UtcNow;
                                await _Database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                                _Logging.Info(_Header + "mission " + missionId + " PR merged, status set to Complete");

                                // Emit mission.completed event
                                try
                                {
                                    ArmadaEvent completedEvent = new ArmadaEvent("mission.completed", "Mission completed (PR merged): " + mission.Title);
                                    completedEvent.EntityType = "mission";
                                    completedEvent.EntityId = mission.Id;
                                    completedEvent.CaptainId = mission.CaptainId;
                                    completedEvent.MissionId = mission.Id;
                                    completedEvent.VesselId = mission.VesselId;
                                    completedEvent.VoyageId = mission.VoyageId;
                                    await _Database.Events.CreateAsync(completedEvent).ConfigureAwait(false);
                                }
                                catch (Exception evtEx)
                                {
                                    _Logging.Warn(_Header + "error emitting mission.completed event for " + missionId + ": " + evtEx.Message);
                                }

                                // Broadcast via WebSocket
                                if (_WebSocketHub != null)
                                {
                                    _WebSocketHub.BroadcastEvent("mission.completed", "Mission completed (PR merged): " + mission.Title, new
                                    {
                                        entityType = "mission",
                                        entityId = mission.Id,
                                        captainId = mission.CaptainId,
                                        missionId = mission.Id,
                                        vesselId = mission.VesselId,
                                        voyageId = mission.VoyageId
                                    });
                                    _WebSocketHub.BroadcastMissionChange(mission.Id, MissionStatusEnum.Complete.ToString(), mission.Title);
                                }
                            }
                        }
                        catch (Exception statusEx)
                        {
                            _Logging.Warn(_Header + "error transitioning mission " + missionId + " to Complete after PR merge: " + statusEx.Message);
                        }

                        // Clean up the mission branch based on cleanup policy
                        if (cleanupPolicy != BranchCleanupPolicyEnum.None)
                        {
                            try
                            {
                                await _Git.DeleteLocalBranchAsync(bareRepoPath, branchName).ConfigureAwait(false);
                                _Logging.Info(_Header + "deleted branch " + branchName + " from bare repo after PR merge");
                            }
                            catch (Exception branchEx)
                            {
                                _Logging.Warn(_Header + "failed to delete branch " + branchName + " from bare repo: " + branchEx.Message);
                            }

                            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
                            {
                                try
                                {
                                    await _Git.DeleteRemoteBranchAsync(workingDirectory, branchName).ConfigureAwait(false);
                                    _Logging.Info(_Header + "deleted remote branch " + branchName + " after PR merge");
                                }
                                catch (Exception remoteBranchEx)
                                {
                                    _Logging.Warn(_Header + "failed to delete remote branch " + branchName + ": " + remoteBranchEx.Message);
                                }
                            }
                        }
                        else
                        {
                            _Logging.Info(_Header + "branch cleanup policy is None — retaining branch " + branchName + " for inspection");
                        }

                        return;
                    }
                }

                _Logging.Info(_Header + "PR " + prUrl + " not merged within 5 minutes — mission " + missionId + " stays PullRequestOpen until merge is confirmed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error polling/pulling after merge for mission " + missionId + ": " + ex.Message);
            }
        }

        private async Task EmitEventAsync(string eventType, string message,
            string? entityType = null, string? entityId = null,
            string? captainId = null, string? missionId = null,
            string? vesselId = null, string? voyageId = null)
        {
            try
            {
                ArmadaEvent evt = new ArmadaEvent(eventType, message);
                evt.EntityType = entityType;
                evt.EntityId = entityId;
                evt.CaptainId = captainId;
                evt.MissionId = missionId;
                evt.VesselId = vesselId;
                evt.VoyageId = voyageId;
                await _Database.Events.CreateAsync(evt).ConfigureAwait(false);

                // Broadcast to WebSocket clients
                if (_WebSocketHub != null)
                {
                    _WebSocketHub.BroadcastEvent(eventType, message, new
                    {
                        entityType = entityType,
                        entityId = entityId,
                        captainId = captainId,
                        missionId = missionId,
                        vesselId = vesselId,
                        voyageId = voyageId
                    });
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error emitting event: " + ex.Message);
            }
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

        private async Task HealthCheckLoopAsync(CancellationToken token)
        {
            // Reset captains left in Working state with dead processes from previous server run
            try
            {
                await _Admiral.CleanupStaleCaptainsAsync(token).ConfigureAwait(false);
                _Logging.Info(_Header + "startup stale captain cleanup completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "startup stale captain cleanup error: " + ex.Message);
            }

            // Run an immediate health check on startup to dispatch any pending missions
            try
            {
                await _Admiral.HealthCheckAsync(token).ConfigureAwait(false);
                _Logging.Info(_Header + "startup health check completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "startup health check error: " + ex.Message);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_Settings.HeartbeatIntervalSeconds * 1000, token).ConfigureAwait(false);
                    await _Admiral.HealthCheckAsync(token).ConfigureAwait(false);

                    // Run log rotation every 10 health check cycles
                    _HealthCheckCycles++;
                    if (_HealthCheckCycles % 10 == 0)
                    {
                        string captainLogDir = Path.Combine(_Settings.LogDirectory, "captains");
                        _LogRotation.RotateAllInDirectory(captainLogDir);
                        _LogRotation.RotateIfNeeded(Path.Combine(_Settings.LogDirectory, "admiral.log"));
                    }

                    // Run data expiry every 100 health check cycles (~50 min at default interval)
                    if (_HealthCheckCycles % 100 == 0)
                    {
                        await _DataExpiry.PurgeExpiredDataAsync(token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "health check error: " + ex.Message);
                }
            }
        }

        #endregion
    }
}
