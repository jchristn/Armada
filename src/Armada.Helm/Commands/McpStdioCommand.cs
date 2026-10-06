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
    using Armada.Helm.Infrastructure;
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
        public override async Task<int> ExecuteAsync(CommandContext context, McpStdioSettings settings, CancellationToken cancellationToken)
        {
            // Load settings using Armada's configured serializer/options so camelCase settings.json is honored.
            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);

            // stdio MCP opens this machine's database directly; a remote target must be reached over HTTP MCP instead
            // (armada mcp install --server ...). Refuse rather than silently serve the local database.
            AdmiralTargetRequest request = new AdmiralTargetRequest { Server = settings.Server, Token = settings.Token, Profile = settings.Profile };
            AdmiralTarget target = await AdmiralTargetResolver.CreateDefault(armadaSettings.AdmiralPort, armadaSettings.ApiKey).ResolveAsync(request, cancellationToken).ConfigureAwait(false);
            if (!target.IsLocal)
            {
                throw new AdmiralTargetException(
                    AdmiralTargetErrorEnum.LocalOnlyCommand,
                    "'armada mcp stdio' acts only on this machine's Admiral (it opens the local database directly), but the target is " + target.Describe()
                    + ". Point the MCP client at the remote Admiral's HTTP MCP endpoint with 'armada mcp install --server <url> --token <bearer>', or pass --profile local.",
                    "mcp stdio");
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

            // Create stdio MCP server
            McpServer mcpServer = new McpServer();
            mcpServer.ServerName = Constants.ProductName;
            mcpServer.ServerVersion = Constants.ProductVersion;
            // Voltaic 2.1.4+ reports a throwing handler as a generic isError result; surface Armada's
            // exception messages (e.g. "captain not found") so agents can react to them, as before.
            mcpServer.IncludeToolExceptionMessages = true;

            // Adapt Armada's JsonElement-based tool handlers to Voltaic's RpcParameters API.
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

            // Register the same tool names as the Admiral's HTTP MCP server (see McpStdioToolSet for the tools that
            // answer Unavailable without the Admiral process).
            using McpStdioToolSet toolSet = new McpStdioToolSet(armadaSettings, logging, database);
            toolSet.Register(RegisterAdapted);

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
