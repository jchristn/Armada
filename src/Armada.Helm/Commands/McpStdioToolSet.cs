namespace Armada.Helm.Commands
{
    using System;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using Armada.Server.Mcp;

    /// <summary>
    /// The services behind <c>armada mcp stdio</c> and the registration of its tools. The stdio server runs standalone
    /// against the database (no Admiral process), and registers the same tool names as the Admiral's HTTP MCP server.
    /// Tools that need the Admiral process answer with a typed <c>Unavailable</c> error instead of being left out:
    /// <c>stop_server</c> (there is no Admiral to stop) and the fleet action tools (runs are executed and tracked by the
    /// Admiral's fleet action runner).
    /// </summary>
    public sealed class McpStdioToolSet : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Error message of <c>stop_server</c> over stdio.
        /// </summary>
        public const string StopServerUnavailableMessage = "stop_server needs the Admiral process; armada mcp stdio runs standalone. Stop the Admiral with 'armada server stop' or call stop_server on the Admiral's HTTP MCP endpoint.";

        #endregion

        #region Private-Members

        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly DatabaseDriver _Database;
        private readonly Armada.Core.Services.Health.VesselHealthService _VesselHealthService;
        private readonly IGitService _Git;
        private readonly IDockService _Docks;
        private readonly IPromptTemplateService _PromptTemplates;
        private readonly IAdmiralService _Admiral;
        private readonly AgentLifecycleHandler _AgentLifecycle;
        private readonly IMergeQueueService _MergeQueue;
        private readonly LandingService _Landing;
        private readonly CheckRunService _CheckRuns;
        private readonly ObjectiveService _Objectives;
        private readonly PlanningSessionCoordinator _PlanningSessions;
        private readonly ObjectiveRefinementCoordinator _RefinementSessions;
        private readonly ReleaseService _Releases;
        private readonly DeploymentService _Deployments;
        private readonly RunbookService _Runbooks;
        private readonly ModelEndpointService _ModelEndpoints;
        private readonly HarborService _Harbors;
        private readonly IVesselService _Vessels;
        private readonly IVesselImportService _VesselImport;
        private readonly IFleetCategorizationService _FleetCategorization;
        private readonly CaptainToolService _CaptainTools;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build the services the stdio tools use.
        /// </summary>
        /// <param name="settings">Armada settings.</param>
        /// <param name="logging">Logging module (file only; stdout is the MCP transport).</param>
        /// <param name="database">Initialized database driver.</param>
        public McpStdioToolSet(ArmadaSettings settings, LoggingModule logging, DatabaseDriver database)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));

            _Git = new GitService(logging);
            _Docks = new DockService(logging, database, settings, _Git);
            ICaptainService captainService = new CaptainService(logging, database, settings, _Git, _Docks);
            _PromptTemplates = new PromptTemplateService(database, logging);
            IMessageTemplateService messageTemplateService = new MessageTemplateService(logging, _PromptTemplates);
            IMissionService missionService = new MissionService(logging, database, settings, _Docks, captainService, _PromptTemplates, _Git);
            IVoyageService voyageService = new VoyageService(logging, database);
            _Admiral = new AdmiralService(logging, database, settings, captainService, missionService, voyageService, _Docks);
            AgentRuntimeFactory runtimeFactory = new AgentRuntimeFactory(logging);
            _AgentLifecycle = new AgentLifecycleHandler(
                logging,
                database,
                settings,
                runtimeFactory,
                _Admiral,
                messageTemplateService,
                _PromptTemplates,
                null,
                (_, _, _, _, _, _, _, _) => Task.CompletedTask);

            _MergeQueue = new MergeQueueService(logging, database, settings, _Git);
            _Landing = new LandingService(logging, database, settings, _Git);
            WorkflowProfileService workflowProfiles = new WorkflowProfileService(database, logging);
            VesselReadinessService vesselReadiness = new VesselReadinessService(database, workflowProfiles, logging);
            _CheckRuns = new CheckRunService(database, workflowProfiles, vesselReadiness, logging);
            _Objectives = new ObjectiveService(database);
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitNoopAsync =
                (_, _, _, _, _, _, _, _) => Task.CompletedTask;
            _PlanningSessions = new PlanningSessionCoordinator(logging, database, settings, _Docks, _Admiral, runtimeFactory, emitNoopAsync);
            _RefinementSessions = new ObjectiveRefinementCoordinator(logging, database, settings, runtimeFactory, emitNoopAsync);
            _Releases = new ReleaseService(database, workflowProfiles, logging);
            DeploymentEnvironmentService environments = new DeploymentEnvironmentService(database, workflowProfiles, logging);
            _Deployments = new DeploymentService(database, workflowProfiles, environments, _CheckRuns, logging);
            _Runbooks = new RunbookService(database, logging);
            _ModelEndpoints = new ModelEndpointService(database, logging);
            _Harbors = new HarborService(database, logging);
            _Vessels = new VesselService(database);
            JobService jobs = new JobService(database, logging);
            _FleetCategorization = new FleetCategorizationService(database, settings, jobs, new CaptainPromptRunner(runtimeFactory, logging), _PromptTemplates, logging);
            _VesselImport = new VesselImportService(database, settings, new VesselDiscoveryService(database, settings), _Vessels, jobs, logging, _FleetCategorization);
            Armada.Core.Services.Health.VesselHealthEvaluator vesselHealthEvaluator = new Armada.Core.Services.Health.VesselHealthEvaluator(
                database, _Git, settings,
                Armada.Core.Services.Health.VesselHealthEvaluator.CreateDefaultCriteria(
                    database, vesselReadiness,
                    new Armada.Core.Services.Health.DependencyScanner(new Armada.Core.Services.Health.DependencyToolRunner(new LocalHostCommandExecutor()))),
                logging);
            _VesselHealthService = new Armada.Core.Services.Health.VesselHealthService(database, settings, vesselHealthEvaluator, jobs, logging);
            _CaptainTools = new CaptainToolService(
                logging,
                database,
                null,
                null,
                settings.McpPort,
                ArmadaMcpConfigBuilder.ClientHostFor(settings.Rest.Hostname));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register every stdio tool. The tool names match the Admiral's HTTP MCP server; see the class remarks for the
        /// tools that answer <c>Unavailable</c> over stdio.
        /// </summary>
        /// <param name="register">Registration delegate.</param>
        public void Register(RegisterToolDelegate register)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            McpToolRegistrar.RegisterAll(
                register,
                _Database,
                _Admiral,
                _Settings,
                _Git,
                _MergeQueue,
                _Docks,
                _Landing,
                _CheckRuns,
                _Objectives,
                _PlanningSessions,
                _RefinementSessions,
                _Releases,
                _Deployments,
                _Runbooks,
                onStop: null,
                onStopCaptain: null,
                agentLifecycle: _AgentLifecycle,
                templateService: _PromptTemplates,
                logging: _Logging,
                captainToolService: _CaptainTools,
                modelEndpointService: _ModelEndpoints,
                harborService: _Harbors,
                vesselService: _Vessels,
                vesselImportService: _VesselImport,
                fleetActionService: null,
                vesselHealthService: _VesselHealthService,
                fleetCategorizationService: _FleetCategorization,
                stopServerUnavailableMessage: StopServerUnavailableMessage);
        }

        /// <summary>
        /// Dispose the services that own background work.
        /// </summary>
        public void Dispose()
        {
            _VesselHealthService.Dispose();
        }

        #endregion
    }
}
