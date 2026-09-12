namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="MuxCliService"/> running through the <see cref="IHostCommandExecutor"/> seam.
    /// A fake executor records the request and returns canned output, so these cases assert that a probe runs
    /// "mux" with the caller's working directory (the hook that lets the probe run on a Harbor), parses the
    /// JSON result, and maps a timed-out execution to a <see cref="TimeoutException"/> -- all without spawning
    /// a real process.
    /// </summary>
    public sealed class MuxCliServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the MuxCliService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("probe_runs_mux_in_working_directory", "ProbeAsync runs mux through the executor and threads the working directory", TestTags.Positive, async () =>
            {
                string json = "{\"success\":true,\"endpointName\":\"local\",\"builtInToolCount\":7,\"mcpConfigured\":true,\"mcpServerCount\":2}";
                RecordingExecutor executor = new RecordingExecutor(new HostCommandResult { ExitCode = 0, StandardOutput = json });
                MuxCliService service = new MuxCliService(CreateLogging(), executor);

                MuxProbeResult result = await service.ProbeAsync("gpt-oss:20b", new MuxCaptainOptions { Endpoint = "local" }, "/work/dock", CancellationToken.None).ConfigureAwait(false);

                AssertTrue(result.Success, "Expected the probe JSON to parse as success.");
                AssertEqual("local", result.EndpointName);
                AssertEqual(7, result.BuiltInToolCount);
                AssertEqual(2, result.McpServerCount);
                AssertTrue(executor.LastRequest != null, "Expected the executor to receive a request.");
                AssertEqual("mux", executor.LastRequest!.Executable);
                AssertEqual("/work/dock", executor.LastRequest!.WorkingDirectory);
                AssertTrue(executor.LastRequest!.Arguments.Count > 0, "Expected probe arguments to be passed to mux.");
            }));

            cases.Add(CaseAsync("timeout_maps_to_exception", "ProbeAsync surfaces a timed-out execution as TimeoutException", TestTags.Negative, async () =>
            {
                RecordingExecutor executor = new RecordingExecutor(new HostCommandResult { ExitCode = -1, TimedOut = true });
                MuxCliService service = new MuxCliService(CreateLogging(), executor);

                await AssertThrowsAsync<TimeoutException>(() => service.ProbeAsync("gpt-oss:20b", new MuxCaptainOptions { Endpoint = "local" }, null, CancellationToken.None)).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.MuxCliService",
                displayName: "Mux CLI Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MuxCliService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Members-Types

        /// <summary>
        /// An <see cref="IHostCommandExecutor"/> that records the last request and returns a canned result.
        /// </summary>
        private sealed class RecordingExecutor : IHostCommandExecutor
        {
            public HostCommandRequest? LastRequest { get; private set; } = null;

            private readonly HostCommandResult _Result;

            public RecordingExecutor(HostCommandResult result)
            {
                _Result = result ?? throw new ArgumentNullException(nameof(result));
            }

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                LastRequest = request;
                return Task.FromResult(_Result);
            }
        }

        #endregion
    }
}
