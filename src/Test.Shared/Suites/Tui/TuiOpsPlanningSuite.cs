namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text.Json;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Planning (W3.4) against a stubbed client: pre-filled start, the transcript with live events (messages, tools,
    /// thinking, status, summary, deletion), send, summarize, dispatch, the sessions table, End Session, and Delete
    /// All with its typed confirmation.
    /// </summary>
    public sealed class TuiOpsPlanningSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.Planning";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "start_prefilled", "A backlog handoff pre-fills the start form; Ctrl+S starts and opens the session with the initial message", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/planning?from=objective&captainId=cpt_1&vesselId=vsl_demo&title=Plan%20it&initialPrompt=Do%20the%20thing&objectiveId=obj_1", stub))
                {
                    AssertTrue(host.WaitForText("Prefilled from a backlog item."), "banner\n" + host.Screen());
                    AssertTrue(host.WaitForText("claude-1 (ClaudeCode) - Idle"), "captain pre-filled\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Plan it", "title pre-filled");
                    TuiCase.Contains(frame, "Planning sessions reserve the selected captain and dock", "reservation notice");
                    TuiCase.Contains(frame, "Planning runs this captain", "runtime note");
                    TuiCase.Contains(frame, "Vessel Readiness", "readiness panel");
                    PlanningScreen screen = (PlanningScreen)host.Tui.Shell.Screen!;
                    AssertEqual("obj_1", screen.ObjectiveId, "objective carried");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/planning-sessions") == 1), "create call");
                    PlanningSessionCreateRequest body = stub.LastBody<PlanningSessionCreateRequest>("POST", "/api/v1/planning-sessions");
                    AssertEqual("cpt_1", body.CaptainId, "captain");
                    AssertEqual("vsl_demo", body.VesselId, "vessel");
                    AssertEqual("obj_1", body.ObjectiveId, "objective");
                    AssertEqual("Plan it", body.Title, "title");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/planning/ps_new"), "opened the new session: " + host.Tui.Context.Router.Current!.FullPath);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Planning session started."))), "toast");
                    PlanningScreen opened = (PlanningScreen)host.Tui.Shell.Screen!;
                    AssertEqual("Do the thing", opened.Composer.Text, "initial message in the composer");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "transcript_live", "The transcript renders replies, tools, thinking, metrics; live events update it; send, summarize, and dispatch", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/planning/ps_1", stub))
                {
                    AssertTrue(host.WaitForText("Here is the plan"), "assistant reply\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Chat with claude-1 against DemoRepo", "intro");
                    TuiCase.Contains(frame, "Runtime: ClaudeCode", "runtime");
                    TuiCase.Contains(frame, "Messages: 2", "message count");
                    TuiCase.Contains(frame, "time to first token", "metrics line");
                    PlanningScreen screen = (PlanningScreen)host.Tui.Shell.Screen!;
                    AssertEqual("pm_2", screen.SelectedMessageId, "latest reply selected for dispatch");
                    AssertEqual("Here is the plan", screen.DispatchDescription.Text, "draft seeded from the reply");

                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.changed", "{\"session\":" + Session("ps_1", "Responding") + "}"));
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.message.created", "{\"sessionId\":\"ps_1\",\"message\":{\"Id\":\"pm_3\",\"PlanningSessionId\":\"ps_1\",\"Role\":\"Assistant\",\"Sequence\":3,\"Content\":\"\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\"}}"));
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.tool", "{\"sessionId\":\"ps_1\",\"messageId\":\"pm_3\",\"phase\":\"started\",\"id\":\"call_1\",\"name\":\"read_file\",\"arguments\":{\"path\":\"README.md\"}}"));
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.thinking", "{\"sessionId\":\"ps_1\",\"messageId\":\"pm_3\",\"delta\":\"considering options\"}"));
                    AssertTrue(host.WaitForText("[..] read_file"), "running tool chip\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Ctrl+C Stop", "stop hint while responding");
                    TuiCase.Contains(host.Screen(), "[+] Thinking (t)", "collapsed thinking");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.tool", "{\"sessionId\":\"ps_1\",\"messageId\":\"pm_3\",\"phase\":\"completed\",\"id\":\"call_1\",\"name\":\"read_file\",\"ok\":true,\"elapsedMs\":120,\"result\":\"ok\"}"));
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.message.updated", "{\"sessionId\":\"ps_1\",\"message\":{\"Id\":\"pm_3\",\"PlanningSessionId\":\"ps_1\",\"Role\":\"Assistant\",\"Sequence\":3,\"Content\":\"Second plan\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\"}}"));
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.changed", "{\"session\":" + Session("ps_1", "Active") + "}"));
                    AssertTrue(host.WaitForText("[ok] read_file  120ms"), "completed tool chip\n" + host.Screen());
                    AssertTrue(host.WaitForText("Second plan"), "updated reply");
                    host.PumpUntil(() => screen.SelectedMessageId == "pm_2", 200);
                    AssertEqual("pm_2", screen.SelectedMessageId, "selection kept while it still exists");

                    host.Type("Refine step two").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/planning-sessions/ps_1/messages") == 1), "send call");
                    StubRequest send = stub.Last("POST", "/api/v1/planning-sessions/ps_1/messages");
                    AssertEqual("Refine step two", send.BodyAs<PlanningSessionMessageRequest>().Content, "send content");
                    AssertEqual(JsonTokenType.True, send.BodyProperty("Stream")?.ValueToken, "send asks to stream (sent, not the model default)");

                    host.Press("esc");
                    host.Press("u");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/planning-sessions/ps_1/summarize") == 1), "summarize call");
                    AssertTrue(host.PumpUntil(() => screen.DispatchTitle.Value == "Summarized title"), "draft from summary");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.summary.created", "{\"sessionId\":\"ps_1\",\"messageId\":\"pm_2\",\"draft\":{\"title\":\"Event title\",\"description\":\"Event body\"}}"));
                    AssertTrue(host.PumpUntil(() => screen.DispatchDescription.Text == "Event body"), "summary event fills the draft");
                    host.Press("D");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/planning-sessions/ps_1/dispatch") == 1), "dispatch call");
                    PlanningSessionDispatchRequest dispatch = stub.LastBody<PlanningSessionDispatchRequest>("POST", "/api/v1/planning-sessions/ps_1/dispatch");
                    AssertEqual("Event body", dispatch.Description, "dispatch description from the summary event");
                    AssertEqual("pm_2", dispatch.MessageId, "dispatch message id");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/voyages/vyg_9"), "opened the voyage");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "open_in_dispatch_and_delete_event", "Open in Dispatch releases the session and hands off; a deleted event closes the session", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/planning/ps_1", stub))
                {
                    AssertTrue(host.WaitForText("Here is the plan"), "reply");
                    host.Press("esc");
                    host.Press("o");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/planning-sessions/ps_1/stop") == 1), "released");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/dispatch"), "dispatch opened");
                    AssertTrue(host.WaitForText("Prefilled from a planning session."), "dispatch banner\n" + host.Screen());
                    DispatchScreen dispatch = (DispatchScreen)((Armada.Tui.Screens.HubScreen)host.Tui.Shell.Screen!).Content;
                    AssertEqual("Here is the plan", dispatch.Description.Text, "prompt handed off");
                    AssertTrue(host.PumpUntil(() => dispatch.Vessel.Value == "vsl_demo"), "vessel handed off");

                    host.Tui.Context.Navigate("/planning/ps_1");
                    AssertTrue(host.WaitForText("Here is the plan"), "back on the session");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("planning-session.deleted", "{\"sessionId\":\"ps_1\"}"));
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/planning"), "left the deleted session");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Planning session deleted."))), "deleted toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sessions_table", "Recent Sessions lists sessions; End Session and Delete All confirm first", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/planning", stub))
                {
                    PlanningScreen screen = (PlanningScreen)host.Tui.Shell.Screen!;
                    host.Press("alt+2");
                    AssertTrue(host.WaitForText("Architecture plan"), "session row\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "DemoRepo", "vessel column");
                    host.Press("E");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "end confirm");
                    TuiCase.Contains(host.Screen(), "release the reserved captain and dock", "end text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/planning-sessions/ps_1/stop") == 1), "stop call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Planning session is ending."))), "ending toast");
                    host.Press("X");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "delete all confirm");
                    TuiCase.Contains(host.Screen(), "Delete all 1 planning session(s)", "delete all text");
                    host.Type("delete").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/planning-sessions/ps_1") == 1), "delete call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("All planning sessions deleted."))), "delete all toast");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI planning", cases: cases);
        }

        internal static string Session(string id, string status)
        {
            return "{\"Id\":\"" + id + "\",\"CaptainId\":\"cpt_1\",\"VesselId\":\"vsl_demo\",\"Title\":\"Architecture plan\",\"Status\":\"" + status + "\",\"BranchName\":\"armada/plan\",\"PipelineId\":\"ppl_r\",\"SelectedPlaybooks\":[],\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:30:00Z\"}";
        }

        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\",\"FleetId\":\"flt_1\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"},{\"Id\":\"cpt_2\",\"Name\":\"busy-1\",\"Runtime\":\"Codex\",\"State\":\"Working\"}],\"TotalRecords\":2}");
            stub.Json("GET", "/api/v1/fleets", "{\"Success\":true,\"Objects\":[{\"Id\":\"flt_1\",\"Name\":\"Main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/pipelines", "{\"Success\":true,\"Objects\":[{\"Id\":\"ppl_r\",\"Name\":\"Reviewed\",\"Stages\":[{\"Id\":\"s1\",\"Order\":1,\"PersonaName\":\"Worker\"},{\"Id\":\"s2\",\"Order\":2,\"PersonaName\":\"Judge\"}]}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/personas", "{\"Success\":true,\"Objects\":[{\"Id\":\"prs_w\",\"Name\":\"Worker\",\"PromptTemplateName\":\"w\",\"DefaultCaptainId\":\"cpt_1\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/playbooks", "{\"Success\":true,\"Objects\":[{\"Id\":\"pbk_1\",\"FileName\":\"style.md\",\"Content\":\"x\",\"Active\":true}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/vessels/vsl_demo/readiness", "{\"VesselId\":\"vsl_demo\",\"HasWorkingDirectory\":true,\"HasRepositoryContext\":true,\"AvailableCheckTypes\":[\"Build\"],\"Issues\":[],\"ErrorCount\":0,\"WarningCount\":0}");
            stub.Json("GET", "/api/v1/planning-sessions", "[" + Session("ps_1", "Active") + "]");
            string detail = "{\"Session\":" + Session("ps_1", "Active") + ",\"Captain\":{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Planning\"},\"Vessel\":{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\"},\"Messages\":[" +
                "{\"Id\":\"pm_1\",\"PlanningSessionId\":\"ps_1\",\"Role\":\"User\",\"Sequence\":1,\"Content\":\"Plan the work\",\"CreatedUtc\":\"2026-10-04T09:10:00Z\"}," +
                "{\"Id\":\"pm_2\",\"PlanningSessionId\":\"ps_1\",\"Role\":\"Assistant\",\"Sequence\":2,\"Content\":\"Here is the plan\",\"CreatedUtc\":\"2026-10-04T09:11:00Z\",\"Metrics\":{\"TimeToFirstTokenMs\":800,\"TotalMs\":2500,\"CompletionTokens\":40,\"TokensPerSecond\":16.5}}]}";
            stub.Json("GET", "/api/v1/planning-sessions/ps_1", detail);
            stub.Json("POST", "/api/v1/planning-sessions/ps_1/messages", detail);
            stub.Json("POST", "/api/v1/planning-sessions/ps_1/stop", "{\"Session\":" + Session("ps_1", "Stopping") + ",\"Messages\":[]}");
            stub.Json("POST", "/api/v1/planning-sessions/ps_1/stop-turn", detail);
            stub.Json("POST", "/api/v1/planning-sessions/ps_1/summarize", "{\"SessionId\":\"ps_1\",\"MessageId\":\"pm_2\",\"Title\":\"Summarized title\",\"Description\":\"Summarized body\",\"Method\":\"Captain\"}");
            stub.Json("POST", "/api/v1/planning-sessions/ps_1/dispatch", "{\"Id\":\"vyg_9\",\"Title\":\"Event title\"}");
            stub.On("DELETE", "/api/v1/planning-sessions/ps_1", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("POST", "/api/v1/planning-sessions", "{\"Session\":" + Session("ps_new", "Active") + ",\"Messages\":[]}");
            stub.Json("GET", "/api/v1/planning-sessions/ps_new", "{\"Session\":" + Session("ps_new", "Active") + ",\"Messages\":[]}");
            stub.Json("GET", "/api/v1/voyages/vyg_9", "{\"Id\":\"vyg_9\",\"Title\":\"Event title\"}");
            return stub;
        }
    }
}
