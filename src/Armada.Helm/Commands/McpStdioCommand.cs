namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using Spectre.Console.Cli;
    using SyslogLogging;
    using Voltaic;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server.Mcp;

    /// <summary>
    /// Run Armada as an MCP server over stdio (stdin/stdout).
    /// Designed to be launched by Claude Code or other MCP clients as a subprocess.
    /// </summary>
    [Description("Run MCP server over stdio for direct Claude Code integration")]
    public class McpStdioCommand : AsyncCommand<McpStdioSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, McpStdioSettings settings, CancellationToken cancellationToken)
        {
            // Load settings
            ArmadaSettings armadaSettings = new ArmadaSettings();
            string settingsPath = Path.Combine(Constants.DefaultDataDirectory, "settings.json");
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                ArmadaSettings? loaded = JsonSerializer.Deserialize<ArmadaSettings>(json);
                if (loaded != null) armadaSettings = loaded;
            }

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
            IMissionService missionService = new MissionService(logging, database, armadaSettings, dockService, captainService, promptTemplateService);
            IVoyageService voyageService = new VoyageService(logging, database);
            IAdmiralService admiral = new AdmiralService(logging, database, armadaSettings, captainService, missionService, voyageService, dockService);

            // Create stdio MCP server
            McpServer mcpServer = new McpServer();
            mcpServer.ServerName = Constants.ProductName;
            mcpServer.ServerVersion = Constants.ProductVersion;

            // Register all Armada tools
            IGitService gitService = git;
            IMergeQueueService mergeQueueService = new MergeQueueService(logging, database, armadaSettings, git);
            LandingService landingService = new LandingService(logging, database, armadaSettings, git);
            McpToolRegistrar.RegisterAll(mcpServer.RegisterTool, database, admiral, armadaSettings, gitService, mergeQueueService, dockService, landingService, templateService: promptTemplateService);

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
