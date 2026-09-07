namespace Armada.Server
{
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Voltaic;
    using Voltaic.Core;
    using Voltaic.Mcp;
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
        private ArmadaTelemetryHost? _TelemetryHost;

        private DatabaseDriver _Database = null!;
        private IGitService _Git = null!;
        private IDockService _Docks = null!;
        private IAdmiralService _Admiral = null!;
        private AgentRuntimeFactory _RuntimeFactory = null!;

        private Webserver _App = null!;
        private McpHttpServer _McpServer = null!;
        private ArmadaWebSocketHub _WebSocketHub = null!;

        private IMergeQueueService _MergeQueue = null!;
        private Armada.Core.Services.JobService _JobService = null!;
        private Armada.Core.Services.MissionRecoveryCoordinator _MissionRecovery = null!;
        private LandingService _LandingService = null!;
        private IMessageTemplateService _TemplateService = null!;
        private IPromptTemplateService _PromptTemplateService = null!;
        private PersonaSeedService _PersonaSeedService = null!;
        private LogRotationService _LogRotation = null!;
        private DataExpiryService _DataExpiry = null!;
        private RemoteTunnelManager _RemoteTunnel = null!;
        private RemoteDashboardRelayService _RemoteDashboardRelay = null!;
        private PlanningSessionCoordinator _PlanningSessions = null!;
        private ObjectiveRefinementCoordinator _ObjectiveRefinementSessions = null!;
        private IWorkspaceService _Workspace = null!;
        private RequestHistoryCaptureService _RequestHistoryCapture = null!;
        private WorkflowProfileService _WorkflowProfileService = null!;
        private ProjectProfileService _ProjectProfileService = null!;
        private VesselReadinessService _VesselReadinessService = null!;
        private DeploymentEnvironmentService _EnvironmentService = null!;
        private CheckRunService _CheckRunService = null!;
        private ObjectiveService _ObjectiveService = null!;
        private ReleaseService _ReleaseService = null!;
        private DeploymentService _DeploymentService = null!;
        private IncidentService _IncidentService = null!;
        private RunbookService _RunbookService = null!;
        private GitHubIntegrationService _GitHubIntegrationService = null!;
        private LandingPreviewService _LandingPreviewService = null!;
        private HistoricalTimelineService _HistoricalTimelineService = null!;
        private ModelEndpointService _ModelEndpointService = null!;
        private HarborService _HarborService = null!;
        private HarborConnectionManager _HarborConnectionManager = null!;
        private HarborLinkEndpoint _HarborLinkEndpoint = null!;

        private ISessionTokenService _SessionTokenService = null!;
        private IAuthenticationService _AuthenticationService = null!;
        private IAuthorizationService _AuthorizationService = null!;
        private IMissionService _MissionService = null!;
        private CaptainToolService _CaptainTools = null!;

        private AgentLifecycleHandler _AgentLifecycle = null!;
        private MissionLandingHandler _MissionLanding = null!;

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private Task _HealthCheckTask = null!;
        private int _HealthCheckCycles = 0;
        private DateTime _StartUtc = DateTime.UtcNow;
        private readonly ConditionalWeakTable<HttpContextBase, AuthContext> _RequestAuthContexts = new ConditionalWeakTable<HttpContextBase, AuthContext>();

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
            // Start the telemetry host first so instruments are observed from the very start.
            _TelemetryHost = new ArmadaTelemetryHost(_Logging);
            _TelemetryHost.Start(_Settings.Telemetry);

            // Initialize database
            _Database = DatabaseDriverFactory.Create(_Settings.Database, _Logging);
            await _Database.InitializeAsync().ConfigureAwait(false);
            _Logging.Info(_Header + "database initialized");

            // Ensure a local API key exists so trusted local clients (the armada CLI) can authenticate
            // to the REST API. Generated once and persisted to settings.json, which the CLI also reads.
            await EnsureApiKeyAsync().ConfigureAwait(false);

            // Initialize services
            _Git = new GitService(_Logging);
            IDockService dockService = new DockService(_Logging, _Database, _Settings, _Git);
            _Docks = dockService;
            ICaptainService captainService = new CaptainService(_Logging, _Database, _Settings, _Git, dockService);
            // Prompt template service must be created before MissionService so it can resolve templates
            _PromptTemplateService = new PromptTemplateService(_Database, _Logging);

            IDefinitionOfDoneGate definitionOfDoneGate = new DefinitionOfDoneGate(_Logging);
            MissionService missionService = new MissionService(_Logging, _Database, _Settings, dockService, captainService, _PromptTemplateService, _Git, definitionOfDoneGate);
            _MissionService = missionService;
            IVoyageService voyageService = new VoyageService(_Logging, _Database);
            IEscalationService escalationService = new EscalationService(_Logging, _Database, _Settings);
            AdmiralService admiralService = new AdmiralService(_Logging, _Database, _Settings, captainService, missionService, voyageService, dockService, escalationService);
            _Admiral = admiralService;
            _MergeQueue = new MergeQueueService(_Logging, _Database, _Settings, _Git);
            _JobService = new Armada.Core.Services.JobService(_Database, _Logging);
            _MissionRecovery = new Armada.Core.Services.MissionRecoveryCoordinator(_Logging, _Database, _Settings);
            _LandingService = new LandingService(_Logging, _Database, _Settings, _Git);
            _TemplateService = new MessageTemplateService(_Logging, _PromptTemplateService);
            _RuntimeFactory = new AgentRuntimeFactory(_Logging, ResolveInferenceEndpoint);
            _Workspace = new WorkspaceService();
            _RequestHistoryCapture = new RequestHistoryCaptureService(_Settings);
            _WorkflowProfileService = new WorkflowProfileService(_Database, _Logging);
            _ProjectProfileService = new ProjectProfileService(_Database, _Logging);
            _VesselReadinessService = new VesselReadinessService(_Database, _WorkflowProfileService, _Logging);
            _EnvironmentService = new DeploymentEnvironmentService(_Database, _WorkflowProfileService, _Logging);
            _CheckRunService = new CheckRunService(_Database, _WorkflowProfileService, _VesselReadinessService, _Logging);
            _ObjectiveService = new ObjectiveService(_Database);
            _ReleaseService = new ReleaseService(_Database, _WorkflowProfileService, _Logging);
            _DeploymentService = new DeploymentService(_Database, _WorkflowProfileService, _EnvironmentService, _CheckRunService, _Logging);
            _IncidentService = new IncidentService(_Database);
            _RunbookService = new RunbookService(_Database, _Logging);
            _GitHubIntegrationService = new GitHubIntegrationService(_Database, _ObjectiveService, _CheckRunService, _DeploymentService, _Settings, _Logging);
            _LandingPreviewService = new LandingPreviewService(_Database, _Logging);
            _HistoricalTimelineService = new HistoricalTimelineService(_Database);
            _ModelEndpointService = new ModelEndpointService(_Database, _Logging);
            _HarborService = new HarborService(_Database, _Logging);
            string harborMcpUrl = String.IsNullOrWhiteSpace(_Settings.Harbor.AdvertisedMcpBaseUrl)
                ? ArmadaMcpConfigBuilder.GetMcpUrl(_Settings.McpPort)
                : _Settings.Harbor.AdvertisedMcpBaseUrl!;
            _HarborConnectionManager = new HarborConnectionManager(_HarborService, _Logging, harborMcpUrl);
            _HarborLinkEndpoint = new HarborLinkEndpoint(_HarborConnectionManager, _Settings.Harbor, _Logging);
            _RemoteTunnel = new RemoteTunnelManager(_Logging, _Settings);
            _RemoteDashboardRelay = new RemoteDashboardRelayService(_Logging, _Settings, _RemoteTunnel.PublishEventAsync);
            admiralService.OnGetRemoteTunnelStatus = _RemoteTunnel.GetStatus;
            // Seed built-in prompt templates, personas, and pipelines
            await _PromptTemplateService.SeedDefaultsAsync().ConfigureAwait(false);
            _Logging.Info(_Header + "prompt template seeding completed");

            _PersonaSeedService = new PersonaSeedService(_Database, _Logging);
            await _PersonaSeedService.SeedAsync().ConfigureAwait(false);
            _Logging.Info(_Header + "persona and pipeline seeding completed");

            await _EnvironmentService.SeedDefaultsAsync().ConfigureAwait(false);
            _Logging.Info(_Header + "deployment environment seeding completed");

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

            // Delegate captain launches to a connected Harbor by default; falls back to local when none is eligible.
            _AgentLifecycle.SetHarborConnections(_HarborConnectionManager);
            // Enable API-endpoint captains delegated to a Harbor to carry their resolved inference endpoint.
            _AgentLifecycle.SetEndpointResolver(ResolveInferenceEndpoint);

            // Wire up agent lifecycle events
            _Admiral.OnLaunchAgent = _AgentLifecycle.HandleLaunchAgentAsync;
            _Admiral.OnStopAgent = _AgentLifecycle.HandleStopAgentAsync;
            _Admiral.OnCaptureDiff = _MissionLanding.HandleCaptureDiffAsync;
            _Admiral.OnIsProcessExitHandled = _AgentLifecycle.IsProcessExitHandled;
            missionService.OnGetMissionOutput = _AgentLifecycle.GetAndClearMissionOutput;

            // When RequireHarborForLaunch is set, defer a mission until an eligible Harbor owned by the
            // requesting user is connected -- never run it in-process or on another user's Harbor.
            missionService.CanAssignMissionAsync = async (mission, captain) =>
            {
                if (!_Settings.RequireHarborForLaunch) return true;
                if (_HarborConnectionManager == null) return false;
                Armada.Core.Services.HarborRoutingRequest request = new Armada.Core.Services.HarborRoutingRequest { RequestedRuntime = captain.Runtime.ToString() };
                return await _HarborConnectionManager.HasEligibleHarborForUserAsync(mission.UserId, request).ConfigureAwait(false);
            };
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
                openApi.Tags.Add(new OpenApiTag { Name = "Workspace", Description = "Workspace browsing, editing, search, and dispatch handoff" });
                openApi.Tags.Add(new OpenApiTag { Name = "Objectives", Description = "Cross-repository objectives and intake-style scope records" });
                openApi.Tags.Add(new OpenApiTag { Name = "WorkflowProfiles", Description = "Project-specific build, test, release, deploy, and verification command profiles" });
                openApi.Tags.Add(new OpenApiTag { Name = "Environments", Description = "First-class deployment environment metadata for vessels" });
                openApi.Tags.Add(new OpenApiTag { Name = "CheckRuns", Description = "Structured build, test, deploy, and verification executions with durable results" });
                openApi.Tags.Add(new OpenApiTag { Name = "Releases", Description = "First-class release records linking work, checks, notes, versions, and artifacts" });
                openApi.Tags.Add(new OpenApiTag { Name = "Deployments", Description = "First-class deployment records with approval, verification, and rollback state" });
                openApi.Tags.Add(new OpenApiTag { Name = "Incidents", Description = "Incident, rollback, and hotfix records tied to current delivery state" });
                openApi.Tags.Add(new OpenApiTag { Name = "Runbooks", Description = "Executable operational runbooks backed by playbooks and execution records" });
                openApi.Tags.Add(new OpenApiTag { Name = "RequestHistory", Description = "Captured REST request history, summaries, and replay metadata" });
                openApi.Tags.Add(new OpenApiTag { Name = "History", Description = "Cross-entity operational timeline and historical memory" });
                openApi.Tags.Add(new OpenApiTag { Name = "Voyages", Description = "Voyage (mission batch) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Missions", Description = "Mission (atomic work unit) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Planning", Description = "Captain planning sessions and transcript-to-dispatch flow" });
                openApi.Tags.Add(new OpenApiTag { Name = "Playbooks", Description = "Markdown playbook management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Captains", Description = "Captain (AI agent) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Signals", Description = "Signal (inter-agent messaging) management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Events", Description = "System event log" });
                openApi.Tags.Add(new OpenApiTag { Name = "Runtimes", Description = "Runtime-specific integration helpers and discovery" });
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
                await CaptureRequestHistoryAsync(ctx).ConfigureAwait(false);
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
            missionService.OnReviewRequested = _WebSocketHub.BroadcastApprovalNeeded;
            _CheckRunService.OnCheckRunChanged = _WebSocketHub.BroadcastCheckRunChange;
            _ObjectiveService.OnObjectiveChanged = _WebSocketHub.BroadcastObjectiveChange;
            _DeploymentService.OnDeploymentChanged = _WebSocketHub.BroadcastDeploymentChange;
            _IncidentService.OnIncidentChanged = _WebSocketHub.BroadcastIncidentChange;
            _RunbookService.OnRunbookExecutionChanged = _WebSocketHub.BroadcastRunbookExecutionChange;
            _PlanningSessions = new PlanningSessionCoordinator(
                _Logging,
                _Database,
                _Settings,
                _Docks,
                _Admiral,
                _RuntimeFactory,
                EmitEventAsync,
                _WebSocketHub);
            _ObjectiveRefinementSessions = new ObjectiveRefinementCoordinator(
                _Logging,
                _Database,
                _Settings,
                _RuntimeFactory,
                EmitEventAsync,
                _WebSocketHub);

            _CaptainTools = new CaptainToolService(
                _Logging,
                _Database);

            _RemoteTunnel.OnHandleRequest = HandleRemoteTunnelRequestAsync;

            RegisterRoutes();
            InitializeDashboard();

            // Register WebSocket route on the main REST server
            _App.WebSocket("/ws", _WebSocketHub.HandleWebSocketAsync);
            _Logging.Info(_Header + "WebSocket route registered at /ws");

            _App.WebSocket(_Settings.Harbor.LinkPath, _HarborLinkEndpoint.HandleWebSocketAsync);
            _Logging.Info(_Header + "Harbor link route registered at " + _Settings.Harbor.LinkPath);

            // Watson 7 StartAsync is long-running; Start() binds and returns after
            // scheduling the accept loop.
            _App.Start(_TokenSource.Token);
            _Logging.Info(_Header + "REST API started on port " + _Settings.AdmiralPort);

            // Initialize MCP server
            _McpServer = new McpHttpServer(_Settings.Rest.Hostname, _Settings.McpPort);
            _McpServer.ServerName = ArmadaConstants.ProductName;
            _McpServer.ServerVersion = ArmadaConstants.ProductVersion;
            _McpServer.AuthenticationHandler = AuthenticateMcpRequestAsync;
            RegisterMcpTools();

            Task mcpTask = Task.Run(() => _McpServer.StartAsync(_TokenSource.Token));
            _Logging.Info(_Header + "MCP server started on port " + _Settings.McpPort);

            _RemoteTunnel.Start(_TokenSource.Token);
            _Logging.Info(_Header + "remote tunnel manager started");

            try
            {
                await _PlanningSessions.RecoverSessionsAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Info(_Header + "planning session recovery completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "planning session recovery error: " + ex.ToString());
            }

            try
            {
                await _PlanningSessions.MaintainSessionsAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Info(_Header + "planning session maintenance completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "planning session maintenance error: " + ex.ToString());
            }

            try
            {
                await _ObjectiveRefinementSessions.RecoverSessionsAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Info(_Header + "objective refinement session recovery completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "objective refinement session recovery error: " + ex.ToString());
            }

            try
            {
                await _ObjectiveRefinementSessions.MaintainSessionsAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Info(_Header + "objective refinement session maintenance completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "objective refinement session maintenance error: " + ex.ToString());
            }

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
                _Logging.Warn(_Header + "REST API stop error: " + ex.ToString());
            }
            // Kill agent subprocesses so none survive as orphans after the Admiral exits.
            // Runs before the token is cancelled and the database is disposed (it needs both).
            try
            {
                _Admiral?.StopAllAgentProcessesAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error stopping agent processes on shutdown: " + ex.ToString());
            }

            _TokenSource.Cancel();
            _RemoteTunnel?.StopAsync().GetAwaiter().GetResult();
            _RemoteDashboardRelay?.DisposeAsync().GetAwaiter().GetResult();
            _McpServer?.Stop();
            _Database?.Dispose();
            _TelemetryHost?.Dispose();
            OnStopping?.Invoke();
        }

        #endregion

        #region Private-Methods

        private async Task<AuthContext> AuthenticateRequestAsync(WatsonWebserver.Core.HttpContextBase ctx)
        {
            string? authHeader = ctx.Request.Headers.Get("Authorization");
            string? tokenHeader = ctx.Request.Headers.Get("X-Token");
            string? apiKeyHeader = ctx.Request.Headers.Get("X-Api-Key");
            AuthContext result = await _AuthenticationService.AuthenticateAsync(authHeader, tokenHeader, apiKeyHeader).ConfigureAwait(false);
            _RequestAuthContexts.Remove(ctx);
            _RequestAuthContexts.Add(ctx, result);
            return result;
        }

        private async Task<AuthenticationResult> AuthenticateMcpRequestAsync(System.Net.HttpListenerRequest request)
        {
            // Populate the caller's identity for the MCP tool handlers when a credential is presented.
            // This is additive: unauthenticated local/stdio callers still succeed (IsAuthenticated = true with
            // no claims), and the tool handlers fall back to the default tenant-admin context. When a valid
            // credential is presented, the resolved tenant/user/role claims flow into the handlers via
            // Voltaic's ambient RpcCallContext so MCP tools are scoped per-user, matching the REST API.
            AuthenticationResult result = new AuthenticationResult { IsAuthenticated = true };
            try
            {
                string? authHeader = request.Headers["Authorization"];
                string? tokenHeader = request.Headers["X-Token"];
                string? apiKeyHeader = request.Headers["X-Api-Key"];
                if (!String.IsNullOrEmpty(authHeader) || !String.IsNullOrEmpty(tokenHeader) || !String.IsNullOrEmpty(apiKeyHeader))
                {
                    AuthContext ctx = await _AuthenticationService.AuthenticateAsync(authHeader, tokenHeader, apiKeyHeader).ConfigureAwait(false);
                    if (ctx != null && ctx.IsAuthenticated && !String.IsNullOrEmpty(ctx.UserId))
                    {
                        result.Principal = ctx.UserId;
                        result.Claims = new Dictionary<string, string>
                        {
                            ["tenantId"] = ctx.TenantId ?? String.Empty,
                            ["userId"] = ctx.UserId ?? String.Empty,
                            ["isAdmin"] = ctx.IsAdmin ? "true" : "false",
                            ["isTenantAdmin"] = ctx.IsTenantAdmin ? "true" : "false",
                            ["authMethod"] = ctx.AuthMethod ?? "Mcp"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                // Never fail the MCP request on an auth-resolution error; treat as an anonymous local caller.
                _Logging.Debug(_Header + "MCP caller authentication skipped: " + ex.Message);
            }

            return result;
        }

        private async Task EnsureApiKeyAsync()
        {
            if (!String.IsNullOrEmpty(_Settings.ApiKey))
                return;

            _Settings.ApiKey = "ak_" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            try
            {
                await _Settings.SaveAsync().ConfigureAwait(false);
                _Logging.Info(_Header + "generated local API key for CLI authentication and saved to settings");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "generated API key but could not persist settings: " + ex.ToString());
            }
        }

        private async Task SeedSyntheticAdminAsync()
        {
            _Logging.Info(_Header + "seeding synthetic admin identity for API key");

            // Create system tenant if not exists
            TenantMetadata? existingTenant = await _Database.Tenants.ReadAsync(ArmadaConstants.SystemTenantId).ConfigureAwait(false);
            if (existingTenant == null)
            {
                TenantMetadata systemTenant = new TenantMetadata();
                systemTenant.Id = ArmadaConstants.SystemTenantId;
                systemTenant.Name = ArmadaConstants.SystemTenantName;
                systemTenant.IsProtected = true;
                await _Database.Tenants.CreateAsync(systemTenant).ConfigureAwait(false);
            }

            // Create system user if not exists
            UserMaster? existingUser = await _Database.Users.ReadByIdAsync(ArmadaConstants.SystemUserId).ConfigureAwait(false);
            if (existingUser == null)
            {
                UserMaster systemUser = new UserMaster();
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
            VesselContextService vesselContextService = new VesselContextService(_Database, _RuntimeFactory, _Docks, _PromptTemplateService, _Logging);
            new VesselRoutes(_Database, _VesselReadinessService, _LandingPreviewService, EmitEventAsync, _JsonOptions, _Docks, vesselContextService, _Git, _Settings)
                .Register(_App, authenticate, _AuthorizationService);

            // Workspace
            new WorkspaceRoutes(_Database, _Workspace, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Workflow profiles
            new WorkflowProfileRoutes(_Database, _WorkflowProfileService, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Project profiles
            new ProjectProfileRoutes(_Database, _ProjectProfileService, _PromptTemplateService, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Skills directory
            new SkillRoutes(_Database, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Ask Armada assistant
            new AskRoutes(new AskArmadaService(_Database, _Admiral, _Logging), new CaptainChatService(_Database, _RuntimeFactory, _WebSocketHub, _PromptTemplateService, _Logging), _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Needs-you inbox
            new InboxRoutes(new InboxService(_Database, _Logging), _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Objectives
            new ObjectiveRoutes(_ObjectiveService, _GitHubIntegrationService)
                .Register(_App, authenticate, _AuthorizationService);

            new ObjectiveRefinementRoutes(_Database, _ObjectiveRefinementSessions, _ObjectiveService, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Environments
            new EnvironmentRoutes(_EnvironmentService)
                .Register(_App, authenticate, _AuthorizationService);

            // Model endpoints (embedding/inference)
            new ModelEndpointRoutes(_ModelEndpointService)
                .Register(_App, authenticate, _AuthorizationService);

            // Harbors (host runners)
            new HarborRoutes(_HarborService, _HarborConnectionManager)
                .Register(_App, authenticate, _AuthorizationService);

            // Structured check runs
            new CheckRunRoutes(_Database, _CheckRunService, _GitHubIntegrationService, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Releases
            new ReleaseRoutes(_ReleaseService, _ObjectiveService, _GitHubIntegrationService)
                .Register(_App, authenticate, _AuthorizationService);

            // Deployments
            new DeploymentRoutes(_DeploymentService, _ObjectiveService, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Incidents
            new IncidentRoutes(_IncidentService, _ObjectiveService)
                .Register(_App, authenticate, _AuthorizationService);

            // Runbooks
            new RunbookRoutes(_RunbookService)
                .Register(_App, authenticate, _AuthorizationService);

            // Request history
            new RequestHistoryRoutes(_Database, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Token usage
            new TokenUsageRoutes(_Database, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Cross-entity history
            new HistoryRoutes(_HistoricalTimelineService)
                .Register(_App, authenticate, _AuthorizationService);

            // Voyages
            new VoyageRoutes(_Database, _Admiral, EmitEventAsync, _WebSocketHub, _Logging, _ObjectiveService, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Missions
            new MissionRoutes(_Database, _Admiral, _MissionService, _Settings, _Git, _LandingService, _LandingPreviewService, _GitHubIntegrationService, EmitEventAsync, _MissionLanding.HandleMissionCompleteAsync, _WebSocketHub, _Logging, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Captains
            new CaptainRoutes(_Database, _Admiral, _Settings, _RuntimeFactory, _AgentLifecycle, _CaptainTools, EmitEventAsync, _JsonOptions, _PlanningSessions, _ObjectiveRefinementSessions)
                .Register(_App, authenticate, _AuthorizationService);

            // Runtime helpers
            new RuntimeRoutes(_Logging)
                .Register(_App, authenticate, _AuthorizationService);

            // Planning sessions
            new PlanningSessionRoutes(_Database, _PlanningSessions, _ObjectiveService, _JsonOptions)
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

            // Background jobs
            new Routes.JobRoutes(_Database, _JobService)
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

            // Fallback: use embedded wwwroot resources (legacy dashboard, not the React dashboard)
            _Logging.Info(_Header + "using embedded legacy dashboard because no external React dashboard was found");
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

        private async Task CaptureRequestHistoryAsync(HttpContextBase ctx)
        {
            try
            {
                string route = ctx.Request.Url.RawWithoutQuery ?? String.Empty;
                if (!_RequestHistoryCapture.ShouldCapture(route)) return;

                AuthContext? auth = null;
                _RequestAuthContexts.TryGetValue(ctx, out auth);

                RequestHistoryCaptureInput input = new RequestHistoryCaptureInput
                {
                    Method = ctx.Request.Method.ToString().ToUpperInvariant(),
                    Route = route,
                    RouteTemplate = route,
                    QueryString = ExtractQueryString(ctx),
                    StatusCode = ctx.Response.StatusCode,
                    DurationMs = Math.Round(ctx.Timestamp.TotalMs ?? 0, 2),
                    RequestSizeBytes = ctx.Request.ContentLength,
                    ResponseSizeBytes = ctx.Response.ContentLength,
                    RequestContentType = ctx.Request.ContentType,
                    ResponseContentType = ctx.Response.ContentType,
                    ClientIp = ctx.Request.Source?.IpAddress?.ToString(),
                    CorrelationId = ctx.Request.Headers.Get("X-Correlation-Id") ?? ctx.Request.Headers.Get("X-Request-Id"),
                    RequestHeaders = ExtractHeaders(ctx.Request.Headers),
                    ResponseHeaders = ExtractHeaders(ctx.Response.Headers),
                    RequestBodyText = ReadBodySnapshot(ctx.Request.ContentType, ctx.Request.ContentLength, () => ctx.Request.DataAsString),
                    ResponseBodyText = ReadBodySnapshot(ctx.Response.ContentType, ctx.Response.ContentLength, () => ctx.Response.DataAsString)
                };

                RequestHistoryRecord record = _RequestHistoryCapture.BuildRecord(auth, input);
                await _Database.RequestHistory.CreateAsync(record.Entry, record.Detail, _TokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "request history capture error: " + ex.ToString());
            }
        }

        private static Dictionary<string, string?> ExtractHeaders(System.Collections.Specialized.NameValueCollection headers)
        {
            Dictionary<string, string?> results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (string? key in headers.AllKeys)
            {
                if (String.IsNullOrWhiteSpace(key)) continue;
                results[key] = headers.Get(key);
            }
            return results;
        }

        private string? ReadBodySnapshot(string? contentType, long contentLength, Func<string?> reader)
        {
            int maxPreviewBytes = Math.Max(_Settings.RequestHistoryMaxBodyBytes * 4, _Settings.RequestHistoryMaxBodyBytes);
            if (contentLength > maxPreviewBytes && !IsTextualContent(contentType)) return null;
            if (contentLength > maxPreviewBytes && String.IsNullOrWhiteSpace(contentType)) return null;

            try
            {
                return reader();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsTextualContent(string? contentType)
        {
            if (String.IsNullOrWhiteSpace(contentType)) return true;
            return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ExtractQueryString(HttpContextBase ctx)
        {
            string? rawWithQuery = ctx.Request.Url.RawWithQuery;
            int idx = !String.IsNullOrWhiteSpace(rawWithQuery) ? rawWithQuery.IndexOf('?') : -1;
            if (!String.IsNullOrWhiteSpace(rawWithQuery) && idx >= 0)
            {
                if (idx == rawWithQuery.Length - 1) return null;
                return rawWithQuery.Substring(idx + 1);
            }

            object? url = ctx.Request.Url;
            string? reflectedUrlQuery = ExtractQueryStringFromObject(url);
            if (!String.IsNullOrWhiteSpace(reflectedUrlQuery))
                return reflectedUrlQuery;

            return ExtractQueryStringFromObject(ctx.Request);
        }

        private static string? ExtractQueryStringFromObject(object? source)
        {
            if (source == null) return null;

            Type type = source.GetType();

            foreach (string propertyName in new[] { "Querystring", "QueryString", "Query" })
            {
                System.Reflection.PropertyInfo? property = type.GetProperty(propertyName);
                if (property == null) continue;

                object? value = property.GetValue(source);
                string? serialized = SerializeQueryValue(value);
                if (!String.IsNullOrWhiteSpace(serialized))
                    return serialized;
            }

            return null;
        }

        private static string? SerializeQueryValue(object? value)
        {
            if (value == null) return null;

            if (value is string stringValue)
            {
                if (String.IsNullOrWhiteSpace(stringValue)) return null;
                return stringValue.StartsWith("?") ? stringValue.Substring(1) : stringValue;
            }

            if (value is System.Collections.Specialized.NameValueCollection nameValueCollection)
            {
                List<string> parts = new List<string>();
                foreach (string? key in nameValueCollection.AllKeys)
                {
                    if (String.IsNullOrWhiteSpace(key)) continue;
                    string? itemValue = nameValueCollection.Get(key);
                    parts.Add(String.IsNullOrEmpty(itemValue)
                        ? Uri.EscapeDataString(key)
                        : Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(itemValue));
                }

                return parts.Count > 0 ? String.Join("&", parts) : null;
            }

            if (value is System.Collections.IDictionary dictionary)
            {
                List<string> parts = new List<string>();
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    if (entry.Key == null) continue;
                    string key = entry.Key.ToString() ?? String.Empty;
                    if (String.IsNullOrWhiteSpace(key)) continue;

                    string? itemValue = entry.Value?.ToString();
                    parts.Add(String.IsNullOrEmpty(itemValue)
                        ? Uri.EscapeDataString(key)
                        : Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(itemValue));
                }

                return parts.Count > 0 ? String.Join("&", parts) : null;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                List<string> parts = new List<string>();
                foreach (object? item in enumerable)
                {
                    if (item == null) continue;

                    Type itemType = item.GetType();
                    System.Reflection.PropertyInfo? keyProperty = itemType.GetProperty("Key");
                    System.Reflection.PropertyInfo? valueProperty = itemType.GetProperty("Value");
                    if (keyProperty == null || valueProperty == null) continue;

                    string? key = keyProperty.GetValue(item)?.ToString();
                    if (String.IsNullOrWhiteSpace(key)) continue;

                    string? itemValue = valueProperty.GetValue(item)?.ToString();
                    parts.Add(String.IsNullOrEmpty(itemValue)
                        ? Uri.EscapeDataString(key)
                        : Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(itemValue));
                }

                return parts.Count > 0 ? String.Join("&", parts) : null;
            }

            return null;
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

        /// <summary>
        /// Adapts Armada's JsonElement-based tool handler to Voltaic 0.6.0's RpcParameters-based
        /// RegisterTool signature, so the tool handlers themselves do not need to change.
        /// </summary>
        private void RegisterAdaptedTool(string name, string description, object inputSchema, Func<System.Text.Json.JsonElement?, Task<object>> handler)
        {
            _McpServer.RegisterTool(name, description, inputSchema, (RpcParameters? parameters) =>
            {
                System.Text.Json.JsonElement? args = null;
                if (parameters != null && parameters.HasValue && !string.IsNullOrEmpty(parameters.RawJson))
                {
                    using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(parameters.RawJson);
                    args = doc.RootElement.Clone();
                }
                return handler(args);
            });
        }

        private void RegisterMcpTools()
        {
            McpToolRegistrar.RegisterAll(
                RegisterAdaptedTool,
                _Database,
                _Admiral,
                _Settings,
                _Git,
                _MergeQueue,
                _Docks,
                _LandingService,
                _CheckRunService,
                _ObjectiveService,
                _PlanningSessions,
                _ObjectiveRefinementSessions,
                _ReleaseService,
                _DeploymentService,
                _RunbookService,
                () => Stop(),
                async (captainId) =>
                {
                    Captain? captain = await _Database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (captain != null)
                        await _AgentLifecycle.HandleStopAgentAsync(captain).ConfigureAwait(false);
                },
                _AgentLifecycle,
                _PromptTemplateService,
                _Logging,
                _CaptainTools,
                _ModelEndpointService,
                _HarborService);
        }

        /// <summary>
        /// Resolve a model-endpoint id to a usable inference endpoint (enabled, Inference-kind), or null.
        /// Shared by the runtime factory (in-process captains) and the lifecycle handler (Harbor-delegated
        /// captains, whose endpoint is shipped in the launch).
        /// </summary>
        /// <param name="endpointId">Model-endpoint identifier.</param>
        /// <returns>The endpoint, or null when missing, disabled, or not an Inference endpoint.</returns>
        private Armada.Core.Models.ModelEndpoint? ResolveInferenceEndpoint(string endpointId)
        {
            if (String.IsNullOrEmpty(endpointId)) return null;
            try
            {
                Armada.Core.Models.ModelEndpoint? endpoint = _Database.ModelEndpoints.ReadAsync(endpointId).GetAwaiter().GetResult();
                if (endpoint == null || !endpoint.Enabled || endpoint.Kind != Armada.Core.Enums.ModelEndpointKindEnum.Inference) return null;
                return endpoint;
            }
            catch
            {
                return null;
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
                _Logging.Warn(_Header + "error emitting event: " + ex.ToString());
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
                _Logging.Warn(_Header + "startup stale captain cleanup error: " + ex.ToString());
            }

            // Run an immediate health check on startup to dispatch any pending missions
            try
            {
                await _Admiral.HealthCheckAsync(token).ConfigureAwait(false);
                await _DeploymentService.MonitorRolloutWindowsAsync(token).ConfigureAwait(false);
                _Logging.Info(_Header + "startup health check completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "startup health check error: " + ex.ToString());
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_Settings.HeartbeatIntervalSeconds * 1000, token).ConfigureAwait(false);
                    await _Admiral.HealthCheckAsync(token).ConfigureAwait(false);
                    await _DeploymentService.MonitorRolloutWindowsAsync(token).ConfigureAwait(false);

                    // Drive the merge queue so auto-enqueued entries land without a manual trigger.
                    try { await _MergeQueue.ProcessQueueAsync(token).ConfigureAwait(false); }
                    catch (Exception mqEx) { _Logging.Warn(_Header + "merge queue processing error: " + mqEx.Message); }

                    // Reap background jobs whose worker died so they do not hang in Running.
                    try { await _JobService.MaintainAsync(token).ConfigureAwait(false); }
                    catch (Exception jobEx) { _Logging.Warn(_Header + "job maintenance error: " + jobEx.Message); }

                    // Autonomous mission recovery: classify failures, open/link incidents, dispatch bounded
                    // rescue missions, and advance incidents Open -> Mitigated -> Closed from mission evidence.
                    try { await _MissionRecovery.MaintainAsync(token).ConfigureAwait(false); }
                    catch (Exception recoveryEx) { _Logging.Warn(_Header + "mission recovery error: " + recoveryEx.Message); }

                    // Run log rotation every 10 health check cycles
                    _HealthCheckCycles++;
                    if (_HealthCheckCycles % 10 == 0)
                    {
                        string captainLogDir = Path.Combine(_Settings.LogDirectory, "captains");
                        _LogRotation.RotateAllInDirectory(captainLogDir);
                        _LogRotation.RotateIfNeeded(Path.Combine(_Settings.LogDirectory, "admiral.log"));
                        await _PlanningSessions.MaintainSessionsAsync(token).ConfigureAwait(false);
                        await _ObjectiveRefinementSessions.MaintainSessionsAsync(token).ConfigureAwait(false);

                        // Sweep managed model endpoints, deduplicated by base URL, so their health status stays current.
                        try { await _ModelEndpointService.CheckHealthAllAsync(token).ConfigureAwait(false); }
                        catch (Exception epEx) { _Logging.Warn(_Header + "model endpoint health sweep error: " + epEx.Message); }
                    }

                    // Run data expiry every 100 health check cycles (~50 min at default interval)
                    if (_HealthCheckCycles % 100 == 0)
                    {
                        await _DataExpiry.PurgeExpiredDataAsync(token).ConfigureAwait(false);
                        await PurgeExpiredRequestHistoryAsync(token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "health check error: " + ex.ToString());
                }
            }
        }

        private async Task<RemoteTunnelRequestResult> HandleRemoteTunnelRequestAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            string method = envelope.Method?.Trim().ToLowerInvariant() ?? String.Empty;
            switch (method)
            {
                case "armada.http.request":
                case "armada.ws.open":
                case "armada.ws.message":
                case "armada.ws.close":
                    return await _RemoteDashboardRelay.HandleAsync(envelope, token).ConfigureAwait(false);
            }

            return new RemoteTunnelRequestResult
            {
                StatusCode = 404,
                ErrorCode = "unsupported_method",
                Message = "Tunnel method " + envelope.Method + " is not supported. Use generic dashboard relay methods instead."
            };
        }

        private async Task PurgeExpiredRequestHistoryAsync(CancellationToken token)
        {
            if (!_Settings.RequestHistoryEnabled || _Settings.RequestHistoryRetentionDays <= 0)
                return;

            try
            {
                int deleted = await _Database.RequestHistory.DeleteByFilterAsync(new RequestHistoryQuery
                {
                    ToUtc = DateTime.UtcNow.AddDays(-_Settings.RequestHistoryRetentionDays)
                }, token).ConfigureAwait(false);

                if (deleted > 0)
                {
                    _Logging.Info(_Header + "purged " + deleted + " expired request history records");
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "request history purge error: " + ex.ToString());
            }
        }

        #endregion
    }
}
