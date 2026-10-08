namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Fleets, the fleet page, Docks, and the dock page (W4.5, W4.8) against a stubbed client.
    /// </summary>
    public sealed class TuiBuildFleetsDocksSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Build.FleetsDocks";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "fleets_list", "Fleets lists counts, filters, creates, edits with the full record, duplicates, and deletes", () =>
            {
                StubHttpHandler stub = BuildStubs.Server();
                stub.Json("POST", "/api/v1/fleets", "{\"Id\":\"flt_new\",\"Name\":\"Data\",\"Active\":true}");
                stub.Json("PUT", "/api/v1/fleets/flt_web", "{\"Id\":\"flt_web\",\"Name\":\"Web2\",\"Active\":true}");
                stub.On("DELETE", "/api/v1/fleets/flt_ops", b => BuildStubs.NoContent());
                stub.Json("GET", "/api/v1/fleets/flt_new", "{\"Fleet\":{\"Id\":\"flt_new\",\"Name\":\"Data\"},\"Vessels\":[]}");
                using (TuiTestHost host = TuiCase.SignedIn(160, 45, "/vessels?tab=fleets", stub))
                {
                    AssertTrue(host.WaitForText("Frontend repos"), "rows\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Fleets]", "tab");
                    TuiCase.Contains(frame, "Fleets are groups of vessels", "subtitle");
                    FleetsScreen screen = (FleetsScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    AssertEqual(2, screen.VesselCount("flt_web"), "vessel count");
                    host.Press("/").Type("Ops");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("Frontend repos")), "name filter");
                    host.Press("ctrl+u").Press("esc");
                    AssertTrue(host.WaitForText("Frontend repos"), "filter cleared");
                    host.Press("home");
                    AssertEqual("flt_ops", screen.Grid.Current!.Id, "sorted by name: Ops first");
                    host.Press("n");
                    AssertTrue(host.WaitForText("Create Fleet"), "create form");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Name is required."), "name required");
                    host.Type("Data");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/fleets") == 1), "create call");
                    AssertEqual("Data", stub.LastBody<Armada.Core.Models.Fleet>("POST", "/api/v1/fleets").Name, "create body");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Fleet \"Data\" created."))), "create toast");
                    host.Press("down");
                    AssertEqual("flt_web", screen.Grid.Current!.Id, "web row");
                    host.Press("e");
                    AssertTrue(host.WaitForText("Edit Fleet"), "edit form");
                    host.Type("2");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/fleets/flt_web") == 1), "update call");
                    StubRequest update = stub.Last("PUT", "/api/v1/fleets/flt_web");
                    Armada.Core.Models.Fleet updated = update.BodyAs<Armada.Core.Models.Fleet>();
                    AssertEqual("Web2", updated.Name, "edited name: " + update.Body);
                    AssertEqual("ppl_std", updated.DefaultPipelineId, "full record keeps the pipeline: " + update.Body);
                    AssertEqual("Frontend repos", updated.Description, "full record keeps the description: " + update.Body);
                    host.Press("home");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete fleet \"Ops\"? This cannot be undone.", "delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/fleets/flt_ops") == 1), "delete call");
                    host.Press("u");
                    AssertTrue(host.PumpUntil(() => stub.BodiesFor<Armada.Core.Models.Fleet>("POST", "/api/v1/fleets").Any(f => f.Name == "Ops (Copy)")), "duplicate body");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/fleets/flt_new"), "duplicate opens the copy");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "fleet_duplicate_name", "Create Fleet shows the server's 409 DuplicateEntity message and stays open", () =>
            {
                StubHttpHandler stub = BuildStubs.Server();
                stub.On("POST", "/api/v1/fleets", b => StubHttpHandler.Response(System.Net.HttpStatusCode.Conflict,
                    "{\"Error\":\"Conflict\",\"Message\":\"A fleet named Web already exists\",\"Data\":{\"Code\":\"DuplicateEntity\",\"EntityType\":\"Fleet\",\"Field\":\"Name\",\"Value\":\"Web\"}}"));
                using (TuiTestHost host = TuiCase.SignedIn(160, 45, "/vessels?tab=fleets", stub))
                {
                    AssertTrue(host.WaitForText("Frontend repos"), "rows\n" + host.Screen());
                    host.Press("n");
                    AssertTrue(host.WaitForText("Create Fleet"), "create form");
                    host.Type("Web");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/fleets") == 1), "create call");
                    AssertTrue(host.WaitForText("A fleet named Web already exists"), "server message\n" + host.Screen());
                    string frame = host.Screen();
                    AssertFalse(frame.Contains("Save failed."), "no generic message\n" + frame);
                    TuiCase.Contains(frame, "Create Fleet", "dialog stays open");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "fleet_detail", "The fleet page shows details and vessels and opens a vessel", () =>
            {
                StubHttpHandler stub = BuildStubs.Server();
                using (TuiTestHost host = TuiCase.SignedIn(160, 45, "/fleets/flt_web", stub))
                {
                    AssertTrue(host.WaitForText("Frontend repos"), "details\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Standard", "default pipeline name");
                    TuiCase.Contains(frame, "DemoRepo", "vessel listed");
                    TuiCase.Contains(frame, "Fleets > Web", "breadcrumb");
                    host.Press("j");
                    AssertTrue(host.WaitForText("\"DefaultPipelineId\""), "json");
                    host.Press("esc");
                    host.Press("]");
                    AssertTrue(host.WaitForText("https://github.com/acme/demorepo.git"), "vessels panel");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path.StartsWith("/vessels/vsl_", StringComparison.Ordinal)), "opens vessel");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "docks", "Docks lists with names, filters, opens the detail with git anchors, and deletes", () =>
            {
                StubHttpHandler stub = BuildStubs.Server();
                stub.On("DELETE", "/api/v1/docks/dck_2", b => BuildStubs.NoContent());
                stub.On("DELETE", "/api/v1/docks/dck_1", b => BuildStubs.NoContent());
                stub.Json("GET", "/api/v1/docks/dck_1", "{\"Id\":\"dck_1\",\"TenantId\":\"ten_default\",\"VesselId\":\"vsl_demo\",\"CaptainId\":\"cpt_1\",\"BranchName\":\"armada/msn_1\",\"WorktreePath\":\"/tmp/docks/one\",\"Active\":true," +
                    "\"GitAnchorsJson\":\"{\\\"StartCommit\\\":\\\"abc1234\\\",\\\"TargetBranch\\\":\\\"main\\\",\\\"WorkingBranch\\\":\\\"armada/msn_1\\\",\\\"RecentPathCommits\\\":[\\\"abc fix parser\\\"],\\\"SubjectTermsPresent\\\":[\\\"parser\\\"]}\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}");
                using (TuiTestHost host = TuiCase.SignedIn(160, 45, "/captains?tab=docks", stub))
                {
                    AssertTrue(host.WaitForText("/tmp/docks/one"), "rows\n" + host.Screen());
                    AssertTrue(host.WaitForText("DemoRepo"), "vessel name");
                    TuiCase.Contains(host.Screen(), "claude-1", "captain name");
                    DocksScreen screen = (DocksScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("/").Press("tab").Press("tab").Type("two");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("/tmp/docks/one")), "path filter");
                    host.Press("esc");
                    AssertEqual("dck_2", screen.Grid.Current!.Id, "filtered row");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "This will clean up the git worktree", "delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/docks/dck_2") == 1), "delete call");
                    host.Tui.Context.Navigate("/docks/dck_1");
                    AssertTrue(host.WaitForText("Starting Point"), "anchors\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "abc1234", "start commit");
                    TuiCase.Contains(frame, "abc fix parser", "recent commit");
                    TuiCase.Contains(frame, "[parser]", "subject term");
                    TuiCase.Contains(frame, "claude-1", "captain");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete dock dck_1?", "detail delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/docks/dck_1") == 1), "detail delete");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/captains"), "back to docks");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI fleets and docks", cases: cases);
        }
    }
}
