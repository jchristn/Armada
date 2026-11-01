namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
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
            cases.Add(CaseAsync("inactive_captain_preserves_turn_and_rejects_new_work", "Disabling preserves an in-flight Ask turn, rejects new turns and summaries, and reactivation works", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_active");
                Captain captain = await NewCaptainAsync(h, "usr_active");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id });
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                AssertEqual(202, (await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Known request" })).StatusCode);
                AssertTrue(await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.Runner.Calls.Count == 1), 10000));
                captain.Active = false;
                await h.Db.Driver.Captains.UpdateAsync(captain, updateActive: true);
                h.Runner.Gate.SetResult(true);
                await WaitForTurnEndAsync(h, thread.Id);
                AssertEqual(2, (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null))!.Messages.Count);
                AssertEqual(409, (await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Refused" })).StatusCode);
                AssertEqual(409, (await h.Turns.SummarizeAsync(owner, thread.Id)).StatusCode);
                AssertEqual(1, h.Runner.Calls.Count);
                captain.Active = true;
                await h.Db.Driver.Captains.UpdateAsync(captain, updateActive: true);
                AssertEqual(202, (await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Resumed" })).StatusCode);
                await WaitForTurnEndAsync(h, thread.Id);
                AssertEqual(2, h.Runner.Calls.Count);
            }));


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
                AssertEqual(1, reply.ToolCallCount, "the message carries its tool call count");
                List<AskMessageToolCall> replyCalls = await h.ToolCallsAsync(owner, reply).ConfigureAwait(false);
                AssertEqual(1, replyCalls.Count, "tool call persisted");
                AssertEqual("mcp__armada__enumerate", replyCalls[0].ToolName);

                CaptainChatTurnOptions options = h.Runner.Calls.Single();
                AssertContains("User: How many voyages?", options.Prompt);
                AssertContains("Proposed as aap_", options.Prompt, "prompt explains proposals");
                AssertNotNull(options.McpSessionToken, "thread-scoped token");

                List<AskRecordedEvent> turnEvents = h.EventsFor("usr_turn", "ask.turn");
                AssertEqual(2, turnEvents.Count, "started and completed");
                AssertEqual("started", AskTurnEventPayload.From(turnEvents[0].Payload).State, "first ask.turn state");
                AskTurnEventPayload completed = AskTurnEventPayload.From(turnEvents[1].Payload);
                AssertEqual("completed", completed.State, "second ask.turn state");
                AssertEqual(thread.Id, completed.ThreadId, "ask.turn carries threadId");
                AssertTrue(h.EventsFor("usr_turn", "ask.chunk").Count > 0, "chunks streamed");
                AssertEqual(thread.Id, AskToolEventPayload.From(h.EventsFor("usr_turn", "ask.tool")[0].Payload).ThreadId, "ask.tool carries threadId");
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

            cases.Add(CaseAsync("focus_scopes_follow_up_questions", "A turn's prompt names the work this conversation started and its vessels, so follow-ups are scoped", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_focus");
                Captain captain = await NewCaptainAsync(h, "usr_focus").ConfigureAwait(false);

                Vessel vessel = new Vessel("TUIKit", "https://github.com/example/tuikit.git");
                vessel.TenantId = Constants.DefaultTenantId;
                vessel = await h.Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                Voyage voyage = new Voyage("Fix the table renderer");
                voyage.TenantId = Constants.DefaultTenantId;
                voyage = await h.Db.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                Mission mission = new Mission("Fix column widths");
                mission.TenantId = Constants.DefaultTenantId;
                mission.VoyageId = voyage.Id;
                mission.VesselId = vessel.Id;
                await h.Db.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AskThread unfocused = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                await h.Turns.SendMessageAsync(owner, unfocused.Id, new AskMessageSendRequest { Content = "What's running?" }).ConfigureAwait(false);
                await WaitForTurnEndAsync(h, unfocused.Id).ConfigureAwait(false);
                AssertFalse(h.Runner.Calls.Last().Prompt.Contains("Conversation focus"), "no focus block before any work is started");

                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AskTrackedWork work = new AskTrackedWork();
                work.TenantId = Constants.DefaultTenantId;
                work.UserId = "usr_focus";
                work.ThreadId = thread.Id;
                work.EntityType = AskTrackedEntityTypeEnum.Voyage;
                work.EntityId = voyage.Id;
                work.Title = voyage.Title;
                work.Status = "InProgress";
                await h.Db.Driver.AskTrackedWork.CreateOrGetAsync(work).ConfigureAwait(false);

                await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "What's running?" }).ConfigureAwait(false);
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);
                string prompt = h.Runner.Calls.Last().Prompt;
                AssertContains("Conversation focus", prompt);
                AssertContains("\"Fix the table renderer\" (" + voyage.Id + ") on TUIKit (" + vessel.Id + "): InProgress (active)", prompt);
                AssertContains("Vessels this conversation is working with: TUIKit (" + vessel.Id + ")", prompt);
                AssertTrue(prompt.IndexOf("Conversation focus", StringComparison.Ordinal) < prompt.IndexOf("User: What's running?", StringComparison.Ordinal), "focus precedes the question");
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
                AssertEqual("cancelled", AskTurnEventPayload.From(h.EventsFor("usr_busy", "ask.turn").Last().Payload).State, "last ask.turn state");
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
                AskTurnEventPayload failedTurn = AskTurnEventPayload.From(h.EventsFor("usr_err", "ask.turn").Last().Payload);
                AssertEqual("failed", failedTurn.State, "last ask.turn state");
                AssertEqual("The captain did not respond within the time limit.", failedTurn.Error, "ask.turn error");
                AssertEqual(last.Id, failedTurn.MessageId, "ask.turn names the error message");
                AssertEqual(400, (await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "   " }).ConfigureAwait(false)).StatusCode, "empty message");
            }));

            cases.Add(CaseAsync("detail_restores_running_turn_progress", "Thread detail read mid-turn carries what the captain streamed so far (text, thinking, tool calls with details), so a reopened conversation restores its live cards", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_live");
                Captain captain = await NewCaptainAsync(h, "usr_live").ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                h.Runner.Reply = "All quiet.";
                h.Runner.ToolCalls = new List<AskMessageToolCall> { new AskMessageToolCall { CallId = "call_1", ToolName = "mcp__armada__status", ArgumentsText = "{\"verbose\":true}", ResultText = "{\"Health\":\"ok\"}", Ok = true } };
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                h.Runner.BeforeGate = options =>
                {
                    options.OnThinking?.Invoke("Checking status.");
                    options.OnTool?.Invoke(new CaptainToolActivity { Phase = "started", Id = "call_1", Name = "mcp__armada__status", Arguments = "{\"verbose\":true}" });
                    options.OnTool?.Invoke(new CaptainToolActivity { Phase = "completed", Id = "call_1", Name = "mcp__armada__status", Ok = true, ElapsedMs = 12, Result = "{\"Health\":\"ok\"}" });
                    options.OnTool?.Invoke(new CaptainToolActivity { Phase = "started", Id = "call_2", Name = "mcp__armada__enumerate", Arguments = "{\"entityType\":\"voyage\"}" });
                    options.OnChunk?.Invoke("so far");
                };

                AskTurnStart start = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "How is the fleet?" }).ConfigureAwait(false);
                AssertEqual(202, start.StatusCode);
                bool streamed = await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.EventsFor("usr_live", "ask.chunk").Count >= 2), 10000).ConfigureAwait(false);
                AssertTrue(streamed, "the turn streamed its events");

                AskThreadDetail during = (await h.Threads.GetThreadDetailAsync(owner, thread.Id).ConfigureAwait(false))!;
                AskTurnProgress? live = during.ActiveTurn;
                AssertNotNull(live, "detail carries the running turn's progress");
                AssertEqual(start.Response!.TurnId, live!.TurnId, "progress names the running turn");
                AssertEqual(during.Thread.ActiveTurnId, live.TurnId, "progress matches the thread's ActiveTurnId");
                AskMessage reserved = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!.Messages.Single(m => m.Role == AskMessageRoleEnum.Assistant);
                AssertEqual(reserved.Id, live.MessageId, "progress names the reserved reply");
                AssertEqual(0, reserved.ToolCallCount, "nothing is persisted on the reply until the turn ends");
                AssertEqual("partial so far", live.Text, "streamed text so far");
                AssertEqual("Checking status.", live.ThinkingText, "streamed thinking so far");
                AssertEqual(2, live.ToolCalls.Count, "both tool calls, merged by call id");
                AssertEqual(2, live.ToolCallCount, "the progress carries the tool call count");
                EnumerationResult<AskMessageToolCall> livePage = (await h.Threads.EnumerateActiveTurnToolCallsAsync(owner, thread.Id, new EnumerationQuery { PageSize = 1, Order = EnumerationOrderEnum.CreatedAscending }).ConfigureAwait(false))!;
                AssertEqual(2L, livePage.TotalRecords, "the running turn's tool calls are paged");
                AssertEqual(1, livePage.Objects.Count, "one per page");
                AssertEqual("call_1", livePage.Objects[0].CallId, "first-seen order");
                AskMessageToolCall done = live.ToolCalls[0];
                AssertEqual("call_1", done.CallId);
                AssertEqual("mcp__armada__status", done.ToolName);
                AssertEqual("{\"verbose\":true}", done.ArgumentsText, "arguments kept for the expanded card");
                AssertEqual("{\"Health\":\"ok\"}", done.ResultText, "result kept for the expanded card");
                AssertEqual(true, done.Ok, "completed call is ok");
                AssertEqual(12L, done.ElapsedMs, "elapsed time kept");
                AskMessageToolCall running = live.ToolCalls[1];
                AssertEqual("call_2", running.CallId);
                AssertNull(running.Ok, "a call still running has no outcome");
                AssertNull(running.ResultText, "a call still running has no result");

                // The REST and MCP surfaces serialize the detail: the progress survives a JSON round trip.
                AskThreadDetail roundTrip = JsonSerializer.Deserialize<AskThreadDetail>(JsonSerializer.Serialize(during))!;
                AssertEqual("partial so far", roundTrip.ActiveTurn!.Text, "text after a round trip");
                AssertEqual(2, roundTrip.ActiveTurn.ToolCallCount, "tool call count after a round trip");
                AssertEqual(0, roundTrip.ActiveTurn.ToolCalls.Count, "the tool calls themselves are not serialized with the detail");
                AssertFalse(JsonSerializer.Serialize(during).Contains("\"ToolCalls\"", StringComparison.Ordinal), "no ToolCalls array in the serialized detail");
                AssertNull(JsonSerializer.Deserialize<AskThreadDetail>("{}")!.ActiveTurn, "minimal detail has no active turn");
                AskTurnProgress minimal = JsonSerializer.Deserialize<AskThreadDetail>("{\"ActiveTurn\":{\"Text\":null,\"ToolCalls\":null}}")!.ActiveTurn!;
                AssertEqual(String.Empty, minimal.Text, "null text defaults to empty");
                AssertEqual(String.Empty, minimal.TurnId, "missing turn id defaults to empty");
                AssertEqual(0, minimal.ToolCalls.Count, "null tool calls default to empty");

                AssertNull((await h.Threads.GetThreadDetailAsync(AskTestHarness.User("usr_other"), thread.Id).ConfigureAwait(false)), "another user cannot read the progress");

                h.Runner.Gate.TrySetResult(true);
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);
                AssertNull((await h.Threads.GetThreadDetailAsync(owner, thread.Id).ConfigureAwait(false))!.ActiveTurn, "no progress once the turn ended");
                AskMessage reply = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!.Messages.Single(m => m.Id == reserved.Id);
                AssertEqual("All quiet.", reply.ContentText);
                List<AskMessageToolCall> stored = await h.ToolCallsAsync(owner, reply).ConfigureAwait(false);
                AssertEqual(1, stored.Count, "the runner's tool calls are stored once, not again from the live record");
                AssertEqual("{\"Health\":\"ok\"}", stored[0].ResultText, "persisted tool result");
            }));

            cases.Add(CaseAsync("stopped_turn_keeps_tool_calls", "A turn stopped mid-way keeps the tool calls it already made on its stopped message", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_stop");
                Captain captain = await NewCaptainAsync(h, "usr_stop").ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                h.Runner.BeforeGate = options => options.OnTool?.Invoke(new CaptainToolActivity { Phase = "started", Id = "call_1", Name = "mcp__armada__status", Arguments = "{\"verbose\":true}" });

                await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Status?" }).ConfigureAwait(false);
                bool running = await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.EventsFor("usr_stop", "ask.tool").Count >= 2), 10000).ConfigureAwait(false);
                AssertTrue(running, "the tool call streamed");
                AssertEqual(200, await h.Turns.CancelAsync(owner, thread.Id).ConfigureAwait(false), "owner stops the turn");
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);

                AskMessage last = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!.Messages.Last();
                AssertEqual(AskMessageKindEnum.Error, last.Kind, "stopped message");
                AssertContains("Stopped before", last.ContentText);
                List<AskMessageToolCall> kept = await h.ToolCallsAsync(owner, last).ConfigureAwait(false);
                AssertEqual(1, kept.Count, "the tool call made before the stop is kept");
                AssertEqual("mcp__armada__status", kept[0].ToolName);
                AssertEqual("{\"verbose\":true}", kept[0].ArgumentsText, "its arguments are kept");
            }));

            cases.Add(CaseAsync("crashed_turn_keeps_tool_calls", "A turn whose runtime throws keeps the tool calls it already made on its error message", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_crash");
                Captain captain = await NewCaptainAsync(h, "usr_crash").ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Status?" }).ConfigureAwait(false);
                bool running = await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.EventsFor("usr_crash", "ask.tool").Count >= 1), 10000).ConfigureAwait(false);
                AssertTrue(running, "the tool call streamed");
                h.Runner.Gate.TrySetException(new InvalidOperationException("runtime crashed"));
                await WaitForTurnEndAsync(h, thread.Id).ConfigureAwait(false);

                AskMessage last = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!.Messages.Last();
                AssertEqual(AskMessageKindEnum.Error, last.Kind, "error message");
                AssertContains("runtime crashed", last.ContentText);
                List<AskMessageToolCall> kept = await h.ToolCallsAsync(owner, last).ConfigureAwait(false);
                AssertEqual(1, kept.Count, "the tool call made before the crash is kept");
                AssertEqual("call_1", kept[0].CallId);
                AssertEqual("failed", AskTurnEventPayload.From(h.EventsFor("usr_crash", "ask.turn").Last().Payload).State, "failed ask.turn");
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
