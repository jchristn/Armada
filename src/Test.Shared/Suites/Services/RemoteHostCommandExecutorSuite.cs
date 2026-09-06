namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="RemoteHostCommandExecutor"/> and the connection manager's git request/reply
    /// correlation, exercised against a simulated Harbor whose send channel echoes a canned result. Positive
    /// cases assert the delegated result maps back correctly; negative cases assert the timeout and
    /// not-connected paths.
    /// </summary>
    public sealed class RemoteHostCommandExecutorSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the RemoteHostCommandExecutor suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("delegated_command_maps_result", "RunAsync delegates and maps the Harbor's result", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                // A simulated Harbor: when it receives a git request, it replies with a canned success result
                // routed back through the manager, exactly as a real Harbor would over the link.
                HarborSendDelegate simulated = (message, token) =>
                {
                    if (message is HarborGitRequest git)
                    {
                        HarborGitResult reply = new HarborGitResult
                        {
                            CorrelationId = git.CorrelationId,
                            RequestId = git.RequestId,
                            ExitCode = 0,
                            StandardOutput = "git version 2.43.0"
                        };
                        return manager.OnMessageAsync("hbr_remote", reply, token);
                    }

                    return Task.CompletedTask;
                };

                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_remote", Name = "Rig", ProtocolVersion = "1.0" }, "ten_r", "usr_r", simulated).ConfigureAwait(false);

                RemoteHostCommandExecutor executor = new RemoteHostCommandExecutor(manager, "hbr_remote");
                HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    Arguments = new List<string> { "--version" },
                    TimeoutMs = 5000
                }).ConfigureAwait(false);

                AssertTrue(result.Success, "Expected the delegated command to succeed.");
                AssertEqual(0, result.ExitCode);
                AssertContains("git version", result.StandardOutput);
            }));

            cases.Add(CaseAsync("timeout_when_no_reply", "RunAsync times out when the Harbor does not reply", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                // A silent Harbor: it accepts the send but never replies.
                HarborSendDelegate silent = (message, token) => Task.CompletedTask;
                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_silent", Name = "Rig", ProtocolVersion = "1.0" }, "ten_s", "usr_s", silent).ConfigureAwait(false);

                RemoteHostCommandExecutor executor = new RemoteHostCommandExecutor(manager, "hbr_silent");
                HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    Arguments = new List<string> { "status" },
                    TimeoutMs = 150
                }).ConfigureAwait(false);

                AssertTrue(result.TimedOut, "Expected a timeout when the Harbor does not reply.");
                AssertTrue(!result.Success, "Expected a timed-out command to be unsuccessful.");
            }));

            cases.Add(CaseAsync("not_connected_throws", "SendGitAsync rejects an unconnected Harbor", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                RemoteHostCommandExecutor executor = new RemoteHostCommandExecutor(manager, "hbr_absent");
                await AssertThrowsAsync<InvalidOperationException>(() => executor.RunAsync(new HostCommandRequest { Executable = "git", Arguments = new List<string> { "status" } }));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.RemoteHostCommandExecutor",
                displayName: "Remote Host Command Executor",
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
                suiteId: "Services.RemoteHostCommandExecutor",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
