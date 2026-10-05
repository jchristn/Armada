namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Delivery;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Releases tab and release detail (W5.3) against a stubbed server: open, KPIs, server filters, sort, paging, row
    /// menu, create form validation and submit, detail panels, Refresh Derived Fields, delete with confirmation, and
    /// /releases/new with a backlog prefill.
    /// </summary>
    public sealed class TuiReleasesSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Releases";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Releases tab shows KPIs, columns, and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=releases"))
                {
                    ReleasesScreen screen = TuiEntityFixtures.Screen<ReleasesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Total Releases 2", "kpi total");
                    TuiCase.Contains(frame, "Shipped 1", "kpi shipped");
                    TuiCase.Contains(frame, "Release 1.0", "row");
                    TuiCase.Contains(frame, "1 voyages, 1 missions", "linked work column");
                    TuiCase.Contains(frame, "web-app", "vessel column");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_go_to_server", "Status, vessel, and search filters are sent to the server", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=releases&status=Candidate"))
                {
                    ReleasesScreen screen = TuiEntityFixtures.Screen<ReleasesScreen>(host);
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/releases", "status", "Candidate");
                    AssertEqual("Candidate", screen.Filters.Value("status"), "deep-linked status");
                    host.Press("/");
                    host.Type("1.0");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/releases", "search", "1.0");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting sorts every match locally; paging asks for the next page", () =>
            {
                StubHttpHandler stub = Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Release("rel_" + i.ToString("00"), "Release " + i.ToString("00"), "Draft")).ToList();
                stub.On("GET", "/api/v1/releases", body => StubHttpHandler.Response(HttpStatusCode.OK, TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray())));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=releases"))
                {
                    ReleasesScreen screen = TuiEntityFixtures.Screen<ReleasesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/releases", "pageNumber", "2");
                    host.Press("<");
                    screen.Grid.SortBy("title", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Title == "Release 25", "sorted descending");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu", "The row menu lists Open, Edit, View JSON, Copy ID, and Delete", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=releases"))
                {
                    ReleasesScreen screen = TuiEntityFixtures.Screen<ReleasesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Open", "Edit", "View JSON", "Copy ID", "Delete" }) TuiCase.Contains(frame, item, "menu item " + item);
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "Create Release requires a title, then posts lists split per line", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/releases", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Release("rel_new", "Hotfix", "Draft"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=releases"))
                {
                    ReleasesScreen screen = TuiEntityFixtures.Screen<ReleasesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Release"), "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    InputField title = (InputField)dialog.Form.Rows.First(r => r.Label == "Title").Field!;
                    TextAreaField missions = (TextAreaField)dialog.Form.Rows.First(r => r.Label == "Mission IDs").Field!;
                    title.Value = "";
                    host.Press("ctrl+s");
                    TuiCase.Contains(host.Screen(), "This field is required.", "validation message");
                    AssertTrue(posted == null, "nothing posted");
                    title.Value = "Hotfix";
                    missions.Value = "msn_a\nmsn_b, msn_c";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted and closed");
                    ReleaseUpsertRequest sent = JsonHelper.Deserialize<ReleaseUpsertRequest>(posted!);
                    AssertEqual("Hotfix", sent.Title, "title: " + posted);
                    AssertEqual("msn_a,msn_b,msn_c", String.Join(",", sent.MissionIds), "missions split: " + posted);
                    AssertEqual(ReleaseStatusEnum.Draft, sent.Status, "status");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_panels_and_refresh", "Release detail shows panels and refreshes derived fields", () =>
            {
                StubHttpHandler stub = Server();
                bool refreshed = false;
                stub.On("POST", "/api/v1/releases/rel_1/refresh", body =>
                {
                    refreshed = true;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Release("rel_1", "Release 1.0", "Shipped"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/releases/rel_1"))
                {
                    ReleaseScreen screen = TuiEntityFixtures.Screen<ReleaseScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Release 1.0", "title");
                    TuiCase.Contains(frame, "Refresh Derived Fields", "refresh action");
                    TuiCase.Contains(frame, "GitHub Pull Requests", "panel tab");
                    AssertTrue(screen.ShowPanel("artifacts"), "artifacts panel");
                    TuiCase.Contains(host.Screen(), "dist/app.zip", "artifact path");
                    AssertTrue(screen.ShowPanel("pulls"), "pull requests panel");
                    TuiCase.Contains(host.Screen(), "Add login", "pull request");
                    AssertTrue(screen.ShowPanel("backlog"), "backlog panel");
                    TuiCase.Contains(host.Screen(), "No backlog items currently reference this release.", "empty backlog");
                    AssertTrue(screen.ShowPanel("deployments"), "deployments panel");
                    TuiCase.Contains(host.Screen(), "Deploy rel", "deployment evidence");
                    AssertTrue(screen.RunAction("refresh-derived"), "refresh");
                    TuiEntityFixtures.WaitFor(host, () => refreshed, "refresh call");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_delete_with_confirm", "Delete asks for confirmation, deletes, and returns to the tab", () =>
            {
                StubHttpHandler stub = Server();
                bool deleted = false;
                stub.On("DELETE", "/api/v1/releases/rel_1", body =>
                {
                    deleted = true;
                    return StubHttpHandler.Response(HttpStatusCode.NoContent, "");
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/releases/rel_1"))
                {
                    ReleaseScreen screen = TuiEntityFixtures.Screen<ReleaseScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete \"Release 1.0\"? This removes only the release record.", "confirm text");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => deleted, "delete call");
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath.Contains("tab=releases"), "back to the tab");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_mode_prefill", "/releases/new takes the prefill and links backlog items on create", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/releases", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Release("rel_1", "Release 1.0", "Draft"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=releases"))
                {
                    ReleaseUpsertRequest prefill = new ReleaseUpsertRequest();
                    prefill.Title = "From backlog";
                    prefill.CheckRunIds = new List<string> { "chk_9" };
                    prefill.ObjectiveIds = new List<string> { "obj_1" };
                    NavigationPrefill.Set(host.Tui.Context, PrefillSlots.CreateRelease, prefill, "/releases/new");
                    host.Pump();
                    ReleaseScreen screen = TuiEntityFixtures.Screen<ReleaseScreen>(host);
                    AssertTrue(screen.IsCreateMode, "create mode");
                    TuiEntityFixtures.WaitFor(host, () => screen.Editor!.Title.Value == "From backlog", "prefilled");
                    TuiCase.Contains(host.Screen(), "Open Backlog Item", "backlog action");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null, "created");
                    ReleaseUpsertRequest linked = JsonHelper.Deserialize<ReleaseUpsertRequest>(posted!);
                    AssertEqual("obj_1", String.Join(",", linked.ObjectiveIds), "objectives linked: " + posted);
                    AssertEqual("chk_9", String.Join(",", linked.CheckRunIds), "checks: " + posted);
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/releases/rel_1", "opened the release");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI releases", cases: cases);
        }

        internal static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/releases", TuiEntityFixtures.Page(
                Release("rel_1", "Release 1.0", "Shipped"),
                Release("rel_2", "Release 1.1", "Candidate")));
            stub.Json("GET", "/api/v1/releases/rel_1", Release("rel_1", "Release 1.0", "Shipped"));
            stub.Json("GET", "/api/v1/releases/rel_1/github/pull-requests", "[{\"Repository\":\"acme/web\",\"Number\":7,\"Url\":\"https://github.com/acme/web/pull/7\",\"Title\":\"Add login\",\"State\":\"open\",\"ReviewStatus\":\"Approved\",\"RequestedReviewers\":[\"bob\"],\"Checks\":[],\"Reviews\":[]}]");
            stub.Json("GET", "/api/v1/deployments", TuiEntityFixtures.Page("{\"Id\":\"dpl_1\",\"Title\":\"Deploy rel\",\"ReleaseId\":\"rel_1\",\"Status\":\"Succeeded\",\"VerificationStatus\":\"Passed\",\"CheckRunIds\":[],\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-01T00:00:00Z\"}"));
            stub.Json("GET", "/api/v1/vessels", TuiEntityFixtures.Page("{\"Id\":\"vsl_1\",\"Name\":\"web-app\"}"));
            return stub;
        }

        private static string Release(string id, string title, string status)
        {
            return "{\"Id\":\"" + id + "\",\"Title\":\"" + title + "\",\"Status\":\"" + status + "\",\"VesselId\":\"vsl_1\",\"Version\":\"1.0.0\",\"TagName\":\"v1.0.0\",\"VoyageIds\":[\"vyg_1\"],\"MissionIds\":[\"msn_1\"],\"CheckRunIds\":[\"chk_1\"],"
                + "\"Artifacts\":[{\"SourceType\":\"CheckRun\",\"SourceId\":\"chk_1\",\"Path\":\"dist/app.zip\",\"SizeBytes\":2048}],\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
