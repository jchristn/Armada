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

            // ---- JSONL protocol handling ----
            cases.Add(Case("protocol_event_recognized", "A JSON object event with a type is a protocol line", TestTags.Positive, () =>
            {
                AssertTrue(OpenCodeRuntime.IsProtocolEventLine("{\"type\":\"message\",\"part\":{\"text\":\"hello\"}}"), "a typed JSON object is a protocol event");
            }));

            cases.Add(Case("plain_text_is_not_protocol", "A plain text line is not a protocol event", TestTags.Negative, () =>
            {
                AssertFalse(OpenCodeRuntime.IsProtocolEventLine("Building the project..."), "plain text is not a protocol event");
                AssertFalse(OpenCodeRuntime.IsProtocolEventLine("{ not json"), "malformed JSON is not a protocol event");
            }));

            cases.Add(Case("assistant_text_extracted", "Human-visible text is extracted from a protocol event", TestTags.Positive, () =>
            {
                AssertEqual("hello", OpenCodeRuntime.TryExtractAssistantText("{\"type\":\"message\",\"part\":{\"text\":\"hello\"}}"));
                AssertEqual("hi", OpenCodeRuntime.TryExtractAssistantText("{\"type\":\"message\",\"text\":\"hi\"}"));
            }));

            cases.Add(Case("tool_event_has_no_text", "A tool-only event yields no assistant text", TestTags.Negative, () =>
            {
                AssertNull(OpenCodeRuntime.TryExtractAssistantText("{\"type\":\"tool\",\"name\":\"bash\"}"));
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
