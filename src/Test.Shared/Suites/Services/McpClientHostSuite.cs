namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Mcp;
    using Armada.Server;
    using Armada.Server.RuntimeTools;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Generated Armada MCP client URLs use the host the MCP listener is bound with. Regression coverage for an Admiral
    /// configured with rest.hostname 127.0.0.1: the listener only answers that exact host, but every generated client URL
    /// hard-coded localhost, so captains and 'armada mcp install' got HTTP 404. Also covers recognition of an existing
    /// client entry written with localhost as the same Armada server.
    /// </summary>
    public sealed class McpClientHostSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.McpClientHost";
        private const int _Port = 7891;
        private const string _Loopback = "127.0.0.1";
        private const string _Token = "tok_host_secret";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("client_host_for_maps_bindings", "ClientHostFor maps wildcards to localhost, brackets IPv6, and keeps named hosts", TestTags.Positive, () =>
            {
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor(null));
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor(""));
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor("  "));
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor("*"));
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor("+"));
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor("0.0.0.0"));
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor("::"));
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor("[::]"));
                AssertEqual("localhost", ArmadaMcpConfigBuilder.ClientHostFor("localhost"));
                AssertEqual("127.0.0.1", ArmadaMcpConfigBuilder.ClientHostFor("127.0.0.1"));
                AssertEqual("[::1]", ArmadaMcpConfigBuilder.ClientHostFor("::1"));
                AssertEqual("[::1]", ArmadaMcpConfigBuilder.ClientHostFor("[::1]"));
                AssertEqual("192.168.1.20", ArmadaMcpConfigBuilder.ClientHostFor("192.168.1.20"));
                AssertEqual("admiral.example.com", ArmadaMcpConfigBuilder.ClientHostFor(" admiral.example.com "));
            }));

            cases.Add(Case("default_urls_stay_localhost", "Builder overloads without a host still generate localhost URLs", TestTags.Positive, () =>
            {
                AssertEqual("http://localhost:7891/mcp", ArmadaMcpConfigBuilder.GetMcpUrl(_Port));
                AssertEqual("http://localhost:7891/mcp", ArmadaMcpConfigBuilder.GetMcpUrl(_Port, ""));
                AssertEqual("http://localhost:7891", ArmadaMcpConfigBuilder.GetMcpBaseUrl(_Port, "localhost"));
                AssertEqual("localhost", new CaptainThreadMcpPlanRequest().McpHost);
            }));

            cases.Add(Case("urls_use_supplied_host", "GetMcpUrl and GetMcpBaseUrl use the supplied host, including bracketed IPv6", TestTags.Positive, () =>
            {
                AssertEqual("http://127.0.0.1:7891/mcp", ArmadaMcpConfigBuilder.GetMcpUrl(_Port, _Loopback));
                AssertEqual("http://127.0.0.1:7891", ArmadaMcpConfigBuilder.GetMcpBaseUrl(_Port, _Loopback));
                AssertEqual("http://[::1]:7891/mcp", ArmadaMcpConfigBuilder.GetMcpUrl(_Port, ArmadaMcpConfigBuilder.ClientHostFor("::1")));
                Uri parsed = new Uri(ArmadaMcpConfigBuilder.GetMcpUrl(_Port, ArmadaMcpConfigBuilder.ClientHostFor("::1")));
                AssertEqual(_Port, parsed.Port, "IPv6 URL must parse with the MCP port");
            }));

            cases.Add(Case("keyed_config_uses_host", "Claude/Gemini/Cursor keyed config carries the 127.0.0.1 URL and the token header", TestTags.Positive, () =>
            {
                KeyedMcpServersDocument doc = JsonHelper.Deserialize<KeyedMcpServersDocument>(ArmadaMcpConfigBuilder.BuildKeyedMcpServersJson(_Port, _Token, _Loopback));
                AssertNotNull(doc.McpServers, "mcpServers");
                AssertTrue(doc.McpServers!.ContainsKey("armada"), "armada entry");
                AssertEqual("http", doc.McpServers["armada"].Type);
                AssertEqual("http://127.0.0.1:7891/mcp", doc.McpServers["armada"].Url);
                AssertEqual(_Token, doc.McpServers["armada"].Headers!["X-Token"]);
            }));

            cases.Add(Case("codex_toml_uses_host", "Codex TOML registers the 127.0.0.1 URL", TestTags.Positive, () =>
            {
                string[] lines = ArmadaMcpConfigBuilder.BuildCodexConfigToml(_Port, null, _Loopback).Replace("\r\n", "\n").Split('\n');
                AssertEqual("[mcp_servers.armada]", lines[0]);
                AssertEqual("url = \"http://127.0.0.1:7891/mcp\"", lines[1]);
            }));

            cases.Add(Case("mux_json_uses_host", "Mux servers document uses the 127.0.0.1 base URL with the /mcp path", TestTags.Positive, () =>
            {
                MuxServersDocument doc = JsonHelper.Deserialize<MuxServersDocument>(ArmadaMcpConfigBuilder.BuildMuxServersJson(_Port, _Loopback));
                AssertEqual(1, doc.Servers!.Count);
                AssertEqual("armada", doc.Servers[0].Name);
                AssertEqual("http://127.0.0.1:7891", doc.Servers[0].Url);
                AssertEqual("/mcp", doc.Servers[0].McpPath);
            }));

            cases.Add(Case("isolation_plans_use_host", "Isolated launch plans for every runtime write the 127.0.0.1 URL", TestTags.Positive, () =>
            {
                string scoped = Path.Combine(Path.GetTempPath(), "armada-mcp-host-test");
                CaptainLaunchIsolationPlan claude = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, _Port, scoped, null, _Loopback);
                AssertEqual("http://127.0.0.1:7891/mcp", JsonHelper.Deserialize<KeyedMcpServersDocument>(claude.FilesToWrite[0].Contents).McpServers!["armada"].Url);

                CaptainLaunchIsolationPlan gemini = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.Gemini, _Port, scoped, null, _Loopback);
                AssertEqual("http://127.0.0.1:7891/mcp", JsonHelper.Deserialize<KeyedMcpServersDocument>(gemini.FilesToWrite[0].Contents).McpServers!["armada"].Url);

                CaptainLaunchIsolationPlan cursor = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.Cursor, _Port, scoped, null, _Loopback);
                AssertEqual("http://127.0.0.1:7891/mcp", JsonHelper.Deserialize<KeyedMcpServersDocument>(cursor.FilesToWrite[0].Contents).McpServers!["armada"].Url);

                CaptainLaunchIsolationPlan codex = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.Codex, _Port, scoped, null, _Loopback);
                AssertTrue(codex.FilesToWrite[0].Contents.Replace("\r\n", "\n").Split('\n').Contains("url = \"http://127.0.0.1:7891/mcp\""), "codex url line");

                CaptainLaunchIsolationPlan mux = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.Mux, _Port, scoped, null, _Loopback);
                AssertEqual("http://127.0.0.1:7891", JsonHelper.Deserialize<MuxServersDocument>(mux.FilesToWrite[0].Contents).Servers![0].Url);
            }));

            cases.Add(Case("thread_plans_use_request_host", "Thread-scoped plans use the request's McpHost for every runtime", TestTags.Positive, () =>
            {
                CaptainLaunchIsolationPlan claude = CaptainThreadMcpPlanner.Plan(ThreadRequest(AgentRuntimeEnum.ClaudeCode));
                AssertEqual("http://127.0.0.1:7891/mcp", JsonHelper.Deserialize<KeyedMcpServersDocument>(claude.FilesToWrite[0].Contents).McpServers!["armada"].Url);

                CaptainLaunchIsolationPlan codex = CaptainThreadMcpPlanner.Plan(ThreadRequest(AgentRuntimeEnum.Codex));
                AssertTrue(codex.ExtraArguments[1].Contains("url = \"http://127.0.0.1:7891/mcp\""), "codex -c url: " + codex.ExtraArguments[1]);

                CaptainLaunchIsolationPlan gemini = CaptainThreadMcpPlanner.Plan(ThreadRequest(AgentRuntimeEnum.Gemini));
                AssertEqual("http://127.0.0.1:7891/mcp", JsonHelper.Deserialize<KeyedMcpServersDocument>(gemini.FilesToWrite[0].Contents).McpServers!["armada"].HttpUrl);

                CaptainLaunchIsolationPlan cursor = CaptainThreadMcpPlanner.Plan(ThreadRequest(AgentRuntimeEnum.Cursor));
                AssertEqual("http://127.0.0.1:7891/mcp", JsonHelper.Deserialize<KeyedMcpServersDocument>(cursor.FilesToWrite[0].Contents).McpServers!["armada"].Url);

                CaptainLaunchIsolationPlan mux = CaptainThreadMcpPlanner.Plan(ThreadRequest(AgentRuntimeEnum.Mux));
                AssertEqual("http://127.0.0.1:7891", JsonHelper.Deserialize<MuxServersDocument>(mux.FilesToWrite[0].Contents).Servers![0].Url);

                CaptainLaunchIsolationPlan openCode = CaptainThreadMcpPlanner.Plan(ThreadRequest(AgentRuntimeEnum.OpenCode));
                KeyedMcpServersDocument openCodeDoc = JsonHelper.Deserialize<KeyedMcpServersDocument>(openCode.EnvironmentOverrides["OPENCODE_CONFIG_CONTENT"]);
                AssertEqual("http://127.0.0.1:7891/mcp", openCodeDoc.Mcp!["armada"].Url);
            }));

            cases.Add(Case("loopback_aliases_are_the_same_armada_server", "Entries at localhost, 127.0.0.1 or ::1 on the MCP port are recognized as Armada; other hosts and ports are not", TestTags.Positive, () =>
            {
                string json = "{ \"mcpServers\": {"
                    + " \"fleet-local\": { \"url\": \"http://localhost:7891/mcp\" },"
                    + " \"fleet-v4\": { \"url\": \"http://127.0.0.1:7891/mcp\" },"
                    + " \"fleet-v6\": { \"url\": \"http://[::1]:7891/mcp\" },"
                    + " \"mux-style\": { \"url\": \"http://localhost:7891\" },"
                    + " \"other-port\": { \"url\": \"http://localhost:7999/mcp\" },"
                    + " \"remote-base\": { \"url\": \"http://remote.example.com:7891\" },"
                    + " \"github\": { \"command\": \"gh-mcp\" } } }";
                List<string> names = CaptainThreadMcpPlanner.FindKeyedArmadaServerNames(json, _Port, _Loopback);
                AssertTrue(names.Contains("fleet-local"), "localhost entry");
                AssertTrue(names.Contains("fleet-v4"), "127.0.0.1 entry");
                AssertTrue(names.Contains("fleet-v6"), "::1 entry");
                AssertTrue(names.Contains("mux-style"), "loopback base URL without the path");
                AssertFalse(names.Contains("other-port"), "another port is not Armada");
                AssertFalse(names.Contains("remote-base"), "a remote base URL is not Armada");
                AssertFalse(names.Contains("github"), "unrelated server");

                string toml = "[mcp_servers.fleet]\nurl = \"http://localhost:7891/mcp\"\n[mcp_servers.v6]\nurl = \"http://[::1]:7891\"\n[mcp_servers.docs]\nurl = \"http://localhost:9000/mcp\"\n";
                List<string> codexNames = CaptainThreadMcpPlanner.FindCodexArmadaServerNames(toml, _Port, _Loopback);
                AssertEqual(2, codexNames.Count);
                AssertEqual("fleet", codexNames[0]);
                AssertEqual("v6", codexNames[1]);
            }));

            cases.Add(Case("is_armada_mcp_url_and_rewrite", "IsArmadaMcpUrl accepts loopback aliases and the configured host; RewriteArmadaMcpUrlHost moves them to the bound host", TestTags.Positive, () =>
            {
                AssertTrue(ArmadaMcpConfigBuilder.IsArmadaMcpUrl("http://localhost:7891/mcp", _Port, _Loopback), "localhost");
                AssertTrue(ArmadaMcpConfigBuilder.IsArmadaMcpUrl("http://[::1]:7891/mcp", _Port, _Loopback), "::1");
                AssertTrue(ArmadaMcpConfigBuilder.IsArmadaMcpUrl("http://admiral.example.com:7891/mcp", _Port, "admiral.example.com"), "configured host");
                AssertFalse(ArmadaMcpConfigBuilder.IsArmadaMcpUrl("http://localhost:7891/other", _Port, _Loopback), "other path");
                AssertFalse(ArmadaMcpConfigBuilder.IsArmadaMcpUrl("http://localhost:7892/mcp", _Port, _Loopback), "other port");
                AssertFalse(ArmadaMcpConfigBuilder.IsArmadaMcpUrl("not a url", _Port, _Loopback), "not a url");
                AssertFalse(ArmadaMcpConfigBuilder.IsArmadaMcpUrl(null, _Port, _Loopback), "null");

                AssertEqual("http://127.0.0.1:7891/mcp", ArmadaMcpConfigBuilder.RewriteArmadaMcpUrlHost("http://localhost:7891/mcp", _Port, _Loopback));
                AssertEqual("http://localhost:7891/mcp", ArmadaMcpConfigBuilder.RewriteArmadaMcpUrlHost("http://127.0.0.1:7891/mcp", _Port, "localhost"));
                AssertEqual("http://[::1]:7891/mcp", ArmadaMcpConfigBuilder.RewriteArmadaMcpUrlHost("http://localhost:7891/mcp", _Port, "[::1]"));
                AssertEqual("http://localhost:9000/mcp", ArmadaMcpConfigBuilder.RewriteArmadaMcpUrlHost("http://localhost:9000/mcp", _Port, _Loopback));
            }));

            cases.Add(Case("loopback_literal_warning", "A loopback IP literal hostname yields one warning naming the generated URL; other hostnames none", TestTags.Positive, () =>
            {
                string? v4 = ArmadaMcpConfigBuilder.LoopbackLiteralHostWarning("127.0.0.1", _Port);
                AssertNotNull(v4, "127.0.0.1 warns");
                AssertContains("http://127.0.0.1:7891/mcp", v4!);
                string? v6 = ArmadaMcpConfigBuilder.LoopbackLiteralHostWarning("::1", _Port);
                AssertNotNull(v6, "::1 warns");
                AssertContains("http://[::1]:7891/mcp", v6!);
                AssertNull(ArmadaMcpConfigBuilder.LoopbackLiteralHostWarning("localhost", _Port), "localhost");
                AssertNull(ArmadaMcpConfigBuilder.LoopbackLiteralHostWarning("0.0.0.0", _Port), "wildcard");
                AssertNull(ArmadaMcpConfigBuilder.LoopbackLiteralHostWarning("*", _Port), "wildcard");
                AssertNull(ArmadaMcpConfigBuilder.LoopbackLiteralHostWarning("192.168.1.20", _Port), "lan address");
                AssertNull(ArmadaMcpConfigBuilder.LoopbackLiteralHostWarning(null, _Port), "null");
            }));

            cases.Add(CaseAsync("tool_catalog_recognizes_localhost_entry", "The tool catalog probes a localhost Armada entry at the bound host and counts its tools as Armada tools", TestTags.Positive, async () =>
            {
                RecordingRuntimeToolDiscoverySource discovery = new RecordingRuntimeToolDiscoverySource();
                discovery.ProfileDirectory = Path.Combine(Path.GetTempPath(), "armada-mcp-host-profile-" + Guid.NewGuid().ToString("N"));
                discovery.ConfigFiles[Path.Combine(discovery.ProfileDirectory, ".claude.json")] =
                    "{ \"mcpServers\": { \"fleet\": { \"type\": \"http\", \"url\": \"http://localhost:7891/mcp\" }, \"docs\": { \"type\": \"http\", \"url\": \"http://localhost:9000/mcp\" } } }";
                discovery.ServerTools["fleet"] = new List<McpRemoteTool> { new McpRemoteTool { Name = "armada_status" }, new McpRemoteTool { Name = "armada_enumerate" } };
                discovery.ServerTools["docs"] = new List<McpRemoteTool> { new McpRemoteTool { Name = "search_docs" } };

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    CaptainRuntimeToolCatalogService catalog = new CaptainRuntimeToolCatalogService(logging, null, discovery, _Port, _Loopback);
                    Captain captain = new Captain("host-test") { Runtime = AgentRuntimeEnum.ClaudeCode };
                    CaptainRuntimeToolCatalogService.RuntimeToolCatalogSnapshot? snapshot = await catalog.TryDescribeAsync(captain, testDb.Driver).ConfigureAwait(false);

                    AssertNotNull(snapshot, "snapshot");
                    AssertEqual(2, snapshot!.ArmadaToolCount, "tools of the localhost Armada entry count as Armada tools");
                    AssertEqual(3, snapshot.EffectiveToolCount, "all tools");
                    List<string> urls = discovery.ServerProbeUrls.ToList();
                    AssertTrue(urls.Contains("http://127.0.0.1:7891/mcp"), "Armada entry probed at the bound host: " + String.Join(", ", urls));
                    AssertTrue(urls.Contains("http://localhost:9000/mcp"), "unrelated entry probed unchanged");
                    AssertFalse(urls.Contains("http://localhost:7891/mcp"), "Armada entry not probed at the unbound host");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "MCP Client Host", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static CaptainThreadMcpPlanRequest ThreadRequest(AgentRuntimeEnum runtime)
        {
            return new CaptainThreadMcpPlanRequest
            {
                Runtime = runtime,
                McpPort = _Port,
                McpHost = _Loopback,
                ScopedConfigDirectory = Path.Combine(Path.GetTempPath(), "armada-mcp-host-test", "scoped"),
                WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada-mcp-host-test", "work"),
                SessionToken = _Token
            };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
