namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Mux's jsonl events as Armada reads them: the mission stream decoder (deltas joined into lines, tool activity, noise
    /// dropped), the log formatter's rendering of every Mux event type and Claude Code's rate_limit_event, the Claude
    /// decoder keeping rate_limit_event out of the output, the Codex error parser, and the telemetry recorder's usage and
    /// model for Mux and Claude Code.
    /// </summary>
    public sealed class MuxStreamEventsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MuxStreamEvents";

        private const string RateLimitAllowed = "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"resetsAt\":1760540400,\"rateLimitType\":\"five_hour\"},\"uuid\":\"u1\",\"session_id\":\"s1\"}";
        private const string RateLimitRejected = "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"rejected\",\"resetsAt\":1760540400,\"rateLimitType\":\"five_hour\",\"utilization\":1.0},\"uuid\":\"u1\",\"session_id\":\"s1\"}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("mux_has_a_mission_decoder", "Mux has structured mission output and a decoder", TestTags.Positive, () =>
            {
                AssertTrue(MissionStreamDecoder.Supports(AgentRuntimeEnum.Mux));
                AssertTrue(MissionStreamDecoder.For(AgentRuntimeEnum.Mux) is MuxMissionStreamDecoder);
            }));

            cases.Add(Case("decoder_joins_deltas_into_lines", "Assistant text deltas are joined into whole stdout lines, so a marker split across deltas is a standalone line", TestTags.Positive, () =>
            {
                Decoded run = DecodeAll(MuxJsonlFixtures.CompletedWithErrorsRun());
                AssertEqual(4, run.Output.Count, String.Join(" | ", run.Output));
                AssertEqual("I will read the README.", run.Output[0]);
                AssertEqual("All done, the work is committed.", run.Output[1]);
                AssertEqual("[ARMADA:STATUS] Testing", run.Output[2]);
                AssertEqual("Finished", run.Output[3], "the open last line is printed at run_completed");
                AssertEqual("Error: tool_call_denied: Tool 'open_browser' is not available.", run.Transcript.Single(), "an error is the stderr line text mode prints");
                AssertEqual(0, run.Plain, "every line was a protocol event");
            }));

            cases.Add(Case("decoder_reports_activity_and_drops_noise", "Tool calls, the first line of each turn, and reasoning are activity; heartbeats and context status are not", TestTags.Positive, () =>
            {
                List<string> lines = new List<string>
                {
                    MuxJsonlFixtures.RunStarted,
                    "{\"contractVersion\":2,\"eventType\":\"assistant_thinking\",\"text\":\"Let me \"}",
                    "{\"contractVersion\":2,\"eventType\":\"assistant_thinking\",\"text\":\"think\"}",
                    MuxJsonlFixtures.ReadProposed,
                    MuxJsonlFixtures.ReadCompleted,
                    "{\"contractVersion\":2,\"eventType\":\"tool_call_proposed\",\"toolCall\":{\"id\":\"c3\",\"name\":\"run_process\",\"arguments\":{\"command\":\"dotnet\",\"args\":[\"test\",\"src/App.sln\"]}}}",
                    "{\"contractVersion\":2,\"eventType\":\"tool_call_completed\",\"toolCallId\":\"c3\",\"toolName\":\"run_process\",\"result\":{\"success\":false,\"content\":\"exit 1\"}}",
                    MuxJsonlFixtures.Heartbeat,
                    MuxJsonlFixtures.ContextStatus,
                    "{\"contractVersion\":2,\"eventType\":\"task_plan_updated\",\"changeKind\":\"plan_created\",\"totalCount\":2,\"completedCount\":0,\"tasks\":[]}"
                };
                lines.AddRange(MuxJsonlFixtures.FinalTurnText);
                Decoded run = DecodeAll(lines);
                List<string> summaries = run.Activities.Select(a => a.Summary).ToList();
                AssertEqual("Thinking|Reading README.md|Finished read_file|Running dotnet test src/App.sln|run_process failed|All done, the work is committed.", String.Join("|", summaries));
                AssertEqual(RuntimeActivityKindEnum.Thinking, run.Activities[0].Kind);
                AssertEqual("read_file", run.Activities[1].ToolName);
                AssertEqual("README.md", run.Activities[1].Detail);
                AssertEqual(RuntimeActivityKindEnum.Text, run.Activities.Last().Kind);
                AssertEqual(3, run.Output.Count, "Flush printed the open line: " + String.Join(" | ", run.Output));
            }));

            cases.Add(Case("decoder_leaves_plain_text_plain", "A line that is not a Mux event stays plain text", TestTags.Negative, () =>
            {
                MuxMissionStreamDecoder decoder = new MuxMissionStreamDecoder();
                AssertFalse(decoder.Decode("warning: something", DateTime.UtcNow).Structured);
                AssertFalse(decoder.Decode("{\"foo\":1}", DateTime.UtcNow).Structured);
            }));

            cases.Add(Case("claude_decoder_drops_rate_limit_event", "Claude Code's rate_limit_event is protocol, not output", TestTags.Negative, () =>
            {
                MissionStreamDecodeResult result = new ClaudeMissionStreamDecoder().Decode(RateLimitAllowed, DateTime.UtcNow);
                AssertTrue(result.Structured, "a protocol event");
                AssertEqual(0, result.OutputLines.Count);
            }));

            cases.Add(Case("log_formatter_renders_mux_events", "Every Mux event type renders as a short readable line or is dropped as noise", TestTags.Positive, () =>
            {
                AssertEqual("(mux) run started: model gpt-oss:20b, endpoint local-ollama, up to 50 iterations", Text(MuxJsonlFixtures.RunStarted));
                AssertEqual("(mux) run completed_with_errors: 3 iterations, 2 tool calls, 4.2s, 1200 in / 340 out tokens", Text(MuxJsonlFixtures.CompletedWithErrors));
                AssertEqual("(error) max_iterations_reached: Agent loop reached the maximum of 50 iterations.", Text(MuxJsonlFixtures.MaxIterationsError));
                AssertEqual("(context approaching) est. 22500 / 28000 tokens, 5500 remaining", Text("{\"contractVersion\":2,\"eventType\":\"context_status\",\"estimatedTokens\":22500,\"usableInputLimit\":28000,\"remainingTokens\":5500,\"warningLevel\":\"approaching\"}"));
                AssertEqual("(context) compacted (summary): est. 26000 -> 9000 tokens", Text("{\"contractVersion\":2,\"eventType\":\"context_compacted\",\"strategy\":\"summary\",\"estimatedTokensBefore\":26000,\"estimatedTokensAfter\":9000}"));
                AssertEqual("(plan) 1 of 3 tasks complete", Text("{\"contractVersion\":2,\"eventType\":\"task_plan_updated\",\"totalCount\":3,\"completedCount\":1,\"tasks\":[]}"));
                AssertEqual("(hook) PreToolUse guard: allowed", Text("{\"contractVersion\":2,\"eventType\":\"hook\",\"hookEvent\":\"PreToolUse\",\"hookName\":\"guard\",\"outcome\":\"allowed\",\"exitCode\":0}"));
                AssertEqual("-> tool read_file", Text(MuxJsonlFixtures.ReadProposed), "tool proposals unchanged");

                AssertTrue(Dropped(MuxJsonlFixtures.Heartbeat), "heartbeat");
                AssertTrue(Dropped(MuxJsonlFixtures.ContextStatus), "routine context status");
                AssertTrue(Dropped(MuxJsonlFixtures.ReadApproved), "approval");
                AssertTrue(Dropped("{\"contractVersion\":2,\"eventType\":\"assistant_thinking\",\"text\":\"hmm\"}"), "reasoning fragment");
            }));

            cases.Add(Case("log_formatter_renders_claude_rate_limit", "Claude Code's routine rate_limit_event is dropped; a rejection reads as a line", TestTags.Positive, () =>
            {
                AssertTrue(RuntimeLogFormatter.Format(RateLimitAllowed, AgentRuntimeEnum.ClaudeCode).Dropped, "allowed is noise");
                FormattedLogLine rejected = RuntimeLogFormatter.Format(RateLimitRejected, AgentRuntimeEnum.ClaudeCode);
                AssertFalse(rejected.Dropped);
                AssertEqual("(rate limit) rejected (five_hour), 100% used, resets 2025-10-15 15:00 UTC", rejected.Text);
            }));

            cases.Add(Case("telemetry_reads_mux_usage_and_model", "Mux's run_completed usage and run_started model reach the telemetry recorder", TestTags.Positive, () =>
            {
                ChatTurnTelemetryRecorder recorder = new ChatTurnTelemetryRecorder();
                foreach (string line in MuxJsonlFixtures.CompletedWithErrorsRun()) recorder.ObserveLine(AgentRuntimeEnum.Mux, line);
                AssertEqual(1200L, recorder.InputTokens ?? 0);
                AssertEqual(340L, recorder.OutputTokens ?? 0);
                AssertEqual(100L, recorder.CachedTokens ?? 0);
                AssertEqual("gpt-oss:20b", recorder.Model);
            }));

            cases.Add(Case("telemetry_reads_claude_model_and_usage", "Claude Code's init model and result usage reach the telemetry recorder", TestTags.Positive, () =>
            {
                ChatTurnTelemetryRecorder recorder = new ChatTurnTelemetryRecorder();
                recorder.ObserveLine(AgentRuntimeEnum.ClaudeCode, "{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"claude-sonnet-4\"}");
                recorder.ObserveLine(AgentRuntimeEnum.ClaudeCode, RateLimitAllowed);
                recorder.ObserveLine(AgentRuntimeEnum.ClaudeCode, "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"done\",\"total_cost_usd\":0.5,\"usage\":{\"input_tokens\":10,\"cache_read_input_tokens\":90,\"output_tokens\":7}}");
                AssertEqual("claude-sonnet-4", recorder.Model);
                AssertEqual(100L, recorder.InputTokens ?? 0);
                AssertEqual(7L, recorder.OutputTokens ?? 0);
                AssertEqual(0.5, recorder.CostUsd ?? 0);
            }));

            cases.Add(Case("codex_error_events_are_provider_errors", "Codex error and turn.failed events are provider errors; other events are not", TestTags.Positive, () =>
            {
                RuntimeProviderError? limit = RuntimeProviderErrorParser.TryParseCodexJsonLine("{\"type\":\"error\",\"message\":\"exceeded retry limit, last status: 429 Too Many Requests\"}");
                AssertEqual(429, limit!.HttpStatusCode ?? 0);
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, limit));
                RuntimeProviderError? failed = RuntimeProviderErrorParser.TryParseCodexJsonLine("{\"type\":\"turn.failed\",\"error\":{\"message\":\"model not reachable\"}}");
                AssertEqual("codex turn failed: model not reachable", failed!.Reason);
                AssertNull(failed.HttpStatusCode);
                AssertNull(RuntimeProviderErrorParser.TryParseCodexJsonLine("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1}}"));
                AssertNull(RuntimeProviderErrorParser.TryParseCodexJsonLine("status 500 in plain text"));
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Mux stream events",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Decoded DecodeAll(List<string> lines)
        {
            MuxMissionStreamDecoder decoder = new MuxMissionStreamDecoder();
            Decoded decoded = new Decoded();
            DateTime now = DateTime.UtcNow;
            foreach (string line in lines)
            {
                MissionStreamDecodeResult result = decoder.Decode(line, now);
                if (!result.Structured) decoded.Plain++;
                decoded.Add(result);
            }

            decoded.Add(decoder.Flush(now));
            return decoded;
        }

        private static string Text(string line)
        {
            FormattedLogLine formatted = RuntimeLogFormatter.Format(line, AgentRuntimeEnum.Mux);
            AssertFalse(formatted.Dropped, "not dropped: " + line);
            return formatted.Text;
        }

        private static bool Dropped(string line)
        {
            return RuntimeLogFormatter.Format(line, AgentRuntimeEnum.Mux).Dropped;
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

        #endregion

        #region Private-Types

        private sealed class Decoded
        {
            public List<string> Output { get; } = new List<string>();

            public List<string> Transcript { get; } = new List<string>();

            public List<RuntimeActivity> Activities { get; } = new List<RuntimeActivity>();

            public int Plain { get; set; } = 0;

            public void Add(MissionStreamDecodeResult result)
            {
                Output.AddRange(result.OutputLines);
                Transcript.AddRange(result.TranscriptLines);
                Activities.AddRange(result.Activities);
            }
        }

        #endregion
    }
}
