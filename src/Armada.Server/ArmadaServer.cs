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
    using Armada.Core.Authorization;
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

        /// <summary>
        /// Factory the server creates captain runtimes from. End-to-end tests replace a runtime type through
        /// <see cref="AgentRuntimeFactory.Override"/> to run a scripted stub captain instead of a real agent CLI.
        /// </summary>
        public AgentRuntimeFactory RuntimeFactory
        {
            get { return _RuntimeFactory; }
        }

        /// <summary>
        /// MCP tool names registered on the HTTP MCP server, in registration order (populated by <see cref="StartAsync"/>).
        /// Used by the authorization coverage test to prove every tool has a declared requirement.
        /// </summary>
        public IReadOnlyList<string> RegisteredMcpTools
        {
            get { lock (_RegisteredMcpToolsLock) { return new List<string>(_RegisteredMcpTools); } }
        }

        /// <summary>
        /// MCP tools registered on the HTTP MCP server with their (marked) descriptions and serialized input schemas, in
        /// registration order (populated by <see cref="StartAsync"/>). Used by the API surface generator and contract test.
        /// </summary>
        public IReadOnlyList<CaptainToolSummary> RegisteredMcpToolDescriptors
        {
            get { lock (_RegisteredMcpToolsLock) { return new List<CaptainToolSummary>(_RegisteredMcpToolDescriptors); } }
        }

        /// <summary>
        /// Source for every host touch point of captain runtime tool discovery (runtime config files, runtime CLIs,
        /// installed-package inventories, MCP server connections). Null (the default) uses
        /// <see cref="Armada.Server.RuntimeTools.HostRuntimeToolDiscoverySource"/>. Set before <see cref="StartAsync"/>;
        /// test hosts set a fake so tests never read or launch the host user's tools.
        /// </summary>
        public Armada.Server.RuntimeTools.IRuntimeToolDiscoverySource? RuntimeToolDiscoverySource { get; set; } = null;

        /// <summary>
        /// Transport for push notifications to the mobile apps. Null (the default) uses
        /// <see cref="Armada.Core.Services.Push.ExpoPushTransport"/> (the Expo Push Service). Set before
        /// <see cref="StartAsync"/>; test hosts set a double so tests never call the real service.
        /// </summary>
        public Armada.Core.Services.Push.IPushTransport? PushTransport { get; set; } = null;

        /// <summary>
        /// The push notification service (populated by <see cref="StartAsync"/>).
        /// </summary>
        public Armada.Core.Services.Push.PushNotificationService? PushNotifications
        {
            get { return _PushNotifications; }
        }

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
        private RetentionService _Retention = null!;
        private RemoteTunnelManager _RemoteTunnel = null!;
        private RemoteDashboardRelayService _RemoteDashboardRelay = null!;
        private PlanningSessionCoordinator _PlanningSessions = null!;
        private ObjectiveRefinementCoordinator _ObjectiveRefinementSessions = null!;
        private IWorkspaceService _Workspace = null!;
        private RequestHistoryCaptureService _RequestHistoryCapture = null!;
        private WorkflowProfileService _WorkflowProfileService = null!;
        private ProjectProfileService _ProjectProfileService = null!;
        private VesselReadinessService _VesselReadinessService = null!;
        private Armada.Core.Services.Health.VesselHealthService _VesselHealthService = null!;
        private DeploymentEnvironmentService _EnvironmentService = null!;
        private CheckRunService _CheckRunService = null!;
        private ObjectiveService _ObjectiveService = null!;
        private ReleaseService _ReleaseService = null!;
        private DeploymentService _DeploymentService = null!;
        private IncidentService _IncidentService = null!;
        private RunbookService _RunbookService = null!;
        private GitHubIntegrationService _GitHubIntegrationService = null!;
        private ManualLandingReconciler _ManualLandingReconciler = null!;
        private LandingPreviewService _LandingPreviewService = null!;
        private HistoricalTimelineService _HistoricalTimelineService = null!;
        private ModelEndpointService _ModelEndpointService = null!;
        private HarborService _HarborService = null!;
        private HarborConnectionManager _HarborConnectionManager = null!;
        private CaptainLaunchRouter _LaunchRouter = null!;
        private HarborLinkEndpoint _HarborLinkEndpoint = null!;
        private IVesselService _VesselService = null!;
        private IVesselImportService _VesselImportService = null!;
        private IFleetCategorizationService _FleetCategorizationService = null!;
        private FleetActionRunner _FleetActionRunner = null!;
        private FleetActionService _FleetActionService = null!;

        private ISessionTokenService _SessionTokenService = null!;
        private IAuthenticationService _AuthenticationService = null!;
        private LoginRateLimiter _LoginRateLimiter = null!;
        private IAuthorizationService _AuthorizationService = null!;
        private IMissionService _MissionService = null!;
        private CaptainToolService _CaptainTools = null!;

        private Armada.Core.Services.Ask.AskThreadService _AskThreads = null!;
        private Armada.Server.Ask.AskActionService _AskActions = null!;
        private Armada.Server.Ask.AskTurnCoordinator _AskTurns = null!;
        private CliPermissionService _CliPermissions = null!;
        private Armada.Core.Services.Push.PushNotificationService? _PushNotifications = null;
        private Armada.Core.Services.Push.PushDeviceService _PushDevices = null!;
        private Armada.Server.Ask.AskWorkTracker _AskTracker = null!;
        private CaptainChatService _CaptainChat = null!;

        private AgentLifecycleHandler _AgentLifecycle = null!;
        private MissionLandingHandler _MissionLanding = null!;

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private Task _HealthCheckTask = null!;
        private Task? _McpListenTask = null;
        private int _HealthCheckCycles = 0;
        private DateTime _StartUtc = DateTime.UtcNow;
        private readonly ConditionalWeakTable<HttpContextBase, AuthContext> _RequestAuthContexts = new ConditionalWeakTable<HttpContextBase, AuthContext>();
        private readonly List<string> _RegisteredMcpTools = new List<string>();
        private readonly List<CaptainToolSummary> _RegisteredMcpToolDescriptors = new List<CaptainToolSummary>();
        private readonly object _RegisteredMcpToolsLock = new object();

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

            // Before migrating an existing database: copy it (SQLite) or warn with the dump command and, when
            // configured, refuse until a backup is confirmed (server providers). Throws MigrationBackupRequiredException.
            await new MigrationBackupService(_Settings, _Logging).PrepareAsync(_Database).ConfigureAwait(false);
            await _Database.InitializeAsync().ConfigureAwait(false);
            _Logging.Debug(_Header + "database initialized");

            // Ensure a local API key exists so trusted local clients (the armada CLI) can authenticate
            // to the REST API, and a session token encryption key exists so sign-ins survive a restart.
            // Both are generated once and persisted to settings.json (which the CLI also reads) in one save.
            await EnsureLocalSecretsAsync().ConfigureAwait(false);

            // Safe defaults: apply an initial admin password from the environment (headless installs), retire the
            // seeded "default" bearer token once the default password is gone, and refuse to listen on a non-loopback
            // hostname while default credentials are still in use unless explicitly allowed.
            // Upgrade password hashes written by earlier releases (unsalted SHA-256) to salted PBKDF2.
            await new PasswordHashUpgradeService(_Database, _Logging).UpgradeLegacyHashesAsync().ConfigureAwait(false);

            await EnforceDefaultCredentialPolicyAsync().ConfigureAwait(false);

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
            _VesselService = new VesselService(_Database);
            _MissionRecovery = new Armada.Core.Services.MissionRecoveryCoordinator(_Logging, _Database, _Settings);
            _LandingService = new LandingService(_Logging, _Database, _Settings, _Git);
            _TemplateService = new MessageTemplateService(_Logging, _PromptTemplateService);
            _RuntimeFactory = new AgentRuntimeFactory(_Logging, ResolveInferenceEndpoint);
            _FleetCategorizationService = new FleetCategorizationService(_Database, _Settings, _JobService, new CaptainPromptRunner(_RuntimeFactory, _Logging) { Settings = _Settings }, _PromptTemplateService, _Logging);
            _VesselImportService = new VesselImportService(_Database, _Settings, new VesselDiscoveryService(_Database, _Settings), _VesselService, _JobService, _Logging, _FleetCategorizationService);
            _Workspace = new WorkspaceService();
            _RequestHistoryCapture = new RequestHistoryCaptureService(_Settings);
            _WorkflowProfileService = new WorkflowProfileService(_Database, _Logging);
            _ProjectProfileService = new ProjectProfileService(_Database, _Logging);
            _VesselReadinessService = new VesselReadinessService(_Database, _WorkflowProfileService, _Logging);
            _EnvironmentService = new DeploymentEnvironmentService(_Database, _WorkflowProfileService, _Logging);
            _CheckRunService = new CheckRunService(_Database, _WorkflowProfileService, _VesselReadinessService, _Logging);

            // Vessel health: evaluator (pluggable criteria) plus the service that owns evaluation jobs and overrides.
            Armada.Core.Services.Health.DependencyScanner healthDependencyScanner = new Armada.Core.Services.Health.DependencyScanner(
                new Armada.Core.Services.Health.DependencyToolRunner(new LocalHostCommandExecutor()));
            Armada.Core.Services.Health.VesselHealthEvaluator healthEvaluator = new Armada.Core.Services.Health.VesselHealthEvaluator(
                _Database, _Git, _Settings,
                Armada.Core.Services.Health.VesselHealthEvaluator.CreateDefaultCriteria(_Database, _VesselReadinessService, healthDependencyScanner),
                _Logging);
            _VesselHealthService = new Armada.Core.Services.Health.VesselHealthService(_Database, _Settings, healthEvaluator, _JobService, _Logging);
            _ObjectiveService = new ObjectiveService(_Database);
            _ReleaseService = new ReleaseService(_Database, _WorkflowProfileService, _Logging);
            _DeploymentService = new DeploymentService(_Database, _WorkflowProfileService, _EnvironmentService, _CheckRunService, _Logging);
            _IncidentService = new IncidentService(_Database);
            _RunbookService = new RunbookService(_Database, _Logging);
            _GitHubIntegrationService = new GitHubIntegrationService(_Database, _ObjectiveService, _CheckRunService, _DeploymentService, _Settings, _Logging);
            _LandingPreviewService = new LandingPreviewService(_Database, _Logging, _Settings);
            _ManualLandingReconciler = new ManualLandingReconciler(_Database, _Settings, _Git, _Logging);
            _ManualLandingReconciler.OnMissionReconciled = async (mission) =>
            {
                await EmitMissionStatusChangedAsync(mission, MissionStatusEnum.WorkProduced, "Mission completed (branch merged by hand): " + mission.Title).ConfigureAwait(false);
                _WebSocketHub?.BroadcastMissionChange(mission, mission.Status.ToString());
            };
            admiralService.OnReconcileManualLandings = (ct) => _ManualLandingReconciler.ReconcileAsync(null, ct);
            _HistoricalTimelineService = new HistoricalTimelineService(_Database);
            _ModelEndpointService = new ModelEndpointService(_Database, _Logging);
            _HarborService = new HarborService(_Database, _Logging, _Settings.Harbor);
            string harborMcpUrl = String.IsNullOrWhiteSpace(_Settings.Harbor.AdvertisedMcpBaseUrl)
                ? ArmadaMcpConfigBuilder.GetMcpUrl(_Settings.McpPort, ArmadaMcpConfigBuilder.ClientHostFor(_Settings.Rest.Hostname))
                : _Settings.Harbor.AdvertisedMcpBaseUrl!;
            _HarborConnectionManager = new HarborConnectionManager(_HarborService, _Logging, harborMcpUrl);
            _HarborLinkEndpoint = new HarborLinkEndpoint(
                _HarborConnectionManager,
                _Settings.Harbor,
                _Logging,
                (authHeader, tokenHeader, apiKeyHeader) => _AuthenticationService.AuthenticateAsync(authHeader, tokenHeader, apiKeyHeader),
                DefaultCredentialService.IsLoopbackHostname(_Settings.Rest.Hostname));
            _RemoteTunnel = new RemoteTunnelManager(_Logging, _Settings);
            _RemoteDashboardRelay = new RemoteDashboardRelayService(_Logging, _Settings, _RemoteTunnel.PublishEventAsync);
            admiralService.OnGetRemoteTunnelStatus = _RemoteTunnel.GetStatus;
            // Seed built-in prompt templates, personas, and pipelines
            await _PromptTemplateService.SeedDefaultsAsync().ConfigureAwait(false);
            _Logging.Debug(_Header + "prompt template seeding completed");

            _PersonaSeedService = new PersonaSeedService(_Database, _Logging);
            await _PersonaSeedService.SeedAsync().ConfigureAwait(false);
            _Logging.Debug(_Header + "persona and pipeline seeding completed");

            await _EnvironmentService.SeedDefaultsAsync().ConfigureAwait(false);
            _Logging.Debug(_Header + "deployment environment seeding completed");

            // Fleet actions: runner (owned here, started below) and service; seed built-ins into every tenant.
            FleetActionSeedService fleetActionSeeder = new FleetActionSeedService(_Database, _Logging);
            _FleetActionRunner = new FleetActionRunner(_Database, _Settings, _Logging, new AdmiralFleetActionMissionDispatcher(_Database, _Admiral), new LocalHostCommandExecutor(), _HarborConnectionManager);
            _FleetActionService = new FleetActionService(_Database, _Settings, _FleetActionRunner, fleetActionSeeder, _Logging);
            try
            {
                await fleetActionSeeder.SeedAllTenantsAsync().ConfigureAwait(false);
                _Logging.Debug(_Header + "fleet action seeding completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "fleet action seeding error: " + ex.ToString());
            }

            // Start the runner before any route can create a run: restart recovery (Running command targets ->
            // Interrupted) must not race a run started through the API.
            try
            {
                await _FleetActionRunner.StartAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Debug(_Header + "fleet action runner started");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "fleet action runner start error: " + ex.ToString());
            }

            // Initialize authentication services
            // The key was generated and persisted by EnsureLocalSecretsAsync, so session tokens stay valid across restarts.
            _SessionTokenService = new SessionTokenService(_Settings.SessionTokenEncryptionKey);
            _AuthenticationService = new AuthenticationService(_Database, _SessionTokenService, _Settings, _Logging);
            _LoginRateLimiter = new LoginRateLimiter(_Settings.LoginRateLimit);
            _AuthorizationService = new AuthorizationService();

            // Seed synthetic admin identity if API key is configured
            if (!string.IsNullOrEmpty(_Settings.ApiKey))
            {
                await SeedSyntheticAdminAsync().ConfigureAwait(false);
            }

            // Initialize log rotation and data expiry
            _LogRotation = new LogRotationService(_Logging, _Settings.MaxLogFileSizeBytes, _Settings.MaxLogFileCount);
            _DataExpiry = new DataExpiryService(_Logging, _Settings.Database.GetConnectionString(), _Settings.DataRetentionDays);
            _Retention = new RetentionService(_Database, _Settings, _Logging);

            // Initialize handler classes (WebSocketHub is created later, so pass null initially)
            _MissionLanding = new MissionLandingHandler(
                _Logging, _Database, _Settings, _Git, _MergeQueue, _TemplateService, _PromptTemplateService, _Docks, null);

            _AgentLifecycle = new AgentLifecycleHandler(
                _Logging, _Database, _Settings, _RuntimeFactory, _Admiral, _TemplateService, _PromptTemplateService, null, EmitEventAsync);

            // Delegate captain launches to a connected Harbor by default; falls back to local when none is eligible.
            _AgentLifecycle.SetHarborConnections(_HarborConnectionManager);
            _AgentLifecycle.SetSessionTokenService(_SessionTokenService);
            // Enable API-endpoint captains delegated to a Harbor to carry their resolved inference endpoint.
            _AgentLifecycle.SetEndpointResolver(ResolveInferenceEndpoint);

            // Interactive captain launches (chat, Ask turns, planning, refinement, vessel context) route to a connected
            // Harbor with the same policy as missions, including requireHarborForLaunch.
            _LaunchRouter = new CaptainLaunchRouter(_Settings, _RuntimeFactory, _HarborConnectionManager, ResolveInferenceEndpoint, _Logging);

            // Wire up agent lifecycle events
            _Admiral.OnLaunchAgent = _AgentLifecycle.HandleLaunchAgentAsync;
            _Admiral.OnStopAgent = _AgentLifecycle.HandleStopAgentAsync;
            _Admiral.OnCaptureDiff = _MissionLanding.HandleCaptureDiffAsync;
            _Admiral.OnIsProcessExitHandled = _AgentLifecycle.IsProcessExitHandled;
            admiralService.OnIsProcessTracked = _AgentLifecycle.IsProcessTracked;
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
            // A duplicate name/email/key (or a provider unique-constraint violation) escaping any API route is a typed
            // 409 Conflict, never a 500 carrying provider text.
            _App.Middleware.Add(RouteErrorMapper.DuplicateEntityMiddlewareAsync);

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
                openApi.Tags.Add(new OpenApiTag { Name = "FleetActions", Description = "Fleet actions: shell commands or AI prompts applied across many vessels, with persisted runs" });
                openApi.Tags.Add(new OpenApiTag { Name = "Authentication", Description = "Authentication and identity" });
                openApi.Tags.Add(new OpenApiTag { Name = "Tenants", Description = "Multi-tenant management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Users", Description = "User management" });
                openApi.Tags.Add(new OpenApiTag { Name = "Credentials", Description = "Credential (API token) management" });
                openApi.Tags.Add(new OpenApiTag { Name = Armada.Core.ApiSurface.ExperimentalSurface.Tag, Description = "Experimental routes: excluded from the 1.0 compatibility promise (see docs/COMPATIBILITY.md) and may change or be removed in a minor release" });

                // API key security scheme
                openApi.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
                {
                    Type = "apiKey",
                    Name = "X-Api-Key",
                    In = "header",
                    Description = "API key for authenticating requests. Configure via ArmadaSettings.ApiKey."
                };
            });

            // Set timestamp on request start, and authenticate /ws upgrades before the handshake: Watson completes the
            // WebSocket handshake before invoking the route handler, so an unauthenticated upgrade must be refused here
            // with 401 (the Harbor link path is separate and keeps its own authentication).
            _App.Routes.PreRouting = async (HttpContextBase ctx) =>
            {
                ctx.Timestamp.Start = DateTime.UtcNow;

                // Watson may reuse context objects across requests: never let a previous request's identity carry over.
                _RequestAuthContexts.Remove(ctx);
                ctx.Response.ContentType = "application/json";
                if (IsDashboardWebSocketUpgrade(ctx) && _WebSocketHub != null)
                {
                    AuthContext wsAuth = await _WebSocketHub.AuthorizeUpgradeAsync(ctx).ConfigureAwait(false);
                    if (!wsAuth.IsAuthenticated)
                    {
                        ctx.Response.StatusCode = 401;
                        ctx.Response.ContentType = "application/json";
                        await ctx.Response.Send(_App.Serializer.SerializeJson(new ApiErrorResponse { Error = ApiResultEnum.NotAuthorized, Message = "Authentication required: pass a token as ?token= or in Sec-WebSocket-Protocol, or the REST credential headers" }, false)).ConfigureAwait(false);
                    }
                    return;
                }

                // Central authorization: every registered REST route has an explicit requirement in
                // RouteAuthorizationRegistry, checked here before the route handler runs.
                await AuthorizeRegisteredRouteAsync(ctx).ConfigureAwait(false);
            };

            // Log every API call and apply CORS on every response
            _App.Routes.PostRouting = async (HttpContextBase ctx) =>
            {
                ctx.Timestamp.End = DateTime.UtcNow;
                _Logging.Debug(
                    _Header +
                    ctx.Request.Method + " " +
                    ctx.Request.Url.RawWithoutQuery + " " +
                    ctx.Response.StatusCode + " " +
                    "(" + (ctx.Timestamp.TotalMs.HasValue ? ctx.Timestamp.TotalMs.Value.ToString("F2") : "?") + "ms)");

                ApplyCorsHeaders(ctx);
                await CaptureRequestHistoryAsync(ctx).ConfigureAwait(false);
                _RequestAuthContexts.Remove(ctx);
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
            _WebSocketHub = new ArmadaWebSocketHub(
                _Logging, _Admiral, _Database, _MergeQueue, _Settings, _Git,
                () => { OnStopping?.Invoke(); _TokenSource.Cancel(); },
                (authHeader, tokenHeader, apiKeyHeader) => _AuthenticationService.AuthenticateAsync(authHeader, tokenHeader, apiKeyHeader));
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
            _PlanningSessions.LaunchRouter = _LaunchRouter;
            _ObjectiveRefinementSessions.LaunchRouter = _LaunchRouter;

            _CaptainTools = new CaptainToolService(
                _Logging,
                _Database,
                _HarborConnectionManager,
                RuntimeToolDiscoverySource,
                _Settings.McpPort,
                ArmadaMcpConfigBuilder.ClientHostFor(_Settings.Rest.Hostname));

            _RemoteTunnel.OnHandleRequest = HandleRemoteTunnelRequestAsync;

            // Ask Armada threads: thread store, approval gate / action executor, turn coordinator, and work tracker.
            // Thread events go only to the owner's sockets.
            _CaptainChat = new CaptainChatService(_Database, _RuntimeFactory, _WebSocketHub, _PromptTemplateService, _SessionTokenService, _Settings.McpPort, _Logging, ArmadaMcpConfigBuilder.ClientHostFor(_Settings.Rest.Hostname));
            _CaptainChat.LaunchRouter = _LaunchRouter;
            _AskThreads = new Armada.Core.Services.Ask.AskThreadService(_Database, _Settings, _Logging);
            _AskActions = new Armada.Server.Ask.AskActionService(_Database, _AskThreads, _Settings, _Logging);
            _AskTurns = new Armada.Server.Ask.AskTurnCoordinator(_Database, _AskThreads, _CaptainChat, _SessionTokenService, _PromptTemplateService, _Settings, _Logging);
            _AskTracker = new Armada.Server.Ask.AskWorkTracker(_Database, _AskThreads, _Settings, _Logging);
            _AskThreads.OnUserEvent = (tenantId, userId, eventType, payload) => _WebSocketHub.SendToUser(tenantId, userId, eventType, payload);
            _AskThreads.ActiveTurnResolver = _AskTurns.ActiveTurnId;
            _AskActions.OnWorkLinked = _AskTracker.OnWorkLinkedAsync;
            _AskActions.OnProposalApproved = _AskTurns.StartFollowUpAsync;
            _AskTracker.Narrate = _AskTurns.NarrateAsync;
            _WebSocketHub.EntityChanged += _AskTracker.OnEntityChanged;

            // CLI tool permissions: captains' permission prompts become requests that approvers decide; events reach the
            // request's approvers (global admins and the tenant's tenant admins) and its owner.
            _CliPermissions = new CliPermissionService(_Database, _Settings, _Logging);
            _CliPermissions.AskThreads = _AskThreads;
            _CliPermissions.OnRequestEvent = (eventType, request) => _WebSocketHub.BroadcastCliPermission(eventType, request);
            _AskTurns.CliPermissions = _CliPermissions;
            _AgentLifecycle.SetCliPermissionService(_CliPermissions);
            _WebSocketHub.SetCliPermissionService(_CliPermissions);
            _AskThreads.SessionTokensAvailable = _SessionTokenService != null;

            // Push notifications to the mobile apps: fed from the hub's broadcast points and new Ask proposals; delivery
            // runs on a background worker and never blocks or fails the operation that raised it.
            if (PushTransport == null) PushTransport = new Armada.Core.Services.Push.ExpoPushTransport();
            _PushNotifications = new Armada.Core.Services.Push.PushNotificationService(_Database, _Settings, _Logging, PushTransport);
            _PushDevices = new Armada.Core.Services.Push.PushDeviceService(_Database, _Settings, _Logging);
            _WebSocketHub.SetPushNotificationService(_PushNotifications);
            Armada.Core.Services.Push.PushNotificationService pushNotifications = _PushNotifications;
            _AskThreads.OnProposalCreated = proposal => pushNotifications.OnAskProposalCreated(proposal);
            _PushNotifications.Start();
            _AskTracker.ExpireProposals = async (CancellationToken expireToken) =>
            {
                int expired = await _AskActions.ExpireDueAsync(expireToken).ConfigureAwait(false);
                try { await _CliPermissions.SweepAsync(expireToken).ConfigureAwait(false); }
                catch (Exception sweepEx) when (!(sweepEx is OperationCanceledException)) { _Logging.Warn(_Header + "CLI permission sweep failed: " + sweepEx.Message); }
                return expired;
            };

            RegisterRoutes();
            MarkExperimentalRoutes();
            InitializeDashboard();

            // Register WebSocket route on the main REST server
            _App.WebSocket("/ws", _WebSocketHub.HandleWebSocketAsync);
            _Logging.Debug(_Header + "WebSocket route registered at /ws");

            _App.WebSocket(_Settings.Harbor.LinkPath, _HarborLinkEndpoint.HandleWebSocketAsync);
            _Logging.Debug(_Header + "Harbor link route registered at " + _Settings.Harbor.LinkPath);

            // Watson 7 StartAsync is long-running; Start() binds and returns after
            // scheduling the accept loop.
            _App.Start(_TokenSource.Token);
            _Logging.Info(_Header + "REST API started on port " + _Settings.AdmiralPort);

            // Initialize MCP server
            // HttpListener cannot bind the literal 0.0.0.0 (the MCP listener silently never started in containers
            // configured that way); "*" is the HttpListener spelling of "all interfaces".
            string mcpHostname = String.Equals(_Settings.Rest.Hostname, "0.0.0.0", StringComparison.Ordinal) ? "*" : _Settings.Rest.Hostname;
            string? loopbackLiteralWarning = ArmadaMcpConfigBuilder.LoopbackLiteralHostWarning(_Settings.Rest.Hostname, _Settings.McpPort);
            if (loopbackLiteralWarning != null) _Logging.Warn(_Header + loopbackLiteralWarning);
            _McpServer = new McpHttpServer(mcpHostname, _Settings.McpPort);
            _McpServer.ServerName = ArmadaConstants.ProductName;
            _McpServer.ServerVersion = ArmadaConstants.ProductVersion;
            // Voltaic 2.1.4+ reports a throwing handler as a generic isError result; surface Armada's
            // exception messages (e.g. "captain not found") so agents can react to them, as before.
            _McpServer.IncludeToolExceptionMessages = true;
            _McpServer.RateLimits.ToolCallsPerSecond = _Settings.Mcp.ToolCallsPerSecond;
            _McpServer.AuthenticationHandler = AuthenticateMcpRequestAsync;
            RegisterMcpTools();

            StartMcpListener();
            _Logging.Info(_Header + "MCP server started on port " + _Settings.McpPort);

            _AskTracker.Start(_TokenSource.Token);
            _Logging.Debug(_Header + "Ask Armada work tracker started");

            _RemoteTunnel.Start(_TokenSource.Token);
            _Logging.Info(_Header + "remote tunnel manager started");

            try
            {
                await _PlanningSessions.RecoverSessionsAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Debug(_Header + "planning session recovery completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "planning session recovery error: " + ex.ToString());
            }

            try
            {
                await _PlanningSessions.MaintainSessionsAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Debug(_Header + "planning session maintenance completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "planning session maintenance error: " + ex.ToString());
            }

            try
            {
                await _VesselImportService.RecoverAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Debug(_Header + "vessel import recovery completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "vessel import recovery error: " + ex.ToString());
            }

            try
            {
                await _ObjectiveRefinementSessions.RecoverSessionsAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Debug(_Header + "objective refinement session recovery completed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "objective refinement session recovery error: " + ex.ToString());
            }

            try
            {
                await _ObjectiveRefinementSessions.MaintainSessionsAsync(_TokenSource.Token).ConfigureAwait(false);
                _Logging.Debug(_Header + "objective refinement session maintenance completed");
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

            _VesselHealthService?.Dispose();
            _TokenSource.Cancel();
            _AskTracker?.Stop();
            _PushNotifications?.Dispose();
            _FleetActionRunner?.Stop();
            _RemoteTunnel?.StopAsync().GetAwaiter().GetResult();
            _RemoteDashboardRelay?.DisposeAsync().GetAwaiter().GetResult();
            _McpServer?.Stop();
            _Database?.Dispose();
            _TelemetryHost?.Dispose();
            OnStopping?.Invoke();
        }

        /// <summary>
        /// Every REST route registered on the Admiral's web server as "METHOD template" (static and parameter routes in
        /// both routing groups). Used by the authorization coverage test to prove every route has a declared requirement.
        /// </summary>
        /// <returns>Registered route keys.</returns>
        public List<string> GetRegisteredRestRoutes()
        {
            List<string> routes = new List<string>();
            if (_App == null) return routes;
            foreach (WatsonWebserver.Core.Routing.RoutingGroup group in new WatsonWebserver.Core.Routing.RoutingGroup[] { _App.Routes.PreAuthentication, _App.Routes.PostAuthentication })
            {
                foreach (WatsonWebserver.Core.Routing.StaticRoute route in group.Static.GetAll())
                    routes.Add(route.Method.ToString().ToUpperInvariant() + " " + (route.Path.Length > 1 ? route.Path.TrimEnd('/') : route.Path));
                foreach (WatsonWebserver.Core.Routing.ParameterRoute route in group.Parameter.GetAll())
                    routes.Add(route.Method.ToString().ToUpperInvariant() + " " + route.Path);
            }

            return routes;
        }

        /// <summary>
        /// Every REST route registered on the Admiral's web server with its OpenAPI metadata (summary, tags, request and
        /// response type names). Used by the API surface generator (docs/API_SURFACE_1.0.md) and the API contract test.
        /// </summary>
        /// <returns>Route descriptors in registration order.</returns>
        public List<RestRouteDescriptor> GetRestRouteDescriptors()
        {
            List<RestRouteDescriptor> routes = new List<RestRouteDescriptor>();
            if (_App == null) return routes;
            foreach (WatsonWebserver.Core.Routing.RoutingGroup group in new WatsonWebserver.Core.Routing.RoutingGroup[] { _App.Routes.PreAuthentication, _App.Routes.PostAuthentication })
            {
                foreach (WatsonWebserver.Core.Routing.StaticRoute route in group.Static.GetAll())
                    routes.Add(RestRouteDescriptor.From(route.Method.ToString().ToUpperInvariant(), route.Path.Length > 1 ? route.Path.TrimEnd('/') : route.Path, route.OpenApiMetadata));
                foreach (WatsonWebserver.Core.Routing.ParameterRoute route in group.Parameter.GetAll())
                    routes.Add(RestRouteDescriptor.From(route.Method.ToString().ToUpperInvariant(), route.Path, route.OpenApiMetadata));
            }

            return routes;
        }

        #endregion

        #region Private-Methods

        private void StartMcpListener()
        {
            // McpHttpServer.StartAsync binds its HttpListener synchronously and then loops accepting requests, so the
            // returned task is already faulted (or completed) when the bind failed, for example because the port is in
            // use. Running it unobserved on a background task used to swallow that failure: the Admiral logged "MCP
            // server started" and ran without MCP. Fail startup instead, the same way a REST bind failure does.
            Task listen;
            try
            {
                listen = _McpServer.StartAsync(_TokenSource.Token);
            }
            catch (Exception ex) when (ex is System.Net.HttpListenerException || ex is System.Net.Sockets.SocketException || ex is InvalidOperationException)
            {
                throw new ListenerBindException("MCP", _Settings.Rest.Hostname, _Settings.McpPort, "MCP server could not listen on " + _Settings.Rest.Hostname + ":" + _Settings.McpPort + ": " + ex.Message, ex);
            }

            if (listen.IsCompleted)
            {
                Exception? cause = listen.Exception?.GetBaseException();
                throw new ListenerBindException("MCP", _Settings.Rest.Hostname, _Settings.McpPort, "MCP server could not listen on " + _Settings.Rest.Hostname + ":" + _Settings.McpPort + ": "
                    + (cause != null ? cause.Message : "the listener stopped immediately"), cause);
            }

            _McpListenTask = listen.ContinueWith(t =>
            {
                if (t.IsFaulted && !_TokenSource.IsCancellationRequested)
                {
                    _Logging.Warn(_Header + "MCP listener on port " + _Settings.McpPort + " stopped: " + t.Exception?.GetBaseException().ToString());
                }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Mark every route listed in <see cref="Armada.Core.ApiSurface.ExperimentalSurface"/> in the OpenAPI document: the
        /// summary gets the experimental prefix and the route gets the Experimental tag.
        /// </summary>
        private void MarkExperimentalRoutes()
        {
            foreach (WatsonWebserver.Core.Routing.RoutingGroup group in new WatsonWebserver.Core.Routing.RoutingGroup[] { _App.Routes.PreAuthentication, _App.Routes.PostAuthentication })
            {
                foreach (WatsonWebserver.Core.Routing.StaticRoute route in group.Static.GetAll())
                    MarkExperimentalRoute(route.Method.ToString(), route.Path.Length > 1 ? route.Path.TrimEnd('/') : route.Path, route.OpenApiMetadata);
                foreach (WatsonWebserver.Core.Routing.ParameterRoute route in group.Parameter.GetAll())
                    MarkExperimentalRoute(route.Method.ToString(), route.Path, route.OpenApiMetadata);
            }
        }

        private static void MarkExperimentalRoute(string method, string template, OpenApiRouteMetadata? metadata)
        {
            if (metadata == null) return;
            if (!Armada.Core.ApiSurface.ExperimentalSurface.IsExperimentalRoute(method, template)) return;
            metadata.Summary = Armada.Core.ApiSurface.ExperimentalSurface.Mark(metadata.Summary);
            if (metadata.Tags == null) metadata.Tags = new List<string>();
            if (!metadata.Tags.Contains(Armada.Core.ApiSurface.ExperimentalSurface.Tag)) metadata.Tags.Add(Armada.Core.ApiSurface.ExperimentalSurface.Tag);
        }

        /// <summary>
        /// Resolve the route template Watson will dispatch this request to (static routes first, then parameter routes,
        /// mirroring Watson's own order), or null when the request falls through to the default route (dashboard files).
        /// </summary>
        private string? ResolveRegisteredRouteTemplate(HttpContextBase ctx)
        {
            string requestPath = ctx.Request.Url.RawWithoutQuery ?? "/";
            string normalizedPath = ctx.Request.Url.NormalizedRawWithoutQuery ?? requestPath;
            foreach (WatsonWebserver.Core.Routing.RoutingGroup group in new WatsonWebserver.Core.Routing.RoutingGroup[] { _App.Routes.PreAuthentication, _App.Routes.PostAuthentication })
            {
                if (group.Static.MatchNormalized(ctx.Request.Method, normalizedPath, out WatsonWebserver.Core.Routing.StaticRoute staticRoute) != null && staticRoute != null)
                    return staticRoute.Path;
                if (group.Parameter.Match(ctx.Request.Method, requestPath, out System.Collections.Specialized.NameValueCollection _, out WatsonWebserver.Core.Routing.ParameterRoute parameterRoute) != null && parameterRoute != null)
                    return parameterRoute.Path;
            }

            return null;
        }

        /// <summary>
        /// Enforce the declared requirement of the route this request will reach. Undeclared routes fail closed to
        /// AdminOnly. Sends 401/403 and ends the request when the caller is not authorized.
        /// </summary>
        private async Task AuthorizeRegisteredRouteAsync(HttpContextBase ctx)
        {
            if (ctx.Request.Method == WatsonWebserver.Core.HttpMethod.OPTIONS) return;
            string? template = ResolveRegisteredRouteTemplate(ctx);
            if (template == null) return;

            string method = ctx.Request.Method.ToString().ToUpperInvariant();
            AuthorizationRequirement requirement;
            if (!RouteAuthorizationRegistry.TryGetByTemplate(method, template, out AuthorizationRequirement? declared) || declared == null)
            {
                _Logging.Warn(_Header + "route " + method + " " + template + " has no declared authorization requirement; treating it as AdminOnly");
                requirement = new AuthorizationRequirement("Undeclared", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            }
            else
            {
                requirement = declared;
            }

            if (requirement.Level == PermissionLevel.NoAuthRequired) return;

            // A client address locked out after repeated failed authentications cannot present a guessable credential
            // (bearer token, API key) until the lockout ends.
            if (AuthRoutes.PresentsGuessableCredential(ctx))
            {
                TimeSpan? retryAfter = _LoginRateLimiter.CheckAddress(AuthRoutes.ClientAddress(ctx));
                if (retryAfter != null)
                {
                    ApiErrorResponse limited = AuthRoutes.TooManyAttempts(ctx, retryAfter.Value);
                    ApplyCorsHeaders(ctx);
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.Send(_App.Serializer.SerializeJson(limited, false)).ConfigureAwait(false);
                    return;
                }
            }

            AuthContext auth = await AuthenticateRequestAsync(ctx).ConfigureAwait(false);
            if (!_AuthorizationService.IsAuthorized(auth, requirement))
            {
                ctx.Response.StatusCode = auth.IsAuthenticated ? 403 : 401;
                ctx.Response.ContentType = "application/json";
                ApiErrorResponse denied = new ApiErrorResponse
                {
                    Error = auth.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized,
                    Message = auth.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                };
                await ctx.Response.Send(_App.Serializer.SerializeJson(denied, false)).ConfigureAwait(false);
                return;
            }
        }

        private async Task<AuthContext> AuthenticateRequestAsync(WatsonWebserver.Core.HttpContextBase ctx)
        {
            // The central authorization check in PreRouting authenticates once per request; route handlers reuse it.
            if (_RequestAuthContexts.TryGetValue(ctx, out AuthContext? cached) && cached != null) return cached;

            string? authHeader = ctx.Request.Headers.Get("Authorization");
            string? tokenHeader = ctx.Request.Headers.Get("X-Token");
            string? apiKeyHeader = ctx.Request.Headers.Get("X-Api-Key");
            AuthContext result = await _AuthenticationService.AuthenticateAsync(authHeader, tokenHeader, apiKeyHeader).ConfigureAwait(false);
            if (!result.IsAuthenticated && (!String.IsNullOrEmpty(authHeader) || !String.IsNullOrEmpty(apiKeyHeader)))
                _LoginRateLimiter?.RecordAddressFailure(AuthRoutes.ClientAddress(ctx));

            // Thread-scoped Ask Armada tokens and mission-scoped captain tokens are minted for a captain's MCP connection
            // only; never accept them on REST.
            if (!String.IsNullOrEmpty(result.AskThreadId) || !String.IsNullOrEmpty(result.MissionId)) result = new AuthContext();
            _RequestAuthContexts.Remove(ctx);
            _RequestAuthContexts.Add(ctx, result);
            return result;
        }

        private static bool IsDashboardWebSocketUpgrade(HttpContextBase ctx)
        {
            string path = ctx.Request.Url.RawWithoutQuery ?? String.Empty;
            if (!String.Equals(path.TrimEnd('/'), "/ws", StringComparison.OrdinalIgnoreCase)) return false;
            string? upgrade = ctx.Request.Headers.Get("Upgrade");
            return !String.IsNullOrEmpty(upgrade) && upgrade.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<AuthenticationResult> AuthenticateMcpRequestAsync(System.Net.HttpListenerRequest request)
        {
            // MCP is authenticated by default (Decision D2). A presented credential must be valid; its tenant, user, and
            // role claims flow into the tool handlers through Voltaic's ambient RpcCallContext so MCP tools are scoped
            // per caller, matching the REST API. A request with no credential is accepted only when
            // Mcp.AllowUnauthenticatedLoopback is on, the MCP listener is bound to a loopback hostname, and the caller
            // connects from loopback; it then runs as the default tenant's tenant admin (the local Claude Code setup).
            string? authHeader = request.Headers["Authorization"];
            string? tokenHeader = request.Headers["X-Token"];
            string? apiKeyHeader = request.Headers["X-Api-Key"];
            bool presented = !String.IsNullOrEmpty(authHeader) || !String.IsNullOrEmpty(tokenHeader) || !String.IsNullOrEmpty(apiKeyHeader);

            if (presented)
            {
                string? address = request.RemoteEndPoint?.Address?.ToString();
                bool guessable = !String.IsNullOrEmpty(authHeader) || !String.IsNullOrEmpty(apiKeyHeader);
                if (guessable)
                {
                    TimeSpan? retryAfter = _LoginRateLimiter?.CheckAddress(address);
                    if (retryAfter != null)
                    {
                        string seconds = LoginRateLimiter.ToRetryAfterSeconds(retryAfter.Value);
                        AuthenticationResult limited = new AuthenticationResult
                        {
                            IsAuthenticated = false,
                            StatusCode = 429,
                            ErrorMessage = "Too many failed attempts; try again in " + seconds + " seconds"
                        };
                        limited.Headers["Retry-After"] = seconds;
                        return limited;
                    }
                }

                AuthContext? ctx = null;
                try
                {
                    ctx = await _AuthenticationService.AuthenticateAsync(authHeader, tokenHeader, apiKeyHeader).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "MCP caller authentication error: " + ex.Message);
                }

                if (ctx == null || !ctx.IsAuthenticated || String.IsNullOrEmpty(ctx.UserId))
                {
                    if (guessable) _LoginRateLimiter?.RecordAddressFailure(address);
                    return AuthenticationResult.BearerChallenge(null, "invalid_token", "Invalid or expired credential", "Invalid or expired credential.");
                }

                AuthenticationResult result = new AuthenticationResult { IsAuthenticated = true, Principal = ctx.UserId };
                result.Claims = new Dictionary<string, string>
                {
                    ["tenantId"] = ctx.TenantId ?? String.Empty,
                    ["userId"] = ctx.UserId ?? String.Empty,
                    ["isAdmin"] = ctx.IsAdmin ? "true" : "false",
                    ["isTenantAdmin"] = ctx.IsTenantAdmin ? "true" : "false",
                    ["authMethod"] = ctx.AuthMethod ?? "Mcp"
                };

                // A thread-scoped token marks every tool call of this request as an Ask Armada thread call, which
                // the tool gate turns into a proposal unless the tool is read-only or the thread auto-approves.
                if (!String.IsNullOrEmpty(ctx.AskThreadId)) result.Claims["askThreadId"] = ctx.AskThreadId!;

                // A mission-scoped token marks the caller as that mission's captain (O-20 / O-04).
                if (!String.IsNullOrEmpty(ctx.MissionId)) result.Claims["missionId"] = ctx.MissionId!;
                return result;
            }

            if (IsUnauthenticatedMcpAllowed(request.RemoteEndPoint?.Address))
                return new AuthenticationResult { IsAuthenticated = true };

            return AuthenticationResult.BearerChallenge(null, null, null, "Authentication required: present Authorization: Bearer <token>, X-Token, or X-Api-Key. Unauthenticated MCP is allowed only on a loopback-bound listener with Mcp.AllowUnauthenticatedLoopback enabled.");
        }

        /// <summary>
        /// Whether an MCP request without credentials is accepted: Mcp.AllowUnauthenticatedLoopback is on, the listener
        /// hostname is loopback, and the remote address is loopback.
        /// </summary>
        /// <param name="remoteAddress">Caller address.</param>
        /// <returns>True when allowed.</returns>
        internal bool IsUnauthenticatedMcpAllowed(System.Net.IPAddress? remoteAddress)
        {
            if (!_Settings.Mcp.AllowUnauthenticatedLoopback) return false;
            if (!DefaultCredentialService.IsLoopbackHostname(_Settings.Rest.Hostname)) return false;
            return remoteAddress != null && System.Net.IPAddress.IsLoopback(remoteAddress);
        }

        private async Task EnforceDefaultCredentialPolicyAsync()
        {
            DefaultCredentialService defaults = new DefaultCredentialService(_Database, _Logging);
            await defaults.ApplyInitialAdminPasswordAsync(Environment.GetEnvironmentVariable(DefaultCredentialService.InitialAdminPasswordEnvironmentVariable)).ConfigureAwait(false);
            await defaults.RetireDefaultBearerTokenAsync().ConfigureAwait(false);

            List<string> inUse = await defaults.GetDefaultsInUseAsync().ConfigureAwait(false);
            if (inUse.Count == 0) return;

            string summary = String.Join("; ", inUse);
            if (DefaultCredentialService.IsLoopbackHostname(_Settings.Rest.Hostname))
            {
                _Logging.Warn(_Header + "default credentials are in use (" + summary + "); change the admin password before exposing this Admiral beyond localhost");
                return;
            }

            if (_Settings.AllowDefaultCredentialsOnNetwork)
            {
                _Logging.Warn(_Header + "listening on non-loopback hostname " + _Settings.Rest.Hostname + " with default credentials in use (" + summary + ") because AllowDefaultCredentialsOnNetwork is set");
                return;
            }

            string message =
                "Refusing to listen on non-loopback hostname '" + _Settings.Rest.Hostname + "' while default credentials are in use (" + summary + "). " +
                "Set the " + DefaultCredentialService.InitialAdminPasswordEnvironmentVariable + " environment variable (first start), or start on localhost and change the admin password " +
                "(dashboard or PUT /api/v1/account/password), or set AllowDefaultCredentialsOnNetwork to true to accept the risk.";
            _Logging.Warn(_Header + message);
            throw new UnsafeListenerConfigurationException(message, _Settings.Rest.Hostname);
        }

        private async Task EnsureLocalSecretsAsync()
        {
            bool generatedApiKey = false;
            bool generatedSessionKey = false;

            if (String.IsNullOrEmpty(_Settings.ApiKey))
            {
                _Settings.ApiKey = "ak_" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                generatedApiKey = true;
            }

            if (String.IsNullOrEmpty(_Settings.SessionTokenEncryptionKey))
            {
                // A new SessionTokenService with no key generates a random one; persist it so tokens survive restarts.
                _Settings.SessionTokenEncryptionKey = new SessionTokenService().GetKeyBase64();
                generatedSessionKey = true;
            }

            if (!generatedApiKey && !generatedSessionKey) return;

            try
            {
                await _Settings.SaveAsync().ConfigureAwait(false);
                if (generatedApiKey) _Logging.Info(_Header + "generated local API key for CLI authentication and saved to settings");
                if (generatedSessionKey) _Logging.Info(_Header + "generated session token encryption key and saved to settings");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "generated local secrets but could not persist settings (sign-ins will not survive a restart): " + ex.ToString());
            }
        }

        private async Task SeedSyntheticAdminAsync()
        {
            _Logging.Debug(_Header + "seeding synthetic admin identity for API key");

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
                // The system identity backs the local API key only; password login is refused for it, and its stored
                // password is a random value nobody knows.
                systemUser.PasswordSha256 = UserMaster.ComputePasswordHash(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
                systemUser.IsAdmin = true;
                systemUser.IsTenantAdmin = true;
                systemUser.IsProtected = true;
                await _Database.Users.CreateAsync(systemUser).ConfigureAwait(false);
            }

            _Logging.Debug(_Header + "synthetic admin identity ready");
        }

        private void RegisterRoutes()
        {
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate = AuthenticateRequestAsync;

            // Authentication & identity
            new AuthRoutes(_SessionTokenService, _AuthenticationService, _Database, _Settings, _JsonOptions, new DefaultCredentialService(_Database, _Logging), _LoginRateLimiter)
                .Register(_App, authenticate, _AuthorizationService);

            // Tenants, users, credentials
            new TenantRoutes(_Database, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Status, health, doctor, settings, server control
            SlotManager slotManager = new SlotManager(Path.Combine(_Settings.DataDirectory, "bin"), retentionCount: _Settings.RebuildSlotRetentionCount);
            ServerRebuildService rebuildService = new ServerRebuildService(_Database, _Settings, slotManager, new LocalHostCommandExecutor(), _Logging, () => Stop(), _HarborConnectionManager);
            new StatusRoutes(_Database, _Settings, _Admiral, () => Stop(), _StartUtc, _JsonOptions, _Logging, slotManager, rebuildService, _RemoteTunnel.GetStatus, _RemoteTunnel.ReloadAsync)
                .Register(_App, authenticate, _AuthorizationService);

            // Fleets
            new FleetRoutes(_Database, EmitEventAsync, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Vessels
            VesselContextService vesselContextService = new VesselContextService(_Database, _RuntimeFactory, _Docks, _PromptTemplateService, _Logging);
            vesselContextService.LaunchRouter = _LaunchRouter;
            new VesselRoutes(_Database, _VesselReadinessService, _LandingPreviewService, EmitEventAsync, _JsonOptions, _Docks, vesselContextService, _Git, _Settings, _VesselService, _ManualLandingReconciler)
                .Register(_App, authenticate, _AuthorizationService);

            // Vessel import (bulk onboarding)
            new VesselImportRoutes(_VesselImportService, _FleetCategorizationService, _Logging)
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
            new AskRoutes(_CaptainChat, _JsonOptions, _AskThreads, _AskTurns, _AskActions)
                .Register(_App, authenticate, _AuthorizationService);

            // Needs-you inbox
            new InboxRoutes(new InboxService(_Database, _Logging, _Settings), _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // CLI tool permissions (requests, decisions, rules, captain and thread policies)
            new CliPermissionRoutes(_CliPermissions, _JsonOptions)
                .Register(_App, authenticate, _AuthorizationService);

            // Push notification devices (mobile apps)
            new PushRoutes(_PushDevices, _PushNotifications!, _JsonOptions)
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

            new MemoryRoutes(new MemoryService(_Database, _Logging))
                .Register(_App, authenticate, _AuthorizationService);

            // Harbors (host runners)
            new HarborRoutes(_HarborService, _HarborConnectionManager, _Database)
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
            new VoyageRoutes(_Database, _Admiral, EmitEventAsync, _WebSocketHub, _Logging, _ObjectiveService, _JsonOptions, _MissionService)
                .Register(_App, authenticate, _AuthorizationService);

            // Missions
            new MissionRoutes(_Database, _Admiral, _MissionService, _Settings, _Git, _LandingService, _LandingPreviewService, _GitHubIntegrationService, EmitEventAsync, EmitMissionStatusChangedAsync, _MissionLanding.HandleMissionCompleteAsync, _WebSocketHub, _Logging, _JsonOptions)
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

            // Fleet actions and runs
            new FleetActionRoutes(_FleetActionService, _JsonOptions, _Logging)
                .Register(_App, authenticate, _AuthorizationService);

            // Vessel health
            new VesselHealthRoutes(_VesselHealthService)
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
                    _Logging.Debug(_Header + "dashboard serving from external path: " + path);
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
                _Logging.Debug(_Header + "dashboard auto-detected at: " + dashboardInData);
                return;
            }

            // Auto-detect: check next to the server executable. Assembly.Location is empty in a single-file publish
            // (the packaged servers), where the application base directory is the executable's folder.
            string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? exeDir = !String.IsNullOrEmpty(assemblyLocation)
                ? Path.GetDirectoryName(assemblyLocation)
                : AppContext.BaseDirectory;
            if (!String.IsNullOrEmpty(exeDir))
            {
                string dashboardNextToExe = Path.Combine(exeDir, "dashboard");
                if (Directory.Exists(dashboardNextToExe) && File.Exists(Path.Combine(dashboardNextToExe, "index.html")))
                {
                    Dashboard.StaticFileHandler.SetExternalPath(dashboardNextToExe);
                    _Logging.Debug(_Header + "dashboard auto-detected at: " + dashboardNextToExe);
                    return;
                }
            }

            // Fallback: use embedded wwwroot resources (legacy dashboard, not the React dashboard)
            _Logging.Debug(_Header + "using embedded legacy dashboard because no external React dashboard was found");
        }

        private static void ApplyCorsHeaders(HttpContextBase ctx)
        {
            if (!ctx.Response.Headers.AllKeys.Contains("Access-Control-Allow-Origin"))
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            if (!ctx.Response.Headers.AllKeys.Contains("Access-Control-Allow-Methods"))
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, PATCH, OPTIONS");
            if (!ctx.Response.Headers.AllKeys.Contains("Access-Control-Allow-Headers"))
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Api-Key, X-Token, Authorization");
            if (!ctx.Response.Headers.AllKeys.Contains("Access-Control-Expose-Headers"))
                ctx.Response.Headers.Add("Access-Control-Expose-Headers", "Retry-After");
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
                return stringValue.StartsWith('?') ? stringValue.Substring(1) : stringValue;
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
            UrlPathCanonicalizationResult canonical = UrlPathCanonicalizer.Canonicalize(ctx.Request.Url.RawWithoutQuery);

            // Redirect root to dashboard
            if (String.IsNullOrEmpty(ctx.Request.Url.RawWithoutQuery) || (canonical.Success && canonical.Segments.Count == 0))
            {
                ctx.Response.StatusCode = 302;
                ctx.Response.Headers.Add("Location", "/dashboard");
                await ctx.Response.Send().ConfigureAwait(false);
                return;
            }

            // Serve dashboard static files
            if (canonical.StartsWithSegments("dashboard"))
            {
                if (Dashboard.StaticFileHandler.TryGetFile(canonical.Path, out byte[] content, out string contentType))
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
            if (canonical.Segments.Count > 1 && (canonical.StartsWithSegments("assets") || canonical.StartsWithSegments("img")))
            {
                string dashPath = "/dashboard" + canonical.Path;
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
            await ctx.Response.Send(_App.Serializer.SerializeJson(new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Not found" }, false)).ConfigureAwait(false);
        }

        /// <summary>
        /// Adapts Armada's JsonElement-based tool handler to Voltaic's RpcParameters-based
        /// RegisterTool signature, so the tool handlers themselves do not need to change.
        /// </summary>
        private void RegisterAdaptedTool(string name, string description, object inputSchema, Func<System.Text.Json.JsonElement?, Task<object>> handler)
        {
            // Every tool goes through the Ask Armada gate: calls made with a thread-scoped token become proposals unless
            // read-only or auto-approved; all other calls run unchanged. The original handler is kept for in-process
            // execution of approved proposals and quick actions.
            handler = AuthorizeMcpTool(name, handler);
            handler = _AskActions.WrapTool(name, handler);
            lock (_RegisteredMcpToolsLock)
            {
                _RegisteredMcpTools.Add(name);
                _RegisteredMcpToolDescriptors.Add(new CaptainToolSummary
                {
                    Name = name,
                    Description = description,
                    InputSchemaJson = System.Text.Json.JsonSerializer.Serialize(inputSchema),
                    RegistrationSource = "Armada MCP"
                });
            }
            _McpServer.RegisterTool(name, description, inputSchema, async (RpcParameters? parameters, CancellationToken callToken) =>
            {
                System.Text.Json.JsonElement? args = null;
                if (parameters != null && parameters.HasValue && !string.IsNullOrEmpty(parameters.RawJson))
                {
                    using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(parameters.RawJson);
                    args = doc.RootElement.Clone();
                }

                // The call's token (cancelled when the client cancels or drops the call) flows to handlers that wait,
                // such as cli_permission_prompt. Set inside this async lambda, so it never leaks to the caller's flow.
                McpToolHelpers.SetCallToken(callToken);
                return await handler(args).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Wrap a tool handler with its declared requirement from <see cref="McpToolAuthorizationRegistry"/>, checked
        /// against the caller of each call (the MCP request's credential, the loopback default context, or the Ask
        /// Armada caller an approved proposal runs as). Undeclared tools fail closed to AdminOnly. A refused call returns
        /// <see cref="McpToolError"/> with <see cref="McpToolErrorCodeEnum.Forbidden"/>.
        /// </summary>
        private Func<System.Text.Json.JsonElement?, Task<object>> AuthorizeMcpTool(string name, Func<System.Text.Json.JsonElement?, Task<object>> handler)
        {
            if (!McpToolAuthorizationRegistry.TryGet(name, out AuthorizationRequirement? _))
                _Logging.Warn(_Header + "MCP tool " + name + " has no declared authorization requirement; treating it as AdminOnly");
            AuthorizationRequirement requirement = McpToolAuthorizationRegistry.GetOrDefault(name);

            return async (System.Text.Json.JsonElement? args) =>
            {
                AuthContext caller = McpToolHelpers.ResolveCallerContext();
                if (!_AuthorizationService.IsAuthorized(caller, requirement)
                    && !await McpMissionScope.AllowsAsync(_Database, caller, name, args).ConfigureAwait(false))
                {
                    string needed = requirement.Level == PermissionLevel.AdminOnly
                        ? "an admin credential"
                        : requirement.Level == PermissionLevel.TenantAdmin ? "a tenant admin or admin credential" : "an authenticated caller";
                    // A typed refusal (ErrorCode Forbidden), like every other tool error, so clients and the Ask
                    // Armada executor branch on the code rather than the message text.
                    return McpToolError.Forbidden("Tool " + name + " requires " + needed + ".");
                }

                return await handler(args).ConfigureAwait(false);
            };
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
                _HarborService,
                _VesselService,
                _VesselImportService,
                _FleetActionService,
                _VesselHealthService,
                _FleetCategorizationService,
                null,
                _CliPermissions);
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

        private Task EmitEventAsync(string eventType, string message,
            string? entityType = null, string? entityId = null,
            string? captainId = null, string? missionId = null,
            string? vesselId = null, string? voyageId = null)
        {
            return EmitEventCoreAsync(eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId, null);
        }

        /// <summary>
        /// Record and broadcast <c>mission.status_changed</c> with the new and previous status as typed fields (event
        /// Payload JSON and WebSocket <c>status</c> / <c>previousStatus</c>), not only in the message text.
        /// </summary>
        private Task EmitMissionStatusChangedAsync(Mission mission, MissionStatusEnum? previousStatus, string message)
        {
            MissionStatusChangedPayload payload = new MissionStatusChangedPayload();
            payload.Status = mission.Status;
            payload.PreviousStatus = previousStatus;
            return EmitEventCoreAsync("mission.status_changed", message, "mission", mission.Id, mission.CaptainId, mission.Id, mission.VesselId, mission.VoyageId, payload);
        }

        private async Task EmitEventCoreAsync(string eventType, string message,
            string? entityType, string? entityId,
            string? captainId, string? missionId,
            string? vesselId, string? voyageId,
            MissionStatusChangedPayload? statusPayload)
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
                if (statusPayload != null) evt.Payload = System.Text.Json.JsonSerializer.Serialize(statusPayload, _JsonOptions);
                await _Database.Events.CreateAsync(evt).ConfigureAwait(false);

                // Broadcast to the WebSocket clients of the entity's tenant
                if (_WebSocketHub != null)
                {
                    string? tenantId = await ResolveEventTenantAsync(entityType, entityId, captainId, missionId, vesselId, voyageId).ConfigureAwait(false);
                    object data = statusPayload == null
                        ? (object)new
                        {
                            entityType = entityType,
                            entityId = entityId,
                            captainId = captainId,
                            missionId = missionId,
                            vesselId = vesselId,
                            voyageId = voyageId
                        }
                        : new
                        {
                            entityType = entityType,
                            entityId = entityId,
                            captainId = captainId,
                            missionId = missionId,
                            vesselId = vesselId,
                            voyageId = voyageId,
                            status = statusPayload.Status.ToString(),
                            previousStatus = statusPayload.PreviousStatus?.ToString()
                        };
                    _WebSocketHub.BroadcastToTenant(tenantId, eventType, message, data);
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

        private async Task<string?> ResolveEventTenantAsync(string? entityType, string? entityId, string? captainId, string? missionId, string? vesselId, string? voyageId)
        {
            try
            {
                if (!String.IsNullOrEmpty(missionId))
                {
                    Mission? mission = await _Database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(mission?.TenantId)) return mission!.TenantId;
                }

                if (!String.IsNullOrEmpty(voyageId))
                {
                    Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId).ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(voyage?.TenantId)) return voyage!.TenantId;
                }

                if (!String.IsNullOrEmpty(vesselId))
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(vessel?.TenantId)) return vessel!.TenantId;
                }

                if (!String.IsNullOrEmpty(captainId))
                {
                    Captain? captain = await _Database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(captain?.TenantId)) return captain!.TenantId;
                }

                if (!String.IsNullOrEmpty(entityId))
                {
                    string type = (entityType ?? String.Empty).ToLowerInvariant();
                    if (type == "fleet")
                    {
                        Fleet? fleet = await _Database.Fleets.ReadAsync(entityId).ConfigureAwait(false);
                        return fleet?.TenantId;
                    }

                    if (type == "dock")
                    {
                        Dock? dock = await _Database.Docks.ReadAsync(entityId).ConfigureAwait(false);
                        return dock?.TenantId;
                    }

                    if (type == "signal")
                    {
                        Signal? signal = await _Database.Signals.ReadAsync(entityId).ConfigureAwait(false);
                        return signal?.TenantId;
                    }

                    if (type == "merge-entry" || type == "merge_entry" || type == "mergeentry")
                    {
                        MergeEntry? entry = await _Database.MergeEntries.ReadAsync(entityId).ConfigureAwait(false);
                        return entry?.TenantId;
                    }

                    if (type == "planning-session")
                    {
                        PlanningSession? session = await _Database.PlanningSessions.ReadAsync(entityId).ConfigureAwait(false);
                        return session?.TenantId;
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "event tenant resolution failed: " + ex.Message);
            }

            return null;
        }

        private async Task HealthCheckLoopAsync(CancellationToken token)
        {
            // Reset captains left in Working state with dead processes from previous server run
            try
            {
                await _Admiral.CleanupStaleCaptainsAsync(token).ConfigureAwait(false);
                _Logging.Debug(_Header + "startup stale captain cleanup completed");
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
                _Logging.Debug(_Header + "startup health check completed");
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

                    // Vessel health schedule: start an evaluation per tenant every RepositoryHealth.IntervalMinutes
                    // (0 disables). Elapsed time is tracked from the last evaluation job, so restarts do not re-trigger.
                    try { await _VesselHealthService.RunScheduleAsync(token).ConfigureAwait(false); }
                    catch (Exception healthEx) when (!(healthEx is OperationCanceledException)) { _Logging.Warn(_Header + "vessel health schedule error: " + healthEx.Message); }

                    // Autonomous mission recovery: classify failures, open/link incidents, dispatch bounded
                    // rescue missions, and advance incidents Open -> Mitigated -> Closed from mission evidence.
                    try { await _MissionRecovery.MaintainAsync(token).ConfigureAwait(false); }
                    catch (Exception recoveryEx) { _Logging.Warn(_Header + "mission recovery error: " + recoveryEx.Message); }

                    // Fleet actions: follow Mission-run voyages and dispatch paced pending targets.
                    try { await _FleetActionRunner.SyncMissionRunsAsync(token).ConfigureAwait(false); }
                    catch (Exception fleetActionEx) when (!(fleetActionEx is OperationCanceledException)) { _Logging.Warn(_Header + "fleet action sync error: " + fleetActionEx.Message); }

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

                    // Run data expiry every 100 health check cycles (100 x HeartbeatIntervalSeconds plus loop time; about 17 minutes at the default 10 s)
                    if (_HealthCheckCycles % 100 == 0)
                    {
                        // DataExpiryService talks to SQLite directly; on server providers it would throw and skip the
                        // rest of this block, so it only runs for SQLite and each step is isolated.
                        if (_Settings.Database.Type == DatabaseTypeEnum.Sqlite)
                        {
                            try { await _DataExpiry.PurgeExpiredDataAsync(token).ConfigureAwait(false); }
                            catch (Exception expiryEx) when (!(expiryEx is OperationCanceledException)) { _Logging.Warn(_Header + "data expiry error: " + expiryEx.Message); }
                        }

                        await PurgeExpiredRequestHistoryAsync(token).ConfigureAwait(false);

                        // Retention: Ask threads (archive/delete), finished jobs, finished import batches.
                        try { await _Retention.PruneAsync(token).ConfigureAwait(false); }
                        catch (Exception retentionEx) when (!(retentionEx is OperationCanceledException)) { _Logging.Warn(_Header + "retention pruning error: " + retentionEx.Message); }

                        // Fleet actions: prune finished runs older than FleetActions.RunRetentionDays.
                        try { await _FleetActionRunner.PruneExpiredRunsAsync(token).ConfigureAwait(false); }
                        catch (Exception pruneEx) when (!(pruneEx is OperationCanceledException)) { _Logging.Warn(_Header + "fleet action run pruning error: " + pruneEx.Message); }
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
                    _Logging.Debug(_Header + "purged " + deleted + " expired request history records");
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
