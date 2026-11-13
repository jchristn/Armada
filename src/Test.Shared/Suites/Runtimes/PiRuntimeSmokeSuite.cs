namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Smoke cases against a real <c>pi</c>: skipped unless <c>pi</c> is on PATH, identifies itself as pi, and
    /// <c>pi auth check --provider &lt;provider&gt; --no-refresh</c> exits 0. The provider comes from
    /// <c>ARMADA_TEST_PI_PROVIDER</c> (default <c>anthropic</c>) and the model from <c>ARMADA_TEST_PI_MODEL</c> (default
    /// <c>anthropic/claude-haiku-4-5</c> for the anthropic provider). Each case calls the real model.
    /// </summary>
    public sealed class PiRuntimeSmokeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.PiSmoke";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the pi smoke suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            string provider = Environment.GetEnvironmentVariable("ARMADA_TEST_PI_PROVIDER") ?? "anthropic";
            string? model = Environment.GetEnvironmentVariable("ARMADA_TEST_PI_MODEL");
            if (String.IsNullOrWhiteSpace(model) && provider == "anthropic") model = "anthropic/claude-haiku-4-5";
            string? skip = SkipReason(provider);

            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("real_pi_creates_file_and_replies", "A real pi run creates hello.txt, replies DONE, exits 0 and reports tokens", skip, async () =>
            {
                string root = TestTemp.NewDirectory("pi_smoke");
                try
                {
                    Git(root, "init", "-q");
                    string finalMessage = Path.Combine(Path.GetTempPath(), "armada-pi-smoke-" + Guid.NewGuid().ToString("N") + ".txt");
                    ChatTurnTelemetryRecorder telemetry = new ChatTurnTelemetryRecorder();
                    PiRuntime runtime = new SmokePiRuntime(Logging());
                    runtime.OnStdoutReceived += (int pid, string line) => telemetry.ObserveLine(AgentRuntimeEnum.Pi, line);
                    int? code = await RunAsync(runtime, root, "Create a file named hello.txt containing exactly the text hi (no newline is fine). Then reply with exactly: DONE", model, finalMessage).ConfigureAwait(false);

                    AssertEqual(0, code ?? -1, "pi exited cleanly");
                    AssertEqual("hi", File.ReadAllText(Path.Combine(root, "hello.txt")).Trim());
                    AssertContains("DONE", File.ReadAllText(finalMessage));
                    AssertTrue((telemetry.InputTokens ?? 0) > 0, "input tokens reported");
                    AssertTrue((telemetry.OutputTokens ?? 0) > 0, "output tokens reported");
                    try { File.Delete(finalMessage); } catch { }
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            cases.Add(Case("real_pi_bad_key_is_classified", "A real pi run with an invalid API key exits nonzero with a classified provider error", skip ?? (String.IsNullOrWhiteSpace(model) ? "Set ARMADA_TEST_PI_MODEL (provider/id) for the bad-key case." : null), async () =>
            {
                string root = TestTemp.NewDirectory("pi_smoke_bad");
                try
                {
                    RuntimeProviderError? error = null;
                    BadKeyPiRuntime runtime = new BadKeyPiRuntime(Logging());
                    runtime.OnProviderError += (int pid, RuntimeProviderError e) => error = RuntimeFailureClassifier.KeepMoreSignificant(error, e);
                    int? code = await RunAsync(runtime, root, "Reply with exactly: hello", model, null).ConfigureAwait(false);

                    AssertTrue(code.HasValue && code.Value != 0, "the run failed even though pi exits 0 in JSON mode (code " + code + ")");
                    AssertNotNull(error, "a provider error was raised");
                    AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(code, error), "classified as an auth failure: " + error!.Reason);
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            cases.Add(Case("real_pi_approve_in_armada_gate", "Approve in Armada with a real pi: an allowed bash call runs, a denied one returns the reason to the model, and the run continues", skip, async () =>
            {
                string root = TestTemp.NewDirectory("pi_smoke_gate");
                try
                {
                    Git(root, "init", "-q");
                    using FakePermissionPromptMcpServer server = new FakePermissionPromptMcpServer();
                    server.Decide = call => Task.FromResult((call.Command ?? "").Contains("approved", StringComparison.Ordinal)
                        ? FakePermissionPromptMcpServer.Allow()
                        : FakePermissionPromptMcpServer.Deny("ARMADA-DENIED-BY-TEST"));
                    string finalMessage = Path.Combine(Path.GetTempPath(), "armada-pi-smoke-gate-" + Guid.NewGuid().ToString("N") + ".txt");
                    List<string> stdout = new List<string>();
                    PiRuntime runtime = new SmokePiRuntime(Logging());
                    runtime.OnStdoutReceived += (int pid, string line) => { lock (stdout) stdout.Add(line); };
                    AssertTrue(CliPermissionPromptSetup.Configure(runtime, CliPermissionPolicyEnum.ApproveInArmada, 60), "the gate is configured");
                    Captain captain = CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("pi-gate", AgentRuntimeEnum.Pi), false);
                    Dictionary<string, string> environment = new Dictionary<string, string>
                    {
                        [CaptainThreadMcpPlanner.UrlEnvironmentVariable] = server.Url,
                        [CaptainThreadMcpPlanner.TokenEnvironmentVariable] = "smoke-token"
                    };

                    TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    runtime.OnProcessExited += (int pid, int? exitCode) => exited.TrySetResult(exitCode);
                    string prompt = "Use the bash tool for each step, one command per call, in order.\n"
                        + "1. Run exactly: echo approved > approved.txt\n"
                        + "2. Run exactly: echo second > second.txt\n"
                        + "If a command is blocked, do not retry it and do not use any other tool for it. Finally reply with exactly: DONE";
                    await runtime.StartAsync(root, prompt, environment, finalMessageFilePath: finalMessage, model: model, captain: captain).ConfigureAwait(false);
                    Task finished = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromMinutes(4))).ConfigureAwait(false);
                    AssertTrue(finished == exited.Task, "pi did not finish within 4 minutes");
                    int? code = await exited.Task.ConfigureAwait(false);
                    string transcript;
                    lock (stdout) transcript = String.Join("\n", stdout);
                    string tail = transcript.Length > 3000 ? transcript.Substring(transcript.Length - 3000) : transcript;

                    AssertEqual(0, code ?? -1, "the run continued after the denial and ended cleanly; stdout tail: " + tail);
                    AssertTrue(server.Calls.Count >= 2, "both bash calls were asked in Armada (" + server.Calls.Count + "); stdout tail: " + tail);
                    AssertEqual("approved", File.ReadAllText(Path.Combine(root, "approved.txt")).Trim(), "the allowed command ran");
                    AssertFalse(File.Exists(Path.Combine(root, "second.txt")), "the denied command did not run");
                    AssertContains("ARMADA-DENIED-BY-TEST", transcript, "the denial reason reached the model as the tool result");
                    AssertContains("DONE", File.ReadAllText(finalMessage));
                    try { File.Delete(finalMessage); } catch { }
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "pi Runtime Smoke (real pi)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string? SkipReason(string provider)
        {
            string? path = HarborRuntimeDetector.ResolveOnPath("pi", Environment.GetEnvironmentVariable("PATH"), OperatingSystem.IsWindows());
            if (path == null) return "pi is not on PATH.";
            if (!RuntimeDetectionService.IsPiCommand(path)) return "The 'pi' on PATH is not the pi coding agent.";
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(path)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string arg in new string[] { "auth", "check", "--provider", provider, "--no-refresh" }) info.ArgumentList.Add(arg);
                using Process process = Process.Start(info)!;
                process.StandardInput.Close();
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(true); } catch { }
                    return "pi auth check timed out.";
                }

                return process.ExitCode == 0 ? null : "pi auth check --provider " + provider + " is not ready (exit " + process.ExitCode + ").";
            }
            catch (Exception ex)
            {
                return "pi auth check failed: " + ex.Message;
            }
        }

        private static async Task<int?> RunAsync(PiRuntime runtime, string work, string prompt, string? model, string? finalMessage)
        {
            TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            List<string> output = new();
            int? ownedProcess = null;
            runtime.OnStdoutReceived += (_, line) => { lock (output) { output.Add(line); if (output.Count>20) output.RemoveAt(0); } };
            runtime.OnProcessExited += (int pid, int? code) => exited.TrySetResult(code);
            try
            {
                ownedProcess = await runtime.StartAsync(work, prompt, finalMessageFilePath: finalMessage, model: model).ConfigureAwait(false);
                Task finished = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromMinutes(4))).ConfigureAwait(false);
                string tail; lock (output) tail = String.Join("\n", output);
                if (tail.Length>2000) tail=tail.Substring(tail.Length-2000);
                AssertTrue(finished == exited.Task, "pi did not finish within 4 minutes; recent output: " + tail);
                return await exited.Task.ConfigureAwait(false);
            }
            finally { if (ownedProcess.HasValue) await runtime.StopAsync(ownedProcess.Value).ConfigureAwait(false); }
        }

        private static void Git(string workingDirectory, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
        }

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string? skipReason, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.EndToEnd },
                skipReason: skipReason);
        }

        #endregion

        #region Private-Classes

        // Keep real provider/tool/permission execution, but do not load the developer's MCP servers,
        // extensions or repository instructions into a disposable smoke fixture. Explicit gate -e still loads.
        private class SmokePiRuntime : PiRuntime
        {
            public SmokePiRuntime(LoggingModule logging) : base(logging) { }

            protected override List<string> BuildArguments(string workingDirectory, string prompt, string? model, string? finalMessageFilePath, Captain? captain)
            {
                List<string> args = base.BuildArguments(workingDirectory, prompt, model, finalMessageFilePath, captain);
                args.AddRange(new[] { "--no-mcp", "--no-extensions", "--no-skills", "--no-prompt-templates", "--no-context-files" });
                return args;
            }
        }

        private sealed class BadKeyPiRuntime : SmokePiRuntime
        {
            public BadKeyPiRuntime(LoggingModule logging) : base(logging) { }

            protected override List<string> BuildArguments(string workingDirectory, string prompt, string? model, string? finalMessageFilePath, Captain? captain)
            {
                List<string> args = base.BuildArguments(workingDirectory, prompt, model, finalMessageFilePath, captain);
                args.Add("--api-key");
                args.Add("sk-armada-smoke-invalid-key");
                return args;
            }
        }

        #endregion
    }
}
