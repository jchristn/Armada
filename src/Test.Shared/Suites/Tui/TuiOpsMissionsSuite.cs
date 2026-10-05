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
    /// Missions list (W3.8) against a stubbed client: server filters, column filters, row menu, create, transition,
    /// retry landing, cancel, purge, and bulk delete with the dashboard's calls and text.
    /// </summary>
    public sealed class TuiOpsMissionsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.Missions";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_filters", "Lists missions with names, filters by status on the server and by title locally", () =>
            {
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    AssertTrue(host.WaitForText("Fix tables"), "rows\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Missions]", "hub tab");
                    TuiCase.Contains(frame, "Individual work units assigned to captains", "subtitle");
                    TuiCase.Contains(frame, "DemoRepo", "vessel name");
                    TuiCase.Contains(frame, "claude-1", "captain name");
                    TuiCase.Contains(frame, "x LandingFailed", "status badge");
                    MissionsScreen screen = Screen(host);
                    screen.StatusFilter.Choose(screen.StatusFilter.Options.First(o => o.Value == "Review"));
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.Contains("/api/v1/missions/summaries") && r.Contains("status=Review"))), "server status filter: " + String.Join(" | ", stub.Requests));
                    host.Press("/");
                    host.Press("tab").Press("tab");
                    host.Type("Add");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("Fix tables")), "local title filter hides others");
                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Grid), "Esc returns to the grid");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_actions", "Row menu actions call the dashboard endpoints with its confirmations", () =>
            {
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    AssertTrue(host.WaitForText("Fix tables"), "rows");
                    MissionsScreen screen = Screen(host);
                    Select(host, screen, "msn_l");
                    host.Press(".");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "menu");
                    string menu = host.Screen();
                    TuiCase.Contains(menu, "Retry Landing", "retry landing offered for LandingFailed");
                    TuiCase.Contains(menu, "Purge (permanent)", "purge");
                    host.Press("esc");
                    host.Press("L");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/missions/msn_l/retry-landing") == 1), "retry landing");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Landing succeeded for \"Ship it\""))), "landing toast");
                    host.Press("t");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "transition dialog");
                    TuiCase.Contains(host.Screen(), "Transition Mission Status", "title");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("Pending")), "status picker");
                    host.Type("Complete").Press("enter");
                    Settle(host);
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/missions/msn_l/status") == 1), "transition call\n" + host.Screen());
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"Status\":\"Complete\"")), "transition body");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Mission \"Ship it\" moved to Complete."))), "transition toast");
                    host.Press("x");
                    TuiCase.Contains(host.Screen(), "Use Purge to permanently remove it.", "cancel text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/missions/msn_l") == 1), "cancel = delete");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "This will permanently remove it", "purge text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/missions/msn_l/purge") == 1), "purge");
                    host.Press("d");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("+added line")), "diff viewer");
                    host.Press("esc");
                    host.Press("l");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("captain output line")), "log viewer\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Total lines: 42", "total lines");
                    host.Press("+");
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.Contains("/log") && r.Contains("lines=500"))), "line count change");
                    host.Press("esc");
                    host.Press("enter");
                    AssertEqual("/missions/msn_l", host.Tui.Context.Router.Current!.FullPath, "Enter opens the mission");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_and_bulk", "Create Mission validates and posts; Delete Selected purges each marked mission", () =>
            {
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    AssertTrue(host.WaitForText("Fix tables"), "rows");
                    MissionsScreen screen = Screen(host);
                    host.Press("n");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "create dialog");
                    TuiCase.Contains(host.Screen(), "Create Mission", "title");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Title is required."), "title validation");
                    host.Type("Write docs");
                    host.Press("tab").Type("Explain the TUI");
                    host.Press("tab").Press("enter").Type("Demo").Press("enter");
                    Settle(host);
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/missions") == 1), "create call");
                    string body = stub.Bodies.Last(b => b.Contains("Write docs"));
                    AssertTrue(body.Contains("\"VesselId\":\"vsl_demo\"") && body.Contains("\"Priority\":100") && body.Contains("Explain the TUI"), "create body: " + body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Mission \"Write docs\" created."))), "create toast");
                    host.Press("home").Press("space").Press("down").Press("space");
                    AssertEqual(2, screen.Grid.Marked.Count, "two marked");
                    TuiCase.Contains(host.Screen(), "Delete Selected (2)", "bulk button");
                    host.Press("D");
                    TuiCase.Contains(host.Screen(), "Delete 2 selected mission(s)? This cannot be undone.", "bulk confirm");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/missions/msn_f/purge") == 1 && stub.Count("DELETE /api/v1/missions/msn_a/purge") == 1), "bulk purge");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Purged 2 missions."))), "bulk toast");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI missions list", cases: cases);
        }

        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            string missions = "[" +
                "{\"Id\":\"msn_f\",\"Title\":\"Fix tables\",\"Status\":\"InProgress\",\"Priority\":100,\"VesselId\":\"vsl_demo\",\"CaptainId\":\"cpt_1\",\"VoyageId\":\"vyg_1\",\"BranchName\":\"armada/fix-tables\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}," +
                "{\"Id\":\"msn_a\",\"Title\":\"Add tests\",\"Status\":\"Review\",\"Priority\":50,\"VesselId\":\"vsl_demo\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}," +
                "{\"Id\":\"msn_l\",\"Title\":\"Ship it\",\"Status\":\"LandingFailed\",\"Priority\":10,\"VesselId\":\"vsl_demo\",\"CreatedUtc\":\"2026-10-04T08:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T08:00:00Z\"}]";
            stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":3,\"Objects\":" + missions + "}");
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/voyages", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/users", "{\"Success\":true,\"Objects\":[{\"Id\":\"usr_admin\",\"TenantId\":\"ten_default\",\"Email\":\"admin@armada\"}],\"TotalRecords\":1}");
            stub.Json("POST", "/api/v1/missions/msn_l/retry-landing", "{\"Success\":true}");
            stub.Json("PUT", "/api/v1/missions/msn_l/status", "{\"Id\":\"msn_l\",\"Title\":\"Ship it\",\"Status\":\"Complete\"}");
            stub.On("DELETE", "/api/v1/missions/msn_l", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/missions/msn_l/purge", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/missions/msn_f/purge", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/missions/msn_a/purge", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("GET", "/api/v1/missions/msn_l/diff", "{\"Diff\":\"diff --git a/x b/x\\n+added line\\n\"}");
            stub.Json("GET", "/api/v1/missions/msn_l/log", "{\"Log\":\"captain output line\\nsecond\",\"Lines\":2,\"TotalLines\":42}");
            stub.Json("POST", "/api/v1/missions", "{\"Id\":\"msn_new\",\"Title\":\"Write docs\",\"Status\":\"Assigned\"}");
            return stub;
        }

        private static TuiTestHost Host(out StubHttpHandler stub)
        {
            stub = Stub();
            return TuiCase.SignedIn(150, 45, "/missions", stub);
        }

        internal static void Settle(TuiTestHost host)
        {
            host.PumpUntil(() => false, 150);
        }

        private static MissionsScreen Screen(TuiTestHost host)
        {
            HubScreen hub = (HubScreen)host.Tui.Shell.Screen!;
            return (MissionsScreen)hub.Content;
        }

        private static void Select(TuiTestHost host, MissionsScreen screen, string id)
        {
            host.Press("home");
            for (int i = 0; i < 10 && screen.Grid.Current?.Id != id; i++) host.Press("down");
            AssertEqual(id, screen.Grid.Current?.Id, "selected " + id);
        }
    }
}
