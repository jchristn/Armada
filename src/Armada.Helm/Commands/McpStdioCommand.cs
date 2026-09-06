namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using Spectre.Console.Cli;
    using SyslogLogging;
    using Voltaic;
    using Voltaic.Core;
    using Voltaic.Mcp;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using Armada.Server.Mcp;

    /// <summary>
    /// Run Armada as an MCP server over stdio (stdin/stdout).
    /// Designed to be launched by Claude Code or other MCP clients as a subprocess.
    /// </summary>
    [Description("Run MCP server over stdio for direct Claude Code integration")]
    public class McpStdioCommand : AsyncCommand<McpStdioSettings>
    {
        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CommandContext context, McpStdioSettings settings, CancellationToken cancellationToken)
        {
            // Load settings using Armada's configured serializer/options so camelCase settings.json is honored.
            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);
            armadaSettings.InitializeDirectories();

            // Quiet logging -- stderr only, no console (stdout is the MCP transport)
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
            if (!Directory.Exists(armadaSettings.LogDirectory))
                Directory.CreateDirectory(armadaSettings.LogDirectory);
            logging.Settings.LogFilename = Path.Combine(armadaSettings.LogDirectory, "mcp-stdio.log");

            // Initialize database using DatabaseDriverFactory (supports SQLite, MySQL, PostgreSQL, SQL Server)
            DatabaseDriver database = DatabaseDriverFactory.Create(armadaSettings.Database, logging);
            await database.InitializeAsync().ConfigureAwait(false);

            // Initialize services
            IGitService git = new GitService(logging);
            IDockService dockService = new DockService(logging, database, armadaSettings, git);
            ICaptainService captainService = new CaptainService(logging, database, armadaSettings, git, dockService);
            IPromptTemplateService promptTemplateService = new PromptTemplateService(database, logging);
            IMessageTemplateService messageTemplateService = new MessageTemplateService(logging, promptTemplateService);
            IMissionService missionService = new MissionService(logging, database, armadaSettings, dockService, captainService, promptTemplateService, git);
            IVoyageService voyageService = new VoyageService(logging, database);
            IAdmiralService admiral = new AdmiralService(logging, database, armadaSettings, captainService, missionService, voyageService, dockService);
            AgentRuntimeFactory runtimeFactory = new AgentRuntimeFactory(logging);
            AgentLifecycleHandler agentLifecycle = new AgentLifecycleHandler(
                logging,
                database,
                armadaSettings,
                runtimeFactory,
                admiral,
                messageTemplateService,
                promptTemplateService,
                null,
                (_, _, _, _, _, _, _, _) => Task.CompletedTask);

            // Create stdio MCP server
            McpServer mcpServer = new McpServer();
            mcpServer.ServerName = Constants.ProductName;
            mcpServer.ServerVersion = Constants.ProductVersion;

            // Register all Armada tools
            IGitService gitService = git;
            IMergeQueueService mergeQueueService = new MergeQueueService(logging, database, armadaSettings, git);
            LandingService landingService = new LandingService(logging, database, armadaSettings, git);
            WorkflowProfileService workflowProfileService = new WorkflowProfileService(database, logging);
            VesselReadinessService vesselReadinessService = new VesselReadinessService(database, workflowProfileService, logging);
            CheckRunService checkRunService = new CheckRunService(database, workflowProfileService, vesselReadinessService, logging);
            ObjectiveService objectiveService = new ObjectiveService(database);
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitNoopAsync =
                (_, _, _, _, _, _, _, _) => Task.CompletedTask;
            PlanningSessionCoordinator planningSessionCoordinator = new PlanningSessionCoordinator(
                logging,
                database,
                armadaSettings,
                dockService,
                admiral,
                runtimeFactory,
                emitNoopAsync);
            ObjectiveRefinementCoordinator objectiveRefinementCoordinator = new ObjectiveRefinementCoordinator(
                logging,
                database,
                armadaSettings,
                runtimeFactory,
                emitNoopAsync);
            ReleaseService releaseService = new ReleaseService(database, workflowProfileService, logging);
            DeploymentEnvironmentService environmentService = new DeploymentEnvironmentService(database, workflowProfileService, logging);
            DeploymentService deploymentService = new DeploymentService(database, workflowProfileService, environmentService, checkRunService, logging);
            RunbookService runbookService = new RunbookService(database, logging);
            ModelEndpointService modelEndpointService = new ModelEndpointService(database, logging);
            HarborService harborService = new HarborService(database, logging);
            // Adapt Armada's JsonElement-based tool handlers to Voltaic 0.6.0's RpcParameters API.
            void RegisterAdapted(string name, string description, object inputSchema, Func<JsonElement?, Task<object>> handler)
            {
                mcpServer.RegisterTool(name, description, inputSchema, (RpcParameters? parameters) =>
                {
                    JsonElement? args = null;
                    if (parameters != null && parameters.HasValue && !string.IsNullOrEmpty(parameters.RawJson))
                    {
                        using JsonDocument doc = JsonDocument.Parse(parameters.RawJson);
                        args = doc.RootElement.Clone();
                    }
                    return handler(args);
                });
            }

            McpToolRegistrar.RegisterAll(
                RegisterAdapted,
                database,
                admiral,
                armadaSettings,
                gitService,
                mergeQueueService,
                dockService,
                landingService,
                checkRunService,
                objectiveService,
                planningSessionCoordinator,
                objectiveRefinementCoordinator,
                releaseService,
                deploymentService,
                runbookService,
                agentLifecycle: agentLifecycle,
                templateService: promptTemplateService,
                modelEndpointService: modelEndpointService,
                harborService: harborService);

            // Run until stdin closes or process is killed
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await mcpServer.RunAsync(cts.Token).ConfigureAwait(false);

            return 0;
        }
    }
}
