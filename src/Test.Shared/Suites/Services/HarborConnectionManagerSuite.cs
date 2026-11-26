namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="HarborConnectionManager"/>: handshakes register/refresh a Harbor and are
    /// acknowledged with the advertised MCP URL, heartbeats advance liveness and the live-job set,
    /// disconnects mark the Harbor offline, and messages for an unknown Harbor are safe no-ops. Positive
    /// cases assert the registry and persisted state; negative cases assert the missing-id/name and null
    /// guards.
    /// </summary>
    public sealed class HarborConnectionManagerSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the HarborConnectionManager suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("handshake_registers_and_acks", "OnHandshakeAsync registers, persists connected, and acks with MCP URL", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), "http://127.0.0.1:7891/mcp");

                HarborHandshake handshake = new HarborHandshake
                {
                    CorrelationId = "c1",
                    HarborId = "hbr_cm1",
                    Name = "Rig",
                    ProtocolVersion = HarborProtocol.Version,
                    OsPlatform = "Linux",
                    Architecture = "X64",
                    MaxConcurrentJobs = 6,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "claude" } }
                };

                HarborHandshakeAck ack = await manager.OnHandshakeAsync(handshake, "ten_cm", "usr_cm", NoopSend).ConfigureAwait(false);
                AssertTrue(ack.Accepted, "Expected the handshake to be accepted.");
                AssertEqual("http://127.0.0.1:7891/mcp", ack.McpBaseUrl);
                AssertEqual("c1", ack.CorrelationId);
                AssertTrue(manager.IsConnected("hbr_cm1"), "Expected the harbor to be tracked as connected.");

                Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_cm1").ConfigureAwait(false);
                AssertNotNull(stored, "Expected the harbor to be persisted.");
                AssertEqual(HarborConnectionStatusEnum.Connected, stored!.ConnectionStatus);
                AssertEqual("ten_cm", stored.TenantId);
            }));

            cases.Add(CaseAsync("prompt_capability_is_read_per_runtime", "A Harbor's cli-permission-prompt capability says which runtimes it can prompt for, scoped by tenant", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborHandshake prompting = new HarborHandshake
                {
                    HarborId = "hbr_prompt",
                    Name = "Rig",
                    ProtocolVersion = HarborProtocol.Version,
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability>
                    {
                        new HarborCapability { Name = "Mux", Available = true },
                        new HarborCapability { Name = HarborProtocol.CliPermissionPromptCapability, Available = true, Detail = "ClaudeCode, mux, ApiEndpoint, Bogus" }
                    }
                };
                await manager.OnHandshakeAsync(prompting, "ten_p", "usr_p", NoopSend).ConfigureAwait(false);

                HarborHandshake older = new HarborHandshake
                {
                    HarborId = "hbr_older",
                    Name = "Old",
                    ProtocolVersion = HarborProtocol.Version,
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "Codex", Available = true } }
                };
                await manager.OnHandshakeAsync(older, "ten_o", "usr_o", NoopSend).ConfigureAwait(false);

                AssertTrue(manager.CanPrompt("hbr_prompt", AgentRuntimeEnum.ClaudeCode), "Claude Code listed");
                AssertTrue(manager.CanPrompt("hbr_prompt", AgentRuntimeEnum.Mux), "names are case-insensitive");
                AssertFalse(manager.CanPrompt("hbr_prompt", AgentRuntimeEnum.Codex), "unlisted runtime");
                AssertFalse(manager.CanPrompt("hbr_prompt", AgentRuntimeEnum.ApiEndpoint), "API endpoints never prompt from a Harbor");
                AssertFalse(manager.CanPrompt("hbr_older", AgentRuntimeEnum.ClaudeCode), "a Harbor without the capability cannot prompt");
                AssertFalse(manager.CanPrompt("hbr_missing", AgentRuntimeEnum.ClaudeCode), "an unknown Harbor cannot prompt");

                AssertTrue(manager.AnyHarborCanPrompt("ten_p", AgentRuntimeEnum.Mux), "the tenant's Harbor can prompt");
                AssertFalse(manager.AnyHarborCanPrompt("ten_o", AgentRuntimeEnum.Mux), "another tenant's Harbor does not count");
                AssertTrue(manager.AnyHarborCanPrompt(null, AgentRuntimeEnum.ClaudeCode), "no tenant filter");
                AssertFalse(manager.AnyHarborCanPrompt(null, AgentRuntimeEnum.Codex), "nobody prompts for Codex");

                await manager.OnDisconnectedAsync("hbr_prompt").ConfigureAwait(false);
                AssertFalse(manager.CanPrompt("hbr_prompt", AgentRuntimeEnum.ClaudeCode), "a disconnected Harbor cannot prompt");
            }));

            cases.Add(CaseAsync("capability_flags_report_update_recommended", "A Harbor missing an expected API capability flag is acked with the missing names and reads UpdateRecommended; a current one does not", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborHandshake older = new HarborHandshake
                {
                    HarborId = "hbr_flags_old",
                    Name = "Old",
                    ProtocolVersion = HarborProtocol.Version,
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = HarborProtocol.InferenceStreamingCapability, Available = true } }
                };
                HarborHandshakeAck olderAck = await manager.OnHandshakeAsync(older, "ten_f", "usr_f", NoopSend).ConfigureAwait(false);
                AssertNotNull(olderAck.MissingCapabilities, "an older Harbor is told what it is missing");
                AssertEqual(String.Join(",", HarborCapabilityFlags.Advertised), String.Join(",", olderAck.MissingCapabilities!));
                AssertTrue(manager.UpdateRecommended("hbr_flags_old"), "older Harbor: update recommended");
                AssertTrue(manager.Supports("hbr_flags_old", HarborCapabilityFlags.InferenceStreaming), "1.4.0 flag still supported");
                AssertFalse(manager.Supports("hbr_flags_old", HarborCapabilityFlags.SubmitTool), "1.5.0 flag missing");

                List<HarborCapability> current = new List<HarborCapability> { new HarborCapability { Name = HarborProtocol.InferenceStreamingCapability, Available = true } };
                HarborCapabilityFlags.AddAdvertised(current);
                HarborCapabilityFlags.AddAdvertised(current);
                AssertEqual(1 + HarborCapabilityFlags.Advertised.Count, current.Count, "AddAdvertised does not duplicate");
                HarborHandshake newer = new HarborHandshake { HarborId = "hbr_flags_new", Name = "New", ProtocolVersion = HarborProtocol.Version, MaxConcurrentJobs = 4, Capabilities = current };
                HarborHandshakeAck newerAck = await manager.OnHandshakeAsync(newer, "ten_f", "usr_f", NoopSend).ConfigureAwait(false);
                AssertNull(newerAck.MissingCapabilities, "a current Harbor gets no missing list");
                AssertFalse(manager.UpdateRecommended("hbr_flags_new"), "current Harbor: no update");
                AssertTrue(manager.Supports("hbr_flags_new", HarborCapabilityFlags.ToolScope.ToUpperInvariant()), "flag names are case-insensitive");
                AssertFalse(manager.UpdateRecommended("hbr_unknown"), "unknown Harbor: no update");
                AssertFalse(manager.Supports(null, HarborCapabilityFlags.SubmitTool), "null Harbor id");
                AssertThrows<ArgumentNullException>(() => manager.Supports("hbr_flags_new", ""));

                Harbor? storedOld = await testDb.Driver.Harbors.ReadAsync("hbr_flags_old").ConfigureAwait(false);
                Harbor? storedNew = await testDb.Driver.Harbors.ReadAsync("hbr_flags_new").ConfigureAwait(false);
                AssertTrue(storedOld!.UpdateRecommended, "the stored older Harbor reads UpdateRecommended on REST");
                AssertEqual(HarborCapabilityFlags.Advertised.Count, storedOld.MissingCapabilities.Count);
                AssertFalse(storedNew!.UpdateRecommended, "the stored current Harbor does not");
                AssertFalse(new Harbor().UpdateRecommended, "a Harbor that never connected is not flagged");

                List<HarborCapability> unavailable = new List<HarborCapability> { new HarborCapability { Name = HarborCapabilityFlags.SubmitTool, Available = false } };
                AssertFalse(HarborCapabilityFlags.Supports(unavailable, HarborCapabilityFlags.SubmitTool), "an unavailable flag does not count");
                AssertFalse(HarborCapabilityFlags.Supports(null, HarborCapabilityFlags.SubmitTool), "null list");
                foreach (string flag in HarborCapabilityFlags.Advertised)
                    AssertTrue(((IList<string>)HarborCapabilityFlags.All).Contains(flag), "every advertised flag is declared: " + flag);
            }));

            cases.Add(CaseAsync("link_client_advertises_capability_flags", "A Harbor link client with a job runner advertises the expected flags; one without is told it is missing them", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborLinkClient runs = new HarborLinkClient("hbr_link_runs", "Runs", new List<HarborCapability>(), 4,
                    new StubHostCommandExecutor(new HostCommandResult { ExitCode = 0 }), CreateLogging(), 0, null, new ControlledHarborJobRunner());
                LoopbackHarborSession current = await LoopbackHarborSession.ConnectAsync(manager, "hbr_link_runs", runs).ConfigureAwait(false);
                AssertFalse(manager.UpdateRecommended("hbr_link_runs"), "a current Harbor advertises every expected flag");
                AssertFalse(runs.UpdateRecommended, "the Admiral reported nothing missing");
                AssertEqual(0, runs.MissingCapabilities.Count);

                HarborLinkClient bare = new HarborLinkClient("hbr_link_bare", "Bare", new List<HarborCapability>(), 4,
                    new StubHostCommandExecutor(new HostCommandResult { ExitCode = 0 }), CreateLogging(), 0, null, null);
                LoopbackHarborSession older = await LoopbackHarborSession.ConnectAsync(manager, "hbr_link_bare", bare).ConfigureAwait(false);
                AssertTrue(manager.UpdateRecommended("hbr_link_bare"), "no job runner, no flags");
                AssertTrue(bare.UpdateRecommended, "the ack's missing list reaches the Harbor");
                AssertEqual(String.Join(",", HarborCapabilityFlags.Advertised), String.Join(",", bare.MissingCapabilities));

                await current.DropAsync().ConfigureAwait(false);
                await older.DropAsync().ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("captain_reads_harbor_update_recommended", "A captain whose active mission runs on a Harbor missing an expected flag reads HarborUpdateRecommended", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);
                Armada.Server.CaptainHarborUpdateResolver resolver = new Armada.Server.CaptainHarborUpdateResolver(testDb.Driver, manager);

                Captain idle = new Captain { Name = "idle" };
                await resolver.ApplyAsync(new List<Captain> { idle }).ConfigureAwait(false);
                AssertFalse(idle.HarborUpdateRecommended, "no Harbor connected");

                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_cap_old", Name = "Old", ProtocolVersion = HarborProtocol.Version, MaxConcurrentJobs = 4 }, null, null, NoopSend).ConfigureAwait(false);
                List<HarborCapability> current = new List<HarborCapability>();
                HarborCapabilityFlags.AddAdvertised(current);
                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_cap_new", Name = "New", ProtocolVersion = HarborProtocol.Version, MaxConcurrentJobs = 4, Capabilities = current }, null, null, NoopSend).ConfigureAwait(false);

                Mission onOld = await testDb.Driver.Missions.CreateAsync(new Mission("on old") { AssignedHarborId = "hbr_cap_old", Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);
                Mission onNew = await testDb.Driver.Missions.CreateAsync(new Mission("on new") { AssignedHarborId = "hbr_cap_new", Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);
                Captain oldWork = new Captain { Name = "old", ActiveMissionIds = new List<string> { onNew.Id, onOld.Id } };
                Captain newWork = new Captain { Name = "new", ActiveMissionIds = new List<string> { onNew.Id, "msn_missing" } };
                await resolver.ApplyAsync(new List<Captain> { oldWork, newWork, idle }).ConfigureAwait(false);
                AssertTrue(oldWork.HarborUpdateRecommended, "work on the older Harbor");
                AssertFalse(newWork.HarborUpdateRecommended, "work on a current Harbor");
                AssertFalse(idle.HarborUpdateRecommended, "no active work");
                AssertThrows<ArgumentNullException>(() => resolver.ApplyAsync(null!).GetAwaiter().GetResult());
            }));

            cases.Add(CaseAsync("harbor_launch_resolves_automatic_streaming", "A Harbor launch sends Automatic streaming resolved: on only for a streaming provider on a Harbor that advertises streaming; On and Off always win", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);
                List<HarborCapability> streams = new List<HarborCapability> { new HarborCapability { Name = "ApiEndpoint", Available = true }, new HarborCapability { Name = HarborProtocol.InferenceStreamingCapability, Available = true } };
                List<HarborCapability> blocking = new List<HarborCapability> { new HarborCapability { Name = "ApiEndpoint", Available = true } };

                async Task<HarborLaunchRequest> LaunchAsync(List<HarborCapability> capabilities, ModelProviderEnum provider, InferenceStreamingModeEnum mode)
                {
                    string harborId = "hbr_stream_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    HarborLaunchRequest? captured = null;
                    using CancellationTokenSource cancel = new CancellationTokenSource();
                    HarborSendDelegate send = (message, token) =>
                    {
                        if (message is HarborLaunchRequest launch && captured == null) { captured = launch; cancel.Cancel(); }
                        return Task.CompletedTask;
                    };
                    await manager.OnHandshakeAsync(new HarborHandshake { HarborId = harborId, Name = "S", ProtocolVersion = HarborProtocol.Version, MaxConcurrentJobs = 4, Capabilities = capabilities }, null, null, send).ConfigureAwait(false);
                    ModelEndpoint endpoint = new ModelEndpoint { Id = "mep_s", Name = "s", Provider = provider, Kind = ModelEndpointKindEnum.Inference, BaseUrl = "http://127.0.0.1:1", Model = "m", Enabled = true };
                    endpoint.InferenceOptions.StreamingMode = mode;
                    Armada.Runtimes.RemoteAgentRuntime runtime = new Armada.Runtimes.RemoteAgentRuntime(manager, harborId, AgentRuntimeEnum.ApiEndpoint, id => endpoint);
                    try { await runtime.StartAsync("/w", "p", captain: new Captain { Name = "c", Runtime = AgentRuntimeEnum.ApiEndpoint, ModelEndpointId = "mep_s" }, token: cancel.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    AssertNotNull(captured, "the launch was sent");
                    return captured!;
                }

                HarborLaunchRequest onOllama = await LaunchAsync(streams, ModelProviderEnum.Ollama, InferenceStreamingModeEnum.Automatic).ConfigureAwait(false);
                AssertEqual(InferenceStreamingModeEnum.On, onOllama.InferenceEndpoint!.InferenceOptions.StreamingMode, "Automatic on Ollama with a streaming Harbor");
                AssertTrue(onOllama.InferenceEndpoint.InferenceOptions.Streaming, "a 1.4.0 Harbor reads the streaming flag");
                HarborLaunchRequest olderHarbor = await LaunchAsync(blocking, ModelProviderEnum.OpenAICompatible, InferenceStreamingModeEnum.Automatic).ConfigureAwait(false);
                AssertEqual(InferenceStreamingModeEnum.Off, olderHarbor.InferenceEndpoint!.InferenceOptions.StreamingMode, "no streaming flag, no default streaming");
                AssertFalse(olderHarbor.InferenceEndpoint.InferenceOptions.Streaming);
                HarborLaunchRequest openAi = await LaunchAsync(streams, ModelProviderEnum.OpenAI, InferenceStreamingModeEnum.Automatic).ConfigureAwait(false);
                AssertEqual(InferenceStreamingModeEnum.Off, openAi.InferenceEndpoint!.InferenceOptions.StreamingMode, "OpenAI does not stream by default");
                HarborLaunchRequest explicitOff = await LaunchAsync(streams, ModelProviderEnum.Ollama, InferenceStreamingModeEnum.Off).ConfigureAwait(false);
                AssertEqual(InferenceStreamingModeEnum.Off, explicitOff.InferenceEndpoint!.InferenceOptions.StreamingMode, "explicit Off wins");
                HarborLaunchRequest explicitOn = await LaunchAsync(streams, ModelProviderEnum.OpenAI, InferenceStreamingModeEnum.On).ConfigureAwait(false);
                AssertEqual(InferenceStreamingModeEnum.On, explicitOn.InferenceEndpoint!.InferenceOptions.StreamingMode, "explicit On wins");
            }));

            cases.Add(CaseAsync("eligible_harbor_is_scoped_to_the_owning_user", "HasEligibleHarborForUserAsync matches only the owning user's connected Harbor", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborHandshake handshake = new HarborHandshake
                {
                    HarborId = "hbr_userscoped",
                    Name = "Rig",
                    ProtocolVersion = HarborProtocol.Version,
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
                };
                await manager.OnHandshakeAsync(handshake, "ten_us", "usr_owner", NoopSend).ConfigureAwait(false);

                Armada.Core.Services.HarborRoutingRequest request = new Armada.Core.Services.HarborRoutingRequest { RequestedRuntime = "ClaudeCode" };

                bool owner = await manager.HasEligibleHarborForUserAsync("usr_owner", request).ConfigureAwait(false);
                bool other = await manager.HasEligibleHarborForUserAsync("usr_other", request).ConfigureAwait(false);

                AssertTrue(owner, "Expected the owning user's connected Harbor to be eligible.");
                AssertTrue(!other, "Expected a different user to have no eligible Harbor.");
            }));

            cases.Add(CaseAsync("own_harbor_disconnected_is_not_eligible", "HasEligibleHarborForUserAsync returns false when the user's only Harbor is disconnected", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborHandshake handshake = new HarborHandshake
                {
                    HarborId = "hbr_offline",
                    Name = "Rig",
                    ProtocolVersion = HarborProtocol.Version,
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
                };
                await manager.OnHandshakeAsync(handshake, "ten_off", "usr_off", NoopSend).ConfigureAwait(false);

                Armada.Core.Services.HarborRoutingRequest request = new Armada.Core.Services.HarborRoutingRequest { RequestedRuntime = "ClaudeCode" };

                // The user's own, capable Harbor is registered and connected: eligible.
                bool whileConnected = await manager.HasEligibleHarborForUserAsync("usr_off", request).ConfigureAwait(false);
                AssertTrue(whileConnected, "Expected the owning user's connected Harbor to be eligible.");

                // The Harbor drops its connection (the "I have a Harbor but it isn't live" case).
                await manager.OnDisconnectedAsync("hbr_offline").ConfigureAwait(false);

                bool whileDisconnected = await manager.HasEligibleHarborForUserAsync("usr_off", request).ConfigureAwait(false);
                AssertTrue(!whileDisconnected, "Expected no eligible Harbor once the user's only Harbor is disconnected.");
            }));

            cases.Add(CaseAsync("heartbeat_marks_seen_and_tracks_jobs", "OnMessageAsync heartbeat advances liveness and job set", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborHandshake handshake = new HarborHandshake { HarborId = "hbr_cm2", Name = "Rig", ProtocolVersion = "1.0" };
                await manager.OnHandshakeAsync(handshake, "ten_cm2", "usr_cm2", NoopSend).ConfigureAwait(false);

                HarborHeartbeat heartbeat = new HarborHeartbeat { LiveJobIds = new List<string> { "job-a", "job-b" } };
                await manager.OnMessageAsync("hbr_cm2", heartbeat).ConfigureAwait(false);

                AssertEqual(2, manager.InFlightJobs("hbr_cm2"));
                Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_cm2").ConfigureAwait(false);
                AssertNotNull(stored, "Expected the harbor to be persisted.");
                AssertEqual(HarborConnectionStatusEnum.Connected, stored!.ConnectionStatus);
            }));

            cases.Add(CaseAsync("disconnect_marks_disconnected", "OnDisconnectedAsync marks the harbor offline", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_cm3", Name = "Rig", ProtocolVersion = "1.0" }, "ten_cm3", "usr_cm3", NoopSend).ConfigureAwait(false);
                await manager.OnDisconnectedAsync("hbr_cm3").ConfigureAwait(false);

                AssertTrue(!manager.IsConnected("hbr_cm3"), "Expected the harbor to be removed from the registry.");
                Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_cm3").ConfigureAwait(false);
                AssertNotNull(stored, "Expected the harbor to be persisted.");
                AssertEqual(HarborConnectionStatusEnum.Disconnected, stored!.ConnectionStatus);
            }));

            cases.Add(CaseAsync("message_for_unknown_harbor_is_noop", "OnMessageAsync for an unknown harbor is a safe no-op", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                await manager.OnMessageAsync("hbr_ghost", new HarborHeartbeat { LiveJobIds = new List<string>() }).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_ghost", new HarborError { Message = "noop" }).ConfigureAwait(false);
                AssertTrue(!manager.IsConnected("hbr_ghost"), "Expected no phantom registration.");
            }));

            cases.Add(CaseAsync("handshake_blank_id_throws", "OnHandshakeAsync rejects a blank harbor id", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);
                await AssertThrowsAsync<ArgumentException>(() => manager.OnHandshakeAsync(new HarborHandshake { HarborId = "", Name = "X", ProtocolVersion = "1.0" }, "t", "u", NoopSend));
            }));

            cases.Add(CaseAsync("handshake_null_send_throws", "OnHandshakeAsync rejects a null send channel", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);
                await AssertThrowsAsync<ArgumentNullException>(() => manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_x", Name = "X", ProtocolVersion = "1.0" }, "t", "u", null!));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HarborConnectionManager",
                displayName: "Harbor Connection Manager",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Task NoopSend(HarborMessage message, CancellationToken token) => Task.CompletedTask;

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.HarborConnectionManager",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
