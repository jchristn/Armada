namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
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
    /// Harbor link loss and reconnect at the protocol level. Client side (<see cref="HarborLinkClient"/>): a dropped
    /// transport ends the session, stops the heartbeat, closes the transport, and a new session re-handshakes. Admiral
    /// side (<see cref="HarborConnectionManager"/>): requests waiting on a Harbor fail as soon as its link closes, a close
    /// from a superseded socket does not disconnect a Harbor that already reconnected, and delegated jobs launched
    /// before a drop keep routing their lifecycle after the reconnect.
    /// </summary>
    public sealed class HarborLinkResilienceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborLinkResilience";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("client_reconnect_rehandshakes", "Client: a closed link ends the session; the next session handshakes again", TestTags.Reliability, async () =>
            {
                HarborLinkClient client = NewClient(0);

                ScriptedHarborTransport first = new ScriptedHarborTransport();
                first.Enqueue(HarborProtocol.Serialize(new HarborHandshakeAck { Accepted = true, McpBaseUrl = "http://127.0.0.1:1/mcp" }));
                await client.RunSessionAsync(first, CancellationToken.None).ConfigureAwait(false);
                AssertTrue(first.Closed, "transport closed when the link ends");

                ScriptedHarborTransport second = new ScriptedHarborTransport();
                second.Enqueue(HarborProtocol.Serialize(new HarborHandshakeAck { Accepted = true, McpBaseUrl = "http://127.0.0.1:2/mcp" }));
                second.Enqueue(HarborProtocol.Serialize(new HarborGitRequest { RequestId = "after-reconnect", Executable = "git", WorkingDirectory = "/repo", Arguments = new List<string> { "status" } }));
                await client.RunSessionAsync(second, CancellationToken.None).ConfigureAwait(false);

                List<string> sent = second.Sent.ToList();
                AssertTrue(sent.Count >= 2, "handshake and git result on the new link");
                AssertTrue(HarborProtocol.Deserialize(sent[0]) is HarborHandshake, "a reconnect starts with a fresh handshake");
                AssertTrue(sent.Exists(s => HarborProtocol.Deserialize(s) is HarborGitResult r && r.RequestId == "after-reconnect"), "requests work after reconnect");
                AssertEqual("http://127.0.0.1:2/mcp", client.McpBaseUrl, "new ack's MCP URL adopted");
            }));

            cases.Add(CaseAsync("client_drop_stops_heartbeat", "Client: a transport error propagates and stops the heartbeat", TestTags.Reliability, async () =>
            {
                HarborLinkClient client = NewClient(20);
                ScriptedHarborTransport transport = new ScriptedHarborTransport { FailAfterScript = true, EndAfterSent = s => HarborProtocol.Deserialize(s) is HarborHeartbeat };
                transport.Enqueue(HarborProtocol.Serialize(new HarborHandshakeAck { Accepted = true }));

                await AssertThrowsAsync<IOException>(() => client.RunSessionAsync(transport, CancellationToken.None)).ConfigureAwait(false);
                AssertTrue(transport.Closed, "transport closed after the drop");
                int heartbeats = transport.Sent.Count(s => HarborProtocol.Deserialize(s) is HarborHeartbeat);
                AssertTrue(heartbeats >= 1, "heartbeats flowed while linked");

                int sentAtDrop = transport.Sent.Count;
                await Task.Delay(200).ConfigureAwait(false);
                AssertEqual(sentAtDrop, transport.Sent.Count, "nothing is sent on a dropped link");
            }));

            cases.Add(CaseAsync("pending_git_fails_on_disconnect", "Admiral: a git request waiting on a Harbor fails when its link closes", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    HarborConnectionManager manager = NewManager(testDb);
                    List<HarborMessage> outbound = new List<HarborMessage>();
                    HarborSendDelegate link = Recorder(outbound);
                    await manager.OnHandshakeAsync(Handshake("hbr_pending"), "ten_x", "usr_x", link).ConfigureAwait(false);

                    // Timeout 0 waits indefinitely: before the fix, a dropped link left this request hanging forever.
                    Task<HarborGitResult?> pending = manager.SendGitAsync("hbr_pending", new HarborGitRequest { RequestId = "g1", Executable = "git" }, 0);
                    await WaitUntilAsync(() => outbound.Count > 0).ConfigureAwait(false);
                    await manager.OnDisconnectedAsync("hbr_pending", link).ConfigureAwait(false);

                    Task finished = await Task.WhenAny(pending, Task.Delay(5000)).ConfigureAwait(false);
                    AssertTrue(finished == pending, "pending request must complete promptly after the disconnect");
                    AssertTrue(pending.IsFaulted && pending.Exception!.GetBaseException() is InvalidOperationException, "fails with InvalidOperationException");
                    AssertContains("disconnected before replying", pending.Exception!.GetBaseException().Message);
                    AssertFalse(manager.IsConnected("hbr_pending"));
                }
            }));

            cases.Add(CaseAsync("pending_deferred_launch_fails_on_disconnect", "Admiral: a deferred-launch request fails when the Harbor's link closes", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    HarborConnectionManager manager = NewManager(testDb);
                    List<HarborMessage> outbound = new List<HarborMessage>();
                    HarborSendDelegate link = Recorder(outbound);
                    await manager.OnHandshakeAsync(Handshake("hbr_deferred"), "ten_x", "usr_x", link).ConfigureAwait(false);

                    Task<HarborDeferredLaunchAck?> pending = manager.SendDeferredLaunchAsync("hbr_deferred", new HarborDeferredLaunchRequest { RequestId = "d1", LaunchExePath = "/x" }, 60000);
                    await WaitUntilAsync(() => outbound.Count > 0).ConfigureAwait(false);
                    await manager.OnDisconnectedAsync("hbr_deferred", link).ConfigureAwait(false);

                    Task finished = await Task.WhenAny(pending, Task.Delay(5000)).ConfigureAwait(false);
                    AssertTrue(finished == pending, "deferred launch must not wait out its 60 s timeout");
                    AssertTrue(pending.IsFaulted, "deferred launch fails");
                }
            }));

            cases.Add(CaseAsync("stale_close_keeps_reconnected_link", "Admiral: a late close of the old socket does not disconnect a reconnected Harbor", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    HarborConnectionManager manager = NewManager(testDb);
                    List<HarborMessage> oldOutbound = new List<HarborMessage>();
                    List<HarborMessage> newOutbound = new List<HarborMessage>();
                    HarborSendDelegate oldLink = Recorder(oldOutbound);
                    HarborSendDelegate newLink = Recorder(newOutbound);

                    await manager.OnHandshakeAsync(Handshake("hbr_flap"), "ten_x", "usr_x", oldLink).ConfigureAwait(false);
                    // The Harbor reconnects on a new socket before the server notices the old one closed.
                    await manager.OnHandshakeAsync(Handshake("hbr_flap"), "ten_x", "usr_x", newLink).ConfigureAwait(false);
                    await manager.OnDisconnectedAsync("hbr_flap", oldLink).ConfigureAwait(false);

                    AssertTrue(manager.IsConnected("hbr_flap"), "the newer link must stay connected");
                    Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_flap").ConfigureAwait(false);
                    AssertEqual(HarborConnectionStatusEnum.Connected, stored!.ConnectionStatus, "persisted status stays Connected");

                    Task<HarborGitResult?> request = manager.SendGitAsync("hbr_flap", new HarborGitRequest { RequestId = "g-new", Executable = "git" }, 2000);
                    await WaitUntilAsync(() => newOutbound.Count > 0).ConfigureAwait(false);
                    AssertEqual(0, oldOutbound.Count, "traffic goes to the new socket only");
                    await manager.OnMessageAsync("hbr_flap", new HarborGitResult { RequestId = "g-new", ExitCode = 0 }).ConfigureAwait(false);
                    AssertEqual(0, (await request.ConfigureAwait(false))!.ExitCode);

                    await manager.OnDisconnectedAsync("hbr_flap", newLink).ConfigureAwait(false);
                    AssertFalse(manager.IsConnected("hbr_flap"), "closing the current link disconnects");
                }
            }));

            cases.Add(CaseAsync("jobs_survive_reconnect", "Admiral: a job launched before a drop keeps reporting after the Harbor reconnects", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    HarborConnectionManager manager = NewManager(testDb);
                    HarborSendDelegate firstLink = Recorder(new List<HarborMessage>());
                    await manager.OnHandshakeAsync(Handshake("hbr_jobs"), "ten_x", "usr_x", firstLink).ConfigureAwait(false);

                    RecordingHarborJobListener listener = new RecordingHarborJobListener();
                    await manager.LaunchAsync("hbr_jobs", new HarborLaunchRequest { JobId = "job-1", Runtime = "claude", WorkingDirectory = "/w" }, listener).ConfigureAwait(false);
                    await manager.OnMessageAsync("hbr_jobs", new HarborStarted { JobId = "job-1", ProcessId = 777 }).ConfigureAwait(false);

                    await manager.OnDisconnectedAsync("hbr_jobs", firstLink).ConfigureAwait(false);
                    AssertEqual(0, manager.InFlightJobs("hbr_jobs"), "no live link, no in-flight count");
                    AssertTrue(manager.IsJobTracked("job-1") || listener.ProcessId == 777, "job lifecycle kept across the drop");

                    HarborSendDelegate secondLink = Recorder(new List<HarborMessage>());
                    await manager.OnHandshakeAsync(Handshake("hbr_jobs"), "ten_x", "usr_x", secondLink).ConfigureAwait(false);
                    await manager.OnMessageAsync("hbr_jobs", new HarborHeartbeat { LiveJobIds = new List<string> { "job-1" } }).ConfigureAwait(false);
                    AssertEqual(1, manager.InFlightJobs("hbr_jobs"), "heartbeat after reconnect rebinds the running job");

                    await manager.OnMessageAsync("hbr_jobs", new HarborOutput { JobId = "job-1", Data = "still running" }).ConfigureAwait(false);
                    await manager.OnMessageAsync("hbr_jobs", new HarborExited { JobId = "job-1", ExitCode = 0 }).ConfigureAwait(false);
                    AssertEqual(777, listener.ProcessId);
                    AssertTrue(listener.Output.Contains("still running"), "output after reconnect reaches the listener");
                    AssertEqual(0, listener.ExitCode, "exit after reconnect reaches the listener");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Harbor Link Resilience", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static HarborLinkClient NewClient(int heartbeatMs)
        {
            return new HarborLinkClient("hbr_client", "Rig", new List<HarborCapability> { new HarborCapability { Name = "git" } }, 4,
                new StubHostCommandExecutor(new HostCommandResult { ExitCode = 0, StandardOutput = "ok" }), Logging(), heartbeatMs);
        }

        private static HarborConnectionManager NewManager(TestDatabase testDb)
        {
            return new HarborConnectionManager(new HarborService(testDb.Driver, Logging()), Logging(), null);
        }

        private static HarborHandshake Handshake(string harborId)
        {
            return new HarborHandshake
            {
                HarborId = harborId,
                Name = "Rig",
                ProtocolVersion = HarborProtocol.Version,
                MaxConcurrentJobs = 4,
                Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
            };
        }

        private static HarborSendDelegate Recorder(List<HarborMessage> sink)
        {
            return (HarborMessage message, CancellationToken token) =>
            {
                lock (sink) sink.Add(message);
                return Task.CompletedTask;
            };
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(5));
            while (!condition())
            {
                if (deadline.Passed) throw new TimeoutException("condition not met within 5 s");
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
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
