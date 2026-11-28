namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Runtimes;
    using PolyPrompt.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The API runtime's context policy compacts before a turn near the window, stops at a full window, and compacts
    /// instead of stopping while the prompt is still below the window (RS-7.11).
    /// </summary>
    public sealed class ApiContextPolicySuite : IArmadaTestSuite
    {
        private const string SuiteId = "Runtimes.ApiContextPolicy";

        /// <summary>Builds the context policy cases.</summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("unknown_window_never_compacts", "An unknown window never compacts or stops", TestTags.Positive, () =>
            {
                ApiContextPolicy policy = new ApiContextPolicy(null);
                List<ChatMessage> messages = Conversation(12, 4000);
                AssertNull(policy.BeforeTurn(messages, new List<ToolDefinition>()));
                ApiContextCheck check = policy.AfterResponse(messages, new TokenUsage { PromptTokens = 900000 });
                AssertNull(check.Failure);
                AssertNull(check.Note);
            }));
            cases.Add(Case("before_turn_compacts_near_window", "The local estimate near the window compacts older tool output before the turn", TestTags.Positive, () =>
            {
                ApiContextPolicy policy = new ApiContextPolicy(10000);
                List<ChatMessage> messages = Conversation(12, 4000);
                string? note = policy.BeforeTurn(messages, new List<ToolDefinition>());
                AssertNotNull(note);
                AssertContains("[context] about", note!);
                AssertContains("of 10000 tokens in use", note!);
                AssertTrue(messages[2].Text!.StartsWith(ApiContextCompactor.CompactedMarker, StringComparison.Ordinal), "the oldest large output is compacted");
            }));
            cases.Add(Case("small_conversation_is_left_alone", "A conversation well inside the window is not compacted", TestTags.Positive, () =>
            {
                ApiContextPolicy policy = new ApiContextPolicy(1000000);
                AssertNull(policy.BeforeTurn(Conversation(3, 100), new List<ToolDefinition>()));
            }));
            cases.Add(Case("provider_count_drives_compaction", "A provider prompt count near the window compacts even when the local estimate is small", TestTags.Positive, () =>
            {
                ApiContextPolicy policy = new ApiContextPolicy(1000000);
                List<ChatMessage> messages = Conversation(12, 4000);
                ApiContextCheck check = policy.AfterResponse(messages, new TokenUsage { PromptTokens = 850000 });
                AssertNull(check.Failure, "85 percent is below the stop threshold");
                AssertEqual(850000L, policy.LastPromptTokens);
                AssertNotNull(policy.BeforeTurn(messages, new List<ToolDefinition>()));
                AssertEqual(0L, policy.LastPromptTokens, "a compaction resets the provider count");
            }));
            cases.Add(Case("full_window_below_limit_compacts_and_continues", "A prompt at 95 percent but below the window compacts and continues", TestTags.Positive, () =>
            {
                ApiContextPolicy policy = new ApiContextPolicy(10000);
                List<ChatMessage> messages = Conversation(12, 4000);
                ApiContextCheck check = policy.AfterResponse(messages, new TokenUsage { PromptTokens = 9600 });
                AssertNull(check.Failure);
                AssertContains("prompt reached 9600 of 10000 tokens", check.Note!);
            }));
            cases.Add(Case("full_window_stops", "A prompt that fills the window, or one with nothing left to compact, stops with context_limit_exceeded", TestTags.Negative, () =>
            {
                ApiContextPolicy policy = new ApiContextPolicy(10000);
                ApiContextCheck atWindow = policy.AfterResponse(Conversation(12, 4000), new TokenUsage { PromptTokens = 10000 });
                AssertEqual("context_limit_exceeded", atWindow.Failure!.ErrorType);
                AssertContains("prompt tokens: 10000 of 10000", atWindow.Failure.Reason!);
                ApiContextCheck nothingLeft = policy.AfterResponse(Conversation(2, 100), new TokenUsage { PromptTokens = 9600 });
                AssertEqual("context_limit_exceeded", nothingLeft.Failure!.ErrorType);
                AssertNull(nothingLeft.Note);
            }));
            cases.Add(Case("limit_failure_threshold", "ContextLimitFailure starts at 95 percent and needs a window and a count", TestTags.Negative, () =>
            {
                AssertNull(ApiContextPolicy.ContextLimitFailure(1000, new TokenUsage { PromptTokens = 949 }));
                AssertNotNull(ApiContextPolicy.ContextLimitFailure(1000, new TokenUsage { PromptTokens = 950 }));
                AssertNull(ApiContextPolicy.ContextLimitFailure(null, new TokenUsage { PromptTokens = 5000 }));
                AssertNull(ApiContextPolicy.ContextLimitFailure(1000, null));
                AssertThrows<ArgumentNullException>(() => new ApiContextPolicy(1000).BeforeTurn(null!, new List<ToolDefinition>()));
            }));
            return new TestSuiteDescriptor(SuiteId, "API runtime context policy", cases);
        }

        private static List<ChatMessage> Conversation(int toolResults, int resultLength)
        {
            List<ChatMessage> messages = new List<ChatMessage> { ChatMessage.System("system"), ChatMessage.User("the mission brief") };
            for (int i = 0; i < toolResults; i++)
            {
                messages.Add(ChatMessage.ToolResult("c" + i, "read_file", new string('x', resultLength)));
            }
            return messages;
        }

        private static TestCaseDescriptor Case(string id, string name, string tag, Action body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, ct =>
            {
                body();
                return Task.CompletedTask;
            }, tags: new List<string> { tag });
        }
    }
}
