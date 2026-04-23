namespace Armada.Server
{
    using System.IO;
    using System.Text.Json;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Voltaic;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server.Mcp;
    using Armada.Server.Mcp.Tools;
    using Armada.Server.Routes;
    using Armada.Server.WebSocket;

    /// <summary>
    /// Admiral server orchestrating REST API, MCP server, and agent coordination.
    /// Routes, MCP tools, WebSocket commands, agent lifecycle, and mission landing
    /// are each handled by dedicated classes — this class wires them together.
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

        private Webserver _App = null!;
        private McpHttpServer _McpServer = null!;
        private ArmadaWebSocketHub _WebSocketHub = null!;

        private IMergeQueueService _MergeQueue = null!;
        private LandingService _LandingService = null!;
        private IMessageTemplateService _TemplateService = null!;
        private IPromptTemplateService _PromptTemplateService = null!;
        private PersonaSeedService _PersonaSeedService = null!;
        private LogRotationService _LogRotation = null!;
        private DataExpiryService _DataExpiry = null!;
        private RemoteTunnelManager _RemoteTunnel = null!;
        private RemoteControlQueryService _RemoteControlQueries = null!;
        private RemoteControlManagementService _RemoteControlManagement = null!;

        private ISessionTokenService _SessionTokenService = null!;
        private IAuthenticationService _AuthenticationService = null!;
        private IAuthorizationService _AuthorizationService = null!;

        private AgentLifecycleHandler _AgentLifecycle = null!;
        private MissionLandingHandler _MissionLanding = null!;

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private Task _HealthCheckTask = null!;
        private int _HealthCheckCycles = 0;
        private DateTime _StartUtc = DateTime.UtcNow;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

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
            _Database = DatabaseDriverFactory.Create(_Settings.Database, _Logging);
            await _Database.InitializeAsync().ConfigureAwait(false);
            _Logging.Info(_Header + "database initialized");

            // Initialize services
            _Git = new GitService(_Logging);
            IDockService dockService = new DockService(_Logging, _Database, _Settings, _Git);
            _Docks = dockService;
            ICaptainService captainService = new CaptainService(_Logging, _Database, _Settings, _Git, dockService);
            // Prompt template service must be created before MissionService so it can resolve templates
            _PromptTemplateService = new PromptTemplateService(_Database, _Logging);

            IMissionService missionService = new MissionService(_Logging, _Database, _Settings, dockService, captainService, _PromptTemplateService, _Git);
            IVoyageService voyageService = new VoyageService(_Logging, _Database);
            IEscalationService escalationService = new EscalationService(_Logging, _Database, _Settings);
            AdmiralService admiralService = new AdmiralService(_Logging, _Database, _Settings, captainService, missionService, voyageService, dockService, escalationService);
            _Admiral = admiralService;
            _MergeQueue = new MergeQueueService(_Logging, _Database, _Settings, _Git);
            _LandingService = new LandingService(_Logging, _Database, _Settings, _Git);
            _TemplateService = new MessageTemplateService(_Logging, _PromptTemplateService);
            _RuntimeFactory = new AgentRuntimeFactory(_Logging);
            _RemoteTunnel = new RemoteTunnelManager(_Logging, _Settings);
            admiralService.OnGetRemoteTunnelStatus = _RemoteTunnel.GetStatus;
            _RemoteControlQueries = new RemoteControlQueryService(
                _Database,
                _Settings,
                _Git,
                token => _Admiral.GetStatusAsync(token),
                _RemoteTunnel.GetStatus,
                _StartUtc);
            _RemoteControlManagement = new RemoteControlManagementService(
                _Database,
                _Admiral,
                EmitEventAsync);
            _RemoteTunnel.OnHandleRequest = HandleRemoteTunnelRequestAsync;

            // Seed built-in prompt templates, personas, and pipelines
            await _PromptTemplateService.SeedDefaultsAsync().ConfigureAwait(false);
            _Logging.Info(_Header + "prompt template seeding completed");

            _PersonaSeedService = new PersonaSeedService(_Database, _Logging);
            await _PersonaSeedService.SeedAsync().ConfigureAwait(false);
            _Logging.Info(_Header + "persona and pipeline seeding completed");

            // Initialize authentication services
            _SessionTokenService = new SessionTokenService(_Settings.SessionTokenEncryptionKey);
            if (string.IsNullOrEmpty(_Settings.SessionTokenEncryptionKey))
            {
                _Settings.SessionTokenEncryptionKey = ((SessionTokenService)_SessionTokenService).GetKeyBase64();
                _Logging.Info(_Header + "auto-generated session token encryption key");
            }
            _AuthenticationService = new AuthenticationService(_Database, _SessionTokenService, _Settings, _Logging);
            _AuthorizationService = new AuthorizationService();

            // Seed synthetic admin identity if API key is configured
            if (!string.IsNullOrEmpty(_Settings.ApiKey))
            {
                await SeedSyntheticAdminAsync().ConfigureAwait(false);
            }

            // Initialize log rotation and data expiry
            _LogRotation = new LogRotationService(_Logging, _Settings.MaxLogFileSizeBytes, _Settings.MaxLogFileCount);
            _DataExpiry = new DataExpiryService(_Logging, _Settings.Database.GetConnectionString(), _Settings.DataRetentionDays);

            // Initialize handler classes (WebSocketHub is created later, so pass null initially)
            _MissionLanding = new MissionLandingHandler(
                _Logging, _Database, _Settings, _Git, _MergeQueue, _TemplateService, _PromptTemplateService, _Docks, null);

            _AgentLifecycle = new AgentLifecycleHandler(
                _Logging, _Database, _Settings, _RuntimeFactory, _Admiral, _TemplateService, _PromptTemplateService, null, EmitEventAsync);

            // Wire up agent lifecycle events
            _Admiral.OnLaunchAgent = _AgentLifecycle.HandleLaunchAgentAsync;
            _Admiral.OnStopAgent = _AgentLifecycle.HandleStopAgentAsync;
            _Admiral.OnCaptureDiff = _MissionLanding.HandleCaptureDiffAsync;
            _Admiral.OnIsProcessExitHandled = _AgentLifecycle.IsProcessExitHandled;
            missionService.OnGetMissionOutput = _AgentLifecycle.GetAndClearMissionOutput;
            _Admiral.OnMissionComplete = _MissionLanding.HandleMissionCompleteAsync;
            _Admiral.OnVoyageComplete = _MissionLanding.HandleVoyageCompleteAsync;
            _Admiral.OnReconcilePullRequest = _MissionLanding.HandleReconcilePullRequestAsync;
            _LandingService.OnPerformLanding = _MissionLanding.HandleMissionCompleteAsync;

            // Initialize REST API (Watson7)
            WebserverSettings wsSettings = new WebserverSettings();
            wsSettings.Hostname = _Settings.Rest.Hostname;
            wsSettings.Port = _Settings.AdmiralPort;
            wsSettings.Ssl.Enable = _Settings.Rest.Ssl;
            wsSettings.WebSockets.Enable = _Settings.WebSocketEnabled;

            _App = new Webserver(wsSettings, DashboardDefaultRouteAsync);
            _App.Events.Logger = (string message) => _Logging.Debug(_Header + message);

            _App.UseOpenApi(openApi =>
            {
                openApi.Info.Title = ArmadaConstants.ProductName + " API";
                openApi.Info.Version = ArmadaConstants.ProductVersion;
                openApi.Info.Description = "Multi-agent orchestration API for scaling human developers with AI captains across git worktrees.";

                // Tags for route grouping
                openApi.Tags.Add(new OpenApiTag { Name = "Status", Description = "Health check and system status" });
                openApi.Tags.Add(new OpenApiTag { Name = "Fleets", Description = "Fleet (repository collection) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Vessels", Description = "Vessel (git repository) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Voyages", Description = "Voyage (mission batch) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Missions", Description = "Mission (atomic work unit) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Playbooks", Description = "Markdown playbook management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Captains", Description = "Captain (AI agent) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Signals", Description = "Signal (inter-agent messaging) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Events", Description = "System event log" });
                openApi.Tags.Add(new OpenApiTag { Name = "MergeQueue", Description = "Bors-style merge queue with batch testing" });
                openApi.Tags.Add(new OpenApiTag { Name = "Authentication", Description = "Authentication and identity" });
                openApi.Tags.Add(new OpenApiTag { Name = "Tenants", Description = "Multi-tenant management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Users", Description = "User management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Credentials", Description = "Credential (API token) management" });

                // API key security scheme
                openApi.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
                {
                    Type = "apiKey",
                    Name = "X-Api-Key",
                    In = "header",
                    Description = "API key for authenticating requests. Configure via ArmadaSettings.ApiKey."
                };
            });

            // Set timestamp on request start
            _App.Routes.PreRouting = async (HttpContextBase ctx) =>
            {
                ctx.Timestamp.Start = DateTime.UtcNow;
                ctx.Response.ContentType = "application/json";
                await Task.CompletedTask.ConfigureAwait(false);
            };

            // Log every API call and apply CORS on every response
            _App.Routes.PostRouting = async (HttpContextBase ctx) =>
            {
                ctx.Timestamp.End = DateTime.UtcNow;
                _Logging.Debug(
                    _Header +
                    ctx.Request.Method + " " +
                    ctx.Request.Url.RawWithQuery + " " +
                    ctx.Response.StatusCode + " " +
                    "(" + (ctx.Timestamp.TotalMs.HasValue ? ctx.Timestamp.TotalMs.Value.ToString("F2") : "?") + "ms)");

                ApplyCorsHeaders(ctx);
                await Task.CompletedTask.ConfigureAwait(false);
            };

            // CORS preflight handler. Browsers send an OPTIONS before cross-origin requests;
            // we must answer 204 with the allow headers before the real call can proceed.
            _App.Routes.Preflight = async (HttpContextBase ctx) =>
            {
                ApplyCorsHeaders(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send().ConfigureAwait(false);
            };

            // Initialize WebSocket hub (before routes so it's available for injection)
            _WebSocketHub = new ArmadaWebSocketHub(_Logging, _Admiral, _Database, _MergeQueue, _Settings, _Git, () => { OnStopping?.Invoke(); _TokenSource.Cancel(); });
            _AgentLifecycle.SetWebSocketHub(_WebSocketHub);
            _MissionLanding.SetWebSocketHub(_WebSocketHub);

            RegisterRoutes();
            InitializeDashboard();

            // Register WebSocket route on the main REST server
            _App.WebSocket("/ws", _WebSocketHub.HandleWebSocketAsync);
            _Logging.Info(_Header + "WebSocket route registered at /ws");

            // Watson 7 StartAsync is long-running; Start() binds and returns after
            // scheduling the accept loop.
            _App.Start(_TokenSource.Token);
            _Logging.Info(_Header + "REST API started on port " + _Settings.AdmiralPort);

            // Initialize MCP server
            _McpServer = new McpHttpServer(_Settings.Rest.Hostname, _Settings.McpPort);
            _McpServer.ServerName = ArmadaConstants.ProductName;
            _McpServer.ServerVersion = ArmadaConstants.ProductVersion;
            RegisterMcpTools();

            Task mcpTask = Task.Run(() => _McpServer.StartAsync(_TokenSource.Token));
            _Logging.Info(_Header + "MCP server started on port " + _Settings.McpPort);

            _RemoteTunnel.Start(_TokenSource.Token);
            _Logging.Info(_Header + "remote tunnel manager started");

            // Start health check loop
            _HealthCheckTask = HealthCheckLoopAsync(_TokenSource.Token);
        }

        /// <summary>
        /// Stop the Admiral server.
        /// </summary>
        public void Stop()
        {
            _Logging.Info(_Header + "stopping");
            try
            {
                if (_App?.IsListening == true)
                    _App.Stop();
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "REST API stop error: " + ex.Message);
            }
            _TokenSource.Cancel();
            _RemoteTunnel?.StopAsync().GetAwaiter().GetResult();
            _McpServer?.Stop();
            _Database?.Dispose();
            OnStopping?.Invoke();
        }

        #endregion

        #region Private-Methods

        private async Task<AuthContext> AuthenticateRequestAsync(WatsonWebserver.Core.HttpContextBase ctx)
        {
            string? authHeader = ctx.Request.Headers.Get("Authorization");
            string? tokenHeader = ctx.Request.Headers.Get("X-Token");
            string? apiKeyHeader = ctx.Request.Headers.Get("X-Api-Key");
            return await _AuthenticationService.AuthenticateAsync(authHeader, tokenHeader, apiKeyHeader).ConfigureAwait(false);
        }

        private async Task SeedSyntheticAdminAsync()
        {
            _Logging.Info(_Header + "seeding synthetic admin identity for API key");

            // Create system tenant if not exists
            var existingTenant = await _Database.Tenants.ReadAsync(ArmadaConstants.SystemTenantId).ConfigureAwait(false);
            if (existingTenant == null)
            {
                var systemTenant = new TenantMetadata();
                systemTenant.Id = ArmadaConstants.SystemTenantId;
                systemTenant.Name = ArmadaConstants.SystemTenantName;
                systemTenant.IsProtected = true;
                await _Database.Tenants.CreateAsync(systemTenant).ConfigureAwait(false);
            }

            // Create system user if not exists
            var existingUser = await _Database.Users.ReadByIdAsync(ArmadaConstants.SystemUserId).ConfigureAwait(false);
            if (existingUser == null)
            {
                var systemUser = new UserMaster();
                systemUser.Id = ArmadaConstants.SystemUserId;
                systemUser.TenantId = ArmadaConstants.SystemTenantId;
                systemUser.Email = ArmadaConstants.SystemUserEmail;
                systemUser.PasswordSha256 = UserMaster.ComputePasswordHash("system");
                systemUser.IsAdmin = true;
                systemUser.IsTenantAdmin = true;
                systemUser.IsProtected = true;
                await _Database.Users.CreateAsync(systemUser).ConfigureAwait(false);
            }

            _Logging.Info(_Header + "synthetic admin identity ready");
        }

        private void RegisterRoutes()
        {
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate = AuthenticateRequestAsync;

            // Authentication & identity
            new AuthRoutes(_SessionTokenService, _AuthenticationService, _Database, _Settings, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Tenants, users, credentials
            new TenantRoutes(_Database, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Status, health, doctor, settings, server control
            new StatusRoutes(_Database, _Settings, _Admiral, () => Stop(), _StartUtc, _JsonOptions, _Logging, _RemoteTunnel.GetStatus, _RemoteTunnel.ReloadAsync)
                .Register(_App, authenticate, _AuthorizationService);

            // Fleets
            new FleetRoutes(_Database, EmitEventAsync, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Vessels
            new VesselRoutes(_Database, EmitEventAsync, _JsonOptions, _Docks)
                .Register(_App, authenticate, _AuthorizationService);

            // Voyages
            new VoyageRoutes(_Database, _Admiral, EmitEventAsync, _WebSocketHub, _Logging, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Missions
            new MissionRoutes(_Database, _Admiral, _Settings, _Git, _LandingService, EmitEventAsync, _MissionLanding.HandleMissionCompleteAsync, _WebSocketHub, _Logging, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Captains
            new CaptainRoutes(_Database, _Admiral, _Settings, _RuntimeFactory, _AgentLifecycle, EmitEventAsync, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Docks
            new DockRoutes(_Database, _Docks, EmitEventAsync, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Signals
            new SignalRoutes(_Database, EmitEventAsync, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Events
            new EventRoutes(_Database, EmitEventAsync, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Merge queue
            new MergeQueueRoutes(_Database, _MergeQueue, EmitEventAsync, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Prompt templates
            new PromptTemplateRoutes(_Database, _PromptTemplateService, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Playbooks
            new PlaybookRoutes(_Database, _Logging, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Personas
            new PersonaRoutes(_Database, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Pipelines
            new PipelineRoutes(_Database, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Backup & restore
            new BackupRoutes(_Database, _Settings, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);
        }

        private void InitializeDashboard()
        {
            // Check for explicit DashboardPath setting
            if (!String.IsNullOrEmpty(_Settings.DashboardPath))
            {
                string path = _Settings.DashboardPath;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(_Settings.DataDirectory, path);

                if (Directory.Exists(path))
                {
                    Dashboard.StaticFileHandler.SetExternalPath(path);
                    _Logging.Info(_Header + "dashboard serving from external path: " + path);
                    return;
                }
                else
                {
                    _Logging.Warn(_Header + "configured DashboardPath not found: " + path + ", trying auto-detection");
                }
            }

            // Auto-detect: check for a 'dashboard' directory in the data directory
            string dashboardInData = Path.Combine(_Settings.DataDirectory, "dashboard");
            if (Directory.Exists(dashboardInData) && File.Exists(Path.Combine(dashboardInData, "index.html")))
            {
                Dashboard.StaticFileHandler.SetExternalPath(dashboardInData);
                _Logging.Info(_Header + "dashboard auto-detected at: " + dashboardInData);
                return;
            }

            // Auto-detect: check next to the server executable
            string? exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (exeDir != null)
            {
                string dashboardNextToExe = Path.Combine(exeDir, "dashboard");
                if (Directory.Exists(dashboardNextToExe) && File.Exists(Path.Combine(dashboardNextToExe, "index.html")))
                {
                    Dashboard.StaticFileHandler.SetExternalPath(dashboardNextToExe);
                    _Logging.Info(_Header + "dashboard auto-detected at: " + dashboardNextToExe);
                    return;
                }
            }

            // Fallback: use embedded wwwroot resources (legacy dashboard)
            _Logging.Info(_Header + "using embedded legacy dashboard (no external dashboard found)");
        }

        private static void ApplyCorsHeaders(HttpContextBase ctx)
        {
            if (!ctx.Response.Headers.AllKeys.Contains("Access-Control-Allow-Origin"))
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            if (!ctx.Response.Headers.AllKeys.Contains("Access-Control-Allow-Methods"))
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, PATCH, OPTIONS");
            if (!ctx.Response.Headers.AllKeys.Contains("Access-Control-Allow-Headers"))
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Api-Key, X-Token, Authorization");
        }

        /// <summary>
        /// Default route used when no other route matches. Serves the static dashboard
        /// assets, the SPA index fallback, and a JSON 404 for anything else.
        /// </summary>
        private async Task DashboardDefaultRouteAsync(HttpContextBase ctx)
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
                // (React router handles client-side routing)
                if (Dashboard.StaticFileHandler.TryGetIndex(out byte[] indexContent, out string indexType))
                {
                    ctx.Response.ContentType = indexType;
                    await ctx.Response.Send(indexContent).ConfigureAwait(false);
                    return;
                }
            }

            // Also serve /img/* and /assets/* at root level for the React dashboard
            // (Vite builds reference assets from root, not /dashboard/)
            if (path.StartsWith("/assets/") || path.StartsWith("/img/"))
            {
                string dashPath = "/dashboard" + path;
                if (Dashboard.StaticFileHandler.TryGetFile(dashPath, out byte[] assetContent, out string assetType))
                {
                    ctx.Response.ContentType = assetType;
                    await ctx.Response.Send(assetContent).ConfigureAwait(false);
                    return;
                }
            }

            // 404 for everything else
            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send("{\"error\":\"Not found\"}").ConfigureAwait(false);
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
                        await _AgentLifecycle.HandleStopAgentAsync(captain).ConfigureAwait(false);
                },
                _AgentLifecycle,
                _PromptTemplateService,
                _Logging);
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

                await _RemoteTunnel.PublishEventAsync(eventType, new
                {
                    message = message,
                    entityType = entityType,
                    entityId = entityId,
                    captainId = captainId,
                    missionId = missionId,
                    vesselId = vesselId,
                    voyageId = voyageId
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error emitting event: " + ex.Message);
            }
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

        private async Task<RemoteTunnelRequestResult> HandleRemoteTunnelRequestAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            string method = envelope.Method?.Trim().ToLowerInvariant() ?? String.Empty;
            switch (method)
            {
                case "armada.fleets.list":
                case "armada.fleet.detail":
                case "armada.fleet.create":
                case "armada.fleet.update":
                case "armada.vessels.list":
                case "armada.vessel.detail":
                case "armada.vessel.create":
                case "armada.vessel.update":
                case "armada.pipelines.list":
                case "armada.playbooks.list":
                case "armada.playbook.detail":
                case "armada.playbook.create":
                case "armada.playbook.update":
                case "armada.playbook.delete":
                case "armada.voyages.list":
                case "armada.voyage.dispatch":
                case "armada.voyage.cancel":
                case "armada.missions.list":
                case "armada.mission.create":
                case "armada.mission.update":
                case "armada.mission.cancel":
                case "armada.mission.restart":
                case "armada.captain.stop":
                    return await _RemoteControlManagement.HandleAsync(envelope, token).ConfigureAwait(false);
            }

            return await _RemoteControlQueries.HandleAsync(envelope, token).ConfigureAwait(false);
        }

        #endregion
    }
}
