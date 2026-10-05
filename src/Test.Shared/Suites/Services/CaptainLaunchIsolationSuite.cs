namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ArmadaMcpConfigBuilder"/> and <see cref="CaptainLaunchIsolationPlanner"/>.
    /// Positive cases confirm each runtime yields the right scoped-config files, arguments, and environment
    /// overrides; negative cases confirm an invalid port or missing scoped directory produces an empty plan
    /// (so isolation never half-applies and disabled launches stay unchanged).
    /// </summary>
    public sealed class CaptainLaunchIsolationSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the launch-isolation suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            string scoped = Path.Combine(Path.GetTempPath(), "armada-isolation-test");

            // ---- MCP config builder ----
            cases.Add(Case("mcp_url_matches_installer", "MCP URL matches the installer endpoint", TestTags.Positive, () =>
            {
                AssertEqual("http://localhost:7891/mcp", ArmadaMcpConfigBuilder.GetMcpUrl(7891));
            }));

            cases.Add(Case("keyed_config_registers_armada_http", "Keyed config registers the armada HTTP server", TestTags.Positive, () =>
            {
                string json = ArmadaMcpConfigBuilder.BuildKeyedMcpServersJson(7891);
                McpKeyedConfigFile config = JsonSerializer.Deserialize<McpKeyedConfigFile>(json)!;
                AssertNotNull(config.McpServers, "expected mcpServers key");
                AssertTrue(config.McpServers!.ContainsKey("armada"), "expected armada server key");
                McpKeyedServerEntry armada = config.McpServers["armada"];
                AssertEqual("http://localhost:7891/mcp", armada.Url, "expected mcp url");
                AssertEqual("http", armada.Type, "expected http transport");
                AssertNull(armada.Headers, "no headers without a session token");
            }));

            cases.Add(Case("mux_config_uses_servers_array", "Mux config uses a named servers array with mcpPath", TestTags.Positive, () =>
            {
                string json = ArmadaMcpConfigBuilder.BuildMuxServersJson(7891);
                MuxServersFile config = JsonSerializer.Deserialize<MuxServersFile>(json)!;
                AssertNotNull(config.Servers, "expected servers array");
                AssertEqual(1, config.Servers!.Count, "one server");
                MuxServerEntry server = config.Servers[0];
                AssertEqual("armada", server.Name, "expected armada name");
                AssertEqual("/mcp", server.McpPath, "expected mcpPath");
                AssertEqual("http://localhost:7891", server.Url, "expected base url");
                AssertEqual("http", server.Transport, "expected http transport");
            }));

            // ---- Planner: Claude Code ----
            cases.Add(Case("plan_claude_injects_strict_mcp", "Claude plan injects strict MCP config + scoped file", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, 7891, scoped);
                AssertFalse(plan.IsEmpty, "expected a non-empty plan");
                AssertTrue(plan.ExtraArguments.Contains("--strict-mcp-config"), "expected --strict-mcp-config");
                AssertTrue(plan.ExtraArguments.Contains("--mcp-config"), "expected --mcp-config");
                AssertTrue(plan.ExtraArguments.Contains("--setting-sources"), "expected --setting-sources");
                AssertTrue(plan.ExtraArguments.Contains("project,local"), "expected project,local source list");
                AssertEqual(1, plan.FilesToWrite.Count);
                AssertEqual("armada-mcp.json", plan.FilesToWrite[0].RelativePath);
                // The --mcp-config value must be the absolute path to the scoped file.
                int idx = plan.ExtraArguments.IndexOf("--mcp-config");
                AssertEqual(Path.Combine(scoped, "armada-mcp.json"), plan.ExtraArguments[idx + 1]);
            }));

            // ---- Planner: Codex ----
            cases.Add(Case("plan_codex_scopes_codex_home", "Codex plan scopes CODEX_HOME with a config.toml", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.Codex, 7891, scoped);
                AssertFalse(plan.IsEmpty, "expected a non-empty plan");
                AssertEqual(0, plan.ExtraArguments.Count);
                AssertTrue(plan.EnvironmentOverrides.ContainsKey("CODEX_HOME"), "expected CODEX_HOME override");
                AssertEqual(scoped, plan.EnvironmentOverrides["CODEX_HOME"]);
                AssertEqual("config.toml", plan.FilesToWrite[0].RelativePath);
            }));

            // ---- Planner: Gemini / Cursor (HOME override) ----
            cases.Add(Case("plan_gemini_overrides_home", "Gemini plan overrides HOME + writes settings.json", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.Gemini, 7891, scoped);
                AssertTrue(plan.EnvironmentOverrides.ContainsKey("HOME"), "expected HOME override");
                AssertTrue(plan.EnvironmentOverrides.ContainsKey("USERPROFILE"), "expected USERPROFILE override");
                AssertEqual(Path.Combine(".gemini", "settings.json"), plan.FilesToWrite[0].RelativePath);
            }));

            cases.Add(Case("plan_cursor_overrides_home", "Cursor plan overrides HOME + writes mcp.json", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.Cursor, 7891, scoped);
                AssertTrue(plan.EnvironmentOverrides.ContainsKey("HOME"), "expected HOME override");
                AssertEqual(Path.Combine(".cursor", "mcp.json"), plan.FilesToWrite[0].RelativePath);
            }));

            // ---- Planner: Mux ----
            cases.Add(Case("plan_mux_scopes_config_dir", "Mux plan scopes MUX_CONFIG_DIR with mcp-servers.json", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.Mux, 7891, scoped);
                AssertTrue(plan.EnvironmentOverrides.ContainsKey("MUX_CONFIG_DIR"), "expected MUX_CONFIG_DIR override");
                AssertEqual(scoped, plan.EnvironmentOverrides["MUX_CONFIG_DIR"]);
                AssertEqual("mcp-servers.json", plan.FilesToWrite[0].RelativePath);
            }));

            // ---- Negative cases ----
            cases.Add(Case("plan_empty_for_invalid_port", "Invalid/zero port yields an empty plan", TestTags.Negative, () =>
            {
                AssertTrue(CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, 0, scoped).IsEmpty, "port 0 should be empty");
                AssertTrue(CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, -5, scoped).IsEmpty, "negative port should be empty");
                AssertTrue(CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, 70000, scoped).IsEmpty, "out-of-range port should be empty");
            }));

            cases.Add(Case("plan_empty_for_missing_scoped_dir", "Missing scoped directory yields an empty plan", TestTags.Negative, () =>
            {
                AssertTrue(CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, 7891, "").IsEmpty, "empty scoped dir should be empty");
                AssertTrue(CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, 7891, "   ").IsEmpty, "whitespace scoped dir should be empty");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.CaptainLaunchIsolation",
                displayName: "Captain Launch Isolation",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.CaptainLaunchIsolation",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
