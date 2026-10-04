namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Configuration;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Skills tab and skill detail (W6.3) against a stubbed server: KPIs, server filters, sort, paging, row menu,
    /// form validation and submit, delete confirmation, scoped editing, and the detail panels.
    /// </summary>
    public sealed class TuiSkillsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Skills";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Skills tab shows KPIs and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=skills"))
                {
                    SkillsScreen screen = TuiEntityFixtures.Screen<SkillsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Total Skills 2", "Inactive 1", "Categories 1", "Testing habits", "engineering", "Tenant-wide");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_go_to_server", "Category deep link and search reach the server", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=skills&category=engineering"))
                {
                    SkillsScreen screen = TuiEntityFixtures.Screen<SkillsScreen>(host);
                    TuiEntityFixtures.WaitForRequest(host, stub, "category=engineering");
                    host.Press("/");
                    host.Type("test");
                    TuiEntityFixtures.WaitForRequest(host, stub, "search=test");
                    host.Press("enter");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Grid), "enter returns to the grid");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting is local over every match; paging asks the server", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Skill("skl_" + i.ToString("00"), "Skill " + i.ToString("00"), "general", true, "TenantWide", null)).ToList();
                stub.Json("GET", "/api/v1/skills", TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray()));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=skills"))
                {
                    SkillsScreen screen = TuiEntityFixtures.Screen<SkillsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForRequest(host, stub, "GET /api/v1/skills?pageNumber=2");
                    host.Press("<");
                    screen.Grid.SortBy("name", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Name == "Skill 25", "sorted descending");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu_and_scope", "Row menu offers Edit and Delete only where scoping allows", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.RegularUserServer();
                stub.Json("GET", "/api/v1/skills", TuiEntityFixtures.Page(
                    Skill("skl_1", "Shared skill", "engineering", true, "TenantWide", "usr_admin"),
                    Skill("skl_2", "My skill", "engineering", true, "UserSpecific", "usr_user")));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=skills"))
                {
                    SkillsScreen screen = TuiEntityFixtures.Screen<SkillsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "View JSON", "json item");
                    TuiCase.NotContains(frame, "Delete", "no delete on a tenant-wide skill");
                    host.Press("esc");
                    host.Press("down");
                    host.Press(".");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Edit", "Delete");
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "A blank name keeps the form open; a valid form posts", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "POST", "/api/v1/skills", Skill("skl_new", "Review habits", "quality", true, "TenantWide", null), box, HttpStatusCode.Created);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=skills"))
                {
                    SkillsScreen screen = TuiEntityFixtures.Screen<SkillsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    FormDialog dialog = TuiConfigTestHelpers.Dialog(host, "Create Skill");
                    TuiConfigTestHelpers.Input(dialog, "Name").Value = "";
                    host.Press("ctrl+s");
                    TuiCase.Contains(host.Screen(), "This field is required.", "required error");
                    AssertTrue(host.App.Modals.IsActive && box.Count == 0, "still open, nothing posted");
                    TuiConfigTestHelpers.Input(dialog, "Name").Value = "Review habits";
                    TuiConfigTestHelpers.Input(dialog, "Category").Value = "quality";
                    TuiConfigTestHelpers.Field<TextAreaField>(dialog, "Content").Value = "Always review.";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => box.Body != null && !host.App.Modals.IsActive, "posted and closed");
                    AssertTrue(box.Body!.Contains("\"Name\":\"Review habits\""), "name: " + box.Body);
                    AssertTrue(box.Body.Contains("\"Content\":\"Always review.\""), "content");
                    AssertTrue(box.Body.Contains("\"Scope\":\"TenantWide\""), "scope");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "delete_with_confirm", "Del asks first, then deletes", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "DELETE", "/api/v1/skills/skl_1", "{}", box);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=skills"))
                {
                    SkillsScreen screen = TuiEntityFixtures.Screen<SkillsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Project profiles referencing it", "confirm text");
                    AssertEqual(0, box.Count, "not yet deleted");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => box.Count == 1, "deleted");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_panels", "Skill detail shows overview and content and edits", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/skills/skl_1"))
                {
                    SkillScreen screen = TuiEntityFixtures.Screen<SkillScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Testing habits", "Overview", "Content", "[ Edit e ]", "engineering");
                    host.Press("]");
                    TuiCase.Contains(host.Screen(), "Write tests first.", "content panel");
                    host.Press("e");
                    TuiConfigTestHelpers.Dialog(host, "Edit Skill");
                    host.Press("esc");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI skills", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/skills", TuiEntityFixtures.Page(
                Skill("skl_1", "Testing habits", "engineering", true, "TenantWide", null),
                Skill("skl_2", "Old habits", null, false, "TenantWide", null)));
            stub.Json("GET", "/api/v1/skills/skl_1", Skill("skl_1", "Testing habits", "engineering", true, "TenantWide", null));
            return stub;
        }

        private static string Skill(string id, string name, string? category, bool active, string scope, string? userId)
        {
            return "{\"Id\":\"" + id + "\",\"TenantId\":\"ten_default\",\"UserId\":" + (userId == null ? "null" : "\"" + userId + "\"") + ",\"Name\":\"" + name + "\",\"Category\":" + (category == null ? "null" : "\"" + category + "\"")
                + ",\"Description\":\"desc\",\"Content\":\"Write tests first.\",\"Scope\":\"" + scope + "\",\"Active\":" + (active ? "true" : "false") + ",\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
