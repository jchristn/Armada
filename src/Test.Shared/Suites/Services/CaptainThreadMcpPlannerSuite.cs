namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="CaptainThreadMcpPlanner"/>: the per-runtime configuration that binds an Ask thread
    /// turn's Armada MCP connection to a thread-scoped token without hiding the CLI's own login. Every plan must carry the
    /// token in the environment (never on the command line), must never redirect HOME / CODEX_HOME / MUX_CONFIG_DIR, and
    /// must neutralize the host user's own Armada entries.
    /// </summary>
    public sealed class CaptainThreadMcpPlannerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string _Token = "tok_thread_secret";
        private const int _Port = 7891;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the thread-scoped MCP planner suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("gating_supported_for_every_product_runtime", "Every product runtime supports Ask approval gating", TestTags.Positive, () =>
            {
                AgentRuntimeEnum[] gated = new AgentRuntimeEnum[]
                {
                    AgentRuntimeEnum.ClaudeCode, AgentRuntimeEnum.Codex, AgentRuntimeEnum.Gemini, AgentRuntimeEnum.Cursor,
                    AgentRuntimeEnum.Mux, AgentRuntimeEnum.OpenCode, AgentRuntimeEnum.ApiEndpoint
                };
                foreach (AgentRuntimeEnum runtime in gated)
                {
                    AssertTrue(CaptainThreadMcpPlanner.SupportsApprovalGating(runtime), runtime + " should be gated");
                }

                AssertFalse(CaptainThreadMcpPlanner.SupportsApprovalGating(AgentRuntimeEnum.Custom), "Custom is not gated");
            }));

            cases.Add(Case("cli_plans_never_hide_login", "No CLI plan redirects HOME, CODEX_HOME, or the Mux config dir, and no token is on the command line", TestTags.Positive, () =>
            {
                AgentRuntimeEnum[] runtimes = new AgentRuntimeEnum[]
                {
                    AgentRuntimeEnum.Codex, AgentRuntimeEnum.Gemini, AgentRuntimeEnum.Cursor, AgentRuntimeEnum.Mux, AgentRuntimeEnum.OpenCode
                };
                foreach (AgentRuntimeEnum runtime in runtimes)
                {
                    CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(Request(runtime));
                    AssertFalse(plan.IsEmpty, runtime + " plan should not be empty");
                    foreach (string key in new string[] { "HOME", "USERPROFILE", "CODEX_HOME", "MUX_CONFIG_DIR", "XDG_CONFIG_HOME" })
                    {
                        AssertFalse(plan.EnvironmentOverrides.ContainsKey(key), runtime + " must not override " + key);
                    }

                    AssertEqual(_Token, plan.EnvironmentOverrides[CaptainThreadMcpPlanner.TokenEnvironmentVariable]);
                    AssertFalse(plan.ExtraArguments.Any(a => a.Contains(_Token)), runtime + " must not put the token on the command line");
                    AssertFalse(plan.FilesToWrite.Any(f => f.Contents.Contains(_Token)), runtime + " must not write the token to disk");
                }
            }));

            cases.Add(Case("claude_uses_strict_scoped_config", "Claude Code keeps the strict per-launch MCP config with the token header", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(Request(AgentRuntimeEnum.ClaudeCode));
                AssertTrue(plan.ExtraArguments.Contains("--strict-mcp-config"), "expected strict MCP config");
                AssertEqual(1, plan.FilesToWrite.Count);
                AssertTrue(plan.FilesToWrite[0].Contents.Contains("X-Token"), "expected X-Token header");
                AssertFalse(plan.FilesToWrite[0].RelativeToWorkingDirectory, "Claude config lives in the scoped directory");
            }));

            cases.Add(Case("codex_adds_env_header_server", "Codex adds a streamable HTTP server whose X-Token comes from the environment", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(Request(AgentRuntimeEnum.Codex));
                AssertEqual(0, plan.FilesToWrite.Count);
                AssertEqual(2, plan.ExtraArguments.Count);
                AssertEqual("-c", plan.ExtraArguments[0]);
                string value = plan.ExtraArguments[1];
                AssertTrue(value.StartsWith("mcp_servers.armada={", StringComparison.Ordinal), "expected a fresh armada table: " + value);
                AssertTrue(value.Contains("url = \"http://localhost:7891/mcp\""), "expected MCP url");
                AssertTrue(value.Contains("env_http_headers = { \"X-Token\" = \"ARMADA_MCP_TOKEN\" }"), "expected env header mapping");
                AssertTrue(value.Contains("default_tools_approval_mode = \"approve\""), "codex exec must not refuse MCP calls");
            }));

            cases.Add(Case("codex_disables_host_armada_entries", "Codex disables the user's Armada entries and uses a fresh name", TestTags.Positive, () =>
            {
                CaptainThreadMcpPlanRequest request = Request(AgentRuntimeEnum.Codex);
                request.HostCodexConfigToml =
                    "model = \"gpt-5\"\n"
                    + "[mcp_servers.armada]\ncommand = \"dotnet\"\nargs = [\"/x/Armada.Helm.dll\", \"mcp\", \"stdio\"]\n"
                    + "[mcp_servers.armada.env]\nFOO = \"1\"\n"
                    + "[mcp_servers.fleet]\nurl = \"http://localhost:7891/mcp\"\n"
                    + "[mcp_servers.\"my docs\"]\ncommand = \"docs-server\"\n"
                    + "[mcp_servers.node_repl]\ncommand = \"/usr/bin/node_repl\"\n";
                CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(request);
                AssertTrue(plan.ExtraArguments.Contains("mcp_servers.armada.enabled=false"), "expected armada disabled");
                AssertTrue(plan.ExtraArguments.Contains("mcp_servers.fleet.enabled=false"), "expected port alias disabled");
                AssertFalse(plan.ExtraArguments.Any(a => a.Contains("node_repl")), "unrelated servers stay untouched");
                AssertFalse(plan.ExtraArguments.Any(a => a.Contains("my docs")), "unrelated servers stay untouched");
                AssertTrue(plan.ExtraArguments.Any(a => a.StartsWith("mcp_servers.armada_ask={", StringComparison.Ordinal)), "expected fresh armada_ask server");
            }));

            cases.Add(Case("codex_alias_finder_handles_inline_tables", "Codex alias finder reads inline entries and ignores comments", TestTags.Positive, () =>
            {
                string toml = "# [mcp_servers.armada]\n[mcp_servers]\narmada = { command = \"armada\", args = [\"mcp\", \"stdio\"] }\nother = { command = \"x\" }\n";
                List<string> names = CaptainThreadMcpPlanner.FindCodexArmadaServerNames(toml, _Port);
                AssertEqual(1, names.Count);
                AssertEqual("armada", names[0]);
                AssertEqual(0, CaptainThreadMcpPlanner.FindCodexArmadaServerNames(null, _Port).Count);
                AssertEqual(0, CaptainThreadMcpPlanner.FindCodexArmadaServerNames("not = [toml", _Port).Count);
            }));

            cases.Add(Case("gemini_writes_workspace_settings", "Gemini writes workspace settings, trusts the throwaway workspace, and allows only armada", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(Request(AgentRuntimeEnum.Gemini));
                AssertEqual(1, plan.FilesToWrite.Count);
                IsolationConfigFile file = plan.FilesToWrite[0];
                AssertTrue(file.RelativeToWorkingDirectory, "expected a workspace file");
                AssertEqual(Path.Combine(".gemini", "settings.json"), file.RelativePath);
                JsonNode? root = JsonNode.Parse(file.Contents);
                AssertEqual("http://localhost:7891/mcp", root!["mcpServers"]!["armada"]!["httpUrl"]!.GetValue<string>());
                AssertEqual("$ARMADA_MCP_TOKEN", root["mcpServers"]!["armada"]!["headers"]!["X-Token"]!.GetValue<string>());
                AssertEqual("true", plan.EnvironmentOverrides["GEMINI_CLI_TRUST_WORKSPACE"]);
                int idx = plan.ExtraArguments.IndexOf("--allowed-mcp-server-names");
                AssertTrue(idx >= 0, "expected allowed server names");
                AssertEqual("armada", plan.ExtraArguments[idx + 1]);
            }));

            cases.Add(Case("cursor_redefines_host_aliases", "Cursor project config redefines armada and every host Armada alias", TestTags.Positive, () =>
            {
                CaptainThreadMcpPlanRequest request = Request(AgentRuntimeEnum.Cursor);
                request.HostCursorMcpJson = "{ \"mcpServers\": { \"armada-local\": { \"url\": \"http://localhost:7891/mcp\" }, \"github\": { \"command\": \"gh-mcp\" } } }";
                CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(request);
                AssertEqual(0, plan.ExtraArguments.Count);
                IsolationConfigFile file = plan.FilesToWrite.Single();
                AssertTrue(file.RelativeToWorkingDirectory, "expected a project file");
                AssertEqual(Path.Combine(".cursor", "mcp.json"), file.RelativePath);
                JsonNode? servers = JsonNode.Parse(file.Contents)!["mcpServers"];
                AssertEqual("${env:ARMADA_MCP_TOKEN}", servers!["armada"]!["headers"]!["X-Token"]!.GetValue<string>());
                AssertEqual("${env:ARMADA_MCP_TOKEN}", servers["armada-local"]!["headers"]!["X-Token"]!.GetValue<string>());
                AssertTrue(servers["github"] == null, "unrelated servers are not redefined");
            }));

            cases.Add(Case("mux_strict_config_with_apikey_header", "Mux uses a strict scoped server document with an X-Token API-key header", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(Request(AgentRuntimeEnum.Mux));
                IsolationConfigFile file = plan.FilesToWrite.Single();
                AssertFalse(file.RelativeToWorkingDirectory, "Mux config lives in the scoped directory");
                JsonNode? server = JsonNode.Parse(file.Contents)!["servers"]![0];
                AssertEqual("http://localhost:7891", server!["url"]!.GetValue<string>());
                AssertEqual("/mcp", server["mcpPath"]!.GetValue<string>());
                AssertEqual("apikey", server["auth"]!["type"]!.GetValue<string>());
                AssertEqual("X-Token", server["auth"]!["apiKeyHeader"]!.GetValue<string>());
                AssertEqual("${ARMADA_MCP_TOKEN}", server["auth"]!["apiKeyValue"]!.GetValue<string>());
                int idx = plan.ExtraArguments.IndexOf("--mcp-config");
                AssertEqual(Path.Combine(ScopedDirectory(), "mcp-servers.json"), plan.ExtraArguments[idx + 1]);
                AssertTrue(plan.ExtraArguments.Contains("--strict-mcp-config"), "expected strict");
            }));

            cases.Add(Case("opencode_inline_config_disables_aliases", "OpenCode inline config disables host aliases, keeps user inline content, and adds a fresh remote server", TestTags.Positive, () =>
            {
                CaptainThreadMcpPlanRequest request = Request(AgentRuntimeEnum.OpenCode);
                request.HostOpenCodeConfigDocuments = new List<string>
                {
                    "// user config\n{ \"mcp\": { \"armada\": { \"type\": \"remote\", \"url\": \"http://localhost:7891/mcp\", \"headers\": { \"Authorization\": \"Bearer x\" } }, \"fs\": { \"type\": \"local\", \"command\": [\"fs\"] }, }, }"
                };
                request.ExistingOpenCodeConfigContent = "{ \"theme\": \"dark\" }";
                CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(request);
                AssertEqual(0, plan.FilesToWrite.Count);
                JsonNode? root = JsonNode.Parse(plan.EnvironmentOverrides["OPENCODE_CONFIG_CONTENT"]);
                AssertEqual("dark", root!["theme"]!.GetValue<string>());
                AssertFalse(root["mcp"]!["armada"]!["enabled"]!.GetValue<bool>(), "host armada disabled");
                AssertTrue(root["mcp"]!["fs"] == null, "unrelated servers untouched");
                JsonNode? fresh = root["mcp"]!["armada_ask"];
                AssertEqual("remote", fresh!["type"]!.GetValue<string>());
                AssertEqual("{env:ARMADA_MCP_TOKEN}", fresh["headers"]!["X-Token"]!.GetValue<string>());
                AssertTrue(fresh["enabled"]!.GetValue<bool>(), "fresh server enabled");
            }));

            cases.Add(Case("opencode_without_host_config_uses_armada", "OpenCode with no host config adds the armada server", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan plan = CaptainThreadMcpPlanner.Plan(Request(AgentRuntimeEnum.OpenCode));
                JsonNode? root = JsonNode.Parse(plan.EnvironmentOverrides["OPENCODE_CONFIG_CONTENT"]);
                AssertEqual("http://localhost:7891/mcp", root!["mcp"]!["armada"]!["url"]!.GetValue<string>());
            }));

            cases.Add(Case("incomplete_requests_yield_empty_plans", "Missing token, bad port, missing directories, or ApiEndpoint yield an empty plan", TestTags.Negative, () =>
            {
                CaptainThreadMcpPlanRequest noToken = Request(AgentRuntimeEnum.Codex);
                noToken.SessionToken = "";
                AssertTrue(CaptainThreadMcpPlanner.Plan(noToken).IsEmpty, "no token");
                CaptainThreadMcpPlanRequest badPort = Request(AgentRuntimeEnum.Gemini);
                badPort.McpPort = 70000;
                AssertTrue(CaptainThreadMcpPlanner.Plan(badPort).IsEmpty, "bad port");
                CaptainThreadMcpPlanRequest noWork = Request(AgentRuntimeEnum.Cursor);
                noWork.WorkingDirectory = " ";
                AssertTrue(CaptainThreadMcpPlanner.Plan(noWork).IsEmpty, "no working directory");
                AssertTrue(CaptainThreadMcpPlanner.Plan(Request(AgentRuntimeEnum.ApiEndpoint)).IsEmpty, "ApiEndpoint is bound in-process");
                AssertThrows<ArgumentNullException>(() => CaptainThreadMcpPlanner.Plan(null!));
            }));

            cases.Add(Case("keyed_alias_finder_tolerates_bad_json", "Keyed alias finder returns nothing for malformed JSON", TestTags.Negative, () =>
            {
                AssertEqual(0, CaptainThreadMcpPlanner.FindKeyedArmadaServerNames("{ not json", _Port).Count);
                AssertEqual(0, CaptainThreadMcpPlanner.FindKeyedArmadaServerNames("[]", _Port).Count);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.CaptainThreadMcpPlanner",
                displayName: "Captain Thread-Scoped MCP Planner",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string ScopedDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "armada-thread-mcp-test", "scoped");
        }

        private static CaptainThreadMcpPlanRequest Request(AgentRuntimeEnum runtime)
        {
            return new CaptainThreadMcpPlanRequest
            {
                Runtime = runtime,
                McpPort = _Port,
                ScopedConfigDirectory = ScopedDirectory(),
                WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada-thread-mcp-test", "work"),
                SessionToken = _Token
            };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.CaptainThreadMcpPlanner",
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
