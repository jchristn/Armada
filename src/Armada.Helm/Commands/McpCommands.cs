namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Text.Json;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Settings;

    #region Settings

    /// <summary>
    /// Settings for MCP install command.
    /// </summary>
    public class McpInstallSettings : BaseSettings
    {
        /// <summary>
        /// Only show the configuration, don't write it.
        /// </summary>
        [Description("Only display the configuration, don't write it")]
        [CommandOption("--dry-run")]
        public bool DryRun { get; set; } = false;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Configure MCP integration for Claude Code.
    /// </summary>
    [Description("Configure MCP integration for Claude Code")]
    public class McpInstallCommand : BaseCommand<McpInstallSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, McpInstallSettings settings, CancellationToken cancellationToken)
        {
            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);

            string mcpUrl = "http://localhost:" + armadaSettings.McpPort;
            string mcpRpcUrl = mcpUrl + "/rpc";

            JsonSerializerOptions jsonOpts = new JsonSerializerOptions { WriteIndented = true };

            AnsiConsole.MarkupLine("[bold dodgerblue1]Armada MCP Configuration[/]");
            AnsiConsole.WriteLine();

            // Claude Code: user-scoped MCP servers live in ~/.claude.json under top-level mcpServers
            AnsiConsole.MarkupLine("[bold]Claude Code[/] — Run:");
            AnsiConsole.MarkupLine("[green]  claude mcp add --transport http --scope user armada " + Markup.Escape(mcpRpcUrl) + "[/]");
            AnsiConsole.WriteLine();

            if (!settings.DryRun)
            {
                string claudeJsonPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude.json");

                bool alreadyConfigured = false;

                // Read existing .claude.json if present
                Dictionary<string, object>? claudeJson = null;
                if (File.Exists(claudeJsonPath))
                {
                    string existingJson = await File.ReadAllTextAsync(claudeJsonPath).ConfigureAwait(false);
                    claudeJson = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson, jsonOpts);
                }

                claudeJson ??= new Dictionary<string, object>();

                // Check if mcpServers.armada already exists
                if (claudeJson.TryGetValue("mcpServers", out object? mcpServersObj) && mcpServersObj != null)
                {
                    JsonElement mcpServersElement = (JsonElement)mcpServersObj;
                    if (mcpServersElement.ValueKind == JsonValueKind.Object && mcpServersElement.TryGetProperty("armada", out _))
                    {
                        alreadyConfigured = true;
                        AnsiConsole.MarkupLine("[gold1]Armada MCP server already configured in Claude Code.[/]");
                    }
                }

                if (!alreadyConfigured)
                {
                    bool confirm = AnsiConsole.Confirm(
                        $"[dodgerblue1]Add Armada MCP server to[/] [green]{Markup.Escape(claudeJsonPath)}[/]?",
                        true);

                    if (confirm)
                    {
                        // Merge mcpServers entry using JsonNode for safe round-trip editing
                        System.Text.Json.Nodes.JsonNode? root = System.Text.Json.Nodes.JsonNode.Parse(
                            await File.ReadAllTextAsync(claudeJsonPath).ConfigureAwait(false));

                        if (root == null)
                            root = new System.Text.Json.Nodes.JsonObject();

                        System.Text.Json.Nodes.JsonObject rootObj = root.AsObject();

                        if (!rootObj.ContainsKey("mcpServers"))
                            rootObj["mcpServers"] = new System.Text.Json.Nodes.JsonObject();

                        System.Text.Json.Nodes.JsonObject mcpServers = rootObj["mcpServers"]!.AsObject();
                        mcpServers["armada"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["type"] = "http",
                            ["url"] = mcpRpcUrl
                        };

                        await File.WriteAllTextAsync(claudeJsonPath, rootObj.ToJsonString(jsonOpts)).ConfigureAwait(false);
                        AnsiConsole.MarkupLine($"[green]Added Armada to {Markup.Escape(claudeJsonPath)}[/]");
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[dim](Dry run — no files written)[/]");
            }

            // Install Claude Code agent definition
            if (!settings.DryRun)
            {
                string agentsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude", "agents");
                string agentPath = Path.Combine(agentsDir, "armada.md");

                if (File.Exists(agentPath))
                {
                    AnsiConsole.MarkupLine("[gold1]Armada agent already installed.[/]");
                }
                else
                {
                    bool confirmAgent = AnsiConsole.Confirm(
                        $"[dodgerblue1]Install Armada agent to[/] [green]{Markup.Escape(agentPath)}[/]?",
                        true);

                    if (confirmAgent)
                    {
                        Directory.CreateDirectory(agentsDir);
                        await File.WriteAllTextAsync(agentPath, GenerateAgentDefinition()).ConfigureAwait(false);
                        AnsiConsole.MarkupLine($"[green]Installed Armada agent to {Markup.Escape(agentPath)}[/]");
                    }
                }
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Launch[/] — Run from any directory:");
            AnsiConsole.MarkupLine("[green]  claude --agent armada[/]");

            AnsiConsole.WriteLine();

            // Claude Desktop config snippet
            object desktopConfig = new
            {
                mcpServers = new Dictionary<string, object>
                {
                    ["armada"] = new
                    {
                        type = "http",
                        url = mcpRpcUrl
                    }
                }
            };

            string desktopJson = JsonSerializer.Serialize(desktopConfig, jsonOpts);

            AnsiConsole.MarkupLine("[bold]Claude Desktop[/] — Add to [green]claude_desktop_config.json[/]:");
            AnsiConsole.WriteLine();
            Console.WriteLine(desktopJson);
            AnsiConsole.WriteLine();

            // Show stdio alternative
            string helmPath = Environment.ProcessPath ?? "armada";
            AnsiConsole.MarkupLine("[bold]Stdio Transport (no server required)[/] — Run:");
            AnsiConsole.MarkupLine("[green]  claude mcp add --scope user armada -- " + Markup.Escape(helmPath) + " mcp stdio[/]");
            AnsiConsole.WriteLine();

            // Show Cursor settings
            AnsiConsole.MarkupLine("[bold]Cursor[/] — Add to [green].cursor/mcp.json[/] in your project:");
            AnsiConsole.WriteLine();

            object cursorConfig = new
            {
                mcpServers = new Dictionary<string, object>
                {
                    ["armada"] = new
                    {
                        url = mcpRpcUrl
                    }
                }
            };

            string cursorJson = JsonSerializer.Serialize(cursorConfig, jsonOpts);
            Console.WriteLine(cursorJson);
            AnsiConsole.WriteLine();

            // Show Gemini CLI settings
            AnsiConsole.MarkupLine("[bold]Gemini CLI[/] — Add to [green]~/.gemini/settings.json[/]:");
            AnsiConsole.WriteLine();

            object geminiConfig = new
            {
                mcpServers = new Dictionary<string, object>
                {
                    ["armada"] = new
                    {
                        httpUrl = mcpRpcUrl
                    }
                }
            };

            string geminiJson = JsonSerializer.Serialize(geminiConfig, jsonOpts);
            Console.WriteLine(geminiJson);
            AnsiConsole.WriteLine();

            // Show Codex settings
            AnsiConsole.MarkupLine("[bold]Codex[/] — Add to [green]~/.codex/config.json[/]:");
            AnsiConsole.WriteLine();

            object codexConfig = new
            {
                mcpServers = new Dictionary<string, object>
                {
                    ["armada"] = new
                    {
                        type = "http",
                        url = mcpRpcUrl
                    }
                }
            };

            string codexJson = JsonSerializer.Serialize(codexConfig, jsonOpts);
            Console.WriteLine(codexJson);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[dim]HTTP mode requires the Admiral server running. Stdio mode runs embedded (no server needed).[/]");

            return 0;
        }

        #region Private-Methods

        /// <summary>
        /// Generate the Armada agent definition markdown for Claude Code.
        /// </summary>
        private static string GenerateAgentDefinition()
        {
            return """
                ---
                name: armada
                description: Armada orchestrator — manage fleets, vessels, captains, missions, and voyages via MCP
                model: inherit
                allowedTools: mcp__armada__*
                ---

                You are an Armada orchestrator. Your sole purpose is to control and monitor the Armada multi-agent system through its MCP tools. You are NOT tied to any project — you operate purely as a control plane.

                ## What is Armada

                Armada is a multi-agent orchestration system that scales human developers with AI. It coordinates AI coding agents ("captains") to work on tasks ("missions") across git repositories ("vessels").

                ## Key Concepts

                - **Admiral** — The coordinator server you're connected to via MCP
                - **Fleet** — A collection of repositories
                - **Vessel** — A single git repository registered in a fleet
                - **Captain** — An AI agent worker (Claude Code, Codex, etc.)
                - **Mission** — An atomic work unit assigned to a captain
                - **Voyage** — A batch of related missions dispatched together
                - **Dock** — A git worktree where a captain does its work
                - **Signal** — A message between the user/admiral and a captain

                ## ID Prefixes

                All entity IDs have prefixes: `flt_` (fleet), `vsl_` (vessel), `cpt_` (captain), `msn_` (mission), `vyg_` (voyage), `dck_` (dock), `sig_` (signal), `mrg_` (merge entry).

                ## How to Behave

                1. **Always use MCP tools** — never search local files or run bash commands. All your work happens through `mcp__armada__*` tools.
                2. **Start with status** — when asked to check on things, call `armada_status` first for an overview.
                3. **Be concise** — show results in clean tables or bullet points, not walls of JSON.
                4. **Confirm destructive actions** — before deleting, cancelling, stopping, or purging, confirm with the user.
                5. **Proactive monitoring** — when checking on missions/voyages, show progress, any failures, and suggest next steps.

                ## Common Workflows

                **Dispatch work:**
                1. Ensure fleet and vessels exist (`armada_list_fleets`, `armada_list_vessels`)
                2. Ensure captains are registered (`armada_list_captains`)
                3. Dispatch a voyage with missions (`armada_dispatch`) or create standalone missions (`armada_create_mission`)

                **Monitor progress:**
                1. `armada_status` for overview
                2. `armada_voyage_status` or `armada_mission_status` for details
                3. `armada_get_mission_log` / `armada_get_captain_log` to see what an agent is doing
                4. `armada_get_mission_diff` to review code changes

                **Review and land:**
                1. Check mission diffs (`armada_get_mission_diff`)
                2. Transition status (`armada_transition_mission_status` to Review/Complete)
                3. Enqueue for merge (`armada_enqueue_merge`)
                4. Process merge queue (`armada_process_merge_queue`)

                ## Mission Status Flow

                Pending -> Assigned -> InProgress -> Testing/Review/Complete/Failed
                Most states allow -> Cancelled
                """;
        }

        #endregion
    }

    #endregion
}
