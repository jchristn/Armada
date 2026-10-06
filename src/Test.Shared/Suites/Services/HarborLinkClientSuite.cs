namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="HarborLinkClient"/> driven over a fake transport with a stubbed command
    /// executor. The client must open with a handshake, execute a delegated git request and reply with a
    /// gitResult, and refuse a launch request with an explanatory error until the process executor exists.
    /// </summary>
    public sealed class HarborLinkClientSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the HarborLinkClient suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("opens_with_handshake_and_runs_git", "Client handshakes then executes a git request", TestTags.Positive, async () =>
            {
                FakeTransport transport = new FakeTransport();
                transport.Enqueue(HarborProtocol.Serialize(new HarborHandshakeAck { Accepted = true, McpBaseUrl = "http://127.0.0.1:7891/mcp" }));
                transport.Enqueue(HarborProtocol.Serialize(new HarborGitRequest { RequestId = "r1", Executable = "git", WorkingDirectory = "/repo", Arguments = new List<string> { "status" } }));

                StubExecutor executor = new StubExecutor(new HostCommandResult { ExitCode = 0, StandardOutput = "clean" });
                HarborLinkClient client = new HarborLinkClient("hbr_lc", "Rig", new List<HarborCapability> { new HarborCapability { Name = "git" } }, 4, executor, CreateLogging(), 0);

                await client.RunSessionAsync(transport, CancellationToken.None).ConfigureAwait(false);

                AssertTrue(transport.Sent.Count >= 2, "Expected at least a handshake and a git result to be sent.");

                HarborMessage first = HarborProtocol.Deserialize(transport.Sent[0]);
                AssertTrue(first is HarborHandshake, "Expected the first message to be a handshake.");

                HarborGitResult? result = FindGitResult(transport.Sent);
                AssertNotNull(result, "Expected a git result to be sent.");
                AssertEqual("r1", result!.RequestId);
                AssertEqual(0, result.ExitCode);
                AssertEqual("clean", result.StandardOutput);
                AssertEqual("http://127.0.0.1:7891/mcp", client.McpBaseUrl);
            }));

            cases.Add(CaseAsync("launch_request_is_refused", "Client refuses a launch request with an error", TestTags.Negative, async () =>
            {
                FakeTransport transport = new FakeTransport();
                transport.Enqueue(HarborProtocol.Serialize(new HarborLaunchRequest { JobId = "job-1", Runtime = "claude", WorkingDirectory = "/repo" }));

                StubExecutor executor = new StubExecutor(new HostCommandResult { ExitCode = 0 });
                HarborLinkClient client = new HarborLinkClient("hbr_lc2", "Rig", new List<HarborCapability>(), 4, executor, CreateLogging(), 0);

                await client.RunSessionAsync(transport, CancellationToken.None).ConfigureAwait(false);

                HarborError? error = FindError(transport.Sent);
                AssertNotNull(error, "Expected a launch request to be refused with an error.");
                AssertEqual("job-1", error!.JobId);
            }));

            cases.Add(CaseAsync("log_lines_never_end_with_period", "Harbor log entries never end with a period (ellipsis kept)", TestTags.Positive, () =>
            {
                AssertEqual("Disconnected by operator", new HarborLogEntry(HarborLogDirection.Info, "Disconnected by operator.").Message);
                AssertEqual("Connect failed: No such host is known", new HarborLogEntry(HarborLogDirection.Info, "Connect failed: No such host is known.  ").Message, "exception text with a trailing period");
                AssertEqual("Odd", new HarborLogEntry(HarborLogDirection.Info, "Odd. .").Message, "repeated periods");
                AssertEqual("Dialing ...", new HarborLogEntry(HarborLogDirection.Info, "Dialing ...").Message, "ellipsis kept");
                AssertEqual("Handshake accepted by Admiral. MCP=x", new HarborLogEntry(HarborLogDirection.In, "Handshake accepted by Admiral. MCP=x").Message, "inner period kept");
                AssertEqual("", new HarborLogEntry(HarborLogDirection.Info, null!).Message, "null becomes empty");
                HarborLogEntry set = new HarborLogEntry();
                set.Message = "Set later.";
                AssertEqual("Set later", set.Message, "setter normalizes too");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("null_transport_throws", "RunSessionAsync rejects a null transport", TestTags.Negative, async () =>
            {
                StubExecutor executor = new StubExecutor(new HostCommandResult());
                HarborLinkClient client = new HarborLinkClient("hbr_lc3", "Rig", new List<HarborCapability>(), 4, executor, CreateLogging(), 0);
                await AssertThrowsAsync<ArgumentNullException>(() => client.RunSessionAsync(null!, CancellationToken.None));
            }));

            cases.Add(CaseAsync("health_poll_survives_wall_clock_jump", "The deferred-launch health poll times out on the monotonic clock: a wall-clock jump (sleep/wake) does not end it early and roll back a healthy slot", TestTags.Reliability, async () =>
            {
                // The poll used a DateTime.UtcNow deadline: when the host slept while the new slot started, the wall
                // clock jumped past the deadline and the poll gave up, rolling back a slot that was about to answer.
                // Here every wall-clock reading is 10 minutes after the previous one.
                HarborLinkClient client = new HarborLinkClient("hbr_lc4", "Rig", new List<HarborCapability>(), 4, new StubExecutor(new HostCommandResult()), CreateLogging(), 0);
                JumpingTimeProvider time = new JumpingTimeProvider();
                client.Time = time;
                int probes = 0;

                bool healthy = await client.PollUntilHealthyAsync(
                    () => Task.FromResult(Interlocked.Increment(ref probes) >= 4),
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

                AssertTrue(healthy, "the poll kept probing until the slot answered instead of giving up on the jumped wall clock");
                AssertEqual(4, probes);
            }));

            cases.Add(CaseAsync("health_poll_times_out_on_monotonic_clock", "The deferred-launch health poll still gives up once its timeout elapses on the monotonic clock", TestTags.Negative, async () =>
            {
                HarborLinkClient client = new HarborLinkClient("hbr_lc5", "Rig", new List<HarborCapability>(), 4, new StubExecutor(new HostCommandResult()), CreateLogging(), 0);
                JumpingTimeProvider time = new JumpingTimeProvider();
                client.Time = time;
                int probes = 0;

                bool healthy = await client.PollUntilHealthyAsync(
                    () =>
                    {
                        Interlocked.Increment(ref probes);
                        time.Advance(TimeSpan.FromSeconds(1));
                        return Task.FromResult(false);
                    },
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);

                AssertFalse(healthy, "a slot that never answers is reported unhealthy");
                AssertEqual(5, probes);
            }));

            cases.Add(CaseAsync("launch_durations_use_monotonic_clock", "A launched job's runtime and time to first output are measured on the monotonic clock, not inflated by a wall-clock jump", TestTags.Reliability, async () =>
            {
                JumpingTimeProvider time = new JumpingTimeProvider();
                ScriptedJobRunner runner = new ScriptedJobRunner(time);
                FakeTransport transport = new FakeTransport();
                transport.Enqueue(HarborProtocol.Serialize(new HarborLaunchRequest { JobId = "job-m", Runtime = "claude", WorkingDirectory = "/repo" }));
                HarborLinkClient client = new HarborLinkClient("hbr_lc6", "Rig", new List<HarborCapability>(), 4, new StubExecutor(new HostCommandResult()), CreateLogging(), 0, null, runner);
                client.Time = time;

                await client.RunSessionAsync(transport, CancellationToken.None).ConfigureAwait(false);

                HarborExited? exited = null;
                foreach (string raw in transport.Sent)
                {
                    if (HarborProtocol.Deserialize(raw) is HarborExited e) exited = e;
                }

                AssertNotNull(exited, "the exit was reported");
                AssertNotNull(exited!.DurationMs, "runtime reported");
                AssertNotNull(exited.TimeToFirstTokenMs, "time to first output reported");
                // 2 s before the first output and 3 s more before the exit, on the monotonic clock. A wall-clock
                // measurement would include the 10-minute jumps.
                AssertTrue(exited.DurationMs!.Value >= 5000 && exited.DurationMs.Value < 60000, "runtime is the monotonic 5 s, not wall-clock jumps: " + exited.DurationMs.Value);
                AssertTrue(exited.TimeToFirstTokenMs!.Value >= 2000 && exited.TimeToFirstTokenMs.Value <= exited.DurationMs.Value, "first output is the monotonic 2 s: " + exited.TimeToFirstTokenMs.Value);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HarborLinkClient",
                displayName: "Harbor Link Client",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static HarborGitResult? FindGitResult(List<string> sent)
        {
            foreach (string raw in sent)
            {
                HarborMessage message = HarborProtocol.Deserialize(raw);
                if (message is HarborGitResult result) return result;
            }

            return null;
        }

        private static HarborError? FindError(List<string> sent)
        {
            foreach (string raw in sent)
            {
                HarborMessage message = HarborProtocol.Deserialize(raw);
                if (message is HarborError error) return error;
            }

            return null;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.HarborLinkClient",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private sealed class FakeTransport : IHarborTransport
        {
            private readonly Queue<string> _Inbound = new Queue<string>();

            public List<string> Sent { get; } = new List<string>();

            public void Enqueue(string message) => _Inbound.Enqueue(message);

            public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;

            public Task SendAsync(string text, CancellationToken token)
            {
                Sent.Add(text);
                return Task.CompletedTask;
            }

            public Task<string?> ReceiveAsync(CancellationToken token)
            {
                if (_Inbound.Count == 0) return Task.FromResult<string?>(null);
                return Task.FromResult<string?>(_Inbound.Dequeue());
            }

            public Task CloseAsync(CancellationToken token) => Task.CompletedTask;
        }

        private sealed class ScriptedJobRunner : IHarborJobRunner
        {
            private readonly JumpingTimeProvider _Time;

            public ScriptedJobRunner(JumpingTimeProvider time) => _Time = time;

            public Task StartAsync(HarborLaunchRequest request, string? mcpBaseUrl, Action<int> onStarted, Action<HarborOutputStreamEnum, string> onOutput, Action<int> onExited, CancellationToken token)
            {
                onStarted(4242);
                _Time.Advance(TimeSpan.FromSeconds(2));
                onOutput(HarborOutputStreamEnum.Stdout, "hello");
                _Time.Advance(TimeSpan.FromSeconds(3));
                onOutput(HarborOutputStreamEnum.Stdout, "more");
                onExited(0);
                return Task.CompletedTask;
            }

            public Task StopAsync(string jobId, int gracefulTimeoutMs, CancellationToken token) => Task.CompletedTask;
        }

        private sealed class StubExecutor : IHostCommandExecutor
        {
            private readonly HostCommandResult _Result;

            public StubExecutor(HostCommandResult result) => _Result = result;

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default) => Task.FromResult(_Result);
        }

        #endregion
    }
}
