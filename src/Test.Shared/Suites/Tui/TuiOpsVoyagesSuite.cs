namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Voyages list, Voyage detail, and Create Voyage (W3.9) against a stubbed client.
    /// </summary>
    public sealed class TuiOpsVoyagesSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.Voyages";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list", "Lists voyages, filters locally, views status, cancels, purges, and bulk cancels", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 45, "/missions?tab=voyages", stub))
                {
                    AssertTrue(host.WaitForText("Greeting rollout"), "rows\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Voyages]", "tab");
                    TuiCase.Contains(frame, "Batches of related missions dispatched together", "subtitle");
                    TuiCase.Contains(frame, "PullRequest", "landing mode");
                    VoyagesScreen screen = (VoyagesScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("/").Press("tab").Type("Greet");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("Billing cleanup")), "title filter");
                    host.Press("ctrl+u");
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("Billing cleanup")), "filter cleared");
                    host.Press("esc");
                    host.Press("home");
                    AssertEqual("vyg_g", screen.Grid.Current!.Id, "first row");
                    host.Press("u");
                    AssertTrue(host.WaitForText("Voyage Status"), "status viewer");
                    TuiCase.Contains(host.Screen(), "\"Status\": \"Complete\"", "status json (voyage with its missions)");
                    host.Press("esc");
                    host.Press("x");
                    TuiCase.Contains(host.Screen(), "Cancel voyage \"Greeting rollout\"? All pending missions will be", "cancel text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/voyages/vyg_g") == 1), "cancel call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Warning && t.Text.Contains("Voyage \"Greeting rollout\" cancelled."))), "cancel toast");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Purge voyage \"Greeting rollout\"?", "purge text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/voyages/vyg_g/purge") == 1), "purge call");
                    host.Press("space").Press("down").Press("space");
                    TuiCase.Contains(host.Screen(), "Cancel Selected (2)", "bulk button");
                    host.Press("X");
                    TuiCase.Contains(host.Screen(), "Cancel 2 selected voyage(s)?", "bulk text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/voyages/vyg_b") == 1), "bulk cancel");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Cancelled 2 voyages."))), "bulk toast");
                    host.Press("n");
                    AssertEqual("/voyages/create", host.Tui.Context.Router.Current!.Path, "new voyage");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail", "Voyage detail shows configuration, progress, assignments, missions; retries failed; deletes", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/voyages/vyg_b", stub))
                {
                    AssertTrue(host.WaitForText("Billing cleanup"), "heading\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "1/3 complete, 1 failed", "progress");
                    TuiCase.Contains(frame, "Retry Failed (1)", "retry button");
                    TuiCase.Contains(frame, "Captain Assignments", "assignments");
                    TuiCase.Contains(frame, "fallback: Premium", "fallback tier");
                    TuiCase.NotContains(frame, "Cancel Voyage x", "finished voyage cannot be cancelled");
                    TuiCase.Contains(frame, "[ Delete Del ]", "delete when finished");
                    host.Press("F");
                    TuiCase.Contains(host.Screen(), "Retry 1 failed mission(s)?", "retry text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/missions") == 1), "retry creates a mission");
                    StubRequest retry = stub.Last("POST", "/api/v1/missions");
                    Armada.Core.Models.Mission retried = retry.BodyAs<Armada.Core.Models.Mission>();
                    AssertEqual("Broken task", retried.Title, "retry title: " + retry.Body);
                    AssertEqual("vyg_b", retried.VoyageId, "retry voyage: " + retry.Body);
                    AssertEqual("vsl_demo", retried.VesselId, "retry vessel: " + retry.Body);
                    host.Press("]");
                    AssertTrue(host.WaitForText("Broken task"), "missions table");
                    TuiCase.Contains(host.Screen(), "claude-1", "captain name");
                    host.Press("down");
                    host.Press("d");
                    AssertTrue(host.WaitForText("+change"), "mission diff\n" + host.Screen());
                    host.Press("esc");
                    host.Press("j");
                    AssertTrue(host.WaitForText("\"CaptainOverridesJson\""), "voyage json");
                    host.Press("esc");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Permanently delete this voyage and all its missions?", "delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/voyages/vyg_b/purge") == 1), "delete purges");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions"), "back to voyages");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create", "Create Voyage validates like the dashboard and posts the voyage with its missions", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/voyages/create", stub))
                {
                    AssertTrue(host.WaitForText("Voyage Details"), "form\n" + host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Voyage title is required."), "title required");
                    host.Press("enter");
                    host.PumpUntil(() => !host.App.Modals.IsActive);
                    host.Type("Docs sweep");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Please select a vessel."), "vessel required");
                    host.Press("enter");
                    host.PumpUntil(() => !host.App.Modals.IsActive);
                    VoyageCreateScreen screen = (VoyageCreateScreen)host.Tui.Shell.Screen!;
                    host.PumpUntil(() => screen.Vessel.Options.Count > 0);
                    screen.Vessel.Choose(screen.Vessel.Options[0]);
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("At least one mission with a title is required."), "mission required");
                    host.Press("enter");
                    host.PumpUntil(() => !host.App.Modals.IsActive);
                    screen.MissionTitles[0].Value = "Update README";
                    host.Press("ctrl+n");
                    AssertEqual(2, screen.MissionTitles.Count, "added a mission");
                    host.Type("Fix links");
                    TuiCase.Contains(host.Screen(), "Missions (2)", "count");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/voyages") == 1), "create call");
                    StubRequest create = stub.Last("POST", "/api/v1/voyages");
                    Armada.Client.Models.VoyageCreateRequest voyage = create.BodyAs<Armada.Client.Models.VoyageCreateRequest>();
                    AssertEqual("Docs sweep", voyage.Title, "title: " + create.Body);
                    AssertEqual("Update README|Fix links", String.Join("|", voyage.Missions.Select(m => m.Title)), "missions: " + create.Body);
                    AssertEqual("vsl_demo", voyage.VesselId, "vessel: " + create.Body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/voyages/vyg_new"), "opens the voyage");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI voyages", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/voyages", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                "{\"Id\":\"vyg_g\",\"Title\":\"Greeting rollout\",\"Status\":\"InProgress\",\"AutoPush\":true,\"AutoCreatePullRequests\":false,\"LandingMode\":\"PullRequest\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}," +
                "{\"Id\":\"vyg_b\",\"Title\":\"Billing cleanup\",\"Status\":\"Complete\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/voyages/vyg_g", "{\"Voyage\":{\"Id\":\"vyg_g\",\"Title\":\"Greeting rollout\",\"Status\":\"InProgress\"},\"Missions\":[{\"Id\":\"msn_g1\",\"Title\":\"Greeting\",\"Status\":\"Complete\"}]}");
            stub.On("DELETE", "/api/v1/voyages/vyg_g", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/voyages/vyg_b", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/voyages/vyg_g/purge", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/voyages/vyg_b/purge", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("GET", "/api/v1/voyages/vyg_b", "{\"Voyage\":{\"Id\":\"vyg_b\",\"Title\":\"Billing cleanup\",\"Status\":\"Complete\",\"Description\":\"Tidy billing\",\"AutoPush\":true,\"CaptainOverridesJson\":\"[{\\\"Persona\\\":\\\"Worker\\\",\\\"CaptainId\\\":\\\"cpt_1\\\",\\\"FallbackTier\\\":\\\"Premium\\\"}]\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"},\"Missions\":[" +
                "{\"Id\":\"msn_1\",\"Title\":\"Done task\",\"Status\":\"Complete\",\"VesselId\":\"vsl_demo\",\"CaptainId\":\"cpt_1\",\"VoyageId\":\"vyg_b\",\"Priority\":100,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}," +
                "{\"Id\":\"msn_2\",\"Title\":\"Broken task\",\"Description\":\"Fix it\",\"Status\":\"Failed\",\"VesselId\":\"vsl_demo\",\"VoyageId\":\"vyg_b\",\"Priority\":50,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}," +
                "{\"Id\":\"msn_3\",\"Title\":\"Running task\",\"Status\":\"InProgress\",\"VesselId\":\"vsl_demo\",\"VoyageId\":\"vyg_b\",\"Priority\":100,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/missions/msn_2/diff", "{\"Diff\":\"diff --git a/y b/y\\n+change\\n\"}");
            stub.Json("POST", "/api/v1/missions", "{\"Id\":\"msn_9\",\"Title\":\"Broken task\",\"Status\":\"Pending\"}");
            stub.Json("POST", "/api/v1/voyages", "{\"Id\":\"vyg_new\",\"Title\":\"Docs sweep\",\"Status\":\"Open\"}");
            stub.Json("GET", "/api/v1/voyages/vyg_new", "{\"Voyage\":{\"Id\":\"vyg_new\",\"Title\":\"Docs sweep\",\"Status\":\"Open\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"},\"Missions\":[]}");
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/pipelines", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/playbooks", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/users", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            return stub;
        }
    }
}
