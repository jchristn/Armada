namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Backlog (W3.6) against a stubbed client: KPIs, group pills, filters, rank moves, row actions, Import GitHub,
    /// and the standalone <c>/objectives</c> route.
    /// </summary>
    public sealed class TuiOpsBacklogSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.Backlog";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "groups_and_filters", "KPIs, group pills with counts, search, and Clear Filters", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(160, 45, "/dispatch?tab=backlog", Stub()))
                {
                    AssertTrue(host.WaitForText("Fix login"), "rows\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Backlog]", "hub tab");
                    TuiCase.Contains(frame, "Backlog Items: 3", "KPI total");
                    TuiCase.Contains(frame, "Ready For Planning: 1", "KPI planning");
                    TuiCase.Contains(frame, "Blocked: 1", "KPI blocked");
                    TuiCase.Contains(frame, "[1 All 3]", "active pill");
                    TuiCase.Contains(frame, "2 Inbox 1", "inbox pill count");
                    TuiCase.Contains(frame, "Showing 3 of 3 backlog items.", "meta");
                    TuiCase.Contains(frame, "DemoRepo", "vessel scope name");
                    BacklogScreen screen = Screen(host);
                    AssertEqual("obj_a", screen.Grid.Current!.Id, "rank order first");
                    host.Press("3");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("Fix login") && host.Screen().Contains("Plan billing")), "planning group only");
                    TuiCase.Contains(host.Screen(), "Showing 1 of 3 backlog items.", "filtered meta");
                    host.Press("X");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("Fix login")), "cleared");
                    host.Press("/");
                    host.Type("billing");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("Fix login")), "search filters locally");
                    host.Press("esc");
                    AssertEqual(1, screen.Grid.Rows.Count, "one match");
                    AssertTrue(screen.HasActiveFilters(), "active");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_actions", "Move rank, duplicate, delete, open, and JSON with the dashboard's calls", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 45, "/dispatch?tab=backlog", stub))
                {
                    AssertTrue(host.WaitForText("Fix login"), "rows");
                    host.Type("\u001b[1;3B");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/backlog/reorder") == 1), "reorder call");
                    string body = stub.Bodies.Last(b => b.Contains("ObjectiveId"));
                    AssertTrue(body.Contains("\"ObjectiveId\":\"obj_a\"") && body.Contains("\"Rank\":2") && body.Contains("\"ObjectiveId\":\"obj_b\"") && body.Contains("\"Rank\":1"), "swap ranks: " + body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Backlog ranking updated."))), "rank toast");
                    host.Press(".");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "row menu");
                    string menu = host.Screen();
                    TuiCase.Contains(menu, "Move Up", "move up");
                    TuiCase.Contains(menu, "Duplicate", "duplicate");
                    host.Press("esc");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "but leaves linked missions", "delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/backlog/obj_a") == 1), "delete call");
                    host.Press("j");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "json");
                    host.Press("esc");
                    host.Press("u");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/backlog") >= 1 && stub.Bodies.Any(b => b.Contains("Fix login (Copy)"))), "duplicate call");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("Fix login (Copy)") && b.Contains("\"BacklogState\":\"Inbox\"") && b.Contains("\"Status\":\"Draft\"")), "duplicate payload");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/backlog/obj_new"), "navigates to the copy");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "import_and_new", "Import GitHub validates and posts; New opens the create route; /objectives is standalone", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 45, "/objectives", stub))
                {
                    AssertTrue(host.WaitForText("Fix login"), "standalone rows");
                    AssertTrue(host.Tui.Shell.Screen is BacklogScreen, "standalone screen");
                    host.Press("i");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "import dialog");
                    TuiCase.Contains(host.Screen(), "Import GitHub Backlog Item", "title");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Select a vessel and enter a valid GitHub issue or pull-request number."), "validation");
                    host.Press("enter").Type("Demo").Press("enter");
                    TuiTestHostSettle(host);
                    host.Press("tab").Press("tab").Type("42");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/objectives/import/github") == 1), "import call\n" + host.Screen());
                    string body = stub.Bodies.Last(b => b.Contains("\"Number\""));
                    AssertTrue(body.Contains("\"Number\":42") && body.Contains("\"VesselId\":\"vsl_demo\"") && body.Contains("\"SourceType\":\"Issue\""), "import body: " + body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/backlog/obj_imp"), "opens imported item");
                    host.Tui.Context.Router.Navigate("/objectives?vesselId=vsl_demo");
                    AssertTrue(host.WaitForText("Fix login"), "rows again");
                    AssertTrue(host.PumpUntil(() => ((BacklogScreen)host.Tui.Shell.Screen!).VesselFilter.Value == "vsl_demo"), "vessel filter from the query");
                    host.Press("n");
                    AssertEqual("/backlog/new?vesselId=vsl_demo", host.Tui.Context.Router.Current!.FullPath, "new carries the vessel filter");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI backlog", cases: cases);
        }

        internal static void TuiTestHostSettle(TuiTestHost host)
        {
            host.PumpUntil(() => false, 150);
        }

        internal static string Objective(string id, string title, int rank, string status, string state, string blockedBy, string extra = "")
        {
            return "{\"Id\":\"" + id + "\",\"Title\":\"" + title + "\",\"Rank\":" + rank + ",\"Status\":\"" + status + "\",\"Kind\":\"Feature\",\"Priority\":\"P1\",\"Effort\":\"M\",\"BacklogState\":\"" + state
                + "\",\"Owner\":\"joel\",\"VesselIds\":[\"vsl_demo\"],\"FleetIds\":[\"flt_1\"],\"BlockedByObjectiveIds\":[" + blockedBy + "],\"Tags\":[\"area:auth\"],\"AcceptanceCriteria\":[\"Users can log in\"],"
                + "\"NonGoals\":[],\"RolloutConstraints\":[],\"EvidenceLinks\":[],\"SuggestedPlaybooks\":[],\"RefinementSessionIds\":[],\"PlanningSessionIds\":[],\"VoyageIds\":[],\"MissionIds\":[],\"CheckRunIds\":[],"
                + "\"ReleaseIds\":[],\"DeploymentIds\":[],\"IncidentIds\":[],\"CreatedUtc\":\"2026-10-01T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"" + extra + "}";
        }

        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            string items = "[" + Objective("obj_a", "Fix login", 1, "Draft", "Inbox", "") + ","
                + Objective("obj_b", "Plan billing", 2, "Scoped", "ReadyForPlanning", "") + ","
                + Objective("obj_c", "Ship exports", 3, "Blocked", "ReadyForDispatch", "\"obj_a\"") + "]";
            stub.Json("GET", "/api/v1/backlog", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":9999,\"TotalPages\":1,\"TotalRecords\":3,\"Objects\":" + items + "}");
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\",\"FleetId\":\"flt_1\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/fleets", "{\"Success\":true,\"Objects\":[{\"Id\":\"flt_1\",\"Name\":\"Core\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/users", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/pipelines", "{\"Success\":true,\"Objects\":[{\"Id\":\"ppl_1\",\"Name\":\"Reviewed\"}],\"TotalRecords\":1}");
            stub.Json("POST", "/api/v1/backlog/reorder", "[" + Objective("obj_a", "Fix login", 2, "Draft", "Inbox", "") + "," + Objective("obj_b", "Plan billing", 1, "Scoped", "ReadyForPlanning", "") + "]");
            stub.On("DELETE", "/api/v1/backlog/obj_a", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("POST", "/api/v1/backlog", Objective("obj_new", "Fix login (Copy)", 4, "Draft", "Inbox", ""));
            stub.Json("POST", "/api/v1/objectives/import/github", Objective("obj_imp", "Imported issue", 5, "Draft", "Inbox", ""));
            return stub;
        }

        private static BacklogScreen Screen(TuiTestHost host)
        {
            HubScreen hub = (HubScreen)host.Tui.Shell.Screen!;
            return (BacklogScreen)hub.Content;
        }
    }
}
