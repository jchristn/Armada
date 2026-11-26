namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the OpenCode runtime's argument builder and reasoning-variant mapping. Positive
    /// cases confirm the headless <c>opencode run</c> command shape and the effort-to-variant collapse;
    /// negative cases confirm optional flags are omitted and bad input is rejected rather than emitted.
    /// </summary>
    public sealed class OpenCodeRuntimeSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the OpenCode runtime suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ---- Command builder ----
            cases.Add(Case("run_args_core_shape", "OpenCode run args start with run --format json; prompt goes to stdin", TestTags.Positive, () =>
            {
                List<string> args = OpenCodeCommandBuilder.BuildRunArguments("/tmp/wd", "do the thing", null, null, false, true);
                AssertEqual("run", args[0]);
                int fmt = args.IndexOf("--format");
                AssertTrue(fmt >= 0, "expected --format");
                AssertEqual("json", args[fmt + 1]);
                // OpenCode 2 rejects --dir; the process starts in the working directory instead.
                AssertFalse(args.Contains("--dir"), "--dir must not be passed");
                AssertFalse(args.Contains("/tmp/wd"), "the working directory is not an argument");
                // The prompt is delivered on stdin (OpenCodeRuntime.UsePromptStdin), not as a positional
                // argument, to avoid Windows cmd.exe multi-line-argument truncation.
                AssertFalse(args.Contains("do the thing"), "prompt must not be a CLI argument");
                AssertTrue(args.Contains("--auto"), "expected --auto when autoApprove");
            }));

            cases.Add(Case("run_args_model_variant_thinking", "OpenCode run args include model, variant, and thinking when set", TestTags.Positive, () =>
            {
                List<string> args = OpenCodeCommandBuilder.BuildRunArguments("/tmp/wd", "hi", "openai/gpt-5.2", "high", true, true);
                int m = args.IndexOf("--model");
                AssertTrue(m >= 0, "expected --model");
                AssertEqual("openai/gpt-5.2", args[m + 1]);
                int v = args.IndexOf("--variant");
                AssertTrue(v >= 0, "expected --variant");
                AssertEqual("high", args[v + 1]);
                AssertTrue(args.Contains("--thinking"), "expected --thinking");
            }));

            cases.Add(Case("run_args_omit_optionals", "OpenCode run args omit model/variant/thinking/auto when not set", TestTags.Negative, () =>
            {
                List<string> args = OpenCodeCommandBuilder.BuildRunArguments("/tmp/wd", "hi", null, null, false, false);
                AssertFalse(args.Contains("--model"), "did not expect --model");
                AssertFalse(args.Contains("--variant"), "did not expect --variant");
                AssertFalse(args.Contains("--thinking"), "did not expect --thinking");
                AssertFalse(args.Contains("--auto"), "did not expect --auto");
            }));

            cases.Add(Case("run_args_reject_blank", "OpenCode run args reject blank working dir / prompt", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => OpenCodeCommandBuilder.BuildRunArguments("", "hi", null, null, false, true));
                AssertThrows<ArgumentNullException>(() => OpenCodeCommandBuilder.BuildRunArguments("/tmp/wd", "", null, null, false, true));
            }));

            // ---- Reasoning variant mapping ----
            cases.Add(Case("variant_maps_levels", "Reasoning effort maps to OpenCode minimal/high", TestTags.Positive, () =>
            {
                AssertEqual("minimal", ReasoningEffortTranslator.ToOpenCodeVariant(ReasoningEffortEnum.Minimal));
                AssertEqual("minimal", ReasoningEffortTranslator.ToOpenCodeVariant(ReasoningEffortEnum.Low));
                AssertEqual("high", ReasoningEffortTranslator.ToOpenCodeVariant(ReasoningEffortEnum.Medium));
                AssertEqual("high", ReasoningEffortTranslator.ToOpenCodeVariant(ReasoningEffortEnum.High));
            }));

            cases.Add(Case("variant_off_and_null_omit", "Reasoning effort Off/null omit the OpenCode variant", TestTags.Negative, () =>
            {
                AssertNull(ReasoningEffortTranslator.ToOpenCodeVariant(ReasoningEffortEnum.Off));
                AssertNull(ReasoningEffortTranslator.ToOpenCodeVariant(null));
            }));

            // ---- JSONL protocol handling (fixtures match real OpenCode 1.18 --format json output) ----
            const string realTextEvent = "{\"type\":\"text\",\"timestamp\":1788469553125,\"sessionID\":\"ses_x\",\"part\":{\"id\":\"prt_x\",\"type\":\"text\",\"text\":\"Hi there!\"}}";
            const string realToolEvent = "{\"type\":\"tool_use\",\"timestamp\":1788469590116,\"sessionID\":\"ses_y\",\"part\":{\"type\":\"tool\",\"tool\":\"bash\",\"callID\":\"call-1\",\"state\":{\"status\":\"completed\",\"input\":{\"command\":\"echo hi\"},\"output\":\"hi\\n\",\"metadata\":{\"exit\":0}}}}";
            const string realStepEvent = "{\"type\":\"step_start\",\"timestamp\":1788469550801,\"sessionID\":\"ses_z\",\"part\":{\"type\":\"step-start\"}}";

            cases.Add(Case("protocol_event_recognized", "Real OpenCode text/tool/step events are protocol lines", TestTags.Positive, () =>
            {
                AssertTrue(OpenCodeRuntime.IsProtocolEventLine(realTextEvent), "a text event is a protocol event");
                AssertTrue(OpenCodeRuntime.IsProtocolEventLine(realToolEvent), "a tool_use event is a protocol event");
                AssertTrue(OpenCodeRuntime.IsProtocolEventLine(realStepEvent), "a step_start event is a protocol event");
            }));

            cases.Add(Case("plain_text_is_not_protocol", "A plain text line is not a protocol event", TestTags.Negative, () =>
            {
                AssertFalse(OpenCodeRuntime.IsProtocolEventLine("Building the project..."), "plain text is not a protocol event");
                AssertFalse(OpenCodeRuntime.IsProtocolEventLine("{ not json"), "malformed JSON is not a protocol event");
            }));

            cases.Add(Case("assistant_text_extracted", "Human-visible text is extracted from a real text event", TestTags.Positive, () =>
            {
                AssertEqual("Hi there!", OpenCodeRuntime.TryExtractAssistantText(realTextEvent));
            }));

            cases.Add(Case("tool_event_has_no_text", "A tool_use event yields no assistant text", TestTags.Negative, () =>
            {
                AssertNull(OpenCodeRuntime.TryExtractAssistantText(realToolEvent));
            }));

            cases.Add(Case("formatter_resolves_opencode_tool", "The runtime-log formatter resolves an OpenCode tool_use event", TestTags.Positive, () =>
            {
                FormattedLogLine line = RuntimeLogFormatter.Format(realToolEvent, AgentRuntimeEnum.OpenCode);
                AssertTrue(line.IsToolCall, "expected a tool call");
                AssertEqual("bash", line.ToolName);
                AssertTrue(line.Text.Contains("bash"), "the tool name should appear in the text");
            }));

            cases.Add(Case("formatter_drops_opencode_step", "The runtime-log formatter drops OpenCode step markers", TestTags.Negative, () =>
            {
                FormattedLogLine line = RuntimeLogFormatter.Format(realStepEvent, AgentRuntimeEnum.OpenCode);
                AssertTrue(line.Dropped, "a step marker should be dropped, not echoed as raw JSON");
            }));

            const string realReasoningEvent = "{\"type\":\"reasoning\",\"timestamp\":1,\"sessionID\":\"ses_r\",\"part\":{\"type\":\"reasoning\",\"text\":\"Let me think about this.\"}}";

            cases.Add(Case("reasoning_is_not_reply_text", "A reasoning event is not surfaced as assistant reply text", TestTags.Negative, () =>
            {
                AssertNull(OpenCodeRuntime.TryExtractAssistantText(realReasoningEvent), "reasoning must not become reply text");
            }));

            cases.Add(Case("formatter_marks_reasoning_as_thinking", "The runtime-log formatter marks reasoning as thinking, not raw JSON", TestTags.Positive, () =>
            {
                FormattedLogLine line = RuntimeLogFormatter.Format(realReasoningEvent, AgentRuntimeEnum.OpenCode);
                AssertFalse(line.Dropped, "reasoning should render");
                AssertTrue(line.Text.Contains("thinking"), "reasoning should be marked as thinking");
                AssertTrue(line.Text.Contains("Let me think"), "the reasoning text should be present");
            }));

            // ---- Final message and provider errors from the JSON stream (RS-7.12) ----
            const string toolThenText = "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"Looking at the parser.\"}}";
            const string noRouteError = "{\"type\":\"error\",\"timestamp\":1795731191537,\"sessionID\":\"ses_x\",\"error\":{\"type\":\"provider.no-route\",\"message\":\"Model unavailable: nosuchprovider/nosuchmodel\"}}";

            cases.Add(Case("final_message_is_last_turn_text", "The reply is the text after the last tool call, without reasoning or events", TestTags.Positive, () =>
            {
                OpenCodeFinalMessage message = new OpenCodeFinalMessage();
                message.Begin(null);
                message.Observe(realStepEvent);
                message.Observe(toolThenText);
                message.Observe(realReasoningEvent);
                message.Observe(realToolEvent);
                message.Observe("{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"Changed the parser.\"}}");
                message.Observe("{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"[ARMADA:RESULT] COMPLETE\"}}");
                message.Observe("not an event");
                AssertEqual("Changed the parser.\n[ARMADA:RESULT] COMPLETE", message.Text());
                AssertEqual(0, message.Complete(0));
            }));

            cases.Add(Case("error_event_without_recovery_fails", "Exit 0 after an error event with no later reply is a failure; a later reply recovers", TestTags.Negative, () =>
            {
                OpenCodeFinalMessage failed = new OpenCodeFinalMessage();
                failed.Begin(null);
                failed.Observe(toolThenText);
                failed.Observe(noRouteError);
                AssertEqual(OpenCodeFinalMessage.FailedRunExitCode, failed.Complete(0));
                AssertEqual(1, failed.Complete(1));

                OpenCodeFinalMessage recovered = new OpenCodeFinalMessage();
                recovered.Begin(null);
                recovered.Observe(noRouteError);
                recovered.Observe(realTextEvent);
                AssertEqual(0, recovered.Complete(0));
            }));

            cases.Add(Case("error_event_parsed_as_provider_error", "An OpenCode error event on stdout becomes a provider error; a status and an open-model message classify", TestTags.Positive, () =>
            {
                RuntimeProviderError? noRoute = RuntimeProviderErrorParser.TryParseOpenCodeJsonLine(noRouteError);
                AssertNotNull(noRoute, "an error event is a provider error");
                AssertEqual("provider.no-route", noRoute!.ErrorType);
                AssertContains("Model unavailable", noRoute.Reason ?? String.Empty);

                RuntimeProviderError? limited = RuntimeProviderErrorParser.TryParseOpenCodeJsonLine("{\"type\":\"error\",\"error\":{\"name\":\"APIError\",\"data\":{\"message\":\"Too Many Requests\",\"statusCode\":429}}}");
                AssertEqual(429, limited?.HttpStatusCode);
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, limited));

                RuntimeProviderError? parse = RuntimeProviderErrorParser.TryParseOpenCodeJsonLine("{\"type\":\"error\",\"error\":{\"name\":\"APIError\",\"data\":{\"message\":\"error parsing tool call: invalid character\",\"statusCode\":500}}}");
                AssertEqual(OpenModelErrorPatterns.ToolCallParseError, parse?.ErrorType);

                AssertNull(RuntimeProviderErrorParser.TryParseOpenCodeJsonLine(realTextEvent), "a text event is not an error");
                AssertNull(RuntimeProviderErrorParser.TryParseOpenCodeJsonLine("Error: 429 on stdout narration"), "plain text is not an error");
            }));

            cases.Add(Case("stub_run_writes_reply_and_classifies_error", "An OpenCode process run writes the rebuilt reply; an error event on stdout raises a provider error", TestTags.Positive, () =>
            {
                if (OperatingSystem.IsWindows()) return;
                string root = Path.Combine(Path.GetTempPath(), "armada-oc-final-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    string reply = "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"<think>check</think>Done.\"}}";
                    string ok = WriteStub(root, "ok.sh", new List<string> { realStepEvent, realReasoningEvent, realToolEvent, reply }, 0);
                    string finalMessage = Path.Combine(root, "final.txt");
                    StubRunResult okRun = RunStub(ok, root, finalMessage);
                    AssertEqual(0, okRun.ExitCode);
                    AssertNull(okRun.Error, "a clean run raises no provider error");
                    AssertEqual("Done.", File.ReadAllText(finalMessage));

                    string bad = WriteStub(root, "bad.sh", new List<string> { noRouteError }, 1);
                    string badFinal = Path.Combine(root, "bad.txt");
                    StubRunResult badRun = RunStub(bad, root, badFinal);
                    AssertEqual(1, badRun.ExitCode);
                    AssertNotNull(badRun.Error, "the stdout error event is a provider error");
                    AssertEqual("provider.no-route", badRun.Error!.ErrorType);
                    AssertFalse(File.Exists(badFinal), "a failed run writes no final message");
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            // ---- Tier recognition ----
            cases.Add(Case("opencode_models_classify_to_standard", "Common OpenCode model families classify to a real tier", TestTags.Positive, () =>
            {
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("glm-4.6"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("qwen2.5-coder"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("deepseek-chat"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("kimi-k2"));
            }));

            cases.Add(CaseAsync("error_event_fails_chat_after_text", "An OpenCode error event fails chat after earlier text", TestTags.Negative, () =>
                RunChatAsync(ChatShimCli.OpenCodeTextThenErrorLines(), response =>
                {
                    AssertFalse(response.Success, "an OpenCode error event must fail the chat turn");
                    ChatShimCli.AssertBoundedProviderFailure(response.Error, "chat error");
                    AssertFalse((response.Reply ?? String.Empty).Contains(ChatShimCli.OpenCodeBodyMarker), "the chat reply includes the response body");
                    AssertFalse((response.Reply ?? String.Empty).Contains(ChatShimCli.OpenCodeHeaderMarker), "the chat reply includes a response header");
                    AssertFalse((response.Reply ?? String.Empty).Contains(ChatShimCli.OpenCodeUrlMarker), "the chat reply includes the URL");
                    AssertFalse((response.Reply ?? String.Empty).Contains(ChatShimCli.OpenCodeSessionMarker), "the chat reply includes the session id");
                })));

            cases.Add(CaseAsync("text_event_chat_stays_successful", "An OpenCode text event with no error still succeeds", TestTags.Positive, () =>
                RunChatAsync(ChatShimCli.OpenCodeTextOnlyLines(), response =>
                {
                    AssertTrue(response.Success, "a text event must stay a successful chat turn");
                    AssertContains(ChatShimCli.OpenCodeCompleteText, response.Reply ?? String.Empty);
                    AssertTrue(String.IsNullOrEmpty(response.Error), "a successful text turn has no error");
                })));

            return new TestSuiteDescriptor(
                suiteId: "Services.OpenCodeRuntime",
                displayName: "OpenCode Runtime",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private sealed class StubRunResult
        {
            public int? ExitCode { get; set; } = null;

            public RuntimeProviderError? Error { get; set; } = null;
        }

        private static string WriteStub(string root, string name, List<string> lines, int exitCode)
        {
            string path = Path.Combine(root, name);
            string fixture = Path.Combine(root, name + ".jsonl");
            File.WriteAllText(fixture, String.Join("\n", lines) + "\n");
            File.WriteAllText(path, "#!/bin/sh\ncat > /dev/null\ncat '" + fixture + "'\nexit " + exitCode + "\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }

        private static StubRunResult RunStub(string stub, string root, string finalMessage)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            OpenCodeRuntime runtime = new OpenCodeRuntime(logging) { ExecutablePath = stub };
            StubRunResult result = new StubRunResult();
            using ManualResetEventSlim exited = new ManualResetEventSlim(false);
            runtime.OnProviderError += (pid, e) => result.Error = RuntimeFailureClassifier.KeepMoreSignificant(result.Error, e);
            runtime.OnProcessExited += (pid, code) => { result.ExitCode = code; exited.Set(); };
            runtime.StartAsync(root, "do it", finalMessageFilePath: finalMessage).GetAwaiter().GetResult();
            AssertTrue(exited.Wait(TimeSpan.FromSeconds(15)), "stub did not exit");
            return result;
        }

        private static async Task RunChatAsync(List<string> lines, Action<CaptainChatResponse> assert)
        {
            using (ChatShimCli.IsolateOpenCodeHostConfig())
            {
                string root = Path.Combine(Path.GetTempPath(), "armada-oc-chat-" + Guid.NewGuid().ToString("N"));
                try
                {
                    string shim = ChatShimCli.WriteOpenCodeShim(root, lines);
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        Captain captain = await testDb.Driver.Captains.CreateAsync(new Captain("opencode-chat", AgentRuntimeEnum.OpenCode)).ConfigureAwait(false);
                        LoggingModule logging = new LoggingModule();
                        logging.Settings.EnableConsole = false;
                        AgentRuntimeFactory factory = new AgentRuntimeFactory(logging);
                        factory.Override(AgentRuntimeEnum.OpenCode, () => new OpenCodeRuntime(logging) { ExecutablePath = shim });
                        CaptainChatService service = new CaptainChatService(testDb.Driver, factory, null, null, null, 0, logging);
                        CaptainChatTurnResult turn = await service.RunTurnAsync(new CaptainChatTurnOptions
                        {
                            Captain = captain,
                            Prompt = "Reply with the fixture"
                        }).ConfigureAwait(false);
                        assert(turn.Response);
                    }
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(root)) Directory.Delete(root, true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.OpenCodeRuntime",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.OpenCodeRuntime",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
