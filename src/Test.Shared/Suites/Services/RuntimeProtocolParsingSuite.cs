namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Protocol;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the shared typed runtime-protocol classes (Claude Code stream-json, Codex exec --json, Mux,
    /// OpenCode) and the string-aware embedded-JSON extractor. Negative cases prove that JSON a
    /// model prints is not mistaken for a protocol event, that braces in prose do not corrupt extraction, and that
    /// protocol variants that used to break the hand-walked parsers (string content) parse.
    /// </summary>
    public sealed class RuntimeProtocolParsingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string _SuiteId = "Services.RuntimeProtocolParsing";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("claude_text_delta_and_result", "Claude stream_event text deltas and the result event (text, duration, usage) parse into typed fields", TestTags.Positive, () =>
            {
                AssertTrue(ClaudeStreamLine.TryParse("{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hel\"}}}", out ClaudeStreamLine? delta));
                AssertEqual("Hel", delta!.TextDelta);

                AssertTrue(ClaudeStreamLine.TryParse("{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"hmm\"}}}", out ClaudeStreamLine? thinking));
                AssertNull(thinking!.TextDelta, "a thinking delta is not reply text");

                AssertTrue(ClaudeStreamLine.TryParse("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":1234.5,\"result\":\"final answer\",\"usage\":{\"input_tokens\":10,\"output_tokens\":42,\"cache_read_input_tokens\":5}}", out ClaudeStreamLine? result));
                AssertEqual("final answer", result!.Result);
                AssertEqual(1234.5, result.DurationMs!.Value);
                AssertEqual(42L, result.Usage!.OutputTokens!.Value);
                AssertEqual(5L, result.Usage.CacheReadInputTokens!.Value);
            }));

            cases.Add(Case("claude_content_string_or_array", "Claude message content and tool_result content accept both the string and the array form", TestTags.Negative, () =>
            {
                // A user event whose content is a plain string used to fail typed deserialization of the whole line.
                AssertTrue(ClaudeStreamLine.TryParse("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"plain prompt\"}}", out ClaudeStreamLine? user));
                AssertEqual(1, user!.Message!.Content!.Count);
                AssertEqual("plain prompt", user.Message.Content[0].Text);

                AssertTrue(ClaudeStreamLine.TryParse("{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"done\",\"is_error\":false},{\"type\":\"tool_result\",\"tool_use_id\":\"t2\",\"content\":[{\"type\":\"text\",\"text\":\"a\"},{\"type\":\"image\"},{\"type\":\"text\",\"text\":\"b\"}],\"is_error\":true}]}}", out ClaudeStreamLine? results));
                AssertEqual("done", results!.Message!.Content![0].ContentText());
                AssertEqual("a\nb", results.Message.Content[1].ContentText());
                AssertEqual(true, results.Message.Content[1].IsError!.Value);

                AssertTrue(ClaudeStreamLine.TryParse("{\"type\":\"assistant\",\"message\":{\"model\":\"claude-x\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Bash\",\"input\":{\"command\":\"ls\"}}]}}", out ClaudeStreamLine? assistant));
                ClaudeStreamContentBlock call = assistant!.Message!.Content![0];
                AssertEqual("Bash", call.Name);
                Dictionary<string, string>? input = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(call.Input!);
                AssertEqual("ls", input!["command"]);
            }));

            cases.Add(Case("unknown_discriminators_are_not_events", "JSON a model prints is not a protocol event unless its discriminator is a known event type", TestTags.Negative, () =>
            {
                // Before: Mux accepted any eventType string and OpenCode accepted any object with a "type" key.
                AssertFalse(MuxRuntime.IsProtocolEventLine("{\"eventType\":\"my_custom_thing\",\"text\":\"hi\"}"), "unknown Mux eventType");
                AssertFalse(OpenCodeRuntime.IsProtocolEventLine("{\"type\":\"object\",\"properties\":{}}"), "a JSON schema the model printed");
                AssertFalse(OpenCodeRuntime.IsProtocolEventLine("{\"part\":{\"text\":\"x\"}}"), "no type at all");
                AssertFalse(ClaudeStreamLine.TryParse("{\"type\":\"object\"}", out ClaudeStreamLine? _), "unknown Claude type");
                AssertFalse(CodexStreamEvent.TryParse("{\"type\":\"item\"}", out CodexStreamEvent? _), "unknown Codex type");
                AssertTrue(MuxRuntime.IsProtocolEventLine("{\"eventType\":\"context_status\",\"contractVersion\":1}"), "known Mux eventType");
                AssertTrue(OpenCodeRuntime.IsProtocolEventLine("{\"type\":\"step_finish\",\"part\":{\"type\":\"step-finish\"}}"), "known OpenCode type");
            }));

            cases.Add(Case("mux_tool_events_typed", "Mux tool_call_proposed and tool_call_completed parse into typed tool call and result", TestTags.Positive, () =>
            {
                AssertTrue(MuxProtocolEvent.TryParse("{\"contractVersion\":1,\"eventType\":\"tool_call_proposed\",\"toolCall\":{\"id\":\"c1\",\"name\":\"read_file\",\"arguments\":{\"file_path\":\"a.txt\"}}}", out MuxProtocolEvent? proposed));
                AssertEqual("c1", proposed!.ToolCall!.Id);
                AssertEqual("{\"file_path\":\"a.txt\"}", proposed.ToolCall.Arguments);

                AssertTrue(MuxProtocolEvent.TryParse("{\"eventType\":\"tool_call_completed\",\"toolCallId\":\"c1\",\"toolName\":\"read_file\",\"elapsedMs\":12,\"result\":{\"toolCallId\":\"c1\",\"success\":false,\"content\":\"boom\"}}", out MuxProtocolEvent? completed));
                AssertEqual(false, completed!.Result!.Success!.Value);
                AssertEqual("\"boom\"", completed.Result.Content);
                AssertEqual(12.0, completed.ElapsedMs!.Value);
            }));

            cases.Add(Case("opencode_events_typed", "OpenCode text, reasoning, and tool_use events parse into typed parts", TestTags.Positive, () =>
            {
                AssertTrue(OpenCodeStreamEvent.TryParse("{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"bash\",\"callID\":\"call-1\",\"state\":{\"status\":\"error\",\"input\":{\"command\":\"false\"},\"error\":\"exit 1\",\"metadata\":{\"exit\":1}}}}", out OpenCodeStreamEvent? tool));
                AssertEqual("bash", tool!.Part!.Tool);
                AssertEqual("error", tool.Part.State!.Status);
                AssertEqual("exit 1", tool.Part.State.Error);
                AssertEqual(1, tool.Part.State.Metadata!.Exit!.Value);
                AssertNull(tool.AssistantText);

                AssertTrue(OpenCodeStreamEvent.TryParse("{\"type\":\"reasoning\",\"part\":{\"type\":\"reasoning\",\"text\":\"thinking\"}}", out OpenCodeStreamEvent? reasoning));
                AssertNull(reasoning!.AssistantText, "reasoning is not reply text");
                AssertEqual("Hi", OpenCodeRuntime.TryExtractAssistantText("{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"Hi\"}}"));
            }));

            cases.Add(Case("codex_events_formatted", "Codex exec --json events are formatted from the typed item: tool calls resolve and lifecycle markers drop", TestTags.Positive, () =>
            {
                FormattedLogLine mcp = RuntimeLogFormatter.Format("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_3\",\"type\":\"mcp_tool_call\",\"server\":\"armada\",\"tool\":\"enumerate\",\"status\":\"completed\"}}", AgentRuntimeEnum.Codex);
                AssertTrue(mcp.IsToolCall);
                AssertEqual("armada.enumerate", mcp.ToolName);

                FormattedLogLine shell = RuntimeLogFormatter.Format("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"command_execution\",\"command\":\"ls\",\"exit_code\":0,\"status\":\"completed\"}}", AgentRuntimeEnum.Codex);
                AssertTrue(shell.IsToolCall);
                AssertEqual("shell", shell.ToolName);

                FormattedLogLine message = RuntimeLogFormatter.Format("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_2\",\"type\":\"agent_message\",\"text\":\"All done.\"}}", AgentRuntimeEnum.Codex);
                AssertEqual("All done.", message.Text);

                AssertTrue(RuntimeLogFormatter.Format("{\"type\":\"turn.started\"}", AgentRuntimeEnum.Codex).Dropped, "lifecycle marker drops");
                AssertTrue(CodexStreamEvent.TryParse("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":100,\"cached_input_tokens\":20,\"output_tokens\":7}}", out CodexStreamEvent? turn));
                AssertEqual(7L, turn!.Usage!.OutputTokens!.Value);
            }));

            cases.Add(Case("embedded_json_is_string_aware", "Embedded JSON extraction ignores stray braces in prose and braces inside strings", TestTags.Negative, () =>
            {
                // Before: everything from the first '{' to the last '}' was taken, which fails on any stray brace.
                string reply = "Here is the {draft} you asked for:\n{\"title\":\"Fix {braces} parsing\",\"description\":\"Handle } and { in strings\"}\nLet me know {if} it works.";
                AssertTrue(EmbeddedJsonExtractor.TryExtract<PlanningSummaryDraftDocument>(reply, d => !String.IsNullOrWhiteSpace(d.Title), out PlanningSummaryDraftDocument? doc));
                AssertEqual("Fix {braces} parsing", doc!.Title);
                AssertEqual("Handle } and { in strings", doc.Description);

                string fenced = "Notes {x}\n```json\n{\"Summary\":\"S\",\"acceptanceCriteria\":[\"a\",\"b\"]}\n```\n";
                AssertTrue(EmbeddedJsonExtractor.TryExtract<ObjectiveRefinementSummaryDocument>(fenced, d => d.Summary != null, out ObjectiveRefinementSummaryDocument? refined));
                AssertEqual("S", refined!.Summary, "property names are case-insensitive");
                AssertEqual(2, refined.AcceptanceCriteria!.Count);

                AssertFalse(EmbeddedJsonExtractor.TryExtract<PlanningSummaryDraftDocument>("no json {here}", d => !String.IsNullOrWhiteSpace(d.Title), out PlanningSummaryDraftDocument? _));
                AssertEqual(2, EmbeddedJsonExtractor.FindObjects("{\"a\":\"}\"} and {\"b\":{\"c\":1}}").Count);
            }));

            return new TestSuiteDescriptor(
                suiteId: _SuiteId,
                displayName: "Runtime Protocol Parsing",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
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
