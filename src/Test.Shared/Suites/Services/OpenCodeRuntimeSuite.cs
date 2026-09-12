namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Runtimes;
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
                int dir = args.IndexOf("--dir");
                AssertTrue(dir >= 0, "expected --dir");
                AssertEqual("/tmp/wd", args[dir + 1]);
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

            // ---- Tier recognition ----
            cases.Add(Case("opencode_models_classify_to_standard", "Common OpenCode model families classify to a real tier", TestTags.Positive, () =>
            {
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("glm-4.6"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("qwen2.5-coder"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("deepseek-chat"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("kimi-k2"));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.OpenCodeRuntime",
                displayName: "OpenCode Runtime",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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
