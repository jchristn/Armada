namespace Test.Shared.Suites.Runtimes
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
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// What a Mux run's exit means to Armada: a run whose run_completed event reports completed or completed_with_errors
    /// is a clean exit even though mux exits 1 or 2, with the final reply rebuilt from the stream when mux wrote none;
    /// genuine failures (iteration limit, connection and endpoint errors) stay failures with a readable reason. Covered for
    /// the outcome tracker, a local Mux launch (a shim CLI), and a Harbor launch (a simulated Harbor), plus Codex and Claude
    /// Code provider errors on the Harbor path.
    /// </summary>
    public sealed class MuxRunOutcomeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.MuxRunOutcome";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ---- Tracker ----

            cases.Add(Case("tracker_completed_with_errors_is_success", "A completed_with_errors run after a denied tool call reports exit 2 and 1 as 0, and rebuilds the last turn's reply", TestTags.Positive, () =>
            {
                MuxRunOutcomeTracker tracker = Feed(MuxJsonlFixtures.CompletedWithErrorsRun(), out List<RuntimeProviderError> errors);
                AssertTrue(tracker.RunCompleted);
                AssertEqual("completed_with_errors", tracker.RunStatus);
                AssertTrue(tracker.Succeeded, "the run succeeded");
                AssertFalse(tracker.HasFatalError, "a denied tool call is not fatal");
                AssertEqual(0, tracker.ResolveExitCode(2) ?? -99, "exit 2 reported as 0");
                AssertEqual(0, tracker.ResolveExitCode(1) ?? -99, "exit 1 reported as 0");
                AssertEqual(-1, tracker.ResolveExitCode(-1) ?? -99, "an interruption is unchanged");
                AssertEqual(3, tracker.ResolveExitCode(3) ?? -99, "another code is unchanged");
                AssertNull(tracker.ResolveExitCode(null), "unknown stays unknown");
                AssertEqual(MuxJsonlFixtures.FinalReply, tracker.FinalAssistantText, "the last turn's text, not the first turn's");
                AssertEqual("gpt-oss:20b", tracker.Model);
                AssertEqual(50, tracker.MaxIterations ?? 0);
                AssertEqual(1, errors.Count, "the denial is reported");
                AssertEqual("tool_call_denied", errors[0].ErrorType);
                AssertContains("mux denied a tool call", errors[0].Reason ?? String.Empty);
            }));

            cases.Add(Case("tracker_max_iterations_stays_failure", "A run stopped at the iteration limit keeps exit 1 and reads as the 50-iteration limit", TestTags.Negative, () =>
            {
                MuxRunOutcomeTracker tracker = Feed(MuxJsonlFixtures.MaxIterationsRun(), out List<RuntimeProviderError> errors);
                AssertFalse(tracker.Succeeded);
                AssertTrue(tracker.HasFatalError);
                AssertEqual(1, tracker.ResolveExitCode(1) ?? -99);
                AssertEqual(1, errors.Count, "the failed status is already explained by the error event");
                AssertEqual("mux stopped: reached the 50-iteration limit (max_iterations_reached)", errors[0].Reason);

                RuntimeExitInfo exit = RuntimeFailureClassifier.Decide(1, errors[0]);
                AssertEqual("mux stopped: reached the 50-iteration limit (max_iterations_reached). Agent process exited with code 1.", exit.Describe().Split('\n')[0]);
                AssertContains("Remediation:", exit.Describe());
                AssertContains("model-turn limit", exit.Describe());
            }));

            cases.Add(Case("tracker_fatal_error_in_completed_run_stays_failure", "A connection error keeps a completed_with_errors run a failure, as does an error after run_completed", TestTags.Negative, () =>
            {
                List<string> lines = new List<string> { MuxJsonlFixtures.RunStarted, MuxJsonlFixtures.ConnectionError };
                lines.AddRange(MuxJsonlFixtures.FinalTurnText);
                lines.Add(MuxJsonlFixtures.CompletedWithErrors);
                MuxRunOutcomeTracker tracker = Feed(lines, out List<RuntimeProviderError> errors);
                AssertFalse(tracker.Succeeded, "a connection error is a genuine failure");
                AssertEqual(1, tracker.ResolveExitCode(1) ?? -99);
                AssertContains("mux could not reach the model endpoint: Failed to connect to http://localhost:11434: Connection refused (llm_connection_error)", errors[0].Reason ?? String.Empty);

                List<string> late = MuxJsonlFixtures.CompletedWithErrorsRun();
                late.Add("{\"contractVersion\":2,\"eventType\":\"error\",\"code\":\"schema_validation_failed\",\"message\":\"Output did not conform to --output-schema: missing field\",\"failureCategory\":\"validation\"}");
                MuxRunOutcomeTracker lateTracker = Feed(late, out List<RuntimeProviderError> _);
                AssertFalse(lateTracker.Succeeded, "an error after run_completed fails the run");
                AssertEqual(1, lateTracker.ResolveExitCode(1) ?? -99);
            }));

            cases.Add(Case("tracker_failed_run_overrides_exit_zero", "Exit 0 is reported as 1 when the structured outcome says the run failed; exit 0 without run_completed stays 0", TestTags.Negative, () =>
            {
                MuxRunOutcomeTracker failed = Feed(MuxJsonlFixtures.MaxIterationsRun(), out List<RuntimeProviderError> _);
                AssertEqual(1, failed.ResolveExitCode(0) ?? -99, "a run stopped at its limit is not a success because the process exited 0");

                List<string> fatal = new List<string> { MuxJsonlFixtures.RunStarted, MuxJsonlFixtures.ConnectionError };
                fatal.AddRange(MuxJsonlFixtures.FinalTurnText);
                fatal.Add(MuxJsonlFixtures.CompletedWithErrors);
                AssertEqual(1, Feed(fatal, out List<RuntimeProviderError> _).ResolveExitCode(0) ?? -99, "a fatal error wins over exit 0");

                MuxRunOutcomeTracker unstructured = Feed(new List<string> { "plain text from an older mux" }, out List<RuntimeProviderError> _);
                AssertEqual(0, unstructured.ResolveExitCode(0) ?? -99, "no structured outcome: the process code stands");
            }));

            cases.Add(Case("tracker_llm_error_status_classifies", "An llm_error names its HTTP status, so a 401 is an auth failure and a 429 backpressure", TestTags.Positive, () =>
            {
                MuxRunOutcomeTracker tracker = new MuxRunOutcomeTracker();
                RuntimeProviderError? unauthorized = tracker.ObserveLine(MuxJsonlFixtures.UnauthorizedError);
                AssertNotNull(unauthorized);
                AssertEqual(401, unauthorized!.HttpStatusCode ?? 0);
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(1, unauthorized));
                AssertContains("the model endpoint rejected the request", unauthorized.Reason ?? String.Empty);

                RuntimeProviderError? named = tracker.ObserveLine("{\"eventType\":\"error\",\"code\":\"llm_error\",\"message\":\"LLM request to http://x failed with status TooManyRequests: slow down\"}");
                AssertEqual(429, named!.HttpStatusCode ?? 0, "a status name is read too");
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, named), "a 429 without a quota signal is backpressure (RS-5.4)");

                RuntimeProviderError? connection = new MuxRunOutcomeTracker().ObserveLine(MuxJsonlFixtures.ConnectionError);
                AssertNull(connection!.HttpStatusCode, "no status without an HTTP response");
            }));

            cases.Add(Case("tracker_without_run_completed_keeps_exit", "A run that never reached run_completed (a bootstrap error, a crash) keeps its exit code", TestTags.Negative, () =>
            {
                MuxRunOutcomeTracker tracker = new MuxRunOutcomeTracker();
                RuntimeProviderError? error = tracker.ObserveLine("{\"contractVersion\":2,\"eventType\":\"error\",\"code\":\"endpoint_not_found\",\"message\":\"Endpoint 'nope' was not found.\",\"failureCategory\":\"configuration\"}");
                tracker.ObserveLine("plain text the CLI printed");
                AssertFalse(tracker.RunCompleted);
                AssertEqual(1, tracker.ResolveExitCode(1) ?? -99);
                AssertEqual("mux error: Endpoint 'nope' was not found (endpoint_not_found)", error!.Reason);
                AssertNull(tracker.FinalAssistantText);
            }));

            cases.Add(Case("tracker_failed_status_without_error_reports_status", "A failed run status no error event explained is reported itself", TestTags.Negative, () =>
            {
                MuxRunOutcomeTracker tracker = new MuxRunOutcomeTracker();
                RuntimeProviderError? error = tracker.ObserveLine("{\"contractVersion\":2,\"eventType\":\"run_completed\",\"status\":\"failed\"}");
                AssertNotNull(error);
                AssertEqual("failed", error!.ErrorType);
                AssertContains("(failed)", error.Reason ?? String.Empty);
                AssertEqual(1, tracker.ResolveExitCode(1) ?? -99);
            }));

            cases.Add(Case("describe_without_reason_preserves_provider_detail", "Provider detail is preserved with remediation; an unknown exit stays unchanged", TestTags.Negative, () =>
            {
                RuntimeProviderError plain = new RuntimeProviderError { HttpStatusCode = 429, ErrorType = "rate_limit_error" };
                AssertEqual("Agent process exited with code 1 (provider error HTTP 429 rate_limit_error)", RuntimeFailureClassifier.Decide(1, plain).Describe().Split('\n')[0]);
                AssertContains("Remediation: The endpoint is rate limiting", RuntimeFailureClassifier.Decide(1, plain).Describe());
                AssertEqual("Agent process exited with code 2", RuntimeFailureClassifier.Decide(2, null).Describe().Split('\n')[0]);
                AssertContains("Remediation: The runtime reported no provider error", RuntimeFailureClassifier.Decide(2, null).Describe(), "an Unclassified exit says where to look (RS-5.2)");
            }));

            // ---- Local launch ----

            cases.Add(CaseAsync("mux_runtime_completed_with_errors_exits_clean", "A local Mux launch that exits 2 after completed_with_errors reports exit 0 and writes the rebuilt final message", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("mux_outcome_clean");
                string shim = MuxShimCli.Write(Path.Combine(root, "bin"), MuxJsonlFixtures.CompletedWithErrorsRun(), 2, null, null);
                MuxRuntime runtime = new MuxRuntime(Logging()) { ExecutablePath = shim };
                string finalFile = Path.Combine(root, "final", "final.txt");
                string log = Path.Combine(root, "mission.log");

                Capture run = await RunAsync(runtime, root, log, finalFile).ConfigureAwait(false);

                AssertEqual(0, run.ExitCode ?? -99, "reported as a clean exit");
                AssertTrue(File.Exists(finalFile), "the final message was written");
                AssertEqual(MuxJsonlFixtures.FinalReply, File.ReadAllText(finalFile));
                AssertEqual(1, run.Errors.Count, "the denial was reported as a provider error");
                AssertContains("Agent exited with code 2 (its run completed; reported as 0)", File.ReadAllText(log));
            }));

            cases.Add(CaseAsync("mux_runtime_max_iterations_stays_failure", "A local Mux launch that stops at the iteration limit reports exit 1 and the reason, and writes no final message", TestTags.Negative, async () =>
            {
                string root = TestTemp.NewDirectory("mux_outcome_limit");
                string shim = MuxShimCli.Write(Path.Combine(root, "bin"), MuxJsonlFixtures.MaxIterationsRun(), 1, null, null);
                MuxRuntime runtime = new MuxRuntime(Logging()) { ExecutablePath = shim };
                string finalFile = Path.Combine(root, "final.txt");

                Capture run = await RunAsync(runtime, root, null, finalFile).ConfigureAwait(false);

                AssertEqual(1, run.ExitCode ?? -99);
                AssertFalse(File.Exists(finalFile), "no final message for a failed run");
                AssertEqual("mux stopped: reached the 50-iteration limit (max_iterations_reached)", run.Errors.Last().Reason);
            }));

            cases.Add(CaseAsync("mux_runtime_keeps_final_message_mux_wrote", "A clean Mux exit keeps the final message mux wrote", TestTags.Negative, async () =>
            {
                string root = TestTemp.NewDirectory("mux_outcome_written");
                List<string> lines = new List<string> { MuxJsonlFixtures.RunStarted };
                lines.AddRange(MuxJsonlFixtures.FinalTurnText);
                lines.Add("{\"contractVersion\":2,\"eventType\":\"run_completed\",\"status\":\"completed\"}");
                string shim = MuxShimCli.Write(Path.Combine(root, "bin"), lines, 0, "Written by mux", null);
                MuxRuntime runtime = new MuxRuntime(Logging()) { ExecutablePath = shim };
                string finalFile = Path.Combine(root, "final.txt");

                Capture run = await RunAsync(runtime, root, null, finalFile).ConfigureAwait(false);

                AssertEqual(0, run.ExitCode ?? -99);
                AssertEqual("Written by mux", File.ReadAllText(finalFile).Trim());
                AssertEqual(0, run.Errors.Count);
            }));

            cases.Add(CaseAsync("mux_runtime_structured_progress_decodes_and_keeps_raw", "A Mux mission streaming its progress raises readable lines (markers on their own line) and activities, and the raw events on OnRawStdoutReceived", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("mux_outcome_structured");
                List<string> lines = MuxJsonlFixtures.CompletedWithErrorsRun();
                string shim = MuxShimCli.Write(Path.Combine(root, "bin"), lines, 2, null, null);
                MuxRuntime runtime = new MuxRuntime(Logging()) { ExecutablePath = shim, StructuredProgress = true };
                string log = Path.Combine(root, "mission.log");

                Capture run = await RunAsync(runtime, root, log, Path.Combine(root, "final.txt")).ConfigureAwait(false);

                AssertEqual(0, run.ExitCode ?? -99);
                AssertEqual(lines.Count, run.Raw.Count, "every raw event reached OnRawStdoutReceived");
                AssertEqual(lines[0], run.Raw[0]);
                AssertTrue(run.Stdout.Contains("[ARMADA:STATUS] Testing"), "the marker is a standalone stdout line: " + String.Join(" | ", run.Stdout));
                AssertTrue(run.Stdout.Contains("I will read the README."), "deltas are joined");
                AssertFalse(run.Stdout.Any(l => l.Contains("\"eventType\"", StringComparison.Ordinal)), "no raw events on stdout");
                List<string> summaries = run.Activities.Select(a => a.Summary).ToList();
                AssertTrue(summaries.Contains("Reading README.md"), String.Join(" | ", summaries));
                AssertTrue(summaries.Contains("Calling tool open_browser"), String.Join(" | ", summaries));
                string text = File.ReadAllText(log);
                AssertContains("> Reading README.md", text);
                AssertFalse(text.Contains("\"eventType\"", StringComparison.Ordinal), "no raw events in the log");
            }));

            // ---- Harbor launch ----

            cases.Add(CaseAsync("remote_mux_completed_with_errors_exits_clean", "A Harbor Mux mission that exits 2 after completed_with_errors reports 0, decodes on the Admiral, and writes the rebuilt final message", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("mux_outcome_remote");
                RemoteRun run = await RunRemoteAsync(AgentRuntimeEnum.Mux, MuxJsonlFixtures.CompletedWithErrorsRun(), 2, root).ConfigureAwait(false);

                AssertEqual(0, run.ExitCode ?? -99, "reported as a clean exit");
                AssertTrue(run.Request!.StreamJsonOutput, "the Harbor passes the raw events through");
                AssertTrue(run.Request.StructuredProgress, "a mission streaming its progress");
                AssertEqual(MuxJsonlFixtures.FinalReply, File.ReadAllText(run.FinalFile));
                AssertEqual(MuxJsonlFixtures.CompletedWithErrorsRun().Count, run.Capture.Raw.Count, "the raw events reach the Admiral");
                AssertTrue(run.Capture.Stdout.Contains("[ARMADA:STATUS] Testing"), String.Join(" | ", run.Capture.Stdout));
                AssertTrue(run.Capture.Activities.Any(a => a.Summary == "Reading README.md"), "activity decoded on the Admiral");
                AssertEqual(1, run.Capture.Errors.Count);
            }));

            cases.Add(CaseAsync("remote_mux_max_iterations_reports_reason", "A Harbor Mux mission stopped at the iteration limit keeps exit 1 and reports the reason", TestTags.Negative, async () =>
            {
                string root = TestTemp.NewDirectory("mux_outcome_remote_limit");
                RemoteRun run = await RunRemoteAsync(AgentRuntimeEnum.Mux, MuxJsonlFixtures.MaxIterationsRun(), 1, root).ConfigureAwait(false);

                AssertEqual(1, run.ExitCode ?? -99);
                AssertFalse(File.Exists(run.FinalFile), "no final message for a failed run");
                AssertEqual("mux stopped: reached the 50-iteration limit (max_iterations_reached)", run.Capture.Errors.Last().Reason);
            }));

            cases.Add(CaseAsync("remote_codex_and_claude_provider_errors", "On the Harbor path Codex turn.failed and Claude Code error results are provider errors", TestTags.Positive, async () =>
            {
                string codexRoot = TestTemp.NewDirectory("mux_outcome_remote_codex");
                RemoteRun codex = await RunRemoteAsync(AgentRuntimeEnum.Codex, new List<string>
                {
                    "{\"type\":\"thread.started\",\"thread_id\":\"th_1\"}",
                    "{\"type\":\"turn.failed\",\"error\":{\"message\":\"unexpected status 401 Unauthorized: invalid api key\"}}"
                }, 1, codexRoot).ConfigureAwait(false);
                AssertEqual(1, codex.Capture.Errors.Count, "the failed turn");
                AssertEqual(401, codex.Capture.Errors[0].HttpStatusCode ?? 0);
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(1, codex.Capture.Errors[0]));
                AssertContains("codex turn failed: unexpected status 401", codex.Capture.Errors[0].Reason ?? String.Empty);

                string claudeRoot = TestTemp.NewDirectory("mux_outcome_remote_claude");
                RemoteRun claude = await RunRemoteAsync(AgentRuntimeEnum.ClaudeCode, new List<string>
                {
                    "{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"claude-sonnet-4\"}",
                    "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":true,\"result\":\"API Error: 429 {\\\"type\\\":\\\"error\\\",\\\"error\\\":{\\\"type\\\":\\\"rate_limit_error\\\",\\\"message\\\":\\\"slow down\\\"}}\"}"
                }, 1, claudeRoot).ConfigureAwait(false);
                AssertTrue(claude.Capture.Errors.Count >= 1, "the error result");
                AssertEqual(429, claude.Capture.Errors[0].HttpStatusCode ?? 0);
                AssertEqual("rate_limit_error", claude.Capture.Errors[0].ErrorType);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Mux run outcome and provider errors",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static MuxRunOutcomeTracker Feed(List<string> lines, out List<RuntimeProviderError> errors)
        {
            MuxRunOutcomeTracker tracker = new MuxRunOutcomeTracker();
            errors = new List<RuntimeProviderError>();
            foreach (string line in lines)
            {
                RuntimeProviderError? error = tracker.ObserveLine(line);
                if (error != null) errors.Add(error);
            }

            return tracker;
        }

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static Capture Subscribe(IAgentRuntime runtime, TaskCompletionSource<int?> exited)
        {
            Capture capture = new Capture();
            runtime.OnStdoutReceived += (pid, line) => { lock (capture) capture.Stdout.Add(line); };
            runtime.OnActivity += (pid, activity) => { lock (capture) capture.Activities.Add(activity); };
            runtime.OnProviderError += (pid, error) => { lock (capture) capture.Errors.Add(error); };
            if (runtime is IRawStdoutSource raw) raw.OnRawStdoutReceived += (pid, line) => { lock (capture) capture.Raw.Add(line); };
            runtime.OnProcessExited += (pid, code) =>
            {
                capture.ExitCode = code;
                exited.TrySetResult(code);
            };
            return capture;
        }

        private static async Task<Capture> RunAsync(BaseAgentRuntime runtime, string workingDirectory, string? logFile, string? finalMessageFile)
        {
            TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Capture capture = Subscribe(runtime, exited);
            await runtime.StartAsync(workingDirectory, "do the work", null, logFile, finalMessageFile).ConfigureAwait(false);
            Task done = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            AssertTrue(done == exited.Task, "the shim exited");
            return capture;
        }

        private static async Task<RemoteRun> RunRemoteAsync(AgentRuntimeEnum runtimeType, List<string> lines, int exitCode, string root)
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            HarborService harbors = new HarborService(testDb.Driver, Logging());
            HarborConnectionManager manager = new HarborConnectionManager(harbors, Logging(), null);
            RemoteRun run = new RemoteRun();
            run.FinalFile = Path.Combine(root, "final", "final.txt");

            // Simulated Harbor: passes the CLI's raw lines through, as a current Harbor does for a passthrough mission
            // (and every Harbor does for Mux), then reports mux's exit code. Mux wrote no final message.
            HarborSendDelegate simulated = async (message, cancellation) =>
            {
                if (message is HarborLaunchRequest launch)
                {
                    run.Request = launch;
                    await manager.OnMessageAsync("hbr_mux", new HarborStarted { JobId = launch.JobId, ProcessId = 5151 }, cancellation).ConfigureAwait(false);
                    foreach (string line in lines)
                        await manager.OnMessageAsync("hbr_mux", new HarborOutput { JobId = launch.JobId, Stream = HarborOutputStreamEnum.Stdout, Data = line }, cancellation).ConfigureAwait(false);
                    await manager.OnMessageAsync("hbr_mux", new HarborExited { JobId = launch.JobId, ExitCode = exitCode }, cancellation).ConfigureAwait(false);
                }
            };

            await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_mux", Name = "Laptop", ProtocolVersion = "1.0" }, "ten_mux", "usr_mux", simulated).ConfigureAwait(false);
            RemoteAgentRuntime runtime = new RemoteAgentRuntime(manager, "hbr_mux", runtimeType) { StructuredProgress = true, JobKind = HarborJobKindEnum.Mission, MissionId = "msn_mux" };
            TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            run.Capture = Subscribe(runtime, exited);
            await runtime.StartAsync(root, "do the work", null, Path.Combine(root, "mission.log"), run.FinalFile).ConfigureAwait(false);
            Task done = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            AssertTrue(done == exited.Task, "the job exited");
            run.ExitCode = run.Capture.ExitCode;
            return run;
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

        #region Private-Types

        private sealed class Capture
        {
            public List<string> Stdout { get; } = new List<string>();

            public List<string> Raw { get; } = new List<string>();

            public List<RuntimeActivity> Activities { get; } = new List<RuntimeActivity>();

            public List<RuntimeProviderError> Errors { get; } = new List<RuntimeProviderError>();

            public int? ExitCode { get; set; } = null;
        }

        private sealed class RemoteRun
        {
            public HarborLaunchRequest? Request { get; set; } = null;

            public Capture Capture { get; set; } = new Capture();

            public string FinalFile { get; set; } = String.Empty;

            public int? ExitCode { get; set; } = null;
        }

        #endregion
    }
}
