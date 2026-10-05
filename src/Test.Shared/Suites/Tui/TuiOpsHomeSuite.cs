namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Home (W3.1) and Needs You (W3.2) against a stubbed client: KPIs, alert banners, health tiles, the history chart,
    /// Voyage Progress, Recent Missions with filters and row actions, Recent Signals, socket-driven reloads, and the
    /// inbox list, KPIs, and recent alerts.
    /// </summary>
    public sealed class TuiOpsHomeSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.Home";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "sections", "Home shows KPIs, alerts, health tiles, the chart, voyages, missions, and signals", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(160, 90, "/", Stub()))
                {
                    AssertTrue(host.WaitForText("Active fleet action runs"), "KPIs\n" + host.Screen());
                    AssertTrue(host.WaitForText("Fix tables"), "recent missions\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "System Status", "title");
                    TuiCase.Contains(frame, "1 captain(s) stalled -- recovery attempts exhausted.", "stalled alert");
                    TuiCase.Contains(frame, "2 mission(s) failed.", "failed alert");
                    TuiCase.Contains(frame, "1 idle  1 working  1 stalled", "captain detail");
                    TuiCase.Contains(frame, "3 deferred for memory pressure", "memory pressure");
                    TuiCase.Contains(frame, "4 running", "running runs");
                    TuiCase.Contains(frame, "Vessels failing health", "health tile");
                    TuiCase.Contains(frame, "Import repositories", "admin shortcut");
                    TuiCase.Contains(frame, "Mission History", "chart heading");
                    TuiCase.Contains(frame, "5 Complete", "history stats");
                    TuiCase.Contains(frame, "Release train", "voyage progress");
                    TuiCase.Contains(frame, "1/2 done", "voyage missions");
                    TuiCase.Contains(frame, "[Nudge] hello captain", "signals");
                    TuiCase.Contains(frame, "DemoRepo", "vessel name");
                    HomeScreen home = (HomeScreen)host.Tui.Shell.Screen!;
                    AssertTrue(home.Chart.Buckets.Count == 2, "chart buckets");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "missions_and_actions", "Recent Missions filters locally; restart, delete, and navigation keys", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 90, "/", stub))
                {
                    AssertTrue(host.WaitForText("Fix tables"), "missions");
                    HomeScreen home = (HomeScreen)host.Tui.Shell.Screen!;
                    home.StatusFilter.Choose(home.StatusFilter.Options.First(o => o.Value == "Failed"));
                    host.Pump();
                    AssertEqual(1, home.Missions.Rows.Count, "status filter");
                    home.Scope.Focus(home.Missions);
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/missions/msn_x/restart") == 1), "restart failed mission");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete this mission?", "delete confirm");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/missions/msn_x") == 1), "delete");
                    int loads = home.Loads;
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("mission.changed", "{\"id\":\"msn_x\",\"title\":\"Broken\",\"status\":\"Pending\"}"));
                    AssertTrue(host.PumpUntil(() => home.Loads > loads, 6000), "socket message reloads");
                    host.Press("i");
                    AssertEqual("/inbox", host.Tui.Context.Router.Current!.FullPath, "Needs You key");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "inbox", "Needs You lists items with KPIs, opens items, and marks alerts read", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/inbox", "[" +
                    "{\"Kind\":\"landing_failed\",\"Severity\":\"Critical\",\"Title\":\"Landing failed: Ship it\",\"Detail\":\"Conflict\",\"EntityType\":\"mission\",\"EntityId\":\"msn_l\",\"Href\":\"/missions/msn_l\"}," +
                    "{\"Kind\":\"review\",\"Severity\":\"Warning\",\"Title\":\"Review: Fix tables\",\"Detail\":\"Waiting 5m\",\"EntityType\":\"mission\",\"EntityId\":\"msn_f\",\"Href\":\"/missions/msn_f\"}]");
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/inbox", stub))
                {
                    AssertTrue(host.WaitForText("Landing failed: Ship it"), "items\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Everything across the fleet that is waiting on a decision or intervention from you.", "subtitle");
                    TuiCase.Contains(frame, "Total 2", "total");
                    TuiCase.Contains(frame, "Critical 1", "critical");
                    TuiCase.Contains(frame, "x Critical", "severity");
                    host.Tui.Context.Notifications.PushEntityChange("Mission", "msn_f", "Fix tables", "Failed");
                    AssertTrue(host.WaitForText("Recent alerts"), "recent alerts\n" + host.Screen());
                    host.Press("m");
                    AssertEqual(0, host.Tui.Context.Notifications.UnreadCount, "mark all read");
                    host.Press("enter");
                    AssertEqual("/missions/msn_l", host.Tui.Context.Router.Current!.FullPath, "Enter opens the item");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "inbox_empty", "Needs You shows the caught-up state", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/inbox", "[]");
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/inbox", stub))
                {
                    AssertTrue(host.WaitForText("You are all caught up."), "empty\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Nothing needs your attention right now.", "empty detail");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI home and needs you", cases: cases);
        }

        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\",\"FleetId\":\"flt_1\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/status", "{\"TotalCaptains\":3,\"IdleCaptains\":1,\"WorkingCaptains\":1,\"StalledCaptains\":1,\"ActiveVoyages\":1,\"MemoryPressureDeferrals\":3," +
                "\"MissionsByStatus\":{\"Failed\":2,\"Complete\":5,\"Pending\":1}," +
                "\"Voyages\":[{\"Voyage\":{\"Id\":\"vyg_1\",\"Title\":\"Release train\",\"Status\":\"InProgress\"},\"TotalMissions\":2,\"CompletedMissions\":1,\"FailedMissions\":0,\"VesselIds\":[\"vsl_demo\"]}]," +
                "\"RecentSignals\":[{\"Id\":\"sig_1\",\"Type\":\"Nudge\",\"Payload\":\"hello captain\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[" +
                "{\"Id\":\"msn_f\",\"Title\":\"Fix tables\",\"Status\":\"InProgress\",\"Priority\":100,\"VesselId\":\"vsl_demo\",\"CaptainId\":\"cpt_1\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}," +
                "{\"Id\":\"msn_x\",\"Title\":\"Broken\",\"Status\":\"Failed\",\"Priority\":100,\"VesselId\":\"vsl_demo\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}],\"TotalRecords\":2}");
            stub.On("POST", "/api/v1/fleet-action-runs/enumerate", body => StubHttpHandler.Response(HttpStatusCode.OK, JsonHelper.Deserialize<FleetActionRunEnumerateQuery>(body).Status == FleetActionRunStatusEnum.Running
                ? "{\"Success\":true,\"Objects\":[],\"TotalRecords\":4}"
                : "{\"Success\":true,\"Objects\":[],\"TotalRecords\":2}"));
            stub.Json("GET", "/api/v1/vessel-health/summary", "{\"TotalVessels\":3,\"Pass\":1,\"Warn\":1,\"Fail\":1,\"NotEvaluated\":0,\"OutdatedMajorVessels\":0,\"HighOrCriticalVulnerabilityVessels\":1}");
            stub.Json("GET", "/api/v1/missions/history", "{\"TotalCount\":7,\"CompleteCount\":5,\"FailedCount\":2,\"OtherCount\":0,\"BucketMinutes\":60,\"Buckets\":[" +
                "{\"StartUtc\":\"2026-10-04T08:00:00Z\",\"CompleteCount\":3,\"FailedCount\":1,\"OtherCount\":0}," +
                "{\"StartUtc\":\"2026-10-04T09:00:00Z\",\"CompleteCount\":2,\"FailedCount\":1,\"OtherCount\":0}]}");
            stub.Json("GET", "/api/v1/fleets", "{\"Success\":true,\"Objects\":[{\"Id\":\"flt_1\",\"Name\":\"Default\"}],\"TotalRecords\":1}");
            stub.Json("POST", "/api/v1/missions/msn_x/restart", "{\"Id\":\"msn_x\",\"Title\":\"Broken\",\"Status\":\"Pending\"}");
            stub.On("DELETE", "/api/v1/missions/msn_x", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            return stub;
        }
    }
}
