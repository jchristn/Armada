namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Armada.Core.Enums;

    /// <summary>
    /// Pure planner that, for a given runtime, produces the steps to launch a captain in an isolated agent
    /// configuration: what extra CLI arguments to append, what environment overrides to apply, and what
    /// scoped configuration files to write so the agent still reaches the Armada MCP server while being
    /// blocked from the host user's global settings.
    ///
    /// Strategy per runtime:
    /// - Claude Code: --strict-mcp-config + an injected --mcp-config file (strict ignores host servers, so
    ///   the Armada server must be supplied explicitly) plus --setting-sources project,local.
    /// - Codex: a scoped CODEX_HOME containing a config.toml that registers the Armada server.
    /// - Gemini / Cursor: a scoped HOME/USERPROFILE containing the client's settings file, so they
    ///   physically cannot read the host user's configuration.
    /// - Mux: a scoped MUX_CONFIG_DIR containing mcp-servers.json.
    ///
    /// Side-effect free (writes nothing) so it can be unit tested in isolation; the caller materializes the
    /// returned files and applies the environment/arguments.
    /// </summary>
    public static class CaptainLaunchIsolationPlanner
    {
        #region Public-Methods

        /// <summary>
        /// Build the isolation plan for a runtime. Returns an empty plan (nothing to apply) when isolation
        /// cannot be expressed for the runtime or when the MCP port is invalid.
        /// </summary>
        /// <param name="runtime">The captain's runtime.</param>
        /// <param name="mcpPort">The Admiral MCP port (must be positive).</param>
        /// <param name="scopedConfigDirectory">Absolute path to the per-launch scoped configuration directory.</param>
        /// <returns>The isolation plan; never null.</returns>
        public static CaptainLaunchIsolationPlan Plan(AgentRuntimeEnum runtime, int mcpPort, string scopedConfigDirectory)
        {
            return Plan(runtime, mcpPort, scopedConfigDirectory, null);
        }

        /// <summary>
        /// Build the isolation plan for a runtime, optionally binding the scoped Armada MCP connection to a session token
        /// (sent as an X-Token header) so the agent's tool calls reach Armada as that caller. Mux carries the token as API-key
        /// authentication in its server document. OpenCode has no isolation plan (see <see cref="CarriesSessionToken"/>).
        /// </summary>
        /// <param name="runtime">The captain's runtime.</param>
        /// <param name="mcpPort">The Admiral MCP port (must be positive).</param>
        /// <param name="scopedConfigDirectory">Absolute path to the per-launch scoped configuration directory.</param>
        /// <param name="mcpSessionToken">Session token for the scoped MCP connection, or null.</param>
        /// <returns>The isolation plan; never null.</returns>
        public static CaptainLaunchIsolationPlan Plan(AgentRuntimeEnum runtime, int mcpPort, string scopedConfigDirectory, string? mcpSessionToken)
        {
            return Plan(runtime, mcpPort, scopedConfigDirectory, mcpSessionToken, ArmadaMcpConfigBuilder.DefaultHost);
        }

        /// <summary>
        /// Build the isolation plan for a runtime against an Armada MCP listener reached at a specific client host (see
        /// <see cref="ArmadaMcpConfigBuilder.ClientHostFor"/>), optionally binding the connection to a session token.
        /// </summary>
        /// <param name="runtime">The captain's runtime.</param>
        /// <param name="mcpPort">The Admiral MCP port (must be positive).</param>
        /// <param name="scopedConfigDirectory">Absolute path to the per-launch scoped configuration directory.</param>
        /// <param name="mcpSessionToken">Session token for the scoped MCP connection, or null.</param>
        /// <param name="mcpHost">Host placed in the generated MCP URLs; empty means localhost.</param>
        /// <returns>The isolation plan; never null.</returns>
        public static CaptainLaunchIsolationPlan Plan(AgentRuntimeEnum runtime, int mcpPort, string scopedConfigDirectory, string? mcpSessionToken, string mcpHost)
        {
            string host = String.IsNullOrWhiteSpace(mcpHost) ? ArmadaMcpConfigBuilder.DefaultHost : mcpHost;
            CaptainLaunchIsolationPlan plan = new CaptainLaunchIsolationPlan();
            if (mcpPort <= 0 || mcpPort > 65535) return plan;
            if (String.IsNullOrWhiteSpace(scopedConfigDirectory)) return plan;

            switch (runtime)
            {
                case AgentRuntimeEnum.ClaudeCode:
                    {
                        plan.FilesToWrite.Add(new IsolationConfigFile("armada-mcp.json", ArmadaMcpConfigBuilder.BuildKeyedMcpServersJson(mcpPort, mcpSessionToken, host)));
                        string mcpConfigPath = Path.Combine(scopedConfigDirectory, "armada-mcp.json");
                        plan.ExtraArguments.Add("--setting-sources");
                        plan.ExtraArguments.Add("project,local");
                        plan.ExtraArguments.Add("--strict-mcp-config");
                        plan.ExtraArguments.Add("--mcp-config");
                        plan.ExtraArguments.Add(mcpConfigPath);
                        break;
                    }
                case AgentRuntimeEnum.Codex:
                    {
                        plan.FilesToWrite.Add(new IsolationConfigFile("config.toml", ArmadaMcpConfigBuilder.BuildCodexConfigToml(mcpPort, mcpSessionToken, host)));
                        plan.EnvironmentOverrides["CODEX_HOME"] = scopedConfigDirectory;
                        break;
                    }
                case AgentRuntimeEnum.Gemini:
                    {
                        plan.FilesToWrite.Add(new IsolationConfigFile(Path.Combine(".gemini", "settings.json"), ArmadaMcpConfigBuilder.BuildKeyedMcpServersJson(mcpPort, mcpSessionToken, host)));
                        ApplyHomeOverride(plan, scopedConfigDirectory);
                        break;
                    }
                case AgentRuntimeEnum.Cursor:
                    {
                        plan.FilesToWrite.Add(new IsolationConfigFile(Path.Combine(".cursor", "mcp.json"), ArmadaMcpConfigBuilder.BuildKeyedMcpServersJson(mcpPort, mcpSessionToken, host)));
                        ApplyHomeOverride(plan, scopedConfigDirectory);
                        break;
                    }
                case AgentRuntimeEnum.Mux:
                    {
                        plan.FilesToWrite.Add(new IsolationConfigFile("mcp-servers.json", BuildMuxServersJson(mcpPort, host, mcpSessionToken)));
                        plan.EnvironmentOverrides["MUX_CONFIG_DIR"] = scopedConfigDirectory;
                        break;
                    }
                default:
                    break;
            }

            return plan;
        }

        /// <summary>
        /// True when <see cref="Plan(AgentRuntimeEnum, int, string, string?, string)"/> binds a session token for this
        /// runtime. A token launch with full isolation for any other runtime (OpenCode, which has no isolation plan) must
        /// use the per-invocation binding of <see cref="CaptainThreadMcpPlanner"/> instead, so it is never launched without
        /// its token.
        /// </summary>
        /// <param name="runtime">The captain's runtime.</param>
        /// <returns>True when the isolation plan carries the token.</returns>
        public static bool CarriesSessionToken(AgentRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AgentRuntimeEnum.ClaudeCode:
                case AgentRuntimeEnum.Codex:
                case AgentRuntimeEnum.Gemini:
                case AgentRuntimeEnum.Cursor:
                case AgentRuntimeEnum.Mux:
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Private-Methods

        private static string BuildMuxServersJson(int mcpPort, string host, string? mcpSessionToken)
        {
            if (String.IsNullOrWhiteSpace(mcpSessionToken)) return ArmadaMcpConfigBuilder.BuildMuxServersJson(mcpPort, host);

            // The scoped document lives in a per-launch directory deleted on exit, like the Claude Code and Codex files
            // that carry the token as a literal header.
            JsonObject server = new JsonObject
            {
                ["name"] = CaptainThreadMcpPlanner.ServerName,
                ["transport"] = "http",
                ["url"] = ArmadaMcpConfigBuilder.GetMcpBaseUrl(mcpPort, host),
                ["mcpPath"] = "/mcp",
                ["auth"] = new JsonObject
                {
                    ["type"] = "apikey",
                    ["apiKeyHeader"] = "X-Token",
                    ["apiKeyValue"] = mcpSessionToken,
                },
            };
            JsonObject root = new JsonObject { ["servers"] = new JsonArray(server) };
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private static void ApplyHomeOverride(CaptainLaunchIsolationPlan plan, string scopedConfigDirectory)
        {
            // HOME is honored on POSIX; USERPROFILE and HOMEPATH cover Windows CLIs that resolve the user
            // profile. Setting all three makes the scoped directory the effective home regardless of OS.
            plan.EnvironmentOverrides["HOME"] = scopedConfigDirectory;
            plan.EnvironmentOverrides["USERPROFILE"] = scopedConfigDirectory;
        }

        #endregion
    }
}
