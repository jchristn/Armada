namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Ask Armada (W2) headless: the thread list (render, search, archive, rename, pin, delete, paging, read marking),
    /// every message kind, streaming from a scripted event source, confirm card decisions (and that a decided status
    /// never regresses), live work card updates, the composer (optimistic send, history, newline, stop with the forced
    /// end), older pages, reconnect refetch, the header (auto-approve, captain picker, no-MCP note, summarize), the dock,
    /// and "Ask about this".
    /// </summary>
    public sealed class TuiAskSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ask";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "thread_list_rows", "The thread list shows pinned first, unread, working, replying, and archived", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskThread pinned = AskFixtures.Thread("ath_1", "TUIKit fixes");
                pinned.Pinned = true;
                AskThread unread = AskFixtures.Thread("ath_2", "Greeting rollout");
                unread.UnreadCount = 3;
                unread.ActiveWorkCount = 1;
                AskThread replying = AskFixtures.Thread("ath_3", "Billing cleanup");
                replying.ActiveTurnId = "atn_9";
                fx.AddThread(pinned).AddThread(unread).AddThread(replying);
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/ask", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    AssertTrue(host.PumpUntil(() => ask.Threads.Count == 3), "threads loaded");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "CONVERSATIONS", "list heading");
                    TuiCase.Contains(frame, "^ TUIKit fixes", "pinned marker");
                    TuiCase.Contains(frame, "Greeting rollout", "row");
                    TuiCase.Contains(frame, " 3", "unread badge");
                    TuiCase.Contains(frame, "* Working", "working marker");
                    TuiCase.Contains(frame, "Replying...", "replying marker");
                    TuiCase.Contains(frame, "[ ] Show archived (A)", "archived toggle");
                    List<AskThread> sorted = AskThreadListLogic.Sort(new List<AskThread> { unread, pinned });
                    AssertEqual("ath_1", sorted[0].Id, "pinned sorts first");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "thread_list_actions", "Search, archive, rename, pin, delete, and new from the list", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes")).AddThread(AskFixtures.Thread("ath_2", "Other"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/ask", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    ask.SearchDebounceMs = 0;
                    host.PumpUntil(() => ask.Threads.Count == 2);
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    screen.Scope.Focus(screen.ThreadList);
                    host.Press("/").Type("tui");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Bodies.Any(b => b.Contains("\"Search\":\"tui\""))), "server-side search");
                    host.Press("enter").Press("A");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Bodies.Any(b => b.Contains("\"IncludeArchived\":true"))), "archived toggle reloads");
                    AssertTrue(ask.IncludeArchived, "archived shown");
                    ask.SetSearch("");
                    host.PumpUntil(() => ask.Query.Length == 0);
                    host.Press("home").Press("e");
                    AssertNotNull(screen.ThreadList.RenamingId, "inline rename");
                    host.Press("ctrl+u").Type("Renamed").Press("enter");
                    AssertTrue(host.PumpUntil(() => ask.Threads.Any(t => t.Title == "Renamed")), "renamed through PUT");
                    host.Press("p");
                    AssertTrue(host.PumpUntil(() => ask.Threads.Any(t => t.Pinned)), "pinned through PUT");
                    host.Press("del");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "delete confirm");
                    TuiCase.Contains(host.Screen(), "Its messages and action history are removed", "dashboard delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => ask.Threads.Count == 1), "deleted");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Conversation deleted."))), "toast");
                    screen = (AskScreen)host.Tui.Shell.Screen!;
                    screen.Scope.Focus(screen.ThreadList);
                    host.Press("n");
                    AssertEqual("/ask", host.Tui.Context.Router.Current!.FullPath, "new conversation route");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "thread_list_paging", "Paging loads the next page when the cursor reaches Load more", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "First"));
                fx.Stub.On("POST", "/api/v1/ask/threads/enumerate", body => body.Contains("\"PageNumber\":2")
                    ? StubHttpHandler.Response(System.Net.HttpStatusCode.OK, "{\"Success\":true,\"PageNumber\":2,\"PageSize\":50,\"TotalPages\":2,\"TotalRecords\":2,\"Objects\":[{\"Id\":\"ath_2\",\"Title\":\"Second\"}]}")
                    : StubHttpHandler.Response(System.Net.HttpStatusCode.OK, "{\"Success\":true,\"PageNumber\":1,\"PageSize\":50,\"TotalPages\":2,\"TotalRecords\":2,\"Objects\":[{\"Id\":\"ath_1\",\"Title\":\"First\"}]}"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/ask", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    AssertTrue(host.PumpUntil(() => ask.ListHasMore), "has more");
                    TuiCase.Contains(host.Screen(), "[Load more]", "load more row");
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    screen.Scope.Focus(screen.ThreadList);
                    host.Press("end");
                    AssertTrue(host.PumpUntil(() => ask.Threads.Count == 2), "second page merged");
                    AssertFalse(ask.ListHasMore, "no more pages");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "read_marking", "Opening a thread marks it read, never while the terminal is unfocused", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskThread t = AskFixtures.Thread("ath_1", "TUIKit fixes");
                t.UnreadCount = 2;
                fx.AddThread(t, AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "hello"));
                fx.AddThread(AskFixtures.Thread("ath_2", "Other"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/ask/ath_1", fx.Stub))
                {
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_1/read") >= 1), "read on open");
                    int before = fx.Stub.Count("POST /api/v1/ask/threads/ath_1/read");
                    host.Tui.Context.Notifications.TerminalFocused = false;
                    AskThread bumped = AskFixtures.Thread("ath_1", "TUIKit fixes");
                    bumped.UnreadCount = 1;
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.thread", new AskThreadEvent { ThreadId = "ath_1", Thread = bumped }));
                    host.Pump();
                    System.Threading.Thread.Sleep(100);
                    host.Pump();
                    AssertEqual(before, fx.Stub.Count("POST /api/v1/ask/threads/ath_1/read"), "not while unfocused");
                    host.Tui.Ask.OnTerminalFocusChanged(true);
                    host.Tui.Context.Notifications.TerminalFocused = true;
                    host.Tui.Ask.OnTerminalFocusChanged(true);
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_1/read") > before), "read when focus returns");
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.thread", new AskThreadEvent { ThreadId = "ath_1", Thread = bumped }));
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_1/read") > before + 1), "read on ask.thread with unread");
                    AssertEqual(0, host.Tui.Ask.Threads.First(x => x.Id == "ath_1").UnreadCount, "open thread unread stays zero");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "message_kinds", "Every message kind renders like the dashboard", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskActionProposal pending = AskFixtures.Proposal("aap_1", "ath_1", "dispatch", AskProposalStatusEnum.Pending);
                AskActionProposal executed = AskFixtures.Proposal("aap_2", "ath_1", "status", AskProposalStatusEnum.Executed);
                executed.Source = AskProposalSourceEnum.QuickAction;
                executed.SummaryText = "Summarize all active work";
                AskMessage user = AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "dispatch a voyage please");
                AskMessage reply = AskFixtures.Message("amg_2", "ath_1", 2, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "Here is the **plan**:\n\n- step one\n- step two");
                reply.CaptainId = "cpt_1";
                reply.DurationMs = 4200;
                reply.ThinkingText = "Considering the vessel";
                AskMessageToolCall call = new AskMessageToolCall();
                call.Id = "atc_1";
                call.MessageId = "amg_2";
                call.ThreadId = "ath_1";
                call.CallId = "call_1";
                call.ToolName = "mcp__armada__enumerate";
                call.ArgumentsText = "{\"entityType\":\"vessels\"}";
                call.ResultText = "{\"count\":2}";
                call.Ok = true;
                call.ElapsedMs = 120;
                reply.ToolCalls.Add(call);
                AskMessage empty = AskFixtures.Message("amg_3", "ath_1", 3, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "");
                AskMessage proposal = AskFixtures.Message("amg_4", "ath_1", 4, AskMessageRoleEnum.Assistant, AskMessageKindEnum.ActionProposal, "");
                proposal.ProposalId = "aap_1";
                proposal.Proposal = pending;
                AskMessage result = AskFixtures.Message("amg_5", "ath_1", 5, AskMessageRoleEnum.System, AskMessageKindEnum.ActionResult, "Status ran.");
                result.ProposalId = "aap_2";
                result.Proposal = executed;
                AskMessage update = AskFixtures.Message("amg_6", "ath_1", 6, AskMessageRoleEnum.System, AskMessageKindEnum.WorkUpdate, "Mission \"Mission 1\" landed.");
                AskMessage summary = AskFixtures.Message("amg_7", "ath_1", 7, AskMessageRoleEnum.System, AskMessageKindEnum.Summary, "We dispatched one voyage.");
                AskMessage error = AskFixtures.Message("amg_8", "ath_1", 8, AskMessageRoleEnum.System, AskMessageKindEnum.Error, "The captain could not be reached.");
                AskMessage system = AskFixtures.Message("amg_9", "ath_1", 9, AskMessageRoleEnum.System, AskMessageKindEnum.Text, "Captain changed to claude-1.");
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), user, reply, empty, proposal, result, update, summary, error, system);
                using (TuiTestHost host = TuiCase.SignedIn(140, 80, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count == 9), "loaded");
                    screen.Transcript.Layout(110);
                    string text = String.Join("\n", screen.Transcript.PlainLines());
                    TuiCase.Contains(text, "You", "user label");
                    TuiCase.Contains(text, "dispatch a voyage please", "user text");
                    TuiCase.Contains(text, "[ok] mcp__armada__enumerate", "tool chip");
                    TuiCase.Contains(text, "120ms", "tool time");
                    TuiCase.Contains(text, "claude-1  4.2s", "captain name and duration");
                    TuiCase.Contains(text, "> Thinking", "collapsed thinking");
                    TuiCase.Contains(text, "step one", "markdown list");
                    TuiCase.NotContains(text, "**plan**", "markdown rendered");
                    TuiCase.Contains(text, "Approval needed  dispatch  Proposed by the captain", "pending card heading");
                    TuiCase.Contains(text, "Dispatch voyage \"Fix tables\" to Alpha (1 mission)", "summary");
                    TuiCase.Contains(text, "Nothing runs until you approve. Expires", "expiry");
                    TuiCase.Contains(text, "[a] Approve   [r] Reject   [x] Arguments", "decision keys");
                    TuiCase.Contains(text, "* Action result", "action result");
                    TuiCase.Contains(text, "Quick action", "quick action source");
                    TuiCase.Contains(text, "Ran successfully.", "executed outcome");
                    TuiCase.Contains(text, "- Progress update", "milestone");
                    TuiCase.Contains(text, "= Conversation summary", "summary kind");
                    TuiCase.Contains(text, "! Error", "error kind");
                    TuiCase.Contains(text, "The captain could not be reached.", "error text");
                    TuiCase.Contains(text, "Captain changed to claude-1.", "system text");
                    AssertFalse(screen.Transcript.Blocks.Any(b => b.Key == "amg_3"), "empty assistant reply hidden");
                    screen.Transcript.ViewState.Toggle(screen.Transcript.ViewState.ExpandedTools, "amg_2");
                    screen.Transcript.ViewState.Toggle(screen.Transcript.ViewState.ExpandedThinking, "amg_2");
                    screen.Transcript.ViewState.Toggle(screen.Transcript.ViewState.ExpandedArguments, "aap_1");
                    screen.Transcript.Layout(110);
                    text = String.Join("\n", screen.Transcript.PlainLines());
                    TuiCase.Contains(text, "\"entityType\": \"vessels\"", "expanded tool arguments");
                    TuiCase.Contains(text, "Considering the vessel", "expanded thinking");
                    TuiCase.Contains(text, "\"vesselId\": \"vsl_a\"", "expanded proposal arguments");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "streaming", "A scripted turn streams chunks, tools, and thinking, then the persisted reply replaces it", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "status?"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    EventPump events = host.Tui.Context.Events;
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Messages.Count == 1), "loaded");
                    events.Inject(AskFixtures.EventJson("ask.turn", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"state\":\"started\"}"));
                    host.Pump();
                    AssertTrue(ask.Conversation.TurnActive, "turn active");
                    AssertTrue(host.WaitForText("Ctrl+C Stop"), "waiting line and stop hint");
                    events.Inject(AskFixtures.EventJson("ask.thinking", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"delta\":\"Looking at the fleet\"}"));
                    events.Inject(AskFixtures.EventJson("ask.tool", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"phase\":\"started\",\"id\":\"c1\",\"name\":\"mcp__armada__status\",\"arguments\":{}}"));
                    host.Pump();
                    TuiCase.Contains(host.Screen(), "[..] mcp__armada__status", "running chip");
                    events.Inject(AskFixtures.EventJson("ask.tool", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"phase\":\"completed\",\"id\":\"c1\",\"ok\":true,\"elapsedMs\":42,\"result\":{\"active\":3}}"));
                    events.Inject(AskFixtures.EventJson("ask.chunk", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"delta\":\"Three things are running:\\n\\n- voyage A\\n\"}"));
                    events.Inject(AskFixtures.EventJson("ask.chunk", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"delta\":\"- voyage B\\n\\n```\\ncode line\"}"));
                    host.Pump();
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[ok] mcp__armada__status", "completed chip");
                    TuiCase.Contains(frame, "Looking at the fleet", "live thinking shown");
                    TuiCase.Contains(frame, "voyage A", "streamed list item");
                    TuiCase.Contains(frame, "voyage B", "second chunk");
                    TuiCase.Contains(frame, "code line", "unclosed code block still shows");
                    TuiCase.Contains(frame, "replying...", "live label");
                    TuiCase.Contains(frame, "first token", "metrics line");
                    TuiCase.NotContains(frame, "- voyage A- voyage B", "newlines kept while streaming");
                    events.Inject(AskFixtures.EventJson("ask.turn", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"state\":\"completed\",\"messageId\":\"amg_2\"}"));
                    AskMessage persisted = AskFixtures.Message("amg_2", "ath_1", 2, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "Three things are running:\n\n- voyage A\n- voyage B");
                    persisted.CaptainId = "cpt_1";
                    events.Inject(AskFixtures.Event("ask.message", new AskMessageEvent { ThreadId = "ath_1", Message = persisted }));
                    host.Pump();
                    AssertFalse(ask.Conversation.TurnActive, "turn finished");
                    AssertNull(ask.Conversation.Streaming, "stream replaced by the persisted reply");
                    AssertTrue(ask.Conversation.Metrics.ContainsKey("amg_2"), "metrics kept for the reply");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_1/messages/enumerate") >= 2), "newest page refetched after the turn");
                    events.Inject(AskFixtures.EventJson("ask.chunk", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"delta\":\"late\"}"));
                    host.Pump();
                    TuiCase.NotContains(host.Screen(), "late", "late chunk for a closed turn ignored");
                    events.Inject(AskFixtures.EventJson("ask.turn", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_2\",\"state\":\"failed\",\"error\":\"runtime exited 1\"}"));
                    AssertTrue(host.WaitForText("The captain turn failed: runtime exited 1"), "failure shown");
                    events.Inject(AskFixtures.EventJson("ask.chunk", "{\"threadId\":\"ath_other\",\"turnId\":\"atn_9\",\"delta\":\"elsewhere\"}"));
                    host.Pump();
                    TuiCase.NotContains(host.Screen(), "elsewhere", "other threads ignored");
                    AssertTrue(ask.Activity["ath_other"].Replying, "other thread shows replying in the list");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "confirm_decisions", "Confirm cards approve and reject with a and r; a decided status never regresses", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskActionProposal pending = AskFixtures.Proposal("aap_1", "ath_1", "dispatch", AskProposalStatusEnum.Pending);
                AskMessage proposal = AskFixtures.Message("amg_4", "ath_1", 4, AskMessageRoleEnum.Assistant, AskMessageKindEnum.ActionProposal, "");
                proposal.ProposalId = "aap_1";
                proposal.Proposal = pending;
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), proposal);
                fx.Pending["ath_1"] = new List<AskActionProposal> { pending };
                fx.Decisions(pending);
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Proposals.ContainsKey("aap_1")), "loaded");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Count == 1), "pending proposal in the approvals queue");
                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Transcript), "Esc moves to the transcript");
                    AssertEqual("amg_4", screen.Transcript.SelectedKey, "newest pending card focused");
                    host.Press("x");
                    TuiCase.Contains(host.Screen(), "\"missions\": [", "arguments expanded");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_1/proposals/aap_1/approve") == 1), "approve call");
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Proposals["aap_1"].Status == AskProposalStatusEnum.Executed), "executed");
                    AssertTrue(host.WaitForText("Ran "), "outcome shown");
                    AssertEqual(0, host.Tui.Context.Approvals.Count, "left the queue");
                    AskActionProposal stale = AskFixtures.Proposal("aap_1", "ath_1", "dispatch", AskProposalStatusEnum.Pending);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_1", Proposal = stale }));
                    host.Pump();
                    AssertEqual(AskProposalStatusEnum.Executed, ask.Conversation.Proposals["aap_1"].Status, "stale Pending ignored");
                    AskActionProposal staleApproved = AskFixtures.Proposal("aap_1", "ath_1", "dispatch", AskProposalStatusEnum.Approved);
                    AssertEqual(AskProposalStatusEnum.Executed, AskConversation.MergeProposal(ask.Conversation.Proposals["aap_1"], staleApproved).Status, "stale Approved ignored");

                    AskActionProposal second = AskFixtures.Proposal("aap_2", "ath_1", "cancel_voyage", AskProposalStatusEnum.Pending);
                    fx.Decisions(second);
                    AskMessage secondMessage = AskFixtures.Message("amg_5", "ath_1", 5, AskMessageRoleEnum.Assistant, AskMessageKindEnum.ActionProposal, "");
                    secondMessage.ProposalId = "aap_2";
                    secondMessage.Proposal = second;
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.message", new AskMessageEvent { ThreadId = "ath_1", Message = secondMessage }));
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_1", Proposal = second }));
                    host.Pump();
                    screen.Transcript.SelectNewestPending();
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Proposals["aap_2"].Status == AskProposalStatusEnum.Rejected), "rejected");
                    AssertTrue(host.WaitForText("Rejected. Nothing was run."), "reject outcome");
                    foreach (AskProposalStatusEnum s in new[] { AskProposalStatusEnum.Approved, AskProposalStatusEnum.Expired, AskProposalStatusEnum.Failed })
                    {
                        AskActionProposal p = AskFixtures.Proposal("aap_s", "ath_1", "dispatch", s);
                        p.ErrorText = s == AskProposalStatusEnum.Failed ? "Vessel not found" : null;
                        string card = String.Join("\n", AskCardRenderer.ConfirmCard(p, false, false, false, host.Tui.Context.Theme.Current, host.Tui.Context.Loc, DateTime.UtcNow, 90).Select(l => l.ToPlainString()));
                        string expected = s == AskProposalStatusEnum.Approved ? "Approved. Running now..." : s == AskProposalStatusEnum.Expired ? "Expired without running." : "Vessel not found";
                        TuiCase.Contains(card, expected, s + " outcome");
                    }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "proposal_other_thread", "A proposal in a background thread joins the approvals queue with an actionable toast", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes")).AddThread(AskFixtures.Thread("ath_2", "Greeting rollout"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/missions", fx.Stub))
                {
                    host.PumpUntil(() => host.Tui.Ask.Threads.Count == 2);
                    int arrived = 0;
                    host.Tui.Context.Approvals.Arrived += (s, e) => arrived++;
                    AskActionProposal p = AskFixtures.Proposal("aap_9", "ath_2", "dispatch", AskProposalStatusEnum.Pending);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_2", Proposal = p }));
                    host.Pump();
                    AssertEqual(1, host.Tui.Context.Approvals.Count, "queued");
                    AssertEqual(1, arrived, "attention raised once");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Approval needed in Greeting rollout: dispatch", "toast text");
                    TuiCase.Contains(frame, "[!1 approvals]", "header count");
                    host.Press("ctrl+o");
                    AssertEqual("/ask/ath_2", host.Tui.Context.Router.Current!.FullPath, "toast opens the thread");
                    AskActionProposal done = AskFixtures.Proposal("aap_9", "ath_2", "dispatch", AskProposalStatusEnum.Expired);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_2", Proposal = done }));
                    host.Pump();
                    AssertEqual(0, host.Tui.Context.Approvals.Count, "expired leaves the queue");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "work_cards", "Live work cards update from ask.work with progress, counts, rows, and drill-down", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskTrackedWork work = new AskTrackedWork();
                work.Id = "atw_1";
                work.ThreadId = "ath_1";
                work.EntityType = AskTrackedEntityTypeEnum.Voyage;
                work.EntityId = "vyg_1";
                work.Title = "Fix tables";
                work.Status = "InProgress";
                AskMessage result = AskFixtures.Message("amg_5", "ath_1", 5, AskMessageRoleEnum.System, AskMessageKindEnum.ActionResult, "Dispatched.");
                result.TrackedWorkId = "atw_1";
                AskMessage milestone = AskFixtures.Message("amg_6", "ath_1", 6, AskMessageRoleEnum.System, AskMessageKindEnum.WorkUpdate, "Work started.");
                milestone.TrackedWorkId = "atw_1";
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), result, milestone);
                fx.Work["ath_1"] = new List<AskTrackedWork> { work };
                fx.Stub.On("GET", "/api/v1/ask/threads/ath_1/work/atw_1", body => StubHttpHandler.Response(System.Net.HttpStatusCode.OK, Armada.Client.ArmadaJson.Serialize(AskFixtures.VoyageSnapshot("atw_1", "ath_1", "InProgress", true, "InProgress", "Pending"))));
                using (TuiTestHost host = TuiCase.SignedIn(140, 60, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Snapshots.ContainsKey("atw_1")), "snapshot fetched for the card");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Voyage  Fix tables", "card heading");
                    TuiCase.Contains(frame, "0 of 2 finished", "progress");
                    TuiCase.Contains(frame, "Work in this conversation (1 active of 1, w)", "work strip");
                    TuiCase.Contains(frame, "[Show live card] Enter", "milestone links to the card");
                    AskWorkSnapshot next = AskFixtures.VoyageSnapshot("atw_1", "ath_1", "InProgress", true, "Landed", "Failed");
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.work", new AskWorkEvent { ThreadId = "ath_1", TrackedWorkId = "atw_1", Snapshot = next }));
                    host.Pump();
                    frame = host.Screen();
                    TuiCase.Contains(frame, "2 of 2 finished, 1 failed", "progress update");
                    TuiCase.Contains(frame, "Landed 1", "counts by status");
                    TuiCase.Contains(frame, "Tests failed", "failure reason");
                    TuiCase.Contains(frame, "Landing: Landed", "landing outcome");
                    TuiCase.Contains(frame, "https://example.com/pr/1", "pull request");
                    AskWorkSnapshot done = AskFixtures.VoyageSnapshot("atw_1", "ath_1", "Complete", false, "Landed", "Landed");
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.work", new AskWorkEvent { ThreadId = "ath_1", TrackedWorkId = "atw_1", Snapshot = done }));
                    host.Pump();
                    AssertTrue(host.WaitForText("1 finished"), "strip shows finished");
                    AssertFalse(AskThreadListLogic.IsWorking(ask.Threads.First(), ask.Activity), "thread no longer working");
                    host.Press("esc");
                    host.Press("w");
                    AssertEqual("amg_5", screen.Transcript.SelectedKey, "w jumps to the hosting card");
                    host.Press("down");
                    AssertEqual(0, screen.Transcript.SelectedRow, "row focus");
                    host.Tui.Context.External.UrlOpener = u => true;
                    host.Press("o");
                    AssertEqual("https://example.com/pr/1", host.Tui.Context.External.LastUrl, "o opens the PR");
                    host.Press("enter");
                    AssertEqual("/missions/msn_1", host.Tui.Context.Router.Current!.FullPath, "Enter opens the mission");
                    AssertEqual("/fleet-actions/runs/far_1", AskWorkLogic.Route(AskTrackedEntityTypeEnum.FleetActionRun, "far_1"), "run route");
                    AssertEqual("/vessels/import?batch=vib_1", AskWorkLogic.Route(AskTrackedEntityTypeEnum.VesselImportBatch, "vib_1"), "import route");
                    AssertEqual("/jobs", AskWorkLogic.Route(AskTrackedEntityTypeEnum.Job, "job_1"), "job route");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "work_card_kinds", "Fleet action runs, jobs, and import batches render their cards", () =>
            {
                AskWorkSnapshot run = new AskWorkSnapshot();
                run.TrackedWorkId = "atw_2";
                run.EntityType = AskTrackedEntityTypeEnum.FleetActionRun;
                run.EntityId = "far_1";
                run.Title = "Bump deps";
                run.Status = "Running";
                AskWorkTargetSnapshot target = new AskWorkTargetSnapshot();
                target.Id = "fat_1";
                target.VesselId = "vsl_a";
                target.VesselName = "Alpha";
                target.Status = "Succeeded";
                target.MissionId = "msn_7";
                run.Targets.Add(target);
                AskWorkTargetSnapshot failed = new AskWorkTargetSnapshot();
                failed.Id = "fat_2";
                failed.VesselId = "vsl_b";
                failed.VesselName = "Beta";
                failed.Status = "Failed";
                failed.Reason = "exit 2";
                run.Targets.Add(failed);
                AskWorkSnapshot job = new AskWorkSnapshot();
                job.EntityType = AskTrackedEntityTypeEnum.Job;
                job.EntityId = "job_1";
                job.Title = "Evaluate health";
                job.Status = "Running";
                job.TotalCount = 10;
                job.CompletedCount = 4;
                AskWorkSnapshot import = new AskWorkSnapshot();
                import.EntityType = AskTrackedEntityTypeEnum.VesselImportBatch;
                import.EntityId = "vib_1";
                import.Title = "Import repos";
                import.Status = "Failed";
                import.State = AskTrackedWorkStateEnum.Failed;
                import.ErrorText = "Root not allowed";
                Armada.Tui.Theming.ArmadaTheme theme = Armada.Tui.Theming.ThemePalettes.Dark();
                LocalizationService loc = new LocalizationService();
                AskBlock b1 = new AskBlock();
                string r = String.Join("\n", AskCardRenderer.WorkCard(b1, null, run, false, 0, theme, loc, DateTime.UtcNow, 90).Select(l => l.ToPlainString()));
                TuiCase.Contains(r, "Fleet action run  Bump deps", "run heading");
                TuiCase.Contains(r, "2 of 2 finished, 1 failed", "run progress counts terminal targets");
                TuiCase.Contains(r, "Alpha", "target row");
                TuiCase.Contains(r, "exit 2", "target reason");
                AssertEqual(2, b1.RowLines.Count, "target rows selectable");
                AssertEqual("/missions/msn_7", b1.RowRoutes[0], "target row opens its mission");
                string j = String.Join("\n", AskCardRenderer.WorkCard(new AskBlock(), null, job, false, 0, theme, loc, DateTime.UtcNow, 90).Select(l => l.ToPlainString()));
                TuiCase.Contains(j, "Background job  Evaluate health", "job heading");
                TuiCase.Contains(j, "4 of 10 finished", "job counters");
                string i = String.Join("\n", AskCardRenderer.WorkCard(new AskBlock(), null, import, false, 0, theme, loc, DateTime.UtcNow, 90).Select(l => l.ToPlainString()));
                TuiCase.Contains(i, "Vessel import  Import repos", "import heading");
                TuiCase.Contains(i, "Root not allowed", "error text");
                string loading = String.Join("\n", AskCardRenderer.WorkCard(new AskBlock(), null, null, false, 0, theme, loc, DateTime.UtcNow, 90).Select(l => l.ToPlainString()));
                TuiCase.Contains(loading, "Loading live status...", "loading state");
            }));

            cases.Add(TuiCase.Sync(Suite, "quick_action_forms", "Dispatch and Fleet action forms build the MCP arguments; Status runs at once; Import opens the import route", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"));
                fx.Stub.On("POST", "/api/v1/ask/threads/ath_1/actions", body => StubHttpHandler.Response(System.Net.HttpStatusCode.OK, Armada.Client.ArmadaJson.Serialize(AskFixtures.Proposal("aap_q", "ath_1", "dispatch", AskProposalStatusEnum.Executed))));
                using (TuiTestHost host = TuiCase.SignedIn(140, 60, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    host.PumpUntil(() => ask.Conversation.Thread != null);
                    host.Type("/");
                    AssertTrue(screen.Composer.MenuOpen, "slash opens the menu");
                    TuiCase.Contains(host.Screen(), "/fleet-action", "menu lists actions");
                    host.Type("dis").Press("enter");
                    AskDispatchFormView? dispatch = screen.Form as AskDispatchFormView;
                    AssertNotNull(dispatch, "dispatch form open");
                    AssertTrue(host.PumpUntil(() => !dispatch!.Loading), "vessels loaded");
                    TuiCase.Contains(host.Screen(), "Dispatch a voyage", "form heading");
                    dispatch!.SubmitForm();
                    AssertEqual("Choose a vessel.", dispatch.Errors["vessel"], "vessel required");
                    AssertEqual("Add at least one mission.", dispatch.Errors["missions"], "mission required");
                    dispatch.Vessel.SetValue("vsl_a");
                    dispatch.MissionTitles[0].Value = "Fix column widths";
                    dispatch.AddMission();
                    dispatch.MissionDescriptions[1].Value = "no title";
                    dispatch.SubmitForm();
                    AssertEqual("Every mission needs a title.", dispatch.Errors["missions"], "titles required");
                    dispatch.MissionTitles[1].Value = "Add tests";
                    dispatch.VoyageTitle.Value = "";
                    AssertTrue(dispatch.SubmitForm(), "submitted");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_1/actions") == 1), "actions call");
                    string body = fx.Stub.Bodies.Last(b => b.Contains("\"ToolName\":\"dispatch\""));
                    JsonObject args = JsonNode.Parse(body)!["Arguments"]!.AsObject();
                    AssertEqual("Fix column widths", args["title"]!.GetValue<string>(), "voyage title defaults to the first mission");
                    AssertEqual("vsl_a", args["vesselId"]!.GetValue<string>(), "vessel");
                    AssertEqual(2, args["missions"]!.AsArray().Count, "two missions");
                    AssertEqual("Fix column widths", args["missions"]![0]!["description"]!.GetValue<string>(), "description defaults to the title");
                    AssertEqual("no title", args["missions"]![1]!["description"]!.GetValue<string>(), "description kept");
                    AssertFalse(args.ContainsKey("pipelineId"), "no pipeline when vessel default");
                    AssertTrue(host.PumpUntil(() => screen.Form == null), "form closes on success");

                    AskQuickAction fleet = ask.QuickActions.First(a => a.Name == "fleet-action");
                    AskFleetActionFormView form = (AskFleetActionFormView)screen.OpenForm(fleet)!;
                    AssertTrue(host.PumpUntil(() => !form.Loading), "actions loaded");
                    form.ActionPicker.SetValue("fa_1");
                    form.Vessels.SetFilter("alp");
                    form.Vessels.ToggleAllVisible();
                    AssertEqual(1, form.Vessels.Selected.Count, "select visible");
                    form.Vessels.SetFilter("");
                    form.Vessels.Toggle("vsl_b");
                    AskFleetActionDraft draft = form.Draft();
                    JsonObject fargs = AskQuickActions.BuildFleetActionArguments(draft);
                    AssertEqual("{\"actionId\":\"fa_1\",\"vesselIds\":[\"vsl_a\",\"vsl_b\"]}", fargs.ToJsonString(), "run_fleet_action arguments");
                    AssertTrue(form.SubmitForm(), "fleet submitted");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Bodies.Any(b => b.Contains("\"ToolName\":\"run_fleet_action\""))), "fleet action call");
                    host.PumpUntil(() => screen.Form == null);

                    screen.Composer.Choose(ask.QuickActions.First(a => a.Name == "status"));
                    AssertTrue(host.PumpUntil(() => fx.Stub.Bodies.Any(b => b.Contains("\"ToolName\":\"status\"") && b.Contains("\"Arguments\":{}"))), "status runs with no form");
                    AssertNull(screen.Form, "no form for status");
                    screen.Composer.Choose(ask.QuickActions.First(a => a.Name == "import"));
                    AssertEqual("/vessels/import", host.Tui.Context.Router.Current!.Path, "import opens its screen");
                    AskDispatchDraft empty = new AskDispatchDraft();
                    empty.VesselId = "vsl_a";
                    empty.PipelineId = "ppl_1";
                    empty.Title = "Voyage";
                    empty.Missions = new List<AskDispatchMissionDraft> { new AskDispatchMissionDraft("One", "Do one") };
                    AssertEqual("{\"title\":\"Voyage\",\"vesselId\":\"vsl_a\",\"missions\":[{\"title\":\"One\",\"description\":\"Do one\"}],\"pipelineId\":\"ppl_1\"}", AskQuickActions.BuildDispatchArguments(empty).ToJsonString(), "dispatch arguments with pipeline");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "composer", "Optimistic send, newline, history recall, and stop with the local force-end", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    host.PumpUntil(() => ask.Conversation.Thread != null && ask.Captains.Count > 0);
                    ask.ForceEndMs = 200;
                    host.Type("line one").Press("ctrl+j").Type("line two");
                    AssertEqual("line one\nline two", screen.Composer.Text, "Ctrl+J inserts a newline");
                    host.Press("enter");
                    AssertTrue(ask.Conversation.Messages.Any(m => AskConversation.IsLocal(m)), "optimistic message");
                    AssertTrue(ask.Conversation.TurnActive, "turn active");
                    AssertEqual("", screen.Composer.Text, "composer cleared");
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Messages.Any(m => m.Id == "amg_sent")), "confirmed with the server id");
                    AssertTrue(fx.Stub.Bodies.Any(b => b.Contains("line one\\nline two")), "sent text");
                    AssertEqual("atn_1", ask.Conversation.Streaming!.TurnId, "following the turn");
                    host.Press("ctrl+c");
                    AssertTrue(ask.Stopping, "stopping");
                    TuiCase.Contains(host.Screen(), "Stopping...", "stopping shown");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_1/cancel") == 1), "cancel call");
                    AssertTrue(host.PumpUntil(() => !ask.Conversation.TurnActive, 3000), "force-ended locally");
                    host.Press("up");
                    AssertEqual("line one\nline two", screen.Composer.Text, "history recall");
                    host.Press("down");
                    AssertEqual("", screen.Composer.Text, "history forward clears");
                    fx.Stub.On("POST", "/api/v1/ask/threads/ath_1/messages", body => StubHttpHandler.Response(System.Net.HttpStatusCode.InternalServerError, "{\"Message\":\"boom\"}"));
                    host.Type("fails").Press("enter");
                    AssertTrue(host.PumpUntil(() => !ask.Conversation.Messages.Any(m => AskConversation.IsLocal(m))), "optimistic copy dropped on failure");
                    AssertTrue(host.WaitForText("The message could not be sent."), "error dialog");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "new_conversation_send", "Sending from a new conversation creates the thread with the draft captain and follows it", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_new", "New conversation"));
                fx.Threads.Clear();
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    host.PumpUntil(() => ask.Captains.Count > 0 && ask.DraftCaptainId == "cpt_1");
                    TuiCase.Contains(host.Screen(), "Ask claude-1 anything about your fleet", "greeting sub text");
                    host.Type("hello").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/ask/ath_new"), "navigated to the new thread");
                    AssertTrue(fx.Stub.Bodies.Any(b => b.Contains("\"CaptainId\":\"cpt_1\"")), "created with the draft captain");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_new/messages") >= 1 && fx.Stub.Requests.Any(r => r == "POST /api/v1/ask/threads/ath_new/messages")), "message sent to it");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "older_and_reconnect", "Earlier pages load with BeforeSequence; a reconnect refetches the list and the conversation", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), AskFixtures.Message("amg_40", "ath_1", 40, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "newest"));
                fx.HasMore["ath_1"] = true;
                fx.OlderPages["ath_1"] = new List<AskMessage> { AskFixtures.Message("amg_10", "ath_1", 10, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "older question") };
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    host.PumpUntil(() => ask.Conversation.HasMore);
                    TuiCase.Contains(host.Screen(), "[Load earlier messages]", "load earlier row");
                    host.Press("esc").Press("home");
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Messages.Count == 2), "older page merged");
                    AssertTrue(fx.Stub.Bodies.Any(b => b.Contains("\"BeforeSequence\":40")), "before the oldest sequence");
                    AssertEqual("amg_10", ask.Conversation.Messages[0].Id, "sorted by sequence");
                    int lists = fx.Stub.Count("POST /api/v1/ask/threads/enumerate");
                    int details = fx.Stub.Count("GET /api/v1/ask/threads/ath_1");
                    ask.HandleReconnect();
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/enumerate") > lists && fx.Stub.Count("GET /api/v1/ask/threads/ath_1") > details), "refetched after reconnect");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "header_controls", "Auto-approve warns and shows the banner; captain picker; no-MCP note; summarize", () =>
            {
                AskFixtures fx = new AskFixtures();
                Captain codex = new Captain();
                codex.Id = "cpt_2";
                codex.Name = "codex-1";
                codex.Runtime = AgentRuntimeEnum.Codex;
                fx.Captains.Add(codex);
                fx.Stub.On("GET", "/api/v1/captains/cpt_2/tools", body => StubHttpHandler.Response(System.Net.HttpStatusCode.OK, "{\"CaptainId\":\"cpt_2\",\"Runtime\":\"Codex\",\"ArmadaToolCount\":0}"));
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    host.PumpUntil(() => ask.Conversation.Thread != null && ask.Captains.Count == 2);
                    TuiCase.Contains(host.Screen(), "Captain: claude-1 (ClaudeCode) [c]", "captain in header");
                    TuiCase.NotContains(host.Screen(), "not connected to Armada over MCP", "Claude Code gets MCP from the server");
                    host.Press("esc").Press("ctrl+y");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "warning confirm");
                    TuiCase.Contains(host.Screen(), "without a confirm card", "dashboard warning text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Thread!.AutoApprove), "auto-approve on");
                    AssertTrue(host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.StartsWith("Auto-approve is on. Actions the captain proposes", StringComparison.Ordinal) && t.Severity == NotificationSeverityEnum.Warning), "warning toast");
                    host.Tui.Context.Notifications.DismissToasts();
                    AssertTrue(host.WaitForText("Auto-approve is on: actions the captain proposes run immediately."), "banner");
                    host.Press("ctrl+y");
                    AssertTrue(host.PumpUntil(() => !ask.Conversation.Thread!.AutoApprove), "off without a confirm");
                    host.Press("c");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "captain picker");
                    TuiCase.Contains(host.Screen(), "No captain (quick actions only)", "no-captain option");
                    host.Press("esc");
                    ask.SetCaptain("cpt_2");
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Thread!.CaptainId == "cpt_2"), "captain changed");
                    AssertTrue(host.WaitForText("not connected to Armada over MCP"), "no-MCP note for Codex");
                    TuiCase.Contains(host.Screen(), "INSTRUCTIONS_FOR_CODEX.md", "per-runtime instructions");
                    ask.SetCaptain("");
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Thread!.CaptainId == null), "captain cleared");
                    AssertTrue(fx.Stub.Bodies.Any(b => b.Contains("\"CaptainId\":null")), "explicit null clears the captain");
                    host.Press("s");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_1/summarize") == 1), "summarize call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Summarizing \"TUIKit fixes\""))), "summarize toast");
                    host.Press("e").Press("ctrl+u").Type("Renamed in header").Press("enter");
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Thread!.Title == "Renamed in header"), "header rename");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "dock_and_ask_about_this", "The Ask dock tails the conversation and sends; Alt+A pre-fills context", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "what is running?"));
                fx.Stub.Json("GET", "/api/v1/vessels/vsl_a", "{\"Id\":\"vsl_a\",\"Name\":\"Alpha\"}");
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    host.PumpUntil(() => ask.Conversation.Messages.Count == 1 && ask.Captains.Count > 0);
                    host.Tui.Context.Navigate("/missions");
                    host.Press("ctrl+j");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Ask Armada: TUIKit fixes", "dock heading");
                    TuiCase.Contains(frame, "what is running?", "conversation tail");
                    host.Tui.Shell.FocusPane("dock");
                    host.Type("from the dock").Press("enter");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Bodies.Any(b => b.Contains("from the dock"))), "dock sends to the open thread");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("ask.chunk", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"delta\":\"Two voyages.\"}"));
                    AssertTrue(host.WaitForText("Two voyages."), "dock streams the reply");
                    host.Tui.Context.Navigate("/vessels/vsl_a");
                    host.Tui.Shell.FocusPane("main");
                    host.Press("alt+a");
                    AssertEqual("/ask", host.Tui.Context.Router.Current!.FullPath, "opens Ask");
                    AssertTrue(host.PumpUntil(() => ((AskScreen)host.Tui.Shell.Screen!).Composer.Text == "On vessel Alpha (vsl_a): "), "context pre-filled with the vessel name");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "narrow_layout", "Below 100 columns the list is an overlay toggled with Ctrl+T; Ask does not auto-refresh", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskThread older = AskFixtures.Thread("ath_2", "Other");
                older.LastMessageUtc = DateTime.UtcNow.AddHours(-2);
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes")).AddThread(older);
                using (TuiTestHost host = TuiCase.SignedIn(96, 30, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    host.PumpUntil(() => ask.Threads.Count == 2 && ask.Conversation.Thread != null);
                    AssertEqual(0, host.Tui.Context.Refresh.IntervalSeconds, "Ask is live, auto-refresh off by default");
                    TuiCase.NotContains(host.Screen(), "CONVERSATIONS", "list folded away");
                    host.Press("ctrl+t");
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    AssertTrue(screen.ListOverlayOpen, "overlay open");
                    TuiCase.Contains(host.Screen(), "CONVERSATIONS", "overlay shows the list");
                    host.Press("down");
                    string focusInfo = (host.Tui.Shell.FocusedLeaf()?.GetType().Name ?? "none") + " cursor=" + screen.ThreadList.Cursor;
                    host.Press("enter");
                    AssertEqual("/ask/ath_2", host.Tui.Context.Router.Current!.FullPath, "opened from the overlay (" + focusInfo + ")");
                    host.Tui.Context.Navigate("/missions");
                    AssertEqual(15, host.Tui.Context.Refresh.IntervalSeconds, "other screens keep the 15 s default");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI Ask Armada", cases: cases);
        }
    }
}
