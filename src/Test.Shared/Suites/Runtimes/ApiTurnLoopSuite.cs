namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
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
            cases.Add(Case("denied_tool", "A tool outside the permitted set gets tool_not_allowed and is not executed", TestTags.Negative, async () =>
            {
                Harness h = new Harness(Calls("rm_rf"), Text(Done));
                await h.RunAsync().ConfigureAwait(false);
                AssertEqual(0, h.Executed.Count);
                AssertContains("tool_not_allowed", h.Messages.Single(m => m.ToolCallId == "call_0").Text!);
                ApiRuntimeToolEvent denied = h.ToolEvents.Single();
                AssertTrue(denied.PermissionDenied == true, "the tool event is marked denied");
                AssertTrue(denied.Ok == false, "the tool event is not ok");
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
            return new TestSuiteDescriptor(SuiteId, "API runtime turn loop", cases);
        }

        private static ChatResponse Text(string text)
        {
            return new ChatResponse { Success = true, Text = text, FinishReason = "stop" };
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
                        Requests.Add(new RequestRecord { ToolNames = request.Tools.Select(t => t.Name).ToList(), ToolChoice = request.ToolChoice });
                        return Task.FromResult(_Script.Count > 0 ? _Script.Dequeue() : Text(Done));
                    },
                    ExecuteToolAsync = (call, ct) =>
                    {
                        Executed.Add(call.Name);
                        return Task.FromResult("ran " + call.Name);
                    },
                    Emit = text => Emitted.Add(text),
                    Diagnostic = (kind, text) => Diagnostics.Add(text),
                    ToolEvent = toolEvent => ToolEvents.Add(toolEvent),
                    ProviderError = error => Errors.Add(error)
                };
                HashSet<string> permitted = Tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
                ApiTurnLoop loop = new ApiTurnLoop(callbacks, new ApiContextPolicy(ContextLength), new ApiTerminalPolicy(SubmitTool == null, null, SubmitTool),
                    SubmitTool, new CompletionOptions(), MaxIterations, false);
                return loop.RunAsync(Messages, Tools, permitted, token);
            }
        }
    }
}
