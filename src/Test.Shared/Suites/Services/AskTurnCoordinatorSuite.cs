namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Ask;
    using Armada.Server.Ask;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Thread turns with a stubbed captain: background turn with persisted reply and tool calls, owner-scoped streaming
    /// and turn events, one running turn per thread (409), cancel, server-side history, thread-scoped token, summaries.
    /// </summary>
    public sealed class AskTurnCoordinatorSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskTurnCoordinator";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("turn_persists_reply_and_tool_calls", "A turn runs in the background and persists the reply and its tool calls", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_turn");
                Captain captain = await NewCaptainAsync(h, "usr_turn").ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                h.Runner.Reply = "There are 2 voyages.";
                h.Runner.ToolCalls = new List<AskMessageToolCall> { new AskMessageToolCall { CallId = "c1", ToolName = "mcp__armada__enumerate", ArgumentsText = "{\"entityType\":\"voyage\"}", ResultText = "{}", Ok = true } };

                AskTurnStart start = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "How many voyages?" }).ConfigureAwait(false);
                AssertEqual(202, start.StatusCode);
                AssertNotNull(start.Response!.TurnId, "turn id");
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AssertEqual(2, page.Messages.Count, "user + assistant");
                AskMessage reply = page.Messages[1];
                AssertEqual(AskMessageRoleEnum.Assistant, reply.Role);
                AssertEqual("There are 2 voyages.", reply.ContentText);
                AssertEqual(captain.Id, reply.CaptainId);
                AssertEqual(1, reply.ToolCalls.Count, "tool call persisted");
                AssertEqual("mcp__armada__enumerate", reply.ToolCalls[0].ToolName);

                CaptainChatTurnOptions options = h.Runner.Calls.Single();
                AssertContains("User: How many voyages?", options.Prompt);
                AssertContains("Proposed as aap_", options.Prompt, "prompt explains proposals");
                AssertNotNull(options.McpSessionToken, "thread-scoped token");

                List<AskRecordedEvent> turnEvents = h.EventsFor("usr_turn", "ask.turn");
                AssertEqual(2, turnEvents.Count, "started and completed");
                AssertContains("completed", System.Text.Json.JsonSerializer.Serialize(turnEvents[1].Payload));
                AssertTrue(h.EventsFor("usr_turn", "ask.chunk").Count > 0, "chunks streamed");
                AssertContains(thread.Id, System.Text.Json.JsonSerializer.Serialize(h.EventsFor("usr_turn", "ask.tool")[0].Payload), "ask.tool carries threadId");
            }));

            cases.Add(CaseAsync("reply_sorts_before_messages_posted_mid_turn", "A confirm card or work update posted while the captain is still writing sorts after the captain's reply", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_order");
                Captain captain = await NewCaptainAsync(h, "usr_order").ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                h.Runner.Reply = "I proposed the dispatch; approve it below.";
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                AskTurnStart start = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Dispatch it." }).ConfigureAwait(false);
                AssertEqual(202, start.StatusCode);
                for (int i = 0; i < 100 && h.Runner.Calls.Count == 0; i++) await Task.Delay(20).ConfigureAwait(false);
                AssertEqual(1, h.Runner.Calls.Count, "turn is running");

                AskThread internalThread = (await h.Threads.ReadThreadInternalAsync(thread.Id).ConfigureAwait(false))!;
                AskMessage midTurn = new AskMessage();
                midTurn.Role = AskMessageRoleEnum.System;
                midTurn.Kind = AskMessageKindEnum.WorkUpdate;
                midTurn.ContentText = "Voyage started.";
                await h.Threads.AppendMessageAsync(internalThread, midTurn, true).ConfigureAwait(false);

                AskMessagePage during = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                h.Runner.Gate.TrySetResult(true);
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                List<AskMessage> ordered = page.Messages.OrderBy(m => m.Sequence).ToList();
                AssertEqual(3, ordered.Count, "user, reply, update (no extra placeholder)");
                AssertEqual(AskMessageRoleEnum.User, ordered[0].Role);
                AssertEqual("I proposed the dispatch; approve it below.", ordered[1].ContentText, "reply filled in at its reserved position");
                AssertEqual(AskMessageKindEnum.WorkUpdate, ordered[2].Kind, "mid-turn update sorts after the reply");
                AssertEqual(3, during.Messages.Count, "reservation existed while the captain was writing");
            }));

            cases.Add(CaseAsync("one_turn_per_thread", "A second message while a turn runs is refused with 409; cancel stops the turn", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_busy");
                Captain captain = await NewCaptainAsync(h, "usr_busy").ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                AskTurnStart first = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "one" }).ConfigureAwait(false);
                AssertEqual(202, first.StatusCode);
                AssertEqual(first.Response!.TurnId, h.Turns.ActiveTurnId(thread.Id), "active turn id");
                AssertEqual(first.Response.TurnId, (await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false))!.ActiveTurnId, "ActiveTurnId on the thread");

                AskTurnStart second = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "two" }).ConfigureAwait(false);
                AssertEqual(409, second.StatusCode, "conflict");
                AssertEqual(409, (await h.Turns.SummarizeAsync(owner, thread.Id).ConfigureAwait(false)).StatusCode, "summary also conflicts");
                AssertEqual(1, (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!.Messages.Count(m => m.Role == AskMessageRoleEnum.User), "refused message not stored (only the first user message exists)");

                AssertEqual(404, await h.Turns.CancelAsync(AskTestHarness.User("usr_else"), thread.Id).ConfigureAwait(false), "another user cannot cancel");
                AssertEqual(200, await h.Turns.CancelAsync(owner, thread.Id).ConfigureAwait(false), "owner cancels");
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);
                AssertContains("cancelled", System.Text.Json.JsonSerializer.Serialize(h.EventsFor("usr_busy", "ask.turn").Last().Payload));
                AssertEqual(409, await h.Turns.CancelAsync(owner, thread.Id).ConfigureAwait(false), "nothing to cancel");

                h.Runner.Gate = null;
                AssertEqual(202, (await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "three" }).ConfigureAwait(false)).StatusCode, "a new turn can start");
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("history_and_summary", "History comes from persisted messages and is bounded; summaries are stored", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.HistoryTurns = 4;
                AuthContext owner = AskTestHarness.User("usr_hist");
                Captain captain = await NewCaptainAsync(h, "usr_hist").ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                for (int i = 1; i <= 3; i++)
                {
                    h.Runner.Reply = "answer " + i;
                    await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "question " + i }).ConfigureAwait(false);
                    await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);
                }

                string prompt = h.Runner.Calls.Last().Prompt;
                AssertContains("User: question 3", prompt);
                AssertContains("Assistant: answer 2", prompt);
                AssertFalse(prompt.Contains("question 1"), "older history trimmed to HistoryTurns");

                h.Runner.Reply = "- Asked three questions.";
                AskTurnStart summary = await h.Turns.SummarizeAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual(202, summary.StatusCode);
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);
                AskThread? summarized = await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual("- Asked three questions.", summarized!.SummaryText);
                AssertNotNull(summarized.SummaryUtc, "summary time");

                h.Runner.Reply = "ok";
                await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "question 4" }).ConfigureAwait(false);
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);
                AssertContains("Summary of the conversation so far:", h.Runner.Calls.Last().Prompt, "summary used as context");

                AskThread noCaptain = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                await h.Turns.SendMessageAsync(owner, noCaptain.Id, new AskMessageSendRequest { Content = "remember this" }).ConfigureAwait(false);
                await h.Turns.SummarizeAsync(owner, noCaptain.Id).ConfigureAwait(false);
                await WaitForTurnEndAsync(h, noCaptain.Id).ConfigureAwait(false);
                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, noCaptain.Id, null).ConfigureAwait(false))!;
                AssertContains("First request: remember this", page.Messages.Single(m => m.Kind == AskMessageKindEnum.Summary).ContentText, "deterministic summary");
            }));

            cases.Add(CaseAsync("failed_turn_posts_error", "A failed captain turn posts an Error message and a failed ask.turn", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_err");
                Captain captain = await NewCaptainAsync(h, "usr_err").ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                h.Runner.Error = "The captain did not respond within the time limit.";
                await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "hello" }).ConfigureAwait(false);
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);
                AskMessage last = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!.Messages.Last();
                AssertEqual(AskMessageKindEnum.Error, last.Kind);
                AssertContains("time limit", last.ContentText);
                AssertContains("failed", System.Text.Json.JsonSerializer.Serialize(h.EventsFor("usr_err", "ask.turn").Last().Payload));
                AssertEqual(400, (await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "   " }).ConfigureAwait(false)).StatusCode, "empty message");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Ask Turn Coordinator", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Captain> NewCaptainAsync(AskTestHarness h, string userId)
        {
            return await h.Db.Driver.Captains.CreateAsync(new Captain("captain-" + userId) { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
        }

        private static async Task WaitForTurnEndAsync(AskTestHarness h, string threadId)
        {
            bool done = await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.Turns.ActiveTurnId(threadId) == null), 10000).ConfigureAwait(false);
            AssertTrue(done, "turn finished");
            await Task.Delay(50).ConfigureAwait(false);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
