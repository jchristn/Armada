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
        private static readonly string _VersionLabel = "v" + Constants.ProductVersion;

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

        private static void WriteVersionSubtitle()
        {
            AnsiConsole.MarkupLine("[dim]Multi-Agent Orchestration System  " + _VersionLabel + "[/]");
            AnsiConsole.WriteLine();
        }

        /// <summary>
        /// Write a section heading in the help menu.
        /// </summary>
        private static void HelpHeading(string title)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold dodgerblue1]{Markup.Escape(title)}[/]");
        }

        /// <summary>
        /// Write a single command row in the help menu: green command, dim description, aligned.
        /// </summary>
        private static void HelpRow(string command, string description)
        {
            string padded = command.Length >= 30 ? command + "  " : command.PadRight(30);
            AnsiConsole.MarkupLine($"  [green]{Markup.Escape(padded)}[/][dim]{Markup.Escape(description)}[/]");
        }

        /// <summary>
        /// Render the full grouped command menu shown for `armada`, `armada help`, `armada --help`, `-h`, `/?`, and `-?`.
        /// </summary>
        private static void PrintHelp()
        {
            AnsiConsole.MarkupLine("[dim]Usage:[/] armada <command> [[options]]");
            AnsiConsole.MarkupLine("[dim]Per-command help:[/] armada <command> --help   (also -h, /?, -?)");

            HelpHeading("Common");
            HelpRow("go \"<task>\"", "Dispatch a task in natural language");
            HelpRow("status [--all]", "System status dashboard (--all for everything)");
            HelpRow("watch [--interval N]", "Live-updating status dashboard");
            HelpRow("ask \"<question>\"", "Ask Armada about fleet state in plain language");
            HelpRow("inbox [--critical]", "Items awaiting your attention");
            HelpRow("log <captain> [--follow]", "Tail a captain's output log");
            HelpRow("diff <mission>", "Show a mission's code changes");
            HelpRow("doctor", "Check system health and report issues");

            HelpHeading("Missions (armada mission ...)");
            HelpRow("mission list", "List missions");
            HelpRow("mission show <id|name>", "Show mission details");
            HelpRow("mission create", "Create a standalone mission");
            HelpRow("mission cancel <id>", "Cancel a mission");
            HelpRow("mission restart <id>", "Restart a failed mission (edit instructions)");
            HelpRow("mission retry <id>", "Retry a failed mission (new copy)");

            HelpHeading("Voyages (armada voyage ...)");
            HelpRow("voyage list", "List all voyages");
            HelpRow("voyage show <id|name>", "Show voyage details");
            HelpRow("voyage create", "Launch a new voyage with missions");
            HelpRow("voyage cancel <id>", "Cancel a voyage and its pending missions");
            HelpRow("voyage retry <id>", "Retry failed missions in a voyage");

            HelpHeading("Backlog (armada backlog ...)");
            HelpRow("backlog list", "List backlog items");
            HelpRow("backlog show <id|title>", "Show a backlog item");
            HelpRow("backlog create", "Create a backlog item");
            HelpRow("backlog update <id>", "Update a backlog item");
            HelpRow("backlog delete <id>", "Delete a backlog item");
            HelpRow("backlog reorder <id>", "Change a backlog item's rank");

            HelpHeading("Playbooks (armada playbook ...)");
            HelpRow("playbook list", "List reusable markdown playbooks");
            HelpRow("playbook show <id|file>", "Show a playbook");
            HelpRow("playbook add", "Create a new playbook");
            HelpRow("playbook remove <id|file>", "Delete a playbook");

            HelpHeading("Vessels (armada vessel ...)");
            HelpRow("vessel list", "List all vessels (repositories)");
            HelpRow("vessel add", "Register a new vessel");
            HelpRow("vessel remove <id|name>", "Decommission a vessel");

            HelpHeading("Captains (armada captain ...)");
            HelpRow("captain list", "List all captains (agents)");
            HelpRow("captain add", "Recruit a new captain");
            HelpRow("captain update <id>", "Update an existing captain");
            HelpRow("captain stop <id|name>", "Recall a captain");
            HelpRow("captain remove <id|name>", "Remove a captain");
            HelpRow("captain stop-all", "Emergency recall of all captains");

            HelpHeading("Fleets (armada fleet ...)");
            HelpRow("fleet list", "List all fleets (groups of repos)");
            HelpRow("fleet add", "Create a new fleet");
            HelpRow("fleet remove <id|name>", "Remove a fleet");

            HelpHeading("Server (armada server ...)");
            HelpRow("server start", "Start the Admiral server");
            HelpRow("server status", "Check Admiral server health");
            HelpRow("server stop", "Stop the Admiral server");
            HelpRow("server restart", "Restart the Admiral server");

            HelpHeading("Configuration (armada config ...)");
            HelpRow("config show", "Display current settings");
            HelpRow("config set <key> <value>", "Set a configuration value");
            HelpRow("config init", "Interactive setup (config auto-initializes)");

            HelpHeading("MCP integration (armada mcp ...)");
            HelpRow("mcp install", "Configure MCP for Claude Code, Codex, Gemini, Cursor, Mux");
            HelpRow("mcp remove", "Remove MCP integration from those clients");
            HelpRow("mcp stdio", "Run the MCP server over stdio (subprocess bridge)");

            HelpHeading("Danger zone");
            HelpRow("reset", "Destructively reset all Armada data back to zero");

            HelpHeading("Global options");
            HelpRow("--help, -h, /?, -?", "Show help (top-level or per-command)");
            HelpRow("--version", "Show the Armada version");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Docs:[/] https://github.com/jchristn/Armada");
            AnsiConsole.WriteLine();
        }

        static int Main(string[] args)
        {
            // Normalize Windows-style help flags (/?  -?) to the standard --help everywhere,
            // so `armada /?`, `armada mission /?`, etc. all work.
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "/?" || args[i] == "-?")
                    args[i] = "--help";
            }

            // `armada help` -> top-level menu; `armada help <command...>` -> that command's help.
            if (args.Length >= 1 && string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length == 1)
                {
                    args = new string[] { "--help" };
                }
                else
                {
                    string[] rewritten = new string[args.Length];
                    Array.Copy(args, 1, rewritten, 0, args.Length - 1);
                    rewritten[args.Length - 1] = "--help";
                    args = rewritten;
                }
            }

            bool wantsTopLevelHelp =
                args.Length == 0
                || string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase);

            // First-run welcome (no config yet, and no explicit help request)
            if (args.Length == 0 && !File.Exists(ArmadaSettings.DefaultSettingsPath))
            {
                WriteBanner();
                WriteVersionSubtitle();
                AnsiConsole.MarkupLine("Welcome to Armada. To dispatch your first task:");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("  [green]armada go \"your task description\"[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("Run [green]armada help[/] to see everything Armada can do.");
                return 0;
            }

            // Top-level help: render the full grouped command menu (banner + sections).
            if (wantsTopLevelHelp)
            {
                WriteBanner();
                WriteVersionSubtitle();
                PrintHelp();
                return 0;
            }

            TypeRegistrar registrar = new TypeRegistrar();
            CommandApp app = new CommandApp(registrar);

            app.Configure(config =>
            {
                config.SetApplicationName("armada");
                config.SetApplicationVersion(Constants.ProductVersion);

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

                config.AddCommand<InboxCommand>("inbox")
                    .WithDescription("Show items awaiting your attention")
                    .WithExample("inbox")
                    .WithExample("inbox", "--critical");

                config.AddCommand<AskCommand>("ask")
                    .WithDescription("Ask Armada about fleet state in plain language")
                    .WithExample("ask", "\"any failures?\"")
                    .WithExample("ask", "\"how many captains?\"");

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

                config.AddBranch("action", action =>
                {
                    action.SetDescription("Run fleet actions (a command or AI prompt across many vessels)");
                    action.AddCommand<ActionListCommand>("list")
                        .WithDescription("List fleet actions (or runs with --runs)");
                    action.AddCommand<ActionRunCommand>("run")
                        .WithDescription("Run a fleet action over vessels (--vessel, --fleet)");
                    action.AddCommand<ActionStatusCommand>("status")
                        .WithDescription("Show a fleet action run and its targets");
                    action.AddCommand<ActionCancelCommand>("cancel")
                        .WithDescription("Cancel a fleet action run");
                });

                config.AddBranch("playbook", playbook =>
                {
                    playbook.SetDescription("Manage reusable markdown playbooks");
                    playbook.AddCommand<PlaybookListCommand>("list")
                        .WithDescription("List all playbooks");
                    playbook.AddCommand<PlaybookAddCommand>("add")
                        .WithDescription("Create a new playbook");
                    playbook.AddCommand<PlaybookShowCommand>("show")
                        .WithDescription("Show playbook details (accepts ID or file name)");
                    playbook.AddCommand<PlaybookRemoveCommand>("remove")
                        .WithDescription("Delete a playbook (accepts ID or file name)");
                });

                config.AddBranch("backlog", backlog =>
                {
                    backlog.SetDescription("Manage backlog items");
                    backlog.AddCommand<BacklogListCommand>("list")
                        .WithDescription("List backlog items")
                        .WithExample("backlog", "list")
                        .WithExample("backlog", "list", "--backlog-state", "ReadyForPlanning");
                    backlog.AddCommand<BacklogShowCommand>("show")
                        .WithDescription("Show backlog item details (accepts ID or title)")
                        .WithExample("backlog", "show", "obj_abc123");
                    backlog.AddCommand<BacklogCreateCommand>("create")
                        .WithDescription("Create a backlog item")
                        .WithExample("backlog", "create", "--title", "\"Improve release rollback\"")
                        .WithExample("backlog", "create", "--title", "\"Import customer incidents\"", "--priority", "P1", "--backlog-state", "ReadyForPlanning");
                    backlog.AddCommand<BacklogUpdateCommand>("update")
                        .WithDescription("Update a backlog item")
                        .WithExample("backlog", "update", "obj_abc123", "--owner", "\"delivery-team\"", "--target-version", "\"0.8.1\"");
                    backlog.AddCommand<BacklogDeleteCommand>("delete")
                        .WithDescription("Delete a backlog item")
                        .WithExample("backlog", "delete", "obj_abc123");
                    backlog.AddCommand<BacklogReorderCommand>("reorder")
                        .WithDescription("Update the rank of a backlog item")
                        .WithExample("backlog", "reorder", "obj_abc123", "--rank", "10");
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
                    captain.AddCommand<CaptainUpdateCommand>("update")
                        .WithDescription("Update an existing captain");
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
                    server.AddCommand<ServerRestartCommand>("restart")
                        .WithDescription("Restart the Admiral server");
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
                        .WithDescription("Configure MCP integration for Claude Code, Codex, Gemini, Cursor, and Mux");
                    mcp.AddCommand<McpRemoveCommand>("remove")
                        .WithDescription("Remove MCP integration for Claude Code, Codex, Gemini, Cursor, and Mux");
                    mcp.AddCommand<McpStdioCommand>("stdio")
                        .WithDescription("Run MCP server over stdio (for Claude Code subprocess)");
                });
            });

            return app.Run(args);
        }
    }
}
