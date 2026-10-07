namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="TestAgentRuntime"/> exercising the shared <c>BaseAgentRuntime</c>
    /// behavior: constructor and argument validation, invalid process-id handling for liveness and
    /// stop, name and resume metadata, valid and invalid command launches, and output/started event
    /// propagation including UTF-8 stderr preservation. Process-launching cases spawn real short-lived
    /// subprocesses.
    /// </summary>
    public sealed class BaseAgentRuntimeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.BaseAgentRuntime";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Base Agent Runtime suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("constructor_null_logging_throws", "Constructor Null Logging Throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => new TestAgentRuntime(null!));
            }));

            cases.Add(CaseAsync("start_async_null_working_directory_throws", "StartAsync Null WorkingDirectory Throws", TestTags.Negative, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await AssertThrowsAsync<ArgumentNullException>(() => runtime.StartAsync(null!, "prompt"));
            }));

            cases.Add(CaseAsync("start_async_empty_working_directory_throws", "StartAsync Empty WorkingDirectory Throws", TestTags.Negative, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await AssertThrowsAsync<ArgumentNullException>(() => runtime.StartAsync("", "prompt"));
            }));

            cases.Add(CaseAsync("start_async_null_prompt_throws", "StartAsync Null Prompt Throws", TestTags.Negative, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await AssertThrowsAsync<ArgumentNullException>(() => runtime.StartAsync("/tmp", null!));
            }));

            cases.Add(CaseAsync("start_async_empty_prompt_throws", "StartAsync Empty Prompt Throws", TestTags.Negative, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await AssertThrowsAsync<ArgumentNullException>(() => runtime.StartAsync("/tmp", ""));
            }));

            cases.Add(CaseAsync("is_running_async_invalid_process_id_returns_false", "IsRunningAsync Invalid ProcessId Returns False", TestTags.Negative, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                bool running = await runtime.IsRunningAsync(99999999);
                AssertFalse(running);
            }));

            cases.Add(CaseAsync("stop_async_invalid_process_id_does_not_throw", "StopAsync Invalid ProcessId Does Not Throw", TestTags.Negative, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await runtime.StopAsync(99999999);
            }));

            cases.Add(Case("name_returns_expected", "Name Returns Expected", TestTags.Positive, () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                AssertEqual("TestRuntime", runtime.Name);
            }));

            cases.Add(Case("supports_resume_returns_false", "SupportsResume Returns False", TestTags.Positive, () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                AssertFalse(runtime.SupportsResume);
            }));

            cases.Add(CaseAsync("start_async_valid_command_returns_process_id", "StartAsync Valid Command Returns ProcessId", TestTags.Positive, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                string tempDir = Path.GetTempPath();

                bool exited = false;
                runtime.OnProcessExited += (_, _) => exited = true;

                int pid = await runtime.StartAsync(tempDir, "test prompt");
                AssertTrue(pid > 0);

                // Let the process finish (dotnet --version exits in well under a second).
                await WaitForConditionAsync(() => exited, 2000);
            }));

            cases.Add(CaseAsync("start_async_invalid_command_throws", "StartAsync Invalid Command Throws", TestTags.Negative, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                runtime.CommandOverride = "nonexistent_command_" + Guid.NewGuid().ToString("N");

                string tempDir = Path.GetTempPath();
                bool threw = false;
                try
                {
                    await runtime.StartAsync(tempDir, "test prompt");
                }
                catch
                {
                    threw = true;
                }
                AssertTrue(threw, "Expected exception for invalid command");
            }));

            cases.Add(CaseAsync("on_output_received_fires_for_output", "OnOutputReceived Fires For Output", TestTags.Positive, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                string tempDir = Path.GetTempPath();

                List<string> outputLines = new List<string>();
                runtime.OnOutputReceived += (pid, line) =>
                {
                    lock (outputLines)
                    {
                        outputLines.Add(line);
                    }
                };

                int pid = await runtime.StartAsync(tempDir, "test prompt");

                // Wait for the process to emit output (arrives in well under the old fixed window).
                await WaitForConditionAsync(() => { lock (outputLines) { return outputLines.Count > 0; } }, 3000);

                AssertTrue(outputLines.Count > 0, "Expected at least one output line");
            }));

            cases.Add(CaseAsync("on_output_received_preserves_utf8_stderr_content", "OnOutputReceived Preserves Utf8 Stderr Content", TestTags.Positive, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                string tempDir = Path.GetTempPath();
                string expected = "I\u2019m";

                if (OperatingSystem.IsWindows())
                {
                    runtime.CommandOverride = "powershell";
                    runtime.ArgsOverride = new List<string>
                    {
                        "-Command",
                        "[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false); " +
                        "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
                        "[Console]::Error.WriteLine('I' + [char]0x2019 + 'm')"
                    };
                }
                else
                {
                    runtime.CommandOverride = "bash";
                    runtime.ArgsOverride = new List<string>
                    {
                        "-lc",
                        "printf 'I\\342\\200\\231m\\n' 1>&2"
                    };
                }

                List<string> outputLines = new List<string>();
                runtime.OnOutputReceived += (pid, line) =>
                {
                    lock (outputLines)
                    {
                        outputLines.Add(line);
                    }
                };
                TaskCompletionSource<bool> exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                runtime.OnProcessExited += (_, _) => exited.TrySetResult(true);

                await runtime.StartAsync(tempDir, "test prompt");
                // Wait on the exit event, not a fixed delay: the runtime delivers every output line before OnProcessExited.
                await exited.Task.WaitAsync(TimeSpan.FromSeconds(30));

                lock (outputLines)
                {
                    AssertTrue(outputLines.Contains(expected), "Expected UTF-8 stderr content to be preserved, and delivered before the exit event; got: " + String.Join(" | ", outputLines));
                }
            }));

            cases.Add(CaseAsync("on_process_started_fires_with_pid", "OnProcessStarted Fires WithPid", TestTags.Positive, async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                string tempDir = Path.GetTempPath();

                int startedPid = 0;
                runtime.OnProcessStarted += pid => startedPid = pid;
                bool exited = false;
                runtime.OnProcessExited += (_, _) => exited = true;

                int pid = await runtime.StartAsync(tempDir, "test prompt");

                AssertTrue(pid > 0, "Expected a valid PID");
                AssertEqual(pid, startedPid, "OnProcessStarted should fire with the launched process PID");

                await WaitForConditionAsync(() => exited, 1000);
            }));

            cases.Add(CaseAsync("stdin_prompt_has_no_byte_order_mark", "A Stdin Prompt Reaches The Agent As Exactly Its UTF-8 Bytes, With No Byte Order Mark", TestTags.Positive, async () =>
            {
                // Regression: stdin used Encoding.UTF8, whose BOM preamble Process.Start flushed to the pipe before the
                // prompt (and, when the agent had already exited, made Process.Start itself throw "Broken pipe").
                string root = TestTemp.NewDirectory("stdin_bom");
                try
                {
                    string captured = Path.Combine(root, "stdin.bin");
                    TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                    runtime.PromptOnStdin = true;
                    if (OperatingSystem.IsWindows())
                    {
                        runtime.CommandOverride = "powershell";
                        runtime.ArgsOverride = new List<string>
                        {
                            "-NoProfile",
                            "-Command",
                            "$i = [Console]::OpenStandardInput(); $o = [System.IO.File]::Create('" + captured + "'); $i.CopyTo($o); $o.Close()"
                        };
                    }
                    else
                    {
                        runtime.CommandOverride = "sh";
                        runtime.ArgsOverride = new List<string> { "-c", "cat > \"$1\"", "sh", captured };
                    }

                    TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    runtime.OnProcessExited += (_, code) => exited.TrySetResult(code);

                    string prompt = "Respond with OK \u2019 done.";
                    await runtime.StartAsync(root, prompt).ConfigureAwait(false);
                    int? exitCode = await exited.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    AssertEqual(0, exitCode ?? -1, "the capturing agent exits cleanly");

                    byte[] expected = System.Text.Encoding.UTF8.GetBytes(prompt);
                    byte[] actual = await File.ReadAllBytesAsync(captured).ConfigureAwait(false);
                    AssertEqual(Convert.ToHexString(expected), Convert.ToHexString(actual), "the agent reads exactly the prompt's UTF-8 bytes (no EF BB BF preamble)");
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            cases.Add(CaseAsync("agent_exiting_before_prompt_is_read_reports_exit", "An Agent That Exits Without Reading A Pipe-Overflowing Prompt Is Reported Through OnProcessExited, Not Thrown", TestTags.Negative, async () =>
            {
                // The agent never reads stdin and exits at once; a 1 MiB prompt exceeds any pipe buffer, so the prompt
                // write must fail with a broken pipe. StartAsync has to return the pid and report the exit code.
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                runtime.PromptOnStdin = true;
                if (OperatingSystem.IsWindows())
                {
                    runtime.CommandOverride = "cmd";
                    runtime.ArgsOverride = new List<string> { "/c", "exit 7" };
                }
                else
                {
                    runtime.CommandOverride = "sh";
                    runtime.ArgsOverride = new List<string> { "-c", "exit 7" };
                }

                TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                runtime.OnProcessExited += (_, code) => exited.TrySetResult(code);

                string prompt = new string('p', 1024 * 1024);
                int pid = await runtime.StartAsync(Path.GetTempPath(), prompt).ConfigureAwait(false);
                AssertTrue(pid > 0, "StartAsync returns the launched pid instead of throwing");

                int? exitCode = await exited.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(7, exitCode ?? -1, "the agent's exit is reported with its exit code");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Base Agent Runtime",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Poll a condition until it becomes true or the timeout elapses, returning as soon as it holds.
        /// Replaces fixed sleeps that waited on subprocess output/exit, which arrives far sooner than the
        /// old fixed windows on any normal machine.
        /// </summary>
        private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs, int pollMs = 25)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (condition()) return;
                await Task.Delay(pollMs).ConfigureAwait(false);
                waited += pollMs;
            }
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
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
