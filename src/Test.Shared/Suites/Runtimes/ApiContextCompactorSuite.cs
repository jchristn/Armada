namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using Armada.Runtimes.Tools;
    using PolyPrompt.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Compaction shrinks old write and edit arguments and old reasoning as well as tool outputs, keeps recent turns
    /// whole and arguments valid JSON, and the local estimate counts 3.2 characters per token or a registered
    /// tokenizer (RS-7.10).
    /// </summary>
    public sealed class ApiContextCompactorSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Runtimes.ApiContextCompactor";

        /// <summary>Builds the compactor cases.</summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("old_write_arguments_keep_path_and_size", "An old write_file call keeps its path and a size note as valid JSON; recent calls stay whole", TestTags.Positive, () =>
            {
                List<ChatMessage> messages = Conversation(10, "write_file", i => Json(new { file_path = "src/File" + i + ".cs", content = new string('c', 5000) }));
                long removed = ApiContextCompactor.Compact(messages);
                AssertTrue(removed > 0, "something was removed");

                ToolCall oldest = FirstCall(messages, 0);
                CompactedToolArguments? compacted = JsonSerializer.Deserialize<CompactedToolArguments>(oldest.ArgumentsJson!);
                AssertNotNull(compacted, "compacted arguments are valid JSON");
                AssertEqual("src/File0.cs", compacted!.FilePath, "path kept");
                AssertTrue(compacted.Note.StartsWith(ApiContextCompactor.CompactedArgumentsMarker, StringComparison.Ordinal), "note marker");
                AssertTrue(compacted.Note.Contains("5000 characters"), "size note: " + compacted.Note);

                for (int i = 4; i < 10; i++)
                    AssertTrue(FirstCall(messages, i).ArgumentsJson!.Contains(new string('c', 5000)), "call " + i + " is recent and kept whole");
                AssertEqual(0L, ApiContextCompactor.Compact(messages), "a second pass finds nothing new");
            }));

            cases.Add(Case("old_edit_and_multi_edit_arguments_compacted", "edit_file and multi_edit bodies are compacted with their edit count", TestTags.Positive, () =>
            {
                ToolCall edit = new ToolCall { Id = "e", Name = "edit_file", ArgumentsJson = Json(new { file_path = "a.cs", old_string = new string('o', 400), new_string = new string('n', 600) }) };
                AssertEqual(true, ApiContextCompactor.CompactArguments(edit) > 0, "edit compacted");
                AssertTrue(edit.ArgumentsJson!.Contains("1000 characters"), edit.ArgumentsJson);

                ToolCall multi = new ToolCall
                {
                    Id = "m",
                    Name = "multi_edit",
                    ArgumentsJson = Json(new { file_path = "b.cs", edits = new[] { new { old_string = new string('o', 300), new_string = new string('n', 300) }, new { old_string = new string('p', 300), new_string = new string('q', 300) } } })
                };
                AssertEqual(true, ApiContextCompactor.CompactArguments(multi) > 0, "multi_edit compacted");
                CompactedToolArguments? parsed = JsonSerializer.Deserialize<CompactedToolArguments>(multi.ArgumentsJson!);
                AssertEqual("b.cs", parsed!.FilePath);
                AssertTrue(parsed.Note.Contains("1200 characters in 2 edits"), parsed.Note);
            }));

            cases.Add(Case("other_small_and_invalid_arguments_untouched", "Other tools, small arguments and invalid JSON are left as they are", TestTags.Negative, () =>
            {
                string readArgs = Json(new { file_path = "a.cs", pattern = new string('r', 2000) });
                ToolCall read = new ToolCall { Id = "r", Name = "read_file", ArgumentsJson = readArgs };
                AssertEqual(0L, ApiContextCompactor.CompactArguments(read));
                AssertEqual(readArgs, read.ArgumentsJson);

                string smallArgs = Json(new { file_path = "a.cs", content = "short" });
                ToolCall small = new ToolCall { Id = "s", Name = "write_file", ArgumentsJson = smallArgs };
                AssertEqual(0L, ApiContextCompactor.CompactArguments(small));
                AssertEqual(smallArgs, small.ArgumentsJson);

                string invalid = "{\"file_path\":\"a.cs\",\"content\":\"" + new string('x', 2000);
                ToolCall broken = new ToolCall { Id = "b", Name = "write_file", ArgumentsJson = invalid };
                AssertEqual(0L, ApiContextCompactor.CompactArguments(broken));
                AssertEqual(invalid, broken.ArgumentsJson);

                AssertThrows<ArgumentNullException>(() => ApiContextCompactor.CompactArguments(null!));
                AssertThrows<ArgumentNullException>(() => ApiContextCompactor.CompactReasoning(null!));
            }));

            cases.Add(Case("old_reasoning_removed_recent_kept", "Old assistant reasoning is removed, tool calls kept; recent reasoning stays", TestTags.Positive, () =>
            {
                string reasoning = "<think>" + new string('t', 3000) + "</think>";
                List<ChatMessage> messages = Conversation(10, "list_directory", i => "{}");
                foreach (ChatMessage message in messages)
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0) message.Text = reasoning + "\nlooking at the tree";
                messages.Insert(2, ChatMessage.Assistant(reasoning));

                ApiContextCompactor.Compact(messages);

                AssertEqual(ApiContextCompactor.CompactedReasoningNote, messages[2].Text, "a reasoning-only reply keeps a note, not an empty message");
                ChatMessage oldCall = AssistantAt(messages, 0);
                AssertEqual("looking at the tree", oldCall.Text, "old reasoning removed, the visible text kept");
                AssertEqual(1, oldCall.ToolCalls!.Count, "tool calls kept");
                AssertTrue(AssistantAt(messages, 9).Text!.Contains("<think>"), "recent reasoning kept");
            }));

            cases.Add(Case("estimate_uses_3_2_characters_per_token", "The local estimate counts 3.2 characters per token, shared with ToolLimits and the composer", TestTags.Positive, () =>
            {
                List<ChatMessage> one = new List<ChatMessage> { ChatMessage.User(new string('a', 320)) };
                AssertEqual(100L + ApiContextCompactor.MessageOverheadTokens, ApiContextCompactor.EstimateTokens(one));
                AssertEqual(CharacterRatioTokenEstimator.CodeCharactersPerToken, ToolLimits.CharactersPerToken);
                AssertEqual(100L, InstructionsComposer.EstimateTokens(new string('a', 320)));
                AssertEqual(1L, CharacterRatioTokenEstimator.Default.CountTokens("a"));
                AssertEqual(0L, CharacterRatioTokenEstimator.Default.CountTokens(null));
                AssertThrows<ArgumentOutOfRangeException>(() => new CharacterRatioTokenEstimator(0));
            }));

            cases.Add(Case("named_tokenizer_resolves_and_drives_policy", "A registered tokenizer resolves by name and the context policy uses it for its estimate", TestTags.Positive, () =>
            {
                AssertTrue(Object.ReferenceEquals(CharacterRatioTokenEstimator.Default, TokenEstimators.Resolve(null)), "no name gives the default");
                AssertTrue(Object.ReferenceEquals(CharacterRatioTokenEstimator.Default, TokenEstimators.Resolve("unknown-tokenizer")), "unknown name gives the default");
                CharacterRatioTokenEstimator dense = new CharacterRatioTokenEstimator(0.5);
                TokenEstimators.Register("rs710-test", dense);
                try
                {
                    AssertTrue(Object.ReferenceEquals(dense, TokenEstimators.Resolve("RS710-TEST")), "names match ignoring case");

                    List<ChatMessage> messages = Conversation(10, "read_file", i => "{}");
                    for (int i = 0; i < messages.Count; i++)
                        if (!String.IsNullOrEmpty(messages[i].ToolCallId)) messages[i] = ChatMessage.ToolResult(messages[i].ToolCallId!, "read_file", new string('x', 1000));
                    AssertNull(new ApiContextPolicy(20000).BeforeTurn(messages, new List<PolyPrompt.Models.ToolDefinition>()), "about 3,200 tokens at 3.2 is below 80% of 20,000");
                    AssertNotNull(new ApiContextPolicy(20000, TokenEstimators.Resolve("rs710-test")).BeforeTurn(messages, new List<PolyPrompt.Models.ToolDefinition>()), "the dense tokenizer counts 20,000 and compacts");
                }
                finally
                {
                    TokenEstimators.Unregister("rs710-test");
                }
                AssertThrows<ArgumentNullException>(() => TokenEstimators.Register(" ", dense));
            }));

            return new TestSuiteDescriptor(SuiteId, "API runtime context compaction", cases);
        }

        private static List<ChatMessage> Conversation(int calls, string tool, Func<int, string> arguments)
        {
            List<ChatMessage> messages = new List<ChatMessage> { ChatMessage.System("system"), ChatMessage.User("the mission brief") };
            for (int i = 0; i < calls; i++)
            {
                messages.Add(ChatMessage.AssistantToolCalls(new List<ToolCall> { new ToolCall { Id = "c" + i, Name = tool, ArgumentsJson = arguments(i) } }));
                messages.Add(ChatMessage.ToolResult("c" + i, tool, "ok"));
            }
            return messages;
        }

        private static ChatMessage AssistantAt(List<ChatMessage> messages, int callIndex)
        {
            int seen = 0;
            foreach (ChatMessage message in messages)
            {
                if (message.ToolCalls == null || message.ToolCalls.Count == 0) continue;
                if (seen == callIndex) return message;
                seen++;
            }
            throw new InvalidOperationException("no assistant call " + callIndex);
        }

        private static ToolCall FirstCall(List<ChatMessage> messages, int callIndex)
        {
            return AssistantAt(messages, callIndex).ToolCalls![0];
        }

        private static string Json(object value)
        {
            return JsonSerializer.Serialize(value);
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
