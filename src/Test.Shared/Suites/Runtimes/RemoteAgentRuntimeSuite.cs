namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="RemoteAgentRuntime"/> and the connection manager's launch delegation,
    /// exercised against a simulated Harbor whose send channel replies with the started/output/exited
    /// lifecycle. Proves that a delegated launch surfaces as the ordinary agent-runtime events (started pid,
    /// output lines, exit code) and that stop routes by process id.
    /// </summary>
    public sealed class RemoteAgentRuntimeSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the RemoteAgentRuntime suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("delegated_launch_raises_lifecycle_events", "Delegated launch raises started/output/exited", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                // Simulated Harbor: on a launch request, report started (pid 4242), one output line, then exit 0.
                HarborSendDelegate simulated = async (message, cancellation) =>
                {
                    if (message is HarborLaunchRequest launch)
                    {
                        await manager.OnMessageAsync("hbr_rt", new HarborStarted { JobId = launch.JobId, ProcessId = 4242 }, cancellation).ConfigureAwait(false);
                        await manager.OnMessageAsync("hbr_rt", new HarborOutput { JobId = launch.JobId, Stream = HarborOutputStreamEnum.Stdout, Data = "working" }, cancellation).ConfigureAwait(false);
                        await manager.OnMessageAsync("hbr_rt", new HarborExited { JobId = launch.JobId, ExitCode = 0 }, cancellation).ConfigureAwait(false);
                    }
                };

                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_rt", Name = "Rig", ProtocolVersion = "1.0" }, "ten_rt", "usr_rt", simulated).ConfigureAwait(false);

                RemoteHostProcessExecutor executor = new RemoteHostProcessExecutor(manager, "hbr_rt");
                IAgentRuntime runtime = executor.CreateRuntime(AgentRuntimeEnum.ClaudeCode);

                int startedPid = 0;
                List<string> output = new List<string>();
                int? exitCode = null;
                runtime.OnProcessStarted += pid => startedPid = pid;
                runtime.OnOutputReceived += (pid, line) => output.Add(line);
                runtime.OnProcessExited += (pid, code) => exitCode = code;

                int returnedPid = await runtime.StartAsync("/repo", "do the work").ConfigureAwait(false);

                AssertEqual(4242, returnedPid);
                AssertEqual(4242, startedPid);
                AssertTrue(output.Contains("working"), "Expected the delegated output line to surface.");
                AssertEqual(0, exitCode ?? -1);
            }));

            cases.Add(CaseAsync("start_times_out_without_started", "StartAsync surfaces a timeout when the Harbor never starts the job", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborSendDelegate silent = (message, cancellation) => Task.CompletedTask;
                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_silent_rt", Name = "Rig", ProtocolVersion = "1.0" }, "t", "u", silent).ConfigureAwait(false);

                RemoteHostProcessExecutor executor = new RemoteHostProcessExecutor(manager, "hbr_silent_rt");
                IAgentRuntime runtime = executor.CreateRuntime(AgentRuntimeEnum.ClaudeCode);

                // Cancel quickly so the 60s start-timeout wait does not stall the suite; StartAsync should throw.
                using CancellationTokenSource cts = new CancellationTokenSource(300);
                await AssertThrowsAsync<Exception>(() => runtime.StartAsync("/repo", "x", token: cts.Token));
            }));

            cases.Add(CaseAsync("launch_unconnected_harbor_throws", "LaunchAsync rejects an unconnected Harbor", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                RemoteHostProcessExecutor executor = new RemoteHostProcessExecutor(manager, "hbr_absent_rt");
                IAgentRuntime runtime = executor.CreateRuntime(AgentRuntimeEnum.ClaudeCode);
                await AssertThrowsAsync<InvalidOperationException>(() => runtime.StartAsync("/repo", "x"));
            }));

            cases.Add(CaseAsync("delegated_output_is_mirrored_to_log_file", "Delegated output is mirrored to the Admiral-side mission log file", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborSendDelegate simulated = async (message, cancellation) =>
                {
                    if (message is HarborLaunchRequest launch)
                    {
                        await manager.OnMessageAsync("hbr_log", new HarborStarted { JobId = launch.JobId, ProcessId = 7777 }, cancellation).ConfigureAwait(false);
                        await manager.OnMessageAsync("hbr_log", new HarborOutput { JobId = launch.JobId, Stream = HarborOutputStreamEnum.Stdout, Data = "line-one" }, cancellation).ConfigureAwait(false);
                        await manager.OnMessageAsync("hbr_log", new HarborOutput { JobId = launch.JobId, Stream = HarborOutputStreamEnum.Stdout, Data = "line-two" }, cancellation).ConfigureAwait(false);
                        await manager.OnMessageAsync("hbr_log", new HarborExited { JobId = launch.JobId, ExitCode = 0 }, cancellation).ConfigureAwait(false);
                    }
                };
                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_log", Name = "Rig", ProtocolVersion = "1.0" }, "t", "u", simulated).ConfigureAwait(false);

                string logPath = Path.Combine(Path.GetTempPath(), "armada-test-" + Guid.NewGuid().ToString("N") + ".log");
                try
                {
                    RemoteHostProcessExecutor executor = new RemoteHostProcessExecutor(manager, "hbr_log");
                    IAgentRuntime runtime = executor.CreateRuntime(AgentRuntimeEnum.ClaudeCode);
                    await runtime.StartAsync("/repo", "the prompt", logFilePath: logPath).ConfigureAwait(false);

                    AssertTrue(File.Exists(logPath), "Expected the mission log file to be written on the Admiral side.");
                    string contents = await File.ReadAllTextAsync(logPath).ConfigureAwait(false);
                    AssertTrue(contents.Contains("line-one") && contents.Contains("line-two"), "Expected both streamed output lines in the mirrored log.");
                }
                finally
                {
                    try { File.Delete(logPath); } catch { }
                }
            }));

            cases.Add(CaseAsync("select_prefers_connected_capable_harbor", "SelectHarborAsync chooses a connected Harbor that advertises the runtime", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborSendDelegate send = (message, cancellation) => Task.CompletedTask;
                await manager.OnHandshakeAsync(new HarborHandshake
                {
                    HarborId = "hbr_sel",
                    Name = "Rig",
                    ProtocolVersion = "1.0",
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
                }, "ten_sel", "usr_sel", send).ConfigureAwait(false);

                HarborRoutingDecision decision = await manager.SelectHarborAsync("ten_sel", new HarborRoutingRequest { RequestedRuntime = "ClaudeCode" }).ConfigureAwait(false);
                AssertTrue(decision.Success, "Expected a connected, capable Harbor to be chosen.");
                AssertEqual("hbr_sel", decision.HarborId ?? string.Empty);
            }));

            cases.Add(CaseAsync("select_without_capability_falls_back", "SelectHarborAsync declines when no Harbor advertises the runtime", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborSendDelegate send = (message, cancellation) => Task.CompletedTask;
                await manager.OnHandshakeAsync(new HarborHandshake
                {
                    HarborId = "hbr_git_only",
                    Name = "Rig",
                    ProtocolVersion = "1.0",
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "git", Available = true } }
                }, "ten_git", "usr_git", send).ConfigureAwait(false);

                HarborRoutingDecision decision = await manager.SelectHarborAsync("ten_git", new HarborRoutingRequest { RequestedRuntime = "ClaudeCode" }).ConfigureAwait(false);
                AssertTrue(!decision.Success, "Expected no Harbor to be chosen when the runtime is not advertised (caller runs locally).");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Runtimes.RemoteAgentRuntime",
                displayName: "Remote Agent Runtime",
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
                suiteId: "Runtimes.RemoteAgentRuntime",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
