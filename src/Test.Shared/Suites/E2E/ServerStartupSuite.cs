namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Startup failure modes of the Admiral's two listeners. Regression coverage for the E2E startup flake: when the MCP
    /// port was already taken, the MCP listener failed on an unobserved background task, the server logged "MCP server
    /// started" anyway, and callers could only time out waiting for it. A taken port must now fail startup immediately
    /// with a message naming the port.
    /// </summary>
    public sealed class ServerStartupSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.ServerStartup";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("mcp_port_in_use_fails_startup", "MCP port already in use fails startup with a clear error", TestTags.Negative, async () =>
            {
                TcpListener squatter = new TcpListener(IPAddress.Loopback, 0);
                squatter.Start();
                int mcpPort = ((IPEndPoint)squatter.LocalEndpoint).Port;
                ArmadaServer server = CreateServer(FreePort(), mcpPort, out string tempDir);
                try
                {
                    ListenerBindException? caught = null;
                    try
                    {
                        await server.StartAsync().ConfigureAwait(false);
                    }
                    catch (ListenerBindException ex)
                    {
                        caught = ex;
                    }

                    AssertNotNull(caught, "StartAsync must throw a typed ListenerBindException when the MCP port is taken");
                    AssertEqual("MCP", caught!.Listener, "the MCP listener failed");
                    AssertEqual(mcpPort, caught.Port, "the taken port");
                    AssertEqual("127.0.0.1", caught.Hostname, "the configured host");
                }
                finally
                {
                    try { server.Stop(); } catch (Exception) { }
                    squatter.Stop();
                    TestTemp.TryDelete(tempDir);
                }
            }));

            cases.Add(CaseAsync("both_listeners_ready_after_start", "Both listeners answer as soon as StartAsync returns", TestTags.Positive, async () =>
            {
                int restPort = FreePort();
                int mcpPort = FreePort();
                ArmadaServer server = CreateServer(restPort, mcpPort, out string tempDir);
                try
                {
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        HttpResponseMessage rest = await client.GetAsync("http://127.0.0.1:" + restPort + "/api/v1/status/health").ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.OK, rest.StatusCode, "REST health");
                        HttpResponseMessage mcp = await client.GetAsync("http://127.0.0.1:" + mcpPort + "/").ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.OK, mcp.StatusCode, "MCP health");
                    }
                }
                finally
                {
                    try { server.Stop(); } catch (Exception) { }
                    TestTemp.TryDelete(tempDir);
                }
            }));

            cases.Add(CaseAsync("generated_captain_mcp_url_answers_on_loopback_literal", "A captain MCP URL generated for an Admiral bound to 127.0.0.1 answers initialize with 200", TestTags.Positive, async () =>
            {
                int restPort = FreePort();
                int mcpPort = FreePort();
                ArmadaServer server = CreateServer(restPort, mcpPort, out string tempDir);
                try
                {
                    await server.StartAsync().ConfigureAwait(false);

                    // The URL a captain is handed: the isolated Claude Code launch config built for the configured hostname
                    // (the same host AgentLifecycleHandler and CaptainChatService pass). Before the fix it was localhost,
                    // which this listener answers with HTTP 404.
                    string host = ArmadaMcpConfigBuilder.ClientHostFor("127.0.0.1");
                    CaptainLaunchIsolationPlan plan = CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, mcpPort, tempDir, null, host);
                    KeyedMcpServersDocument document = JsonHelper.Deserialize<KeyedMcpServersDocument>(plan.FilesToWrite[0].Contents);
                    string url = document.McpServers!["armada"].Url!;
                    AssertEqual("http://127.0.0.1:" + mcpPort + "/mcp", url, "generated captain MCP URL");

                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Content = JsonHelper.ToJsonContent(new
                        {
                            jsonrpc = "2.0",
                            id = 1,
                            method = "initialize",
                            @params = new
                            {
                                protocolVersion = "2024-11-05",
                                capabilities = new { },
                                clientInfo = new { name = "mcp-host-test", version = "1.0" }
                            }
                        });
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                        HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.OK, response.StatusCode, "initialize at the generated URL");
                        AssertTrue(response.Headers.Contains("Mcp-Session-Id"), "initialize assigns an MCP session");
                    }
                }
                finally
                {
                    try { server.Stop(); } catch (Exception) { }
                    TestTemp.TryDelete(tempDir);
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Server Startup", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaServer CreateServer(int restPort, int mcpPort, out string tempDir)
        {
            tempDir = TestTemp.NewDirectory("startup");
            string sqlitePath = Path.Combine(tempDir, "armada.db");
            TestDatabaseHelper.SeedDatabaseFile(sqlitePath);

            DatabaseSettings db = new DatabaseSettings();
            db.Type = DatabaseTypeEnum.Sqlite;
            db.Filename = sqlitePath;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = tempDir;
            settings.DatabasePath = sqlitePath;
            settings.Database = db;
            settings.LogDirectory = Path.Combine(tempDir, "logs");
            settings.DocksDirectory = Path.Combine(tempDir, "docks");
            settings.ReposDirectory = Path.Combine(tempDir, "repos");
            settings.AdmiralPort = restPort;
            settings.McpPort = mcpPort;
            settings.ApiKey = "test-key-" + Guid.NewGuid().ToString("N");
            settings.HeartbeatIntervalSeconds = 300;
            settings.Rest.Hostname = "127.0.0.1";
            settings.InitializeDirectories();

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaServer server = new ArmadaServer(logging, settings, quiet: true);
            server.RuntimeToolDiscoverySource = new RecordingRuntimeToolDiscoverySource();
            return server;
        }

        private static int FreePort()
        {
            // Same non-ephemeral range as E2EServerFixture, so outbound sockets cannot take the port before the bind.
            Random random = new Random();
            for (int i = 0; i < 200; i++)
            {
                int candidate = random.Next(20000, 32000);
                if (candidate >= 25000 && candidate < 25100) continue;
                if (candidate >= 21000 && candidate < 21100) continue;
                TcpListener probe = new TcpListener(IPAddress.Loopback, candidate);
                try
                {
                    probe.Start();
                    return candidate;
                }
                catch (SocketException)
                {
                }
                finally
                {
                    probe.Stop();
                }
            }
            throw new InvalidOperationException("no free port in 20000-31999");
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.EndToEnd });
        }

        #endregion
    }
}
