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
            HelpRow("inbox [--critical]", "Items awaiting your attention");
            HelpRow("log <captain> [--follow]", "Tail a captain's output log");
            HelpRow("diff <mission>", "Show a mission's code changes");
            HelpRow("doctor", "Check system health and report issues");
            HelpRow("tui [--server URL] [--profile NAME]", "Open the terminal UI (the dashboard in a terminal)");
            HelpRow("health [--evaluate]", "Show vessel health (dependencies, tests, branches)");

            HelpHeading("Missions (armada mission ...)");
            HelpRow("mission list", "List missions");
            HelpRow("mission show <id|name>", "Show mission details");
            HelpRow("mission create", "Create a standalone mission");
            HelpRow("mission cancel <id>", "Cancel a mission");
            HelpRow("mission restart <id>", "Restart a failed mission (edit instructions)");
            HelpRow("mission retry <id>", "Retry a failed mission (new copy)");
            HelpRow("mission failure <id>", "Show the mission's black box entry (--brief)");
            HelpRow("mission lint-brief --brief-file f", "Lint a mission brief before dispatch (--vessel)");

            HelpHeading("Voyages (armada voyage ...)");
            HelpRow("voyage list", "List all voyages");
            HelpRow("voyage show <id|name>", "Show voyage details");
            HelpRow("voyage create", "Launch a new voyage with missions");
            HelpRow("voyage cancel <id>", "Cancel a voyage and its pending missions");
            HelpRow("voyage retry <id>", "Retry failed missions in a voyage");
            HelpRow("voyage failure <id>", "Show the voyage's black box entry");
            HelpRow("voyage cascade <id>", "Which failure took the voyage's other missions down");

            HelpHeading("Black box (armada blackbox ...)");
            HelpRow("blackbox show <id> [--brief]", "Failure record of a mission, voyage, or entry (--brief | pbcopy)");
            HelpRow("blackbox list", "List failures (--vessel, --triage, --since 30d, ...)");
            HelpRow("blackbox objects <id>", "List an entry's evidence objects");
            HelpRow("blackbox cat <id> <name>", "Print an evidence object (--offset, --length)");
            HelpRow("blackbox download <id> [-o f.zip]", "Download an entry as a .zip");
            HelpRow("blackbox recover <id> <path>", "Dry run a recovery path (--execute to run it)");
            HelpRow("blackbox resolve <id>", "Resolve an entry (--resolution, --note, --link)");
            HelpRow("blackbox note <id> \"<text>\"", "Add a note to an entry");
            HelpRow("blackbox pin|unpin|delete <id...>", "Pin, unpin, or delete entries");
            HelpRow("blackbox purge [filters]", "Purge by filter (dry run unless --execute)");
            HelpRow("blackbox gc [--dry-run]", "Run black box garbage collection");
            HelpRow("blackbox retention", "Retention policy, usage, and GC runs");
            HelpRow("blackbox storage status|migrate", "Storage target health, or migrate --to <target>");
            HelpRow("blackbox bundle [-o bundle.md]", "Maintainer bundle of entries by fingerprint");
            HelpRow("blackbox backfill [--since 30d]", "Record entries for past failures");
            HelpRow("blackbox export-fixture <id>", "Write a sanitized regression fixture");
            HelpRow("blackbox timeline <id>", "Combined timeline of an entry (--newest-first)");
            HelpRow("blackbox diff <id> <otherId>", "What changed between two attempts");
            HelpRow("blackbox nearby <id>", "Successful landings near a failure on the same files");
            HelpRow("blackbox cascade <voyageId>", "Voyage cascade graph (alias of voyage cascade)");
            HelpRow("blackbox recovery-stats", "Recovery path success rates (--rule, --vessel)");

            HelpHeading("Fleet actions (armada action ...)");
            HelpRow("action list [--runs]", "List fleet actions (or their runs)");
            HelpRow("action run", "Run a fleet action over vessels");
            HelpRow("action status <run>", "Show a fleet action run and its targets");
            HelpRow("action cancel <run>", "Cancel a fleet action run");

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
            HelpRow("vessel import <paths...> [--root <dir>]", "Discover repositories and import them as vessels");
            HelpRow("vessel history <id|name>", "Commit history by day (--before, --all, --verbose)");
            HelpRow("vessel history <id|name> --heatmap", "Commit activity heatmap for the last year (--from/--to)");
            HelpRow("vessel update <id|name>", "Change settings (--serialize-overlapping-missions)");
            HelpRow("vessel overlap-preview <id|name>", "Preview a brief's overlaps and lint (--brief-file)");
            HelpRow("vessel reservations <id|name>", "Soft directory reservations of active missions");
            HelpRow("vessel doctor <id|name>", "Recurring black box configuration recommendations");

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

            HelpHeading("Remote Admirals (armada profile ...)");
            HelpRow("profile list", "List saved Admiral profiles and the active target");
            HelpRow("profile add <name>", "Save a remote Admiral (--server <url>, --token, --use)");
            HelpRow("profile use <name>", "Switch the active target ('local' for this machine)");
            HelpRow("profile remove <name>", "Remove a profile and its stored token");

            HelpHeading("MCP integration (armada mcp ...)");
            HelpRow("mcp install", "Configure MCP for Claude Code, Codex, Gemini, Cursor, Mux");
            HelpRow("mcp remove", "Remove MCP integration from those clients");
            HelpRow("mcp stdio", "Run the MCP server over stdio (subprocess bridge)");

            HelpHeading("Danger zone");
            HelpRow("reset", "Destructively reset all Armada data back to zero");

            HelpHeading("Global options");
            HelpRow("--help, -h, /?, -?", "Show help (top-level or per-command)");
            HelpRow("--version", "Show the Armada version");
            HelpRow("--server <url>", "Talk to this Admiral (or ARMADA_SERVER_URL)");
            HelpRow("--token <bearer>", "Credential for it (or ARMADA_TOKEN)");
            HelpRow("--profile <name>", "Use a saved profile ('local' for this machine)");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Docs:[/] https://github.com/jchristn/Armada");
            AnsiConsole.WriteLine();
        }

        /// <summary>
        /// Register every Armada CLI command and branch. Shared by <see cref="Main"/> and
        /// <see cref="DescribeCommandModelXml"/> so the API surface file describes exactly what ships.
        /// </summary>
        /// <param name="config">Spectre configurator.</param>
        public static void ConfigureCommands(IConfigurator config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            config.SetApplicationName("armada");
            config.SetApplicationVersion(Constants.ProductVersion);
            config.SetExceptionHandler(HelmErrorHandler.Handle);

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

            config.AddCommand<TuiCommand>("tui")
                .WithDescription("Open the Armada terminal UI")
                .WithExample("tui")
                .WithExample("tui", "--server", "http://127.0.0.1:7890")
                .WithExample("tui", "--profile", "work");

            config.AddCommand<InboxCommand>("inbox")
                .WithDescription("Show items awaiting your attention")
                .WithExample("inbox")
                .WithExample("inbox", "--critical");

            config.AddCommand<HealthCommand>("health")
                .WithDescription("Show vessel health (outdated dependencies, tests, branches, divergence)")
                .WithExample("health")
                .WithExample("health", "--status", "Fail")
                .WithExample("health", "--fleet", "flt_abc123", "--evaluate");

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
                mission.AddCommand<MissionFailureCommand>("failure")
                    .WithDescription("Show the mission's black box entry (alias of blackbox show)")
                    .WithExample("mission", "failure", "msn_abc123", "--brief");
                mission.AddCommand<MissionLintBriefCommand>("lint-brief")
                    .WithDescription("Lint a mission brief (warnings only)")
                    .WithExample("mission", "lint-brief", "--brief-file", "brief.md", "--vessel", "my-repo");
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
                voyage.AddCommand<VoyageFailureCommand>("failure")
                    .WithDescription("Show the voyage's black box entry (alias of blackbox show)")
                    .WithExample("voyage", "failure", "vyg_abc123");
                voyage.AddCommand<BlackBoxCascadeCommand>("cascade")
                    .WithDescription("Show which failure in a voyage took the others down")
                    .WithExample("voyage", "cascade", "vyg_abc123");
            });

            config.AddBranch("blackbox", blackbox =>
            {
                blackbox.SetDescription("Mission and voyage black box: failure records, briefs, evidence, and recovery");
                blackbox.AddCommand<BlackBoxShowCommand>("show")
                    .WithDescription("Show the black box entry of a mission, voyage, or entry id")
                    .WithExample("blackbox", "show", "msn_abc123")
                    .WithExample("blackbox", "show", "msn_abc123", "--brief")
                    .WithExample("blackbox", "show", "bbx_abc123", "--audience", "maintainer", "--json");
                blackbox.AddCommand<BlackBoxListCommand>("list")
                    .WithDescription("List black box entries, newest first")
                    .WithExample("blackbox", "list", "--vessel", "vsl_abc123", "--triage", "ArmadaDefect", "--since", "30d");
                blackbox.AddCommand<BlackBoxObjectsCommand>("objects")
                    .WithDescription("List an entry's evidence objects");
                blackbox.AddCommand<BlackBoxCatCommand>("cat")
                    .WithDescription("Print an evidence object (or a byte range) to stdout")
                    .WithExample("blackbox", "cat", "bbx_abc123", "evidence/git-status.txt", "--offset", "0", "--length", "65536");
                blackbox.AddCommand<BlackBoxDownloadCommand>("download")
                    .WithDescription("Download an entry as a .zip")
                    .WithExample("blackbox", "download", "bbx_abc123", "-o", "failure.zip");
                blackbox.AddCommand<BlackBoxRecoverCommand>("recover")
                    .WithDescription("Run a recovery path (dry run unless --execute)")
                    .WithExample("blackbox", "recover", "bbx_abc123", "rp1")
                    .WithExample("blackbox", "recover", "bbx_abc123", "rp1", "--execute");
                blackbox.AddCommand<BlackBoxResolveCommand>("resolve")
                    .WithDescription("Resolve an entry")
                    .WithExample("blackbox", "resolve", "bbx_abc123", "--resolution", "Resolved", "--note", "\"Fixed the checkout\"");
                blackbox.AddCommand<BlackBoxNoteCommand>("note")
                    .WithDescription("Add a note to an entry")
                    .WithExample("blackbox", "note", "bbx_abc123", "\"Checked the dock by hand\"");
                blackbox.AddCommand<BlackBoxPinCommand>("pin")
                    .WithDescription("Pin entries (kept by garbage collection)");
                blackbox.AddCommand<BlackBoxUnpinCommand>("unpin")
                    .WithDescription("Unpin entries");
                blackbox.AddCommand<BlackBoxDeleteCommand>("delete")
                    .WithDescription("Delete entries and their evidence");
                blackbox.AddCommand<BlackBoxPurgeCommand>("purge")
                    .WithDescription("Purge entries by filter (dry run unless --execute)")
                    .WithExample("blackbox", "purge", "--vessel", "vsl_abc123", "--before", "2026-09-01")
                    .WithExample("blackbox", "purge", "--vessel", "vsl_abc123", "--before", "2026-09-01", "--execute");
                blackbox.AddCommand<BlackBoxGcCommand>("gc")
                    .WithDescription("Run garbage collection now (--dry-run reports what would go)")
                    .WithExample("blackbox", "gc", "--dry-run");
                blackbox.AddCommand<BlackBoxRetentionCommand>("retention")
                    .WithDescription("Show the retention policy, usage, and garbage collection runs");
                blackbox.AddBranch("storage", storage =>
                {
                    storage.SetDescription("Black box storage target");
                    storage.AddCommand<BlackBoxStorageStatusCommand>("status")
                        .WithDescription("Show the storage target, health, and usage");
                    storage.AddCommand<BlackBoxStorageMigrateCommand>("migrate")
                        .WithDescription("Migrate objects to another storage target")
                        .WithExample("blackbox", "storage", "migrate", "--to", "Disk");
                    storage.AddCommand<BlackBoxStorageMigrationCommand>("migration")
                        .WithDescription("Show a storage migration job");
                });
                blackbox.AddCommand<BlackBoxBundleCommand>("bundle")
                    .WithDescription("Build the maintainer bundle (up to 50 entries by fingerprint)")
                    .WithExample("blackbox", "bundle", "--triage", "ArmadaDefect", "-o", "bundle.md");
                blackbox.AddCommand<BlackBoxBackfillCommand>("backfill")
                    .WithDescription("Backfill entries for past failures")
                    .WithExample("blackbox", "backfill", "--since", "30d");
                blackbox.AddCommand<BlackBoxExportFixtureCommand>("export-fixture")
                    .WithDescription("Export an entry as a sanitized regression fixture")
                    .WithExample("blackbox", "export-fixture", "bbx_abc123", "--name", "land-003-example");
                blackbox.AddCommand<BlackBoxTimelineCommand>("timeline")
                    .WithDescription("Show an entry's combined timeline (oldest first)")
                    .WithExample("blackbox", "timeline", "bbx_abc123", "--page-size", "50");
                blackbox.AddCommand<BlackBoxDiffCommand>("diff")
                    .WithDescription("Show what changed between two entries")
                    .WithExample("blackbox", "diff", "bbx_abc123", "bbx_def456");
                blackbox.AddCommand<BlackBoxNearbyCommand>("nearby")
                    .WithDescription("List the successful landings near a failure that touched the same files");
                blackbox.AddCommand<BlackBoxCascadeCommand>("cascade")
                    .WithDescription("Show a voyage's cascade graph (alias of voyage cascade)")
                    .WithExample("blackbox", "cascade", "vyg_abc123");
                blackbox.AddCommand<BlackBoxRecoveryStatsCommand>("recovery-stats")
                    .WithDescription("Show recovery path success rates")
                    .WithExample("blackbox", "recovery-stats", "--rule", "BBX-LAND-001");
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
                vessel.AddCommand<VesselImportCommand>("import")
                    .WithDescription("Discover repositories and import them as vessels")
                    .WithExample("vessel", "import", "~/Code/my-repo", "--root", "~/Code", "--dry-run");
                vessel.AddCommand<VesselRemoveCommand>("remove")
                    .WithDescription("Decommission a vessel (accepts name or ID)");
                vessel.AddCommand<VesselUpdateCommand>("update")
                    .WithDescription("Update a vessel's settings (accepts name or ID)")
                    .WithExample("vessel", "update", "my-repo", "--serialize-overlapping-missions", "true");
                vessel.AddCommand<VesselOverlapPreviewCommand>("overlap-preview")
                    .WithDescription("Preview a mission's overlaps and brief lint on a vessel (nothing is dispatched)")
                    .WithExample("vessel", "overlap-preview", "my-repo", "--brief-file", "brief.md");
                vessel.AddCommand<VesselReservationsCommand>("reservations")
                    .WithDescription("List a vessel's soft directory reservations");
                vessel.AddCommand<VesselDoctorCommand>("doctor")
                    .WithDescription("List a vessel's recurring black box configuration recommendations")
                    .WithExample("vessel", "doctor", "my-repo", "--window-days", "30");
                vessel.AddCommand<VesselHistoryCommand>("history")
                    .WithDescription("Show a vessel's commit history or activity heatmap (accepts name or ID)")
                    .WithExample("vessel", "history", "my-repo", "--before", "2026-01-01")
                    .WithExample("vessel", "history", "my-repo", "--heatmap");
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

            config.AddBranch("profile", profile =>
            {
                profile.SetDescription("Saved Admiral targets shared with armada tui (remote servers)");
                profile.AddCommand<ProfileListCommand>("list")
                    .WithDescription("List saved Admiral profiles and the active target");
                profile.AddCommand<ProfileAddCommand>("add")
                    .WithDescription("Add or update a profile (--server <url>, --token <bearer>, --use)");
                profile.AddCommand<ProfileUseCommand>("use")
                    .WithDescription("Make a profile the active target ('local' for this machine)");
                profile.AddCommand<ProfileRemoveCommand>("remove")
                    .WithDescription("Remove a profile and its stored token");
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
        }

        /// <summary>
        /// The CLI command model (every command, branch, argument, and option) as Spectre.Console.Cli XML, produced by the
        /// built-in <c>cli xmldoc</c> command against <see cref="ConfigureCommands"/>. Used by the API surface generator and
        /// the API contract test.
        /// </summary>
        /// <returns>Command model XML.</returns>
        public static string DescribeCommandModelXml()
        {
            using (StringWriter writer = new StringWriter())
            {
                IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Ansi = AnsiSupport.No,
                    ColorSystem = ColorSystemSupport.NoColors,
                    Out = new AnsiConsoleOutput(writer)
                });
                console.Profile.Width = 100000;

                CommandApp app = new CommandApp(new TypeRegistrar());
                app.Configure(config =>
                {
                    ConfigureCommands(config);
                    config.ConfigureConsole(console);
                });
                app.Run(new string[] { "cli", "xmldoc" });
                return writer.ToString();
            }
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

            app.Configure(ConfigureCommands);

            return app.Run(args);
        }
    }
}
