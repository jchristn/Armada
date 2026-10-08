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
    using Armada.Core.Models;
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
                ArmadaSettings settings = CreateSettings(out string tempDir);
                settings.McpPort = mcpPort;
                ArmadaServer? server = null;
                try
                {
                    // Only the REST port comes from TestPorts (and is replaced if something else bound it first); the
                    // MCP failure on the squatter's port is the result under test, so it is returned, never retried.
                    ListenerBindException? caught = await TestPorts.StartOnFreePortsAsync(1, async ports =>
                    {
                        settings.AdmiralPort = ports[0];
                        ArmadaServer candidate = CreateServerFromSettings(settings);
                        server = candidate;
                        try
                        {
                            await candidate.StartAsync().ConfigureAwait(false);
                            return (ListenerBindException?)null;
                        }
                        catch (ListenerBindException ex) when (ex.Listener == "MCP" && ex.Port == mcpPort)
                        {
                            return ex;
                        }
                        catch
                        {
                            server = null;
                            try { candidate.Stop(); } catch (Exception) { }
                            throw;
                        }
                    }).ConfigureAwait(false);

                    AssertNotNull(caught, "StartAsync must throw a typed ListenerBindException when the MCP port is taken");
                    AssertEqual("MCP", caught!.Listener, "the MCP listener failed");
                    AssertEqual(mcpPort, caught.Port, "the taken port");
                    AssertEqual("127.0.0.1", caught.Hostname, "the configured host");
                }
                finally
                {
                    if (server != null) { try { server.Stop(); } catch (Exception) { } }
                    squatter.Stop();
                    TestTemp.TryDelete(tempDir);
                }
            }));

            cases.Add(CaseAsync("both_listeners_ready_after_start", "Both listeners answer as soon as StartAsync returns", TestTags.Positive, async () =>
            {
                ArmadaSettings settings = CreateSettings(out string tempDir);
                ArmadaServer? server = null;
                try
                {
                    server = await StartOnFreePortsAsync(settings).ConfigureAwait(false);
                    int restPort = settings.AdmiralPort;
                    int mcpPort = settings.McpPort;
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
                    if (server != null) { try { server.Stop(); } catch (Exception) { } }
                    TestTemp.TryDelete(tempDir);
                }
            }));

            cases.Add(CaseAsync("generated_captain_mcp_url_answers_on_loopback_literal", "A captain MCP URL generated for an Admiral bound to 127.0.0.1 answers initialize with 200", TestTags.Positive, async () =>
            {
                ArmadaSettings settings = CreateSettings(out string tempDir);
                ArmadaServer? server = null;
                try
                {
                    server = await StartOnFreePortsAsync(settings).ConfigureAwait(false);
                    int restPort = settings.AdmiralPort;
                    int mcpPort = settings.McpPort;

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
                    if (server != null) { try { server.Stop(); } catch (Exception) { } }
                    TestTemp.TryDelete(tempDir);
                }
            }));

            cases.Add(CaseAsync("session_token_survives_restart", "A session token issued before a restart is still valid after the Admiral restarts on the same data directory (F8)", TestTags.Positive, async () =>
            {
                // No preset API key: like a real first start, the Admiral generates its local secrets and writes
                // settings.json. Before the fix the session key was generated after that save and never persisted.
                ArmadaSettings firstSettings = CreateSettings(out string tempDir, presetApiKey: false);
                string settingsPath = Path.Combine(tempDir, "settings.json");
                ArmadaServer? first = null;
                ArmadaServer? second = null;
                try
                {
                    first = await StartOnFreePortsAsync(firstSettings).ConfigureAwait(false);
                    ArmadaSettings loadedFirst = await ArmadaSettings.LoadAsync(settingsPath).ConfigureAwait(false);
                    AssertFalse(String.IsNullOrEmpty(loadedFirst.ApiKey), "the generated API key is persisted");
                    AssertFalse(String.IsNullOrEmpty(loadedFirst.SessionTokenEncryptionKey), "the session token encryption key is persisted");

                    string token;
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        HttpRequestMessage auth = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + firstSettings.AdmiralPort + "/api/v1/authenticate");
                        auth.Headers.Add("X-Api-Key", loadedFirst.ApiKey);
                        HttpResponseMessage authResponse = await client.SendAsync(auth).ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.OK, authResponse.StatusCode, "authenticate before restart");
                        AuthenticateResult result = JsonHelper.Deserialize<AuthenticateResult>(await authResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
                        AssertFalse(String.IsNullOrEmpty(result.Token), "a session token is issued");
                        token = result.Token!;
                        AssertEqual(HttpStatusCode.OK, await WhoAmIStatusAsync(client, firstSettings.AdmiralPort, token).ConfigureAwait(false), "whoami before restart");
                    }

                    first.Stop();

                    // Restart the way Program.cs does: load settings.json from the same data directory.
                    ArmadaSettings reloaded = await ArmadaSettings.LoadAsync(settingsPath).ConfigureAwait(false);
                    second = await StartOnFreePortsAsync(reloaded).ConfigureAwait(false);
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        AssertEqual(HttpStatusCode.OK, await WhoAmIStatusAsync(client, reloaded.AdmiralPort, token).ConfigureAwait(false), "whoami with the pre-restart session token after restart");
                    }
                }
                finally
                {
                    if (first != null) { try { first.Stop(); } catch (Exception) { } }
                    if (second != null) { try { second.Stop(); } catch (Exception) { } }
                    TestTemp.TryDelete(tempDir);
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Server Startup", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<HttpStatusCode> WhoAmIStatusAsync(HttpClient client, int restPort, string token)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + restPort + "/api/v1/whoami");
            request.Headers.Add("X-Token", token);
            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            return response.StatusCode;
        }

        private static ArmadaServer CreateServerFromSettings(ArmadaSettings settings)
        {
            settings.InitializeDirectories();
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaServer server = new ArmadaServer(logging, settings, quiet: true);
            server.RuntimeToolDiscoverySource = new RecordingRuntimeToolDiscoverySource();
            server.PushTransport = new RecordingPushTransport();
            return server;
        }

        private static async Task<ArmadaServer> StartOnFreePortsAsync(ArmadaSettings settings)
        {
            // Bind through TestPorts: a reserved port that something else bound before the Admiral did is replaced
            // by fresh ports instead of failing the case with "Address already in use".
            return await TestPorts.StartOnFreePortsAsync(2, async ports =>
            {
                settings.AdmiralPort = ports[0];
                settings.McpPort = ports[1];
                ArmadaServer candidate = CreateServerFromSettings(settings);
                try
                {
                    await candidate.StartAsync().ConfigureAwait(false);
                    return candidate;
                }
                catch
                {
                    try { candidate.Stop(); } catch (Exception) { }
                    throw;
                }
            }).ConfigureAwait(false);
        }

        private static ArmadaSettings CreateSettings(out string tempDir, bool presetApiKey = true)
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
            if (presetApiKey) settings.ApiKey = "test-key-" + Guid.NewGuid().ToString("N");
            settings.HeartbeatIntervalSeconds = 300;
            settings.Rest.Hostname = "127.0.0.1";
            // Keep any settings save (generated local secrets) inside the temp directory.
            settings.SettingsFilePath = Path.Combine(tempDir, "settings.json");
            return settings;
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
