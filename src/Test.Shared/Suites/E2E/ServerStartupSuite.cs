namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Sockets;
    using System.Runtime.InteropServices;
    using System.Text;
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

            cases.Add(CaseAsync("harbor_mission_survives_admiral_rollout", "Two real Admiral instances recover the same Harbor mission over WebSocket without relaunch", TestTags.Reliability, async () =>
            {
                ArmadaSettings settings = CreateSettings(out string tempDir);
                ArmadaServer? first = null;
                ArmadaServer? second = null;
                using CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                try
                {
                    Mission mission;
                    Dock dock;
                    Captain captain;
                    using (Armada.Core.Database.DatabaseDriver db = Armada.Core.Database.DatabaseDriverFactory.Create(settings.Database, quiet))
                    {
                        await db.InitializeAsync().ConfigureAwait(false);
                        Harbor harbor = await db.Harbors.CreateAsync(new Harbor { Name = "Rollout Harbor" }).ConfigureAwait(false);
                        Vessel vessel = await db.Vessels.CreateAsync(new Vessel("Rollout vessel", "https://github.com/test/rollout.git")).ConfigureAwait(false);
                        dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { HarborId = harbor.Id, BranchName = "armada/original", WorktreePath = Path.Combine(tempDir, "preserved-dock") }).ConfigureAwait(false);
                        Directory.CreateDirectory(dock.WorktreePath!);
                        File.WriteAllText(Path.Combine(dock.WorktreePath!, "uncommitted.txt"), "survives rollout");
                        captain = await db.Captains.CreateAsync(new Captain("Remote API captain", AgentRuntimeEnum.ApiEndpoint) { State = CaptainStateEnum.Working, ProcessId = 2000000007, CurrentDockId = dock.Id }).ConfigureAwait(false);
                        mission = await db.Missions.CreateAsync(new Mission("Keep running") { Status = MissionStatusEnum.InProgress, CaptainId = captain.Id, VesselId = vessel.Id, DockId = dock.Id, BranchName = dock.BranchName, StartedUtc = DateTime.UtcNow.AddMinutes(-1), ProcessId = captain.ProcessId }).ConfigureAwait(false);
                        captain.CurrentMissionId = mission.Id;
                        await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                        await db.HarborJobs.CreateAsync(new HarborJobRecord { JobId = "rollout-existing-job", HarborId = harbor.Id, MissionId = mission.Id, CaptainId = captain.Id, Kind = Armada.Core.Harbor.HarborJobKindEnum.Mission, Runtime = captain.Runtime.ToString(), StartedUtc = mission.StartedUtc }).ConfigureAwait(false);
                    }

                    first = await StartOnFreePortsAsync(settings).ConfigureAwait(false);
                    using Armada.Runtimes.Mcp.McpToolClient mcp = new Armada.Runtimes.Mcp.McpToolClient("http://127.0.0.1:" + settings.McpPort + "/mcp", null, null, quiet);
                    await mcp.InitializeAsync(deadline.Token).ConfigureAwait(false);
                    int originalToolCount = (await mcp.ListToolsAsync(deadline.Token).ConfigureAwait(false)).Count;
                    AssertTrue(originalToolCount > 0, "captain MCP session is live before rollout");
                    using (WebSocketHarborTransport link = await ConnectRolloutHarborAsync(settings, dock.HarborId!, deadline.Token).ConfigureAwait(false))
                    {
                        first.Stop();
                        first = null;
                    }
                    // Reuse both ports, as a real image update does: the existing captain keeps its original MCP URL.
                    second = CreateServerFromSettings(settings);
                    await second.StartAsync().ConfigureAwait(false);
                    AssertEqual(originalToolCount, (await mcp.ListToolsAsync(deadline.Token).ConfigureAwait(false)).Count,
                        "existing captain client recovers its stale MCP session after restart");
                    using (WebSocketHarborTransport link = await ConnectRolloutHarborAsync(settings, dock.HarborId!, deadline.Token).ConfigureAwait(false))
                    {
                        // Only an output is sent: a hidden relaunch would either lose its original owner on handshake,
                        // change the persisted attempt metadata, or fail to append to this mission's original log.
                        await link.SendAsync(Armada.Core.Harbor.HarborProtocol.Serialize(new Armada.Core.Harbor.HarborOutput
                        { JobId = "rollout-existing-job", Stream = Armada.Core.Harbor.HarborOutputStreamEnum.Stdout, Data = "original agent continued after rollout" }), deadline.Token).ConfigureAwait(false);
                        string log = Path.Combine(settings.LogDirectory, "missions", mission.Id + ".log");
                        MonotonicDeadline wait = MonotonicDeadline.After(TimeSpan.FromSeconds(5));
                        while ((!File.Exists(log) || !File.ReadAllText(log).Contains("original agent continued after rollout", StringComparison.Ordinal)) && !wait.Passed)
                            await Task.Delay(20, deadline.Token).ConfigureAwait(false);
                        AssertContains("original agent continued after rollout", File.ReadAllText(log), "output reaches restored lifecycle over the real WebSocket");
                        using HttpClient client = new HttpClient();
                        client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
                        HttpResponseMessage response = await client.GetAsync("http://127.0.0.1:" + settings.AdmiralPort + "/api/v1/missions/" + mission.Id, deadline.Token).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, response);
                        Mission after = await JsonHelper.DeserializeAsync<Mission>(response).ConfigureAwait(false);
                        AssertEqual(MissionStatusEnum.InProgress, after.Status);
                        AssertEqual(mission.ProcessId, after.ProcessId);
                        AssertEqual(mission.StartedUtc, after.StartedUtc);
                        AssertEqual(mission.BranchName, after.BranchName);
                        AssertEqual(dock.Id, after.DockId);
                        AssertEqual(0, after.RedispatchAttempts);
                        AssertEqual("survives rollout", File.ReadAllText(Path.Combine(dock.WorktreePath!, "uncommitted.txt")));
                    }
                }
                finally
                {
                    if (first != null) { try { first.Stop(); } catch (Exception) { } }
                    if (second != null) { try { second.Stop(); } catch (Exception) { } }
                    TestTemp.TryDelete(tempDir);
                }
            }));

            cases.Add(CaseAsync("mission_diff_uses_harbor_host", "Mission diff uses its Harbor over WebSocket, distinguishes offline from missing, and keeps saved snapshots", TestTags.Reliability, async () =>
            {
                ArmadaSettings settings = CreateSettings(out string tempDir);
                ArmadaServer? server = null;
                using CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                try
                {
                    Mission mission;
                    Dock dock;
                    using (Armada.Core.Database.DatabaseDriver db = Armada.Core.Database.DatabaseDriverFactory.Create(settings.Database, quiet))
                    {
                        await db.InitializeAsync().ConfigureAwait(false);
                        Harbor harbor = await db.Harbors.CreateAsync(new Harbor { Name = "Diff Harbor" }).ConfigureAwait(false);
                        Vessel vessel = await db.Vessels.CreateAsync(new Vessel("Diff vessel", "https://github.com/test/diff.git") { DefaultBranch = "acceptance-base" }).ConfigureAwait(false);
                        dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { HarborId = harbor.Id, RepositoryPath = "/remote/repo", BranchName = "armada/remote", WorktreePath = Path.Combine(tempDir, "only-on-harbor") }).ConfigureAwait(false);
                        Captain captain = await db.Captains.CreateAsync(new Captain("Remote diff captain", AgentRuntimeEnum.ApiEndpoint) { State = CaptainStateEnum.Working, ProcessId = 2000000007, CurrentDockId = dock.Id }).ConfigureAwait(false);
                        mission = await db.Missions.CreateAsync(new Mission("Read remote diff") { Status = MissionStatusEnum.InProgress, CaptainId = captain.Id, VesselId = vessel.Id, DockId = dock.Id, BranchName = dock.BranchName, StartedUtc = DateTime.UtcNow, ProcessId = captain.ProcessId }).ConfigureAwait(false);
                        captain.CurrentMissionId = mission.Id;
                        await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                        await db.HarborJobs.CreateAsync(new HarborJobRecord { JobId = "rollout-existing-job", HarborId = harbor.Id, MissionId = mission.Id, CaptainId = captain.Id, Kind = Armada.Core.Harbor.HarborJobKindEnum.Mission, Runtime = captain.Runtime.ToString(), StartedUtc = mission.StartedUtc }).ConfigureAwait(false);
                    }
                    AssertFalse(Directory.Exists(dock.WorktreePath), "remote path deliberately does not exist on Admiral");
                    server = await StartOnFreePortsAsync(settings).ConfigureAwait(false);
                    using HttpClient client = new HttpClient();
                    client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
                    string url = "http://127.0.0.1:" + settings.AdmiralPort + "/api/v1/missions/" + mission.Id + "/diff";
                    using HttpResponseMessage offline = await client.GetAsync(url, deadline.Token).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.ServiceUnavailable, offline);
                    AssertContains("Connect that Harbor", await offline.Content.ReadAsStringAsync().ConfigureAwait(false));
                    using WebSocketHarborTransport link = await ConnectRolloutHarborAsync(settings, dock.HarborId!, deadline.Token).ConfigureAwait(false);
                    bool exists = true;
                    bool diffRequested = false;
                    const string expectedDiff = "diff --git a/change.cs b/change.cs\n+remote change\n";
                    Task responder = Task.Run(async () =>
                    {
                        while (!deadline.IsCancellationRequested)
                        {
                            string? payload = await link.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                            if (payload == null) break;
                            Armada.Core.Harbor.HarborMessage message = Armada.Core.Harbor.HarborProtocol.Deserialize(payload);
                            if (message is Armada.Core.Harbor.HarborFileRequest file)
                            {
                                AssertEqual(dock.WorktreePath, file.Path);
                                await link.SendAsync(Armada.Core.Harbor.HarborProtocol.Serialize(new Armada.Core.Harbor.HarborFileResult { RequestId = file.RequestId, Success = true, Exists = exists, IsDirectory = exists }), deadline.Token).ConfigureAwait(false);
                            }
                            else if (message is Armada.Core.Harbor.HarborGitRequest git)
                            {
                                AssertEqual(dock.WorktreePath, git.WorkingDirectory);
                                bool isDiff = git.Arguments.Contains("diff");
                                if (isDiff) diffRequested = true;
                                await link.SendAsync(Armada.Core.Harbor.HarborProtocol.Serialize(new Armada.Core.Harbor.HarborGitResult { RequestId = git.RequestId, StandardOutput = isDiff ? expectedDiff : new string('a', 40) }), deadline.Token).ConfigureAwait(false);
                            }
                        }
                    }, deadline.Token);
                    try
                    {
                        using HttpResponseMessage live = await client.GetAsync(url, deadline.Token).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, live);
                        using System.Text.Json.JsonDocument diff = System.Text.Json.JsonDocument.Parse(await live.Content.ReadAsStringAsync().ConfigureAwait(false));
                        AssertEqual(expectedDiff, diff.RootElement.GetProperty("Diff").GetString());
                        AssertTrue(diffRequested, "git diff actually ran through the Harbor protocol");
                        using Armada.Runtimes.Mcp.McpToolClient mcp = new Armada.Runtimes.Mcp.McpToolClient("http://127.0.0.1:" + settings.McpPort + "/mcp", null, new Dictionary<string, string> { ["X-Api-Key"] = settings.ApiKey! }, quiet);
                        await mcp.InitializeAsync(deadline.Token).ConfigureAwait(false);
                        string toolResult = await mcp.CallToolAsync("get_mission_diff", System.Text.Json.JsonSerializer.Serialize(new { missionId = mission.Id }), deadline.Token).ConfigureAwait(false);
                        AssertContains("remote change", toolResult, "MCP also delegates to the owning Harbor");
                        exists = false;
                        using HttpResponseMessage missing = await client.GetAsync(url, deadline.Token).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.NotFound, missing);
                        using (Armada.Core.Database.DatabaseDriver db = Armada.Core.Database.DatabaseDriverFactory.Create(settings.Database, quiet))
                        {
                            await db.InitializeAsync().ConfigureAwait(false);
                            Mission stored = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                            stored.DiffSnapshot = expectedDiff;
                            await db.Missions.UpdateAsync(stored).ConfigureAwait(false);
                        }
                        using HttpResponseMessage saved = await client.GetAsync(url, deadline.Token).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, saved);
                        using System.Text.Json.JsonDocument snapshot = System.Text.Json.JsonDocument.Parse(await saved.Content.ReadAsStringAsync().ConfigureAwait(false));
                        AssertEqual(expectedDiff, snapshot.RootElement.GetProperty("Diff").GetString());
                    }
                    finally
                    {
                        deadline.Cancel();
                        try { await responder.ConfigureAwait(false); } catch (OperationCanceledException) { }
                    }
                }
                finally
                {
                    if (server != null) { try { server.Stop(); } catch (Exception) { } }
                    TestTemp.TryDelete(tempDir);
                }
            }));

            bool sigTermSkip = !OperatingSystem.IsLinux();
            cases.Add(CaseAsync(
                "sigterm_reaches_server_stop",
                "SIGTERM cancels termination and the main thread runs ArmadaServer.Stop",
                TestTags.Positive,
                SigTermReachesStopAsync,
                sigTermSkip,
                sigTermSkip ? "This case sends SIGTERM to a child Admiral and runs on Linux." : null));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Server Startup", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<WebSocketHarborTransport> ConnectRolloutHarborAsync(ArmadaSettings settings, string harborId, CancellationToken token)
        {
            WebSocketHarborTransport link = new WebSocketHarborTransport(new Uri("ws://127.0.0.1:" + settings.AdmiralPort + settings.Harbor.LinkPath),
                new Dictionary<string, string> { ["X-Api-Key"] = settings.ApiKey! });
            try
            {
                await link.ConnectAsync(token).ConfigureAwait(false);
                await link.SendAsync(Armada.Core.Harbor.HarborProtocol.Serialize(new Armada.Core.Harbor.HarborHandshake
                { HarborId = harborId, Name = "Rollout Harbor", Capabilities = new List<HarborCapability> { new HarborCapability { Name = Armada.Core.Harbor.HarborProtocol.DockCapability } }, LiveJobIds = new List<string> { "rollout-existing-job" }, JobOutboxEpoch = "unchanged-harbor-process" }), token).ConfigureAwait(false);
                Armada.Core.Harbor.HarborMessage message = Armada.Core.Harbor.HarborProtocol.Deserialize((await link.ReceiveAsync(token).ConfigureAwait(false))!);
                AssertTrue(message is Armada.Core.Harbor.HarborHandshakeAck, "real endpoint accepts reconnect");
                Armada.Core.Harbor.HarborHandshakeAck ack = (Armada.Core.Harbor.HarborHandshakeAck)message;
                AssertTrue(ack.Accepted && ack.OwnedJobIds!.Contains("rollout-existing-job"), "Harbor is told to retain the same running job");
                return link;
            }
            catch
            {
                link.Dispose();
                throw;
            }
        }

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

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body, bool skip = false, string? skipReason = null)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.EndToEnd },
                skip: skip,
                skipReason: skipReason);
        }

        private const int SigTerm = 15;

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int KillProcess(int pid, int signal);

        /// <summary>
        /// A child Admiral started from the built host. SIGTERM is sent to that child so the test process stays alive.
        /// </summary>
        private sealed class RunningAdmiral
        {
            private readonly StringBuilder _Stdout = new StringBuilder();
            private readonly StringBuilder _Stderr = new StringBuilder();
            private readonly object _Gate = new object();

            public RunningAdmiral(Process process)
            {
                Process = process ?? throw new ArgumentNullException(nameof(process));
            }

            public Process Process { get; }

            public TaskCompletionSource<bool> Ready { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void OnOutput(string? line)
            {
                if (line == null) return;
                lock (_Gate)
                {
                    if (_Stdout.Length < 200000) _Stdout.AppendLine(line);
                }

                if (line.Contains("Admiral running on port", StringComparison.Ordinal)) Ready.TrySetResult(true);
            }

            public void OnError(string? line)
            {
                if (line == null) return;
                lock (_Gate)
                {
                    if (_Stderr.Length < 200000) _Stderr.AppendLine(line);
                }
            }

            public string Combined
            {
                get
                {
                    lock (_Gate) return _Stdout.ToString() + _Stderr.ToString();
                }
            }
        }

        private static async Task SigTermReachesStopAsync()
        {
            ArmadaSettings settings = CreateSettings(out string tempDir);
            RunningAdmiral? child = null;
            try
            {
                child = await StartReadyAdmiralAsync(settings, tempDir).ConfigureAwait(false);
                // The first signal must reach the stop handle. A second signal, sent while that wait is still
                // active, must not run Stop again. The logger writes to the dated log file when stdout is not a
                // console, so the stop lines are read from that file.
                SendSigTerm(child.Process.Id);
                KillProcess(child.Process.Id, SigTerm);

                bool exited = child.Process.WaitForExit(45000);
                if (exited)
                {
                    child.Process.WaitForExit();
                }
                else
                {
                    try { child.Process.Kill(true); } catch (Exception) { }
                    child.Process.WaitForExit(5000);
                    exited = child.Process.HasExited;
                }
                string log = ReadAdmiralLog(settings.LogDirectory);
                bool stopFinished = exited
                    && child.Process.ExitCode == 0
                    && log.Contains("[ArmadaServer] stopping", StringComparison.Ordinal)
                    && log.Contains("[Program] stopped at", StringComparison.Ordinal);
                AssertTrue(stopFinished,
                    "SIGTERM must cancel termination and let the main thread finish ArmadaServer.Stop. Exit "
                    + (exited ? child.Process.ExitCode.ToString() : "timeout")
                    + ". "
                    + Tail(log));
                AssertEqual(1, CountLinesContaining(log, "[ArmadaServer] stopping"), "Stop runs once");
            }
            finally
            {
                if (child != null)
                {
                    try
                    {
                        if (!child.Process.HasExited) child.Process.Kill(true);
                    }
                    catch (Exception)
                    {
                    }

                    child.Process.Dispose();
                }

                TestTemp.TryDelete(tempDir);
            }
        }

        private static async Task<RunningAdmiral> StartReadyAdmiralAsync(ArmadaSettings settings, string tempDir)
        {
            string serverDll = typeof(ArmadaServer).Assembly.Location;
            AssertTrue(!String.IsNullOrEmpty(serverDll) && File.Exists(serverDll), "the Admiral assembly is a built file");
            string directory = Path.GetDirectoryName(serverDll) ?? "";
            string appHost = Path.Combine(directory, "Armada.Server");
            string profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".armada");
            AssertNotEqual(profileDir, tempDir, "the child data directory is the test directory");

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                int[] ports = TestPorts.Reserve(2);
                settings.AdmiralPort = ports[0];
                settings.McpPort = ports[1];
                await settings.SaveAsync(Path.Combine(tempDir, "settings.json")).ConfigureAwait(false);

                ProcessStartInfo info = File.Exists(appHost)
                    ? new ProcessStartInfo(appHost)
                    : new ProcessStartInfo("dotnet");
                if (!File.Exists(appHost))
                {
                    info.ArgumentList.Add("exec");
                    info.ArgumentList.Add(serverDll);
                }

                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.CreateNoWindow = true;
                info.WorkingDirectory = tempDir;
                RemoveArmadaEnvironment(info);
                ApplyChildEnvironment(info, tempDir);
                string? childData = info.Environment[Armada.Core.Constants.DataDirectoryEnvVar];
                AssertEqual(tempDir, childData ?? "", "child data directory");

                Process process = new Process();
                process.StartInfo = info;
                process.EnableRaisingEvents = true;
                RunningAdmiral child = new RunningAdmiral(process);
                process.OutputDataReceived += (sender, args) => child.OnOutput(args.Data);
                process.ErrorDataReceived += (sender, args) => child.OnError(args.Data);
                if (!process.Start())
                {
                    process.Dispose();
                    throw new AssertionException("the Admiral process did not start");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Task completed = await Task.WhenAny(
                    child.Ready.Task,
                    process.WaitForExitAsync(),
                    Task.Delay(TimeSpan.FromSeconds(90))).ConfigureAwait(false);
                if (completed == child.Ready.Task)
                {
                    try
                    {
                        using (HttpClient client = new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromSeconds(10);
                            using (HttpResponseMessage health = await client.GetAsync("http://127.0.0.1:" + settings.AdmiralPort + "/api/v1/status/health").ConfigureAwait(false))
                            {
                                AssertEqual(HttpStatusCode.OK, health.StatusCode, "Admiral health before SIGTERM");
                            }
                        }

                        return child;
                    }
                    catch (Exception)
                    {
                        try { if (!process.HasExited) process.Kill(true); } catch (Exception) { }
                        try { process.WaitForExit(5000); } catch (Exception) { }
                        process.Dispose();
                        throw;
                    }
                }

                string output = child.Combined;
                bool didExit = process.HasExited;
                int code = didExit ? process.ExitCode : -1;
                try { if (!didExit) process.Kill(true); } catch (Exception) { }
                try { process.WaitForExit(5000); } catch (Exception) { }
                process.Dispose();

                bool portTaken = output.Contains("could not listen", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("Address already in use", StringComparison.OrdinalIgnoreCase);
                if (portTaken && attempt < 3) continue;

                throw new AssertionException("the Admiral did not reach its running state (attempt " + attempt + ", exit " + code + "). " + Tail(output));
            }

            throw new AssertionException("the Admiral did not reach its running state");
        }

        private static void SendSigTerm(int pid)
        {
            int result = KillProcess(pid, SigTerm);
            int error = Marshal.GetLastWin32Error();
            AssertEqual(0, result, "SIGTERM delivery errno " + error);
        }

        private static void RemoveArmadaEnvironment(ProcessStartInfo info)
        {
            List<string> names = new List<string>();
            foreach (string name in info.Environment.Keys)
            {
                if (name.StartsWith("ARMADA_", StringComparison.OrdinalIgnoreCase)) names.Add(name);
            }

            for (int i = 0; i < names.Count; i++) info.Environment.Remove(names[i]);
        }

        private static void ApplyChildEnvironment(ProcessStartInfo info, string tempDir)
        {
            string home = Path.Combine(tempDir, "home");
            string scratch = Path.Combine(tempDir, "tmp");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(Path.Combine(home, "data"));
            Directory.CreateDirectory(Path.Combine(home, "config"));
            Directory.CreateDirectory(Path.Combine(home, "cache"));
            Directory.CreateDirectory(scratch);
            info.Environment["HOME"] = home;
            info.Environment["USERPROFILE"] = home;
            info.Environment["XDG_DATA_HOME"] = Path.Combine(home, "data");
            info.Environment["XDG_CONFIG_HOME"] = Path.Combine(home, "config");
            info.Environment["XDG_CACHE_HOME"] = Path.Combine(home, "cache");
            info.Environment["TMPDIR"] = scratch;
            info.Environment["TMP"] = scratch;
            info.Environment["TEMP"] = scratch;
            info.Environment[Armada.Core.Constants.DataDirectoryEnvVar] = tempDir;
        }

        /// <summary>
        /// Read every file in the Admiral log directory. The dated log name is owned by the logger.
        /// </summary>
        /// <param name="logDirectory">Directory the child Admiral was given for its log.</param>
        /// <returns>The combined log text, or an empty string when the directory is absent.</returns>
        private static string ReadAdmiralLog(string logDirectory)
        {
            if (String.IsNullOrEmpty(logDirectory) || !Directory.Exists(logDirectory)) return "";
            string[] files = Directory.GetFiles(logDirectory);
            Array.Sort(files, StringComparer.Ordinal);
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < files.Length; i++)
            {
                using (FileStream stream = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        text.Append(reader.ReadToEnd());
                    }
                }
            }

            return text.ToString();
        }

        private static int CountLinesContaining(string text, string fragment)
        {
            int count = 0;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(fragment, StringComparison.Ordinal)) count++;
            }

            return count;
        }

        private static string Tail(string text)
        {
            if (text.Length <= 1500) return text;
            return text.Substring(text.Length - 1500);
        }

        #endregion
    }
}
