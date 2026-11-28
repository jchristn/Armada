namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using PolyPrompt.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The API runtime's turn loop runs tool calls, denies tools outside the permitted set, stops on failed responses,
    /// a full window and the turn limit, and follows the terminal policy's continue and forced-submit decisions (RS-7.11).
    /// </summary>
    public sealed class ApiTurnLoopSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Runtimes.ApiTurnLoop";
        private const string Done = "Implemented and tested.\n[ARMADA:RESULT] COMPLETE";

        /// <summary>Builds the turn loop cases.</summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("tool_call_then_complete", "A tool call runs and its result is sent back; the completed reply ends the run", TestTags.Positive, async () =>
            {
                Harness h = new Harness(Calls("read_file"), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, result.ExitCode);
                AssertEqual(Done, result.FinalText);
                AssertEqual(2, result.Turns);
                AssertEqual("read_file", h.Executed.Single());
                ChatMessage toolResult = h.Messages.Single(m => m.ToolCallId == "call_0");
                AssertEqual("ran read_file", toolResult.Text);
                AssertEqual(1, h.Emitted.Count, "only the final text was visible");
                AssertEqual(0, h.Errors.Count);
            }));
            cases.Add(Case("denied_tool", "A tool the endpoint's policy disables gets tool_not_allowed and is not executed", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Calls("rm_rf"), Text(Done));
                h.DisabledTools = new HashSet<string> { "rm_rf" };
                await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, h.Executed.Count);
                AssertContains("tool_not_allowed", h.Messages.Single(m => m.ToolCallId == "call_0").Text!);
                ApiRuntimeToolEvent denied = h.ToolEvents.Single();
                AssertTrue(denied.PermissionDenied == true, "the tool event is marked denied");
                AssertTrue(denied.Ok == false, "the tool event is not ok");
            }));
            cases.Add(Case("unknown_tool_lists_available", "RS-7.4: a tool that does not exist gets unknown_tool naming the tools the captain can call", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Calls("open_file_in_editor"), Text(Done));
                await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, h.Executed.Count);
                string result = h.Messages.Single(m => m.ToolCallId == "call_0").Text!;
                AssertContains("unknown_tool", result);
                AssertContains("unknown tool open_file_in_editor; available: read_file", result);
                AssertFalse(result.Contains("inference policy", StringComparison.Ordinal), "an unknown name is not blamed on the policy");
            }));
            cases.Add(Case("unknown_tool_list_is_capped", "RS-7.4: the available list stops at the cap and counts the rest", TestTags.Negative, () =>
            {
                HashSet<string> permitted = new HashSet<string>(Enumerable.Range(0, ApiTurnLoop.MaxListedTools + 5).Select(i => "tool_" + i.ToString("D3")));
                string result = ApiTurnLoop.DeniedToolResult(new ToolCall { Id = "x", Name = "nope" }, permitted, null);
                AssertContains("and 5 more", result);
                return Task.CompletedTask;
            }));
            cases.Add(Case("text_tool_call_recovered", "RS-7.2: a tool call written as text runs as a real call and the assistant turn carries it", TestTags.Positive, async () =>
            {
                Harness h = new Harness(Text("Reading it.\n<tool_call>\n{\"name\":\"read_file\",\"arguments\":{\"path\":\"a.txt\"}}\n</tool_call>"), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, result.ExitCode);
                AssertEqual(1, result.TextToolCallRecoveries);
                AssertEqual("read_file", h.Executed.Single());
                AssertEqual("{\"path\":\"a.txt\"}", h.ExecutedArguments.Single());
                ChatMessage assistant = h.Messages[2];
                AssertEqual(1, assistant.ToolCalls?.Count ?? 0, "the assistant message carries the recovered call");
                AssertEqual("Reading it.", assistant.Text);
                AssertTrue(h.Messages.Any(m => m.ToolCallId != null && m.ToolCallId.StartsWith("text_call_", StringComparison.Ordinal)), "the tool result answers the recovered call id");
                AssertTrue(h.Diagnostics.Any(d => d.StartsWith("[recovery] ", StringComparison.Ordinal) && d.Contains("\"format\":\"hermes\"", StringComparison.Ordinal)), "the recovery is logged");
                AssertTrue(h.Emitted.All(e => !e.Contains("<tool_call>", StringComparison.Ordinal)), "call markup is not emitted as visible text");
            }));
            cases.Add(Case("text_tool_call_not_recovered_after_length_cut", "RS-7.2: a response cut at the token limit is never recovered", TestTags.Negative, async () =>
            {
                ChatResponse cut = Text("<tool_call>{\"name\":\"read_file\",\"arguments\":{\"path\":\"a.txt\"}}</tool_call>");
                cut.FinishReason = "length";
                Harness h = new Harness(cut, Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, h.Executed.Count);
                AssertEqual(0, result.TextToolCallRecoveries);
            }));
            cases.Add(Case("text_tool_call_recovery_off", "RS-7.2: with recovery turned off a text tool call is ordinary text", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Text("<tool_call>{\"name\":\"read_file\",\"arguments\":{}}</tool_call>"), Text(Done));
                h.Behaviors.TextToolCallRecovery = false;
                await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, h.Executed.Count);
            }));
            cases.Add(Case("text_tool_call_unpermitted_not_recovered", "RS-7.2: a text call to a tool outside the permitted set is not recovered", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Text("<tool_call>{\"name\":\"rm_rf\",\"arguments\":{}}</tool_call>"), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, h.Executed.Count);
                AssertEqual(0, result.TextToolCallRecoveries);
                AssertEqual(0, h.ToolEvents.Count, "nothing was denied either: the text stays text");
            }));
            cases.Add(Case("repeated_calls_get_note", "RS-7.5: the third identical call in a row runs and its result carries a corrective note", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Calls("read_file"), Calls("read_file"), Calls("read_file"), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, result.ExitCode);
                AssertEqual(3, h.Executed.Count);
                List<ChatMessage> results = h.Messages.Where(m => m.ToolCallId == "call_0").ToList();
                AssertFalse(results[1].Text!.Contains("[armada]", StringComparison.Ordinal), "the second call gets no note");
                AssertContains("identical arguments 3 times in a row", results[2].Text!);
            }));
            cases.Add(Case("different_arguments_reset_count", "RS-7.5: a call with other arguments resets the count; key order and spacing do not", TestTags.Negative, () =>
            {
                AssertEqual(ApiTurnLoop.CallKey(new ToolCall { Name = "grep", ArgumentsJson = "{\"a\":1, \"b\":2}" }), ApiTurnLoop.CallKey(new ToolCall { Name = "grep", ArgumentsJson = "{\"b\":2,\"a\":1}" }));
                AssertFalse(ApiTurnLoop.CallKey(new ToolCall { Name = "grep", ArgumentsJson = "{\"a\":1}" }) == ApiTurnLoop.CallKey(new ToolCall { Name = "grep", ArgumentsJson = "{\"a\":2}" }), "other arguments are another call");
                return Task.CompletedTask;
            }));
            cases.Add(Case("repeated_calls_stop_run", "RS-7.5: twice the limit of identical calls ends the run as degenerate_output without running the last", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Calls("read_file"), Calls("read_file"), Calls("read_file"), Calls("read_file"), Calls("read_file"), Calls("read_file"), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(1, result.ExitCode);
                AssertEqual(5, h.Executed.Count);
                RuntimeProviderError error = h.Errors.Single();
                AssertEqual("degenerate_output", error.ErrorType);
                AssertEqual(RuntimeFailureKindEnum.TaskFailure, RuntimeFailureClassifier.Classify(1, error));
                AssertContains("identical arguments 6 times in a row", error.Reason!);
            }));
            cases.Add(Case("repeated_call_limit_configurable", "RS-7.5: a limit of 2 notes the second identical call and stops at the fourth", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Calls("read_file"), Calls("read_file"), Calls("read_file"), Calls("read_file"), Text(Done));
                h.RepeatedToolCallLimit = 2;
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(1, result.ExitCode);
                AssertEqual(3, h.Executed.Count);
                AssertThrows<ArgumentException>(() => new InferenceOptions { RepeatedToolCallLimit = 1 }.Validate());
            }));
            cases.Add(Case("failed_response_stops", "A failed response raises the host's inference failure and stops", TestTags.Negative, async () =>
            {
                Harness h = new Harness(new ChatResponse { Success = false, Error = "boom" });
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(1, result.ExitCode);
                AssertEqual("Inference request failed: boom", h.Errors.Single().Reason);
                AssertTrue(h.Diagnostics.Any(d => d == "[error] inference call failed: boom"), "the failure is logged");
            }));
            cases.Add(Case("turn_limit", "A run that keeps calling tools stops at the turn limit with max_iterations_reached", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Calls("read_file"), Calls("read_file"), Calls("read_file"), Calls("read_file"));
                h.MaxIterations = 3;
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(1, result.ExitCode);
                AssertEqual(3, result.Turns);
                AssertEqual("max_iterations_reached", h.Errors.Single().ErrorType);
                AssertEqual(String.Empty, result.FinalText);
            }));
            cases.Add(Case("full_window_stops", "A response whose prompt fills the window stops with context_limit_exceeded", TestTags.Negative, async () =>
            {
                ChatResponse full = Calls("read_file");
                full.Usage = new TokenUsage { PromptTokens = 10000 };
                Harness h = new Harness(full);
                h.ContextLength = 10000;
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(1, result.ExitCode);
                AssertEqual("context_limit_exceeded", h.Errors.Single().ErrorType);
                AssertEqual(0, h.Executed.Count, "no tool runs after the window is full");
            }));
            cases.Add(Case("continuation_follow_up", "An incomplete reply gets the continuation prompt as the next user turn", TestTags.Positive, async () =>
            {
                Harness h = new Harness(Text("Planning."), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, result.ExitCode);
                AssertContains("Your last response did not complete the task.", h.Messages[3].Text!);
                AssertTrue(h.Diagnostics.Any(d => d.Contains("asking the captain to continue (1/2)", StringComparison.Ordinal)), "the continuation is logged");
            }));
            cases.Add(Case("forced_submit_turn", "A stage that stops without submitting gets one turn offering only the submit tool with a required tool call", TestTags.Positive, async () =>
            {
                NativeSubmitTool submit = new NativeSubmitTool(StageSubmissionKindEnum.Verdict, "Judge", 1);
                Harness h = new Harness(Text("Looks fine."), Text("All checks pass.\n[ARMADA:VERDICT] PASS"));
                h.SubmitTool = submit;
                h.Tools.Add(submit.Definition());
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, result.ExitCode);
                AssertEqual(2, h.Requests.Count);
                AssertTrue(h.Requests[0].ToolChoice != "required", "the first turn does not force a tool call");
                AssertEqual("required", h.Requests[1].ToolChoice);
                AssertEqual(submit.Name, h.Requests[1].ToolNames.Single());
                AssertEqual(submit.ForcePrompt(), h.Messages[3].Text);
            }));
            cases.Add(Case("cancellation", "A cancelled token stops the loop before the next turn", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Text(Done));
                using CancellationTokenSource cts = new CancellationTokenSource();
                cts.Cancel();
                await AssertThrowsAsync<OperationCanceledException>(() => h.RunAsync(cts.Token)).ConfigureAwait(false);
                AssertEqual(0, h.Requests.Count);
            }));
            cases.Add(Case("malformed_call_gets_corrective_turn", "A malformed tool-call 500 is not resent unchanged: the next request carries a corrective turn naming the tools and a higher temperature (RS-7.3)", TestTags.Positive, async () =>
            {
                Harness h = new Harness(Failed(500, "error parsing tool call: unexpected end of JSON input"), Calls("read_file"), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, result.ExitCode);
                AssertEqual(0, h.Errors.Count, "a corrected failure raises no provider error");
                AssertEqual(3, h.Requests.Count);
                AssertNull(h.Requests[0].Temperature, "the first request keeps the configured options");
                AssertEqual(0.8, h.Requests[1].Temperature ?? 0, "the corrective retry raises the temperature from 0.6 by 0.2");
                AssertNull(h.Requests[2].Temperature, "later turns return to the configured options");
                AssertContains("could not parse your last tool call", h.Requests[1].LastUser!);
                AssertContains("read_file", h.Requests[1].LastUser!);
                AssertTrue(h.Diagnostics.Any(d => d.StartsWith("[retry] malformed or degenerate generation", StringComparison.Ordinal)), "the correction is logged");
            }));
            cases.Add(Case("repeat_loop_gets_no_repeat_turn", "A token repeat limit 500 gets a corrective turn telling the model not to repeat itself (RS-7.3, RS-7.17)", TestTags.Positive, async () =>
            {
                Harness h = new Harness(Failed(500, "token repeat limit reached"), Text(Done));
                h.Temperature = 0.2;
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, result.ExitCode);
                AssertContains("Do not repeat yourself", h.Requests[1].LastUser!);
                AssertEqual(0.4, h.Requests[1].Temperature ?? 0, "the configured temperature is the starting point");
            }));
            cases.Add(Case("malformed_call_retries_are_bounded", "Consecutive malformed generations end the run with the provider error after the configured retries (RS-7.3)", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Failed(500, "token repeat limit reached"), Failed(500, "token repeat limit reached"), Failed(500, "token repeat limit reached"), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(1, result.ExitCode);
                AssertEqual(3, h.Requests.Count, "the first failure plus two corrective retries");
                AssertEqual(1.0, h.Requests[2].Temperature ?? 0, "the second retry raises it again, capped at 1.0");
                AssertContains("token repeat limit reached", h.Errors.Single().Reason);
            }));
            cases.Add(Case("malformed_call_retry_off_fails_at_once", "With corrective retries off (a Harbor without api-malformed-call-retry-v1), a malformed generation fails as in 1.4.0", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Failed(500, "error parsing tool call: invalid character"), Text(Done));
                h.Behaviors.MalformedCallRetries = 0;
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(1, result.ExitCode);
                AssertEqual(1, h.Requests.Count);
            }));
            cases.Add(Case("other_failures_are_not_corrected", "A failure that is not a malformed or degenerate generation still stops at once", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Failed(500, "llama runner process has terminated"), Text(Done));
                ApiTurnLoopResult result = await h.RunAsync().ConfigureAwait(false);
                AssertEqual(1, result.ExitCode);
                AssertEqual(1, h.Requests.Count);
            }));
            cases.Add(Case("history_reasoning_is_stripped", "Leaked reasoning (a bare closing think tag) is removed from visible text and from the assistant text kept in history; provider state is kept (RS-7.6)", TestTags.Positive, async () =>
            {
                ChatResponse leaked = Calls("read_file");
                leaked.Text = "I should read the file first.\n</think>\nReading it.";
                leaked.ProviderState = System.Text.Json.JsonDocument.Parse("{\"thinking\":\"kept\"}").RootElement.Clone();
                Harness h = new Harness(leaked, Text(Done));
                await h.RunAsync().ConfigureAwait(false);
                ChatMessage assistant = h.Messages[2];
                AssertEqual("Reading it.", assistant.Text);
                AssertNotNull(assistant.ProviderState, "provider continuation state is replayed unchanged");
                AssertEqual("Reading it.", h.Emitted[0]);
            }));
            cases.Add(Case("history_reasoning_kept_when_off", "With reasoning hygiene off, history keeps the assistant text as 1.4.0 did", TestTags.Negative, async () =>
            {
                ChatResponse leaked = Calls("read_file");
                leaked.Text = "I should read the file first.\n</think>\nReading it.";
                Harness h = new Harness(leaked, Text(Done));
                h.Behaviors.ReasoningHygiene = false;
                await h.RunAsync().ConfigureAwait(false);
                AssertContains("</think>", h.Messages[2].Text!);
            }));
            return new TestSuiteDescriptor(SuiteId, "API runtime turn loop", cases);
        }

        private static ChatResponse Text(string text)
        {
            return new ChatResponse { Success = true, Text = text, FinishReason = "stop" };
        }

        private static ChatResponse Failed(int status, string error)
        {
            return new ChatResponse { Success = false, StatusCode = status, Error = error };
        }

        private static ChatResponse Calls(params string[] names)
        {
            ChatResponse response = new ChatResponse { Success = true, FinishReason = "tool_calls" };
            for (int i = 0; i < names.Length; i++) response.ToolCalls.Add(new ToolCall { Id = "call_" + i, Name = names[i], ArgumentsJson = "{}" });
            return response;
        }

        private static TestCaseDescriptor Case(string id, string name, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, ct => body(), tags: new List<string> { tag });
        }

        private sealed class RequestRecord
        {
            public List<string> ToolNames { get; set; } = new List<string>();
            public string? ToolChoice { get; set; } = null;
            public double? Temperature { get; set; } = null;
            public string? LastUser { get; set; } = null;
        }

        private sealed class Harness
        {
            public List<ChatMessage> Messages { get; } = new List<ChatMessage> { ChatMessage.System("system"), ChatMessage.User("brief") };
            public List<ToolDefinition> Tools { get; } = new List<ToolDefinition> { ToolDefinition.Function("read_file", "Read a file.", new Dictionary<string, object> { { "type", "object" } }) };
            public List<RequestRecord> Requests { get; } = new List<RequestRecord>();
            public List<string> Executed { get; } = new List<string>();
            public List<string> Emitted { get; } = new List<string>();
            public List<string> Diagnostics { get; } = new List<string>();
            public List<ApiRuntimeToolEvent> ToolEvents { get; } = new List<ApiRuntimeToolEvent>();
            public List<RuntimeProviderError> Errors { get; } = new List<RuntimeProviderError>();
            public int MaxIterations { get; set; } = 10;
            public int? ContextLength { get; set; } = null;
            public NativeSubmitTool? SubmitTool { get; set; } = null;
            public ApiRuntimeBehaviors Behaviors { get; } = new ApiRuntimeBehaviors();
            public double? Temperature { get; set; } = null;
            public List<string> ExecutedArguments { get; } = new List<string>();
            public ISet<string>? DisabledTools { get; set; } = null;
            public int RepeatedToolCallLimit { get; set; } = ApiTurnLoop.DefaultRepeatedToolCallLimit;
            private readonly Queue<ChatResponse> _Script;

            public Harness(params ChatResponse[] script)
            {
                _Script = new Queue<ChatResponse>(script);
            }

            public Task<ApiTurnLoopResult> RunAsync(CancellationToken token = default)
            {
                ApiTurnLoopCallbacks callbacks = new ApiTurnLoopCallbacks
                {
                    InferAsync = (request, ct) =>
                    {
                        Requests.Add(new RequestRecord
                        {
                            ToolNames = request.Tools.Select(t => t.Name).ToList(),
                            ToolChoice = request.ToolChoice,
                            Temperature = request.Options?.Temperature,
                            LastUser = request.Messages.Count == 0 ? null : request.Messages[request.Messages.Count - 1].Text
                        });
                        return Task.FromResult(_Script.Count > 0 ? _Script.Dequeue() : Text(Done));
                    },
                    ExecuteToolAsync = (call, ct) =>
                    {
                        Executed.Add(call.Name);
                        ExecutedArguments.Add(call.ArgumentsJson);
                        return Task.FromResult("ran " + call.Name);
                    },
                    Emit = text => Emitted.Add(text),
                    Diagnostic = (kind, text) => Diagnostics.Add(text),
                    ToolEvent = toolEvent => ToolEvents.Add(toolEvent),
                    ProviderError = error => Errors.Add(error)
                };
                HashSet<string> permitted = Tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
                ApiTurnLoop loop = new ApiTurnLoop(callbacks, new ApiContextPolicy(ContextLength), new ApiTerminalPolicy(SubmitTool == null, null, SubmitTool, Behaviors.ReasoningHygiene),
                    SubmitTool, new CompletionOptions { Temperature = Temperature }, MaxIterations, false, Behaviors);
                loop.DisabledTools = DisabledTools;
                loop.RepeatedToolCallLimit = RepeatedToolCallLimit;
                return loop.RunAsync(Messages, Tools, permitted, token);
            }
        }
    }
}
