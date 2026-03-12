namespace Armada.Helm
{
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Settings;
    using Armada.Helm.Commands;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Armada CLI entry point.
    /// </summary>
    public class Program
    {
        private static readonly string[] _Banner = new[]
        {
            @"                        _      ",
            @" __ _ _ _ _ __  __ _ __| |__ _ ",
            @"/ _` | '_| '  \/ _` / _` / _` |",
            @"\__,_|_| |_|_|_\__,_\__,_\__,_|",
        };

        /// <summary>
        /// Write the Armada banner to the console.
        /// </summary>
        public static void WriteBanner()
        {
            foreach (string line in _Banner)
            {
                AnsiConsole.MarkupLine($"[dodgerblue1]{Markup.Escape(line)}[/]");
            }
            AnsiConsole.WriteLine();
        }

        static int Main(string[] args)
        {
            // First-run welcome
            if (args.Length == 0 && !File.Exists(ArmadaSettings.DefaultSettingsPath))
            {
                WriteBanner();
                AnsiConsole.MarkupLine("[dim]Multi-Agent Orchestration System  v0.2.0[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("Welcome to Armada. To dispatch your first task:");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("  [green]armada go \"your task description\"[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("Run [green]armada --help[/] for more options.");
                return 0;
            }

            // Show banner for help/no-args
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                WriteBanner();
                AnsiConsole.MarkupLine("[dim]Multi-Agent Orchestration System  v0.2.0[/]");
                AnsiConsole.WriteLine();
            }

            TypeRegistrar registrar = new TypeRegistrar();
            CommandApp app = new CommandApp(registrar);

            app.Configure(config =>
            {
                config.SetApplicationName("armada");
                config.SetApplicationVersion("0.2.0");

                // --- Common commands (top-level, used most often) ---
                config.AddCommand<GoCommand>("go")
                    .WithDescription("Quick dispatch -- natural language task assignment")
                    .WithExample("go", "\"Add input validation to signup\"")
                    .WithExample("go", "\"Fix login bug\"", "--repo", ".");

                config.AddCommand<StatusCommand>("status")
                    .WithDescription("Show status dashboard (contextual to current repo)")
                    .WithExample("status")
                    .WithExample("status", "--all");

                config.AddCommand<WatchCommand>("watch")
                    .WithDescription("Live-updating status dashboard")
                    .WithExample("watch")
                    .WithExample("watch", "--interval", "2");

                config.AddCommand<LogCommand>("log")
                    .WithDescription("Tail a captain's output log")
                    .WithExample("log", "captain-1")
                    .WithExample("log", "captain-1", "--follow");

                config.AddCommand<DiffCommand>("diff")
                    .WithDescription("Show diff of a mission's changes")
                    .WithExample("diff", "msn_abc123")
                    .WithExample("diff", "\"Add validation\"");

                config.AddCommand<DoctorCommand>("doctor")
                    .WithDescription("Check system health and report issues");

                config.AddCommand<ResetCommand>("reset")
                    .WithDescription("Destructively reset all Armada data back to zero");

                // --- Entity management ---
                config.AddBranch("mission", mission =>
                {
                    mission.SetDescription("Manage missions (tasks)");
                    mission.AddCommand<MissionListCommand>("list")
                        .WithDescription("List missions");
                    mission.AddCommand<MissionCreateCommand>("create")
                        .WithDescription("Create a new mission");
                    mission.AddCommand<MissionShowCommand>("show")
                        .WithDescription("Show mission details (accepts name or ID)");
                    mission.AddCommand<MissionCancelCommand>("cancel")
                        .WithDescription("Cancel a mission");
                    mission.AddCommand<MissionRestartCommand>("restart")
                        .WithDescription("Restart a failed mission (with optional instruction edits)");
                    mission.AddCommand<MissionRetryCommand>("retry")
                        .WithDescription("Retry a failed mission (creates a new copy)");
                });

                config.AddBranch("voyage", voyage =>
                {
                    voyage.SetDescription("Manage voyages (batches of missions)");
                    voyage.AddCommand<VoyageListCommand>("list")
                        .WithDescription("List all voyages");
                    voyage.AddCommand<VoyageCreateCommand>("create")
                        .WithDescription("Launch a new voyage with missions");
                    voyage.AddCommand<VoyageShowCommand>("show")
                        .WithDescription("Show voyage details (accepts name or ID)");
                    voyage.AddCommand<VoyageCancelCommand>("cancel")
                        .WithDescription("Cancel a voyage");
                    voyage.AddCommand<VoyageRetryCommand>("retry")
                        .WithDescription("Retry failed missions in a voyage");
                });

                config.AddBranch("vessel", vessel =>
                {
                    vessel.SetDescription("Manage vessels (repositories)");
                    vessel.AddCommand<VesselListCommand>("list")
                        .WithDescription("List all vessels");
                    vessel.AddCommand<VesselAddCommand>("add")
                        .WithDescription("Register a new vessel");
                    vessel.AddCommand<VesselRemoveCommand>("remove")
                        .WithDescription("Decommission a vessel (accepts name or ID)");
                });

                config.AddBranch("captain", captain =>
                {
                    captain.SetDescription("Manage captains (agents)");
                    captain.AddCommand<CaptainListCommand>("list")
                        .WithDescription("List all captains");
                    captain.AddCommand<CaptainAddCommand>("add")
                        .WithDescription("Recruit a new captain");
                    captain.AddCommand<CaptainStopCommand>("stop")
                        .WithDescription("Recall a captain (accepts name or ID)");
                    captain.AddCommand<CaptainRemoveCommand>("remove")
                        .WithDescription("Remove a captain (accepts name or ID)");
                    captain.AddCommand<CaptainStopAllCommand>("stop-all")
                        .WithDescription("Emergency recall all captains");
                });

                config.AddBranch("fleet", fleet =>
                {
                    fleet.SetDescription("Manage fleets (groups of repos)");
                    fleet.AddCommand<FleetListCommand>("list")
                        .WithDescription("List all fleets");
                    fleet.AddCommand<FleetAddCommand>("add")
                        .WithDescription("Create a new fleet");
                    fleet.AddCommand<FleetRemoveCommand>("remove")
                        .WithDescription("Remove a fleet (accepts name or ID)");
                });

                // --- Infrastructure ---
                config.AddBranch("server", server =>
                {
                    server.SetDescription("Manage Admiral server");
                    server.AddCommand<ServerStartCommand>("start")
                        .WithDescription("Start the Admiral server");
                    server.AddCommand<ServerStatusCommand>("status")
                        .WithDescription("Check Admiral server health");
                    server.AddCommand<ServerStopCommand>("stop")
                        .WithDescription("Stop the Admiral server");
                });

                config.AddBranch("config", cfg =>
                {
                    cfg.SetDescription("Manage configuration");
                    cfg.AddCommand<ConfigShowCommand>("show")
                        .WithDescription("Display current settings");
                    cfg.AddCommand<ConfigSetCommand>("set")
                        .WithDescription("Set a configuration value");
                    cfg.AddCommand<ConfigInitCommand>("init")
                        .WithDescription("Interactive setup (optional -- config auto-initializes)");
                });

                config.AddBranch("mcp", mcp =>
                {
                    mcp.SetDescription("MCP integration");
                    mcp.AddCommand<McpInstallCommand>("install")
                        .WithDescription("Configure MCP integration for Claude Code");
                    mcp.AddCommand<McpStdioCommand>("stdio")
                        .WithDescription("Run MCP server over stdio (for Claude Code subprocess)");
                });
            });

            return app.Run(args);
        }
    }
}
