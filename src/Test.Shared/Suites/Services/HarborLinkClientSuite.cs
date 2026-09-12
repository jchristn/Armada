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

            cases.Add(CaseAsync("null_transport_throws", "RunSessionAsync rejects a null transport", TestTags.Negative, async () =>
            {
                StubExecutor executor = new StubExecutor(new HostCommandResult());
                HarborLinkClient client = new HarborLinkClient("hbr_lc3", "Rig", new List<HarborCapability>(), 4, executor, CreateLogging(), 0);
                await AssertThrowsAsync<ArgumentNullException>(() => client.RunSessionAsync(null!, CancellationToken.None));
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

        private sealed class StubExecutor : IHostCommandExecutor
        {
            private readonly HostCommandResult _Result;

            public StubExecutor(HostCommandResult result) => _Result = result;

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default) => Task.FromResult(_Result);
        }

        #endregion
    }
}
