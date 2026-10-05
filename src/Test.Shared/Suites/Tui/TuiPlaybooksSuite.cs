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
    /// Playbooks tab and playbook detail (W6.7) against a stubbed server: KPIs, local filters, sort, paging, row menu
    /// with Duplicate, form validation and submit, statistics and preview, and delete with confirmation.
    /// </summary>
    public sealed class TuiPlaybooksSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Playbooks";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Playbooks tab shows KPIs and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=playbooks"))
                {
                    PlaybooksScreen screen = TuiEntityFixtures.Screen<PlaybooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Total Playbooks 2", "Active 1", "Stored Markdown", "ARCH.md", "Architecture rules");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters", "Search and status filter the list", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=playbooks&status=inactive"))
                {
                    PlaybooksScreen screen = TuiEntityFixtures.Screen<PlaybooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.LoadCount >= 1 && screen.Grid.Rows.Count == 1, "inactive only");
                    AssertEqual("OLD.md", screen.Grid.Rows[0].FileName, "inactive row");
                    screen.Filters.SetValue("status", "");
                    host.Press("/");
                    host.Type("arch");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 1 && screen.Grid.Rows[0].FileName == "ARCH.md", "search");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sort and page over the local list", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.Server();
                string[] many = Enumerable.Range(1, 30).Select(i => Playbook("pbk_" + i.ToString("00"), "P" + i.ToString("00") + ".md", true)).ToArray();
                stub.Json("GET", "/api/v1/playbooks", TuiEntityFixtures.Page(many));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=playbooks"))
                {
                    PlaybooksScreen screen = TuiEntityFixtures.Screen<PlaybooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.PageNumber == 2 && screen.Grid.Rows.Count == 5, "second page");
                    TuiCase.Contains(host.Screen(), "Showing 26-30 of 30", "paging bar");
                    screen.Grid.SortBy("fileName", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].FileName == "P30.md", "sorted descending");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu_duplicate", "The row menu includes Duplicate, which posts a copy", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "POST", "/api/v1/playbooks", Playbook("pbk_copy", "ARCH (Copy).md", true), box, HttpStatusCode.Created);
                stub.Json("GET", "/api/v1/playbooks/pbk_copy", Playbook("pbk_copy", "ARCH (Copy).md", true));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=playbooks"))
                {
                    PlaybooksScreen screen = TuiEntityFixtures.Screen<PlaybooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Open", "Edit", "Duplicate", "View JSON", "Delete");
                    host.Type("Duplicate");
                    host.Press("enter");
                    TuiEntityFixtures.WaitFor(host, () => box.Body != null, "duplicate posted");
                    AssertTrue(box.Body!.Contains("\"FileName\":\"ARCH (Copy).md\""), "copy name: " + box.Body);
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/playbooks/pbk_copy", "opened the copy");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "A blank file name keeps the form open; a valid form posts", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "POST", "/api/v1/playbooks", Playbook("pbk_new", "NEW.md", true), box, HttpStatusCode.Created);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=playbooks"))
                {
                    PlaybooksScreen screen = TuiEntityFixtures.Screen<PlaybooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    FormDialog dialog = TuiConfigTestHelpers.Dialog(host, "Create Playbook");
                    AssertTrue(TuiConfigTestHelpers.Field<TextAreaField>(dialog, "Markdown Content").Value.StartsWith("# Playbook", StringComparison.Ordinal), "default content");
                    TuiConfigTestHelpers.Input(dialog, "File Name").Value = " ";
                    host.Press("ctrl+s");
                    TuiCase.Contains(host.Screen(), "This field is required.", "required error");
                    AssertEqual(0, box.Count, "nothing posted");
                    TuiConfigTestHelpers.Input(dialog, "File Name").Value = "NEW.md";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => box.Body != null && !host.App.Modals.IsActive, "posted and closed");
                    AssertTrue(box.Body!.Contains("\"FileName\":\"NEW.md\""), "file name: " + box.Body);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_statistics_and_delete", "Detail shows statistics and preview; Delete confirms and navigates back", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "DELETE", "/api/v1/playbooks/pbk_1", "{}", box);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/playbooks/pbk_1"))
                {
                    PlaybookScreen screen = TuiEntityFixtures.Screen<PlaybookScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    TuiConfigTestHelpers.Contains(host.Screen(), "ARCH.md", "Statistics", "Characters", "Headings", "Preview", "[ Duplicate ]");
                    int[] stats = PlaybookScreen.Statistics("# A\n\ntext\n## B\n");
                    AssertEqual(2, stats[2], "two headings");
                    AssertEqual(5, stats[1], "five lines");
                    host.Press("]");
                    TuiCase.Contains(host.Screen(), "Rules", "preview");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Existing mission snapshots remain immutable", "confirm");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => box.Count == 1 && host.Tui.Context.Router.Current!.FullPath.Contains("tab=playbooks"), "deleted and back");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI playbooks", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/playbooks", TuiEntityFixtures.Page(Playbook("pbk_1", "ARCH.md", true), Playbook("pbk_2", "OLD.md", false)));
            stub.Json("GET", "/api/v1/playbooks/pbk_1", Playbook("pbk_1", "ARCH.md", true));
            return stub;
        }

        private static string Playbook(string id, string fileName, bool active)
        {
            string description = fileName == "ARCH.md" ? "Architecture rules" : "Retired";
            return "{\"Id\":\"" + id + "\",\"TenantId\":\"ten_default\",\"FileName\":\"" + fileName + "\",\"Description\":\"" + description + "\",\"Content\":\"# Rules\\n\\nUse tests.\\n\",\"Scope\":\"TenantWide\",\"Active\":"
                + (active ? "true" : "false") + ",\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
