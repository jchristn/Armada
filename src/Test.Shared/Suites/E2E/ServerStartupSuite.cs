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
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database;
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
        private const string InterruptedCheckSummary = "The command died with the process and this record has no verdict.";

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

            cases.Add(CaseAsync("startup_cancels_armada_checks_left_running_by_a_stopped_process", "Startup cancels Armada checks left Running by a stopped process", TestTags.Positive, async () =>
            {
                string tempDir = TestTemp.NewDirectory("startup-checks");
                ArmadaServer? server = null;
                try
                {
                    DatabaseSettings database = await PrepareStartupDatabaseAsync(tempDir).ConfigureAwait(false);
                    ArmadaSettings settings = CreateStartupSettings(tempDir, database);
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    string oldId;
                    string nullStartId;
                    string externalId;
                    string currentId;
                    using (DatabaseDriver seed = DatabaseDriverFactory.Create(database, logging))
                    {
                        await seed.InitializeAsync().ConfigureAwait(false);
                        CheckRun oldRun = await SeedRunningCheckAsync(seed, CheckRunSourceEnum.Armada, DateTime.UtcNow.AddDays(-1), null, 88001L, null).ConfigureAwait(false);
                        CheckRun nullStart = await SeedRunningCheckAsync(seed, CheckRunSourceEnum.Armada, null, "kept output", 42L, null).ConfigureAwait(false);
                        CheckRun external = await SeedRunningCheckAsync(seed, CheckRunSourceEnum.External, DateTime.UtcNow.AddDays(-1), "external output", 9L, "imported run").ConfigureAwait(false);
                        CheckRun current = await SeedRunningCheckAsync(seed, CheckRunSourceEnum.Armada, DateTime.UtcNow.AddDays(1), "current output", 11L, null).ConfigureAwait(false);
                        oldId = oldRun.Id;
                        nullStartId = nullStart.Id;
                        externalId = external.Id;
                        currentId = current.Id;

                        CheckRun storedOld = (await seed.CheckRuns.ReadAsync(oldId).ConfigureAwait(false))!;
                        CheckRun storedNull = (await seed.CheckRuns.ReadAsync(nullStartId).ConfigureAwait(false))!;
                        CheckRun storedExternal = (await seed.CheckRuns.ReadAsync(externalId).ConfigureAwait(false))!;
                        CheckRun storedCurrent = (await seed.CheckRuns.ReadAsync(currentId).ConfigureAwait(false))!;
                        AssertEqual(CheckRunStatusEnum.Running, storedOld.Status, "seeded old Armada run");
                        AssertEqual(CheckRunStatusEnum.Running, storedNull.Status, "seeded Armada run with no start");
                        AssertFalse(storedNull.StartedUtc.HasValue, "a null start is stored as null");
                        AssertEqual(CheckRunSourceEnum.External, storedExternal.Source, "seeded external source");
                        AssertEqual(CheckRunStatusEnum.Running, storedExternal.Status, "seeded external run");
                        AssertEqual(CheckRunStatusEnum.Running, storedCurrent.Status, "seeded current Armada run");
                        AssertTrue(storedOld.StartedUtc.HasValue && storedCurrent.StartedUtc.HasValue && storedCurrent.StartedUtc.Value > storedOld.StartedUtc.Value, "the current run starts after the old run");
                    }

                    server = await StartOnFreePortsAsync(settings).ConfigureAwait(false);

                    CheckRun? oldAfter = null;
                    CheckRun? nullAfter = null;
                    using (DatabaseDriver reader = DatabaseDriverFactory.Create(database, logging))
                    {
                        MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(20));
                        while (!deadline.Passed)
                        {
                            oldAfter = await reader.CheckRuns.ReadAsync(oldId).ConfigureAwait(false);
                            nullAfter = await reader.CheckRuns.ReadAsync(nullStartId).ConfigureAwait(false);
                            if (oldAfter != null
                                && oldAfter.Status == CheckRunStatusEnum.Canceled
                                && nullAfter != null
                                && nullAfter.Status == CheckRunStatusEnum.Canceled)
                            {
                                break;
                            }

                            await Task.Delay(100).ConfigureAwait(false);
                        }

                        AssertNotNull(oldAfter, "old Armada run still exists");
                        AssertEqual(CheckRunStatusEnum.Canceled, oldAfter!.Status, "old Armada run after startup");
                        AssertEqual(InterruptedCheckSummary, oldAfter.Summary, "old Armada run summary");
                        AssertEqual(InterruptedCheckSummary, oldAfter.Output, "empty output receives the summary");
                        AssertTrue(oldAfter.CompletedUtc.HasValue, "old Armada run has a completion time");
                        AssertEqual(88001L, oldAfter.DurationMs ?? -1L, "old Armada run duration is unchanged");

                        AssertNotNull(nullAfter, "Armada run with no start still exists");
                        AssertEqual(CheckRunStatusEnum.Canceled, nullAfter!.Status, "Armada run with no start after startup");
                        AssertEqual(InterruptedCheckSummary, nullAfter.Summary, "null-start summary");
                        AssertEqual("kept output", nullAfter.Output, "stored output is kept");
                        AssertTrue(nullAfter.CompletedUtc.HasValue, "null-start run has a completion time");
                        AssertEqual(42L, nullAfter.DurationMs ?? -1L, "null-start duration is unchanged");
                        AssertFalse(nullAfter.StartedUtc.HasValue, "null start stays null");

                        CheckRun externalAfter = (await reader.CheckRuns.ReadAsync(externalId).ConfigureAwait(false))!;
                        AssertEqual(CheckRunStatusEnum.Running, externalAfter.Status, "external run stays Running");
                        AssertEqual("imported run", externalAfter.Summary, "external summary stays");
                        AssertEqual("external output", externalAfter.Output, "external output stays");
                        AssertFalse(externalAfter.CompletedUtc.HasValue, "external run has no completion time");
                        AssertEqual(9L, externalAfter.DurationMs ?? -1L, "external duration stays");

                        CheckRun currentAfter = (await reader.CheckRuns.ReadAsync(currentId).ConfigureAwait(false))!;
                        AssertEqual(CheckRunStatusEnum.Running, currentAfter.Status, "Armada run started after the process stays Running");
                        AssertEqual("current output", currentAfter.Output, "current output stays");
                        AssertFalse(currentAfter.CompletedUtc.HasValue, "current run has no completion time");
                        AssertEqual(11L, currentAfter.DurationMs ?? -1L, "current duration stays");
                    }
                }
                finally
                {
                    if (server != null) { try { server.Stop(); } catch (Exception) { } }
                    SqliteConnection.ClearAllPools();
                    TestTemp.TryDelete(tempDir);
                }
            }));

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

        private static async Task<DatabaseSettings> PrepareStartupDatabaseAsync(string tempDir)
        {
            if (TestDatabaseConfig.IsSqlite)
            {
                string sqlitePath = Path.Combine(tempDir, "armada.db");
                TestDatabaseHelper.SeedDatabaseFile(sqlitePath);
                DatabaseSettings sqlite = new DatabaseSettings();
                sqlite.Type = DatabaseTypeEnum.Sqlite;
                sqlite.Filename = sqlitePath;
                return sqlite;
            }

            TestDatabase prepared = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            prepared.Dispose();
            // CreateDatabaseAsync resets one shared server database. Its name is the configured base name plus this suffix.
            return TestDatabaseConfig.BuildSettings(TestDatabaseConfig.BaseDatabaseName + "_shared");
        }

        private static ArmadaSettings CreateStartupSettings(string tempDir, DatabaseSettings database)
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = tempDir;
            settings.Database = database;
            if (database.Type == DatabaseTypeEnum.Sqlite) settings.DatabasePath = database.Filename;
            settings.LogDirectory = Path.Combine(tempDir, "logs");
            settings.DocksDirectory = Path.Combine(tempDir, "docks");
            settings.ReposDirectory = Path.Combine(tempDir, "repos");
            settings.ApiKey = "test-key-" + Guid.NewGuid().ToString("N");
            settings.HeartbeatIntervalSeconds = 300;
            settings.Rest.Hostname = "127.0.0.1";
            settings.SettingsFilePath = Path.Combine(tempDir, "settings.json");
            return settings;
        }

        private static async Task<CheckRun> SeedRunningCheckAsync(DatabaseDriver database, CheckRunSourceEnum source, DateTime? startedUtc, string? output, long durationMs, string? summary)
        {
            CheckRun run = new CheckRun
            {
                Source = source,
                Status = CheckRunStatusEnum.Running,
                Command = "echo interrupted",
                StartedUtc = startedUtc,
                Output = output,
                Summary = summary,
                DurationMs = durationMs,
                Type = CheckRunTypeEnum.Build
            };
            return await database.CheckRuns.CreateAsync(run).ConfigureAwait(false);
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
