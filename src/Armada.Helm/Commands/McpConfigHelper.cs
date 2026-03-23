namespace Armada.Helm.Commands
{
    using System.Text.Json;
    using System.Text.Json.Nodes;

    internal static class McpConfigHelper
    {
        internal sealed record ConfigTarget(
            string ClientName,
            string FilePath,
            JsonObject ArmadaConfig,
            bool IsProjectScoped = false,
            bool InstallAgent = false);

        internal sealed record ApplyResult(string ClientName, string FilePath, bool Changed, string Message, bool IsProjectScoped = false);

        internal static JsonSerializerOptions JsonOptions { get; } = new JsonSerializerOptions { WriteIndented = true };

        internal static string GetMcpRpcUrl(int mcpPort)
        {
            return $"http://localhost:{mcpPort}/rpc";
        }

        internal static string GetClaudeJsonPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
        }

        internal static string GetClaudeAgentPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "agents", "armada.md");
        }

        internal static string GetCodexConfigPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.json");
        }

        internal static string GetGeminiConfigPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "settings.json");
        }

        internal static string GetCursorConfigPath()
        {
            return Path.Combine(Environment.CurrentDirectory, ".cursor", "mcp.json");
        }

        internal static List<ConfigTarget> BuildTargets(int mcpPort)
        {
            string mcpRpcUrl = GetMcpRpcUrl(mcpPort);

            return new List<ConfigTarget>
            {
                new(
                    "Claude Code",
                    GetClaudeJsonPath(),
                    new JsonObject
                    {
                        ["type"] = "http",
                        ["url"] = mcpRpcUrl,
                    },
                    InstallAgent: true),
                new(
                    "Codex",
                    GetCodexConfigPath(),
                    new JsonObject
                    {
                        ["type"] = "http",
                        ["url"] = mcpRpcUrl,
                    }),
                new(
                    "Gemini CLI",
                    GetGeminiConfigPath(),
                    new JsonObject
                    {
                        ["httpUrl"] = mcpRpcUrl,
                    }),
                new(
                    "Cursor",
                    GetCursorConfigPath(),
                    new JsonObject
                    {
                        ["url"] = mcpRpcUrl,
                    },
                    IsProjectScoped: true),
            };
        }

        internal static async Task<ApplyResult> InstallTargetAsync(ConfigTarget target)
        {
            JsonObject root = await ReadOrCreateRootAsync(target.FilePath).ConfigureAwait(false);
            if (root["mcpServers"] is not JsonObject)
            {
                root["mcpServers"] = new JsonObject();
            }

            JsonObject mcpServers = root["mcpServers"]!.AsObject();
            JsonNode? existing = mcpServers["armada"];
            bool changed = existing == null || !JsonNode.DeepEquals(existing, target.ArmadaConfig);
            mcpServers["armada"] = target.ArmadaConfig.DeepClone();

            Directory.CreateDirectory(Path.GetDirectoryName(target.FilePath)!);
            await File.WriteAllTextAsync(target.FilePath, root.ToJsonString(JsonOptions)).ConfigureAwait(false);

            return new ApplyResult(
                target.ClientName,
                target.FilePath,
                changed,
                changed ? "Configured Armada MCP entry." : "Armada MCP entry already matched the expected configuration.",
                target.IsProjectScoped);
        }

        internal static async Task<ApplyResult> RemoveTargetAsync(ConfigTarget target)
        {
            if (!File.Exists(target.FilePath))
            {
                return new ApplyResult(
                    target.ClientName,
                    target.FilePath,
                    false,
                    "Configuration file does not exist; nothing to remove.",
                    target.IsProjectScoped);
            }

            JsonObject root = await ReadOrCreateRootAsync(target.FilePath).ConfigureAwait(false);
            JsonObject? mcpServers = root["mcpServers"] as JsonObject;
            if (mcpServers == null || !mcpServers.ContainsKey("armada"))
            {
                return new ApplyResult(
                    target.ClientName,
                    target.FilePath,
                    false,
                    "No Armada MCP entry was present.",
                    target.IsProjectScoped);
            }

            mcpServers.Remove("armada");
            if (mcpServers.Count == 0)
            {
                root.Remove("mcpServers");
            }

            await File.WriteAllTextAsync(target.FilePath, root.ToJsonString(JsonOptions)).ConfigureAwait(false);

            return new ApplyResult(
                target.ClientName,
                target.FilePath,
                true,
                "Removed Armada MCP entry.",
                target.IsProjectScoped);
        }

        internal static async Task<ApplyResult> InstallClaudeAgentAsync()
        {
            string agentPath = GetClaudeAgentPath();
            Directory.CreateDirectory(Path.GetDirectoryName(agentPath)!);
            string content = GenerateAgentDefinition();
            bool changed = !File.Exists(agentPath) || !string.Equals(await File.ReadAllTextAsync(agentPath).ConfigureAwait(false), content, StringComparison.Ordinal);
            await File.WriteAllTextAsync(agentPath, content).ConfigureAwait(false);
            return new ApplyResult("Claude Code Agent", agentPath, changed, changed ? "Installed/updated armada agent definition." : "Armada agent definition already up to date.");
        }

        internal static Task<ApplyResult> RemoveClaudeAgentAsync()
        {
            string agentPath = GetClaudeAgentPath();
            if (!File.Exists(agentPath))
            {
                return Task.FromResult(new ApplyResult("Claude Code Agent", agentPath, false, "Agent file does not exist; nothing to remove."));
            }

            File.Delete(agentPath);
            return Task.FromResult(new ApplyResult("Claude Code Agent", agentPath, true, "Removed armada agent definition."));
        }

        internal static string BuildManualSnippet(ConfigTarget target)
        {
            JsonObject root = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["armada"] = target.ArmadaConfig.DeepClone(),
                },
            };
            return root.ToJsonString(JsonOptions);
        }

        internal static string BuildClaudeCliCommand(int mcpPort)
        {
            return $"claude mcp add --transport http --scope user armada {GetMcpRpcUrl(mcpPort)}";
        }

        internal static string BuildClaudeStdioCommand()
        {
            return "claude mcp add --scope user armada -- armada mcp stdio";
        }

        private static async Task<JsonObject> ReadOrCreateRootAsync(string path)
        {
            if (!File.Exists(path))
            {
                return new JsonObject();
            }

            string text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new JsonObject();
            }

            JsonNode? node = JsonNode.Parse(text);
            return node as JsonObject ?? new JsonObject();
        }

        /// <summary>
        /// Generate the Armada agent definition markdown for Claude Code.
        /// </summary>
        internal static string GenerateAgentDefinition()
        {
            return """
                ---
                name: armada
                description: Armada orchestrator - manage fleets, vessels, captains, missions, and voyages via MCP
                model: inherit
                allowedTools: mcp__armada__*
                ---

                You are an Armada orchestrator. Your sole purpose is to control and monitor the Armada multi-agent system through its MCP tools. You are NOT tied to any project - you operate purely as a control plane.

                ## What is Armada

                Armada is a multi-agent orchestration system that scales human developers with AI. It coordinates AI coding agents ("captains") to work on tasks ("missions") across git repositories ("vessels").

                ## Key Concepts

                - **Admiral** - The coordinator server you're connected to via MCP
                - **Fleet** - A collection of repositories
                - **Vessel** - A single git repository registered in a fleet
                - **Captain** - An AI agent worker (Claude Code, Codex, etc.)
                - **Mission** - An atomic work unit assigned to a captain
                - **Voyage** - A batch of related missions dispatched together
                - **Dock** - A git worktree where a captain does its work
                - **Signal** - A message between the user/admiral and a captain

                ## ID Prefixes

                All entity IDs have prefixes: `flt_` (fleet), `vsl_` (vessel), `cpt_` (captain), `msn_` (mission), `vyg_` (voyage), `dck_` (dock), `sig_` (signal), `mrg_` (merge entry).

                ## How to Behave

                1. **Always use MCP tools** - never search local files or run bash commands. All your work happens through `mcp__armada__*` tools.
                2. **Start with status** - when asked to check on things, call `armada_status` first for an overview.
                3. **Be concise** - show results in clean tables or bullet points, not walls of JSON.
                4. **Confirm destructive actions** - before deleting, cancelling, stopping, or purging, confirm with the user.
                5. **Proactive monitoring** - when checking on missions/voyages, show progress, any failures, and suggest next steps.

                ## Common Workflows

                **Dispatch work:**
                1. Ensure fleet and vessels exist (`armada_enumerate` with entityType 'fleets' or 'vessels')
                2. Ensure captains are registered (`armada_enumerate` with entityType 'captains')
                3. Dispatch a voyage with missions (`armada_dispatch`) or create standalone missions (`armada_create_mission`)

                **Monitor progress:**
                1. `armada_status` for overview
                2. `armada_voyage_status` for voyage summary (returns mission counts by status; set summary=false and includeMissions=true for full mission objects) or `armada_mission_status` for individual mission details
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
    }
}
