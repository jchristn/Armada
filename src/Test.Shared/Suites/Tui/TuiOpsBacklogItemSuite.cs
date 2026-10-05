namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text.Json;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Backlog item (W3.6) against a stubbed client: the form and save, header handoffs, links, create mode, and the
    /// refinement panel with live <c>objective-refinement-session.*</c> events.
    /// </summary>
    public sealed class TuiOpsBacklogItemSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.BacklogItem";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "form_save", "Loads every field, edits, and saves the full payload", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/backlog/obj_a", stub))
                {
                    BacklogItemScreen screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.Item != null && host.Screen().Contains("Fix login")), "loaded\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Backlog Item]", "form tab");
                    TuiCase.Contains(frame, "Transcript", "transcript tab");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("DemoRepo (Core)")), "vessel select label\n" + host.Screen());
                    AssertFalse(screen.Form.IsDirty, "clean after load");
                    host.Type(" v2");
                    AssertTrue(screen.Form.IsDirty, "dirty after edit");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/backlog/obj_a") == 1), "update call");
                    ObjectiveUpsertRequest body = stub.LastBody<ObjectiveUpsertRequest>("PUT", "/api/v1/backlog/obj_a");
                    AssertEqual("Fix login v2", body.Title, "edited title");
                    AssertEqual("vsl_demo", String.Join(",", body.VesselIds ?? new List<string>()), "vessel scope kept");
                    AssertEqual("flt_1", String.Join(",", body.FleetIds ?? new List<string>()), "fleet scope kept");
                    AssertEqual("area:auth", String.Join(",", body.Tags ?? new List<string>()), "tags");
                    AssertEqual("Users can log in", String.Join(",", body.AcceptanceCriteria ?? new List<string>()), "acceptance criteria");
                    AssertEqual("msn_1", String.Join(",", body.MissionIds ?? new List<string>()), "links carried");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Backlog item \"Fix login\" saved."))), "save toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "handoffs_and_links", "Start Planning, Open In Dispatch, Draft Release, History, links, overview", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/backlog/obj_a", stub))
                {
                    BacklogItemScreen screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.Item != null), "loaded");
                    screen.SelectPanel("overview");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "GitHub Source", "github panel");
                    TuiCase.Contains(frame, "acme/demo#42", "source id");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("Primary vessel DemoRepo is linked")), "vessel note");
                    host.Press("P");
                    string planning = host.Tui.Context.Router.Current!.FullPath;
                    AssertTrue(planning.StartsWith("/planning?") && planning.Contains("from=objective") && planning.Contains("objectiveId=obj_a") && planning.Contains("vesselId=vsl_demo") && planning.Contains("fleetId=flt_1"), "planning handoff: " + planning);
                    AssertTrue(Uri.UnescapeDataString(planning).Contains("Backlog Item: Fix login") && Uri.UnescapeDataString(planning).Contains("title=Fix login Planning"), "planning prompt: " + planning);
                    host.Tui.Context.Router.Back();
                    screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.Item != null), "reloaded");
                    screen.SelectPanel("overview");
                    host.Press("D");
                    string dispatch = Uri.UnescapeDataString(host.Tui.Context.Router.Current!.FullPath);
                    AssertTrue(dispatch.Contains("from=objective") && dispatch.Contains("pipelineName=Reviewed") && dispatch.Contains("Implement backlog item: Fix login") && dispatch.Contains("voyageTitle=Fix login"), "dispatch handoff: " + dispatch);
                    host.Tui.Context.Router.Back();
                    screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.Item != null), "reloaded again");
                    screen.SelectPanel("overview");
                    host.Press("R");
                    string release = Uri.UnescapeDataString(host.Tui.Context.Router.Current!.FullPath);
                    AssertTrue(release.StartsWith("/releases/new?") && release.Contains("objectiveIds=obj_a") && release.Contains("Backlog-derived release notes for Fix login"), "release handoff: " + release);
                    host.Tui.Context.Router.Back();
                    screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.Item != null), "reloaded third");
                    screen.SelectPanel("links");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("msn_1")), "links\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Linked Vessels", "vessel link");
                    host.Press("down").Press("down").Press("down");
                    host.Press("enter");
                    AssertEqual("/missions/msn_1", host.Tui.Context.Router.Current!.FullPath, "Enter opens the linked mission");
                    host.Tui.Context.Router.Back();
                    screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.Item != null), "reloaded fourth");
                    screen.SelectPanel("overview");
                    host.Press("H");
                    AssertTrue(host.Tui.Context.Router.Current!.FullPath.Contains("objectiveId=obj_a"), "history carries the id: " + host.Tui.Context.Router.Current!.FullPath);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "refinement", "Sessions, transcript selection, send, summarize, apply, start, and live events", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/backlog/obj_a", stub))
                {
                    BacklogItemScreen screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.Detail != null), "session detail loaded");
                    screen.SelectPanel("transcript");
                    AssertTrue(host.WaitForText("Here is a sharper scope."), "transcript\n" + host.Screen());
                    AssertEqual("orm_2", screen.Transcript.SelectedId, "latest assistant message selected");
                    host.Press("up");
                    AssertEqual("orm_1", screen.Transcript.SelectedId, "Up selects the previous message");
                    host.Press("down");
                    host.Press("m");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/objective-refinement-sessions/ors_1/summarize") == 1), "summarize");
                    AssertEqual("orm_2", stub.LastBody<ObjectiveRefinementSummaryRequest>("POST", "/api/v1/objective-refinement-sessions/ors_1/summarize").MessageId, "summarize message id");
                    AssertTrue(host.PumpUntil(() => screen.SummaryDraft != null), "summary draft");
                    screen.SelectPanel("summary");
                    AssertTrue(host.WaitForText("Narrow scope"), "summary shown\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "heuristic", "method");
                    screen.SelectPanel("transcript");
                    host.Press("A");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/objective-refinement-sessions/ors_1/apply") == 1), "apply");
                    StubRequest apply = stub.Last("POST", "/api/v1/objective-refinement-sessions/ors_1/apply");
                    AssertEqual(JsonTokenType.True, apply.BodyProperty("PromoteBacklogState")?.ValueToken, "apply sends PromoteBacklogState true (not the model default)");
                    AssertEqual(JsonTokenType.True, apply.BodyProperty("MarkMessageSelected")?.ValueToken, "apply sends MarkMessageSelected true (not the model default)");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Applied refinement summary back to the backlog item."))), "apply toast");
                    screen.Composer.Text = "Add rollout notes";
                    AssertTrue(screen.RunAction("send-refinement"), "send action");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/objective-refinement-sessions/ors_1/messages") == 1), "send");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("objective-refinement-session.message.created",
                        "{\"sessionId\":\"ors_1\",\"objectiveId\":\"obj_a\",\"message\":{\"id\":\"orm_9\",\"objectiveRefinementSessionId\":\"ors_1\",\"objectiveId\":\"obj_a\",\"role\":\"Assistant\",\"sequence\":9,\"content\":\"Live streamed reply\",\"createdUtc\":\"2026-10-04T10:00:00Z\",\"lastUpdateUtc\":\"2026-10-04T10:00:00Z\"}}"));
                    AssertTrue(host.WaitForText("Live streamed reply"), "live message\n" + host.Screen());
                    host.Press("x");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/objective-refinement-sessions/ors_1/stop") == 1), "stop");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("objective-refinement-session.deleted", "{\"sessionId\":\"ors_1\",\"objectiveId\":\"obj_a\"}"));
                    AssertTrue(host.PumpUntil(() => screen.Detail == null), "deleted event clears the transcript");
                    AssertTrue(host.WaitForText("No active refinement transcript selected."), "empty state");
                    screen.SelectPanel("refinement");
                    screen.RefinementCaptain.Choose(screen.RefinementCaptain.Options.First(o => o.Value == "cpt_1"));
                    AssertTrue(host.WaitForText("Selected captain claude-1 is currently Idle."), "captain helper\n" + host.Screen());
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("captain.changed", "{\"id\":\"cpt_1\",\"name\":\"claude-1\",\"state\":\"Working\"}"));
                    AssertTrue(host.WaitForText("claude-1 is currently Working"), "captain.changed updates state");
                    AssertTrue(screen.RunAction("start-refinement"), "start action");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/backlog/obj_a/refinement-sessions") == 1), "start call");
                    ObjectiveRefinementSessionCreateRequest start = stub.LastBody<ObjectiveRefinementSessionCreateRequest>("POST", "/api/v1/backlog/obj_a/refinement-sessions");
                    AssertEqual("cpt_1", start.CaptainId, "start captain");
                    AssertEqual("vsl_demo", start.VesselId, "start vessel");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Refinement session started with claude-1."))), "start toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_and_delete", "Create mode pre-fills the vessel and fleet; Delete confirms and returns", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/backlog/new?vesselId=vsl_demo", stub))
                {
                    BacklogItemScreen screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.WaitForText("Create Backlog Item"), "create heading");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("DemoRepo (Core)")), "vessel prefilled\n" + host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Backlog item title is required."), "title required");
                    host.Press("enter");
                    host.Type("Brand new item");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/backlog") == 1), "create call");
                    ObjectiveUpsertRequest body = stub.LastBody<ObjectiveUpsertRequest>("POST", "/api/v1/backlog");
                    AssertEqual("Brand new item", body.Title, "created title");
                    AssertEqual("vsl_demo", String.Join(",", body.VesselIds ?? new List<string>()), "vessel scope from query");
                    AssertEqual("flt_1", String.Join(",", body.FleetIds ?? new List<string>()), "fleet scope from query");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/backlog/obj_new"), "opens the created item");
                    screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    host.Tui.Context.Router.Navigate("/objectives/obj_a");
                    screen = (BacklogItemScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.Item != null), "objectives route loads");
                    screen.SelectPanel("overview");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "snapshot history only.", "delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/backlog/obj_a") == 1), "delete");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath.StartsWith("/dispatch")), "back to the backlog");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI backlog item", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiOpsBacklogSuite.Stub();
            string item = TuiOpsBacklogSuite.Objective("obj_a", "Fix login", 1, "Draft", "Inbox", "",
                ",\"MissionIds\":[\"msn_1\"],\"SuggestedPipelineId\":\"ppl_1\",\"Description\":\"Login fails on retry\",\"SourceProvider\":\"GitHub\",\"SourceType\":\"Issue\",\"SourceId\":\"acme/demo#42\",\"SourceUrl\":\"https://github.com/acme/demo/issues/42\",\"RefinementSessionIds\":[\"ors_1\"]");
            item = item.Replace("\"MissionIds\":[],", "").Replace("\"RefinementSessionIds\":[],", "");
            stub.Json("GET", "/api/v1/backlog/obj_a", item);
            stub.Json("PUT", "/api/v1/backlog/obj_a", item);
            string session = "{\"Id\":\"ors_1\",\"ObjectiveId\":\"obj_a\",\"CaptainId\":\"cpt_1\",\"VesselId\":\"vsl_demo\",\"Title\":\"Refine login\",\"Status\":\"Active\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:30:00Z\"}";
            stub.Json("GET", "/api/v1/backlog/obj_a/refinement-sessions", "[" + session + "]");
            string messages = "[{\"Id\":\"orm_1\",\"ObjectiveRefinementSessionId\":\"ors_1\",\"ObjectiveId\":\"obj_a\",\"Role\":\"User\",\"Sequence\":1,\"Content\":\"Sharpen this\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"},"
                + "{\"Id\":\"orm_2\",\"ObjectiveRefinementSessionId\":\"ors_1\",\"ObjectiveId\":\"obj_a\",\"Role\":\"Assistant\",\"Sequence\":2,\"Content\":\"Here is a sharper scope.\",\"CreatedUtc\":\"2026-10-04T09:01:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:01:00Z\"}]";
            string detail = "{\"Session\":" + session + ",\"Messages\":" + messages + ",\"Captain\":{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"},\"Vessel\":{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\"}}";
            stub.Json("GET", "/api/v1/objective-refinement-sessions/ors_1", detail);
            stub.Json("POST", "/api/v1/objective-refinement-sessions/ors_1/messages", detail);
            stub.Json("POST", "/api/v1/objective-refinement-sessions/ors_1/stop", detail);
            string summary = "{\"SessionId\":\"ors_1\",\"MessageId\":\"orm_2\",\"Summary\":\"Narrow scope to retries\",\"AcceptanceCriteria\":[\"Retry works\"],\"NonGoals\":[],\"RolloutConstraints\":[],\"SuggestedPipelineId\":\"ppl_1\",\"Method\":\"heuristic\"}";
            stub.Json("POST", "/api/v1/objective-refinement-sessions/ors_1/summarize", summary);
            stub.Json("POST", "/api/v1/objective-refinement-sessions/ors_1/apply", "{\"Summary\":" + summary + ",\"Objective\":" + item + "}");
            stub.Json("POST", "/api/v1/backlog/obj_a/refinement-sessions", detail.Replace("ors_1", "ors_2"));
            stub.Json("GET", "/api/v1/objective-refinement-sessions/ors_2", detail.Replace("ors_1", "ors_2"));
            stub.Json("GET", "/api/v1/backlog/obj_new", TuiOpsBacklogSuite.Objective("obj_new", "Brand new item", 4, "Draft", "Inbox", ""));
            stub.Json("GET", "/api/v1/backlog/obj_new/refinement-sessions", "[]");
            return stub;
        }
    }
}
