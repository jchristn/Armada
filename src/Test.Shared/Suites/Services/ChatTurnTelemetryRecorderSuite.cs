namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ChatTurnTelemetryRecorder"/>, the shared per-turn telemetry of Ask Armada, captain
    /// chat, and planning turns, driven by scripted runtime output on a manual monotonic clock: Claude Code stream-json
    /// with a result usage block and cost, Codex exec --json items and turn.completed usage, OpenCode step_finish usage
    /// summed across steps, Mux (whose whole-context estimate is never taken as usage), and a plain-text runtime with no
    /// usage at all (estimated tokens). Also covers the tool call count and tool time.
    /// </summary>
    public sealed class ChatTurnTelemetryRecorderSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ChatTurnTelemetryRecorder";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("claude_stream_json_result_usage", "Claude Code stream-json: first output, first text, usage, cost, and tools", TestTags.Positive, () =>
            {
                ManualMonotonicTimeProvider clock = new ManualMonotonicTimeProvider();
                ChatTurnTelemetryRecorder recorder = new ChatTurnTelemetryRecorder(clock);

                AssertTrue(recorder.ObserveLine(AgentRuntimeEnum.ClaudeCode, "{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"claude-x\"}"), "init is a typed event");
                clock.AdvanceMs(400);
                recorder.ObserveLine(AgentRuntimeEnum.ClaudeCode, "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\"}}}");
                clock.AdvanceMs(600);
                recorder.ObserveLine(AgentRuntimeEnum.ClaudeCode, "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello \"}}}");
                clock.AdvanceMs(1000);
                recorder.ObserveLine(AgentRuntimeEnum.ClaudeCode, "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\"world\"}}}");
                clock.AdvanceMs(500);
                recorder.ObserveLine(AgentRuntimeEnum.ClaudeCode, "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":2400,\"total_cost_usd\":0.0123,\"usage\":{\"input_tokens\":100,\"cache_read_input_tokens\":900,\"cache_creation_input_tokens\":50,\"output_tokens\":120},\"result\":\"Hello world\"}");

                List<AskMessageToolCall> tools = new List<AskMessageToolCall>
                {
                    new AskMessageToolCall { ToolName = "mcp__armada__status", Ok = true, ElapsedMs = 250, ResultText = "{}" },
                    new AskMessageToolCall { ToolName = "mcp__armada__never_finished" }
                };
                CaptainChatMetrics m = recorder.Build("Hello world", tools);

                AssertEqual(2500.0, m.TotalMs, "total");
                AssertEqual(400.0, m.TimeToFirstTokenMs, "first output: the thinking block started");
                AssertEqual(1000.0, m.TimeToFirstTextMs, "first visible text");
                AssertEqual(2100.0, m.StreamingMs, "streaming = total - first token");
                AssertEqual(1050, m.PromptTokens, "input includes cache reads and writes");
                AssertEqual(900, m.CachedTokens, "cache reads");
                AssertEqual(120, m.CompletionTokens, "reported output tokens");
                AssertEqual(false, m.TokensEstimated, "reported, not estimated");
                AssertEqual(1170, m.TotalTokens, "input + output");
                AssertTrue(m.CostUsd.HasValue && Math.Abs(m.CostUsd.Value - 0.0123) < 1e-9, "cost from total_cost_usd");
                AssertTrue(m.TokensPerSecond.HasValue && Math.Abs(m.TokensPerSecond.Value - (120 / 2.1)) < 0.001, "tokens/sec over the streaming window");
                AssertEqual(1, m.ToolCallCount, "only the completed call counts");
                AssertEqual(250.0, m.ToolTimeMs, "tool time");
            }));

            cases.Add(Case("codex_json_usage_events", "Codex exec --json: items are output, the agent message is text, turn.completed is usage", TestTags.Positive, () =>
            {
                ManualMonotonicTimeProvider clock = new ManualMonotonicTimeProvider();
                ChatTurnTelemetryRecorder recorder = new ChatTurnTelemetryRecorder(clock);

                recorder.ObserveLine(AgentRuntimeEnum.Codex, "{\"type\":\"thread.started\",\"thread_id\":\"t1\"}");
                recorder.ObserveLine(AgentRuntimeEnum.Codex, "{\"type\":\"turn.started\"}");
                AssertNull(recorder.FirstOutputAfter, "lifecycle markers are not output");
                clock.AdvanceMs(300);
                recorder.ObserveLine(AgentRuntimeEnum.Codex, "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"reasoning\",\"text\":\"Looking at the fleet\"}}");
                clock.AdvanceMs(700);
                recorder.ObserveLine(AgentRuntimeEnum.Codex, "{\"type\":\"item.started\",\"item\":{\"id\":\"item_1\",\"type\":\"command_execution\",\"command\":\"ls\",\"status\":\"in_progress\"}}");
                clock.AdvanceMs(200);
                recorder.ObserveLine(AgentRuntimeEnum.Codex, "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"command_execution\",\"command\":\"ls\",\"exit_code\":0,\"status\":\"completed\"}}");
                AssertNull(recorder.FirstTextAfter, "no reply text yet");
                clock.AdvanceMs(800);
                recorder.ObserveLine(AgentRuntimeEnum.Codex, "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_2\",\"type\":\"agent_message\",\"text\":\"Two voyages are running.\"}}");
                clock.AdvanceMs(100);
                recorder.ObserveLine(AgentRuntimeEnum.Codex, "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":2000,\"cached_input_tokens\":1500,\"output_tokens\":300}}");

                CaptainChatMetrics m = recorder.Build("Two voyages are running.", new List<AskMessageToolCall>());
                AssertEqual(2100.0, m.TotalMs, "total");
                AssertEqual(300.0, m.TimeToFirstTokenMs, "first output: reasoning");
                AssertEqual(2000.0, m.TimeToFirstTextMs, "first text: the agent message");
                AssertEqual(2000, m.PromptTokens, "Codex input already includes cached");
                AssertEqual(1500, m.CachedTokens, "cached input");
                AssertEqual(300, m.CompletionTokens, "output");
                AssertEqual(false, m.TokensEstimated, "reported");
                AssertNull(m.CostUsd, "Codex reports no cost");
                AssertEqual(0, m.ToolCallCount, "no tool calls passed");
            }));

            cases.Add(Case("runtime_without_usage_estimates", "A plain-text runtime with no usage: timing only, estimated completion tokens", TestTags.Positive, () =>
            {
                ManualMonotonicTimeProvider clock = new ManualMonotonicTimeProvider();
                ChatTurnTelemetryRecorder recorder = new ChatTurnTelemetryRecorder(clock);

                AssertFalse(recorder.ObserveLine(AgentRuntimeEnum.Gemini, "   "), "plain text");
                AssertNull(recorder.FirstOutputAfter, "a blank line is not output");
                clock.AdvanceMs(800);
                recorder.ObserveLine(AgentRuntimeEnum.Gemini, "Hello there, captain.");
                clock.AdvanceMs(700);
                recorder.ObserveLine(AgentRuntimeEnum.Gemini, "All quiet.");

                string reply = "Hello there, captain.\nAll quiet.";
                CaptainChatMetrics m = recorder.Build(reply, null);
                AssertEqual(1500.0, m.TotalMs);
                AssertEqual(800.0, m.TimeToFirstTokenMs);
                AssertEqual(800.0, m.TimeToFirstTextMs, "first text is the first output");
                AssertNull(m.PromptTokens, "no input reported");
                AssertNull(m.CachedTokens, "no cache reported");
                AssertNull(m.CostUsd, "no cost reported");
                AssertNull(m.TotalTokens, "no total without input");
                AssertEqual(true, m.TokensEstimated, "estimated from the reply");
                AssertEqual((int)Math.Round(reply.Length / 3.5), m.CompletionTokens, "estimate");
                AssertNull(m.ToolCallCount, "tools not tracked by this caller");
                AssertNull(m.ToolTimeMs);
            }));

            cases.Add(Case("opencode_step_finish_sums_steps", "OpenCode step_finish usage and cost add up across steps", TestTags.Positive, () =>
            {
                ManualMonotonicTimeProvider clock = new ManualMonotonicTimeProvider();
                ChatTurnTelemetryRecorder recorder = new ChatTurnTelemetryRecorder(clock);
                clock.AdvanceMs(100);
                recorder.ObserveLine(AgentRuntimeEnum.OpenCode, "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"bash\",\"callID\":\"c1\",\"state\":{\"status\":\"running\"}}}");
                recorder.ObserveLine(AgentRuntimeEnum.OpenCode, "{\"type\":\"step_finish\",\"part\":{\"type\":\"step-finish\",\"cost\":0.01,\"tokens\":{\"input\":10,\"output\":5,\"reasoning\":2,\"cache\":{\"read\":100,\"write\":3}}}}");
                clock.AdvanceMs(400);
                recorder.ObserveLine(AgentRuntimeEnum.OpenCode, "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"Done\"}}");
                recorder.ObserveLine(AgentRuntimeEnum.OpenCode, "{\"type\":\"step_finish\",\"part\":{\"type\":\"step-finish\",\"cost\":0.02,\"tokens\":{\"input\":20,\"output\":8,\"reasoning\":0,\"cache\":{\"read\":200,\"write\":0}}}}");

                CaptainChatMetrics m = recorder.Build("Done", null);
                AssertEqual(100.0, m.TimeToFirstTokenMs, "the tool call was the first output");
                AssertEqual(500.0, m.TimeToFirstTextMs);
                AssertEqual(333, m.PromptTokens, "10+100+3 + 20+200+0");
                AssertEqual(300, m.CachedTokens, "cache reads");
                AssertEqual(15, m.CompletionTokens, "output plus reasoning");
                AssertTrue(m.CostUsd.HasValue && Math.Abs(m.CostUsd.Value - 0.03) < 1e-9, "cost summed");
            }));

            cases.Add(Case("mux_context_estimate_not_usage", "Mux: its whole-context token estimate is never taken as the reply's usage", TestTags.Negative, () =>
            {
                ManualMonotonicTimeProvider clock = new ManualMonotonicTimeProvider();
                ChatTurnTelemetryRecorder recorder = new ChatTurnTelemetryRecorder(clock);
                recorder.ObserveLine(AgentRuntimeEnum.Mux, "{\"eventType\":\"run_started\",\"contractVersion\":1,\"model\":\"m\"}");
                clock.AdvanceMs(250);
                recorder.ObserveLine(AgentRuntimeEnum.Mux, "{\"eventType\":\"assistant_thinking\",\"contractVersion\":1,\"text\":\"hmm\"}");
                clock.AdvanceMs(250);
                recorder.ObserveLine(AgentRuntimeEnum.Mux, "{\"eventType\":\"assistant_text\",\"contractVersion\":1,\"text\":\"Hi\"}");
                recorder.ObserveLine(AgentRuntimeEnum.Mux, "{\"eventType\":\"run_completed\",\"contractVersion\":1,\"durationMs\":9,\"finalEstimatedTokens\":45000}");

                CaptainChatMetrics m = recorder.Build("Hi", null);
                AssertEqual(250.0, m.TimeToFirstTokenMs, "thinking is output");
                AssertEqual(500.0, m.TimeToFirstTextMs);
                AssertNull(recorder.OutputTokens, "no usage taken from run_completed");
                AssertEqual(true, m.TokensEstimated);
                AssertTrue(m.CompletionTokens < 10, "estimate from the two-character reply, not 45000");
            }));

            cases.Add(Case("first_values_kept_and_capped", "Only the first output and text count, and neither exceeds the total", TestTags.Negative, () =>
            {
                ManualMonotonicTimeProvider clock = new ManualMonotonicTimeProvider();
                ChatTurnTelemetryRecorder recorder = new ChatTurnTelemetryRecorder(clock);
                clock.AdvanceMs(100);
                recorder.MarkOutput();
                clock.AdvanceMs(100);
                recorder.MarkOutput();
                recorder.MarkText();
                clock.AdvanceMs(100);
                recorder.MarkText();
                AssertEqual(TimeSpan.FromMilliseconds(100), recorder.FirstOutputAfter);
                AssertEqual(TimeSpan.FromMilliseconds(200), recorder.FirstTextAfter);

                CaptainChatMetrics m = recorder.Build("x", null);
                AssertTrue(m.TimeToFirstTokenMs <= m.TotalMs && m.TimeToFirstTextMs <= m.TotalMs, "capped by the total");
                AssertEqual(200.0, m.StreamingMs);

                // Sub-millisecond times round to whole milliseconds, keeping first token <= total once the total is
                // persisted as an integral duration.
                clock.Advance(TimeSpan.FromTicks(4000));
                CaptainChatMetrics rounded = recorder.Build("x", null);
                AssertEqual(300.0, rounded.TotalMs, "300.4ms rounds to 300");
                AssertTrue(rounded.TimeToFirstTextMs <= Math.Round(rounded.TotalMs!.Value), "first text within the rounded total");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Chat Turn Telemetry Recorder",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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

        #endregion
    }
}
