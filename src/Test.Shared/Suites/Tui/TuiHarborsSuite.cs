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
    /// Harbors tab (W6.9) against a stubbed server: KPIs, local filters, sort, paging, details with capabilities,
    /// enable/disable, register form validation and submit, delete with confirmation, and the read-only hint.
    /// </summary>
    public sealed class TuiHarborsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Harbors";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Harbors tab shows KPIs and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=harbors"))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Total Harbors 2", "Connected 1", "Disconnected 1", "build-box", "git, docker", "linux / x64", "Never");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filter_sort_page", "Status filter, search, sort, and local paging", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Harbor("hbr_" + i.ToString("00"), "h-" + i.ToString("00"), i % 3 == 0 ? "Connected" : "Disconnected", true)).ToList();
                stub.Json("GET", "/api/v1/harbors", "[" + String.Join(",", many) + "]");
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=harbors"))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.PageNumber == 2, "second page");
                    screen.Grid.SortBy("name", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Name == "h-30", "sorted");
                    SelectField<string> status = (SelectField<string>)screen.Filters.Field("status")!;
                    status.Choose(status.Options.First(o => o.Value == "Connected"));
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.TotalRecords == 10, "status filter");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "details_and_toggle", "Enter shows details with capabilities; t disables", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "POST", "/api/v1/harbors/hbr_1/disable", Harbor("hbr_1", "build-box", "Connected", false), box);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=harbors"))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("enter");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Capabilities", "+ git", "x docker (unavailable)", "Protocol");
                    host.Press("esc");
                    host.Press(".");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Details", "Edit", "Disable", "View JSON", "Delete");
                    host.Press("esc");
                    host.Press("t");
                    TuiEntityFixtures.WaitFor(host, () => box.Count == 1, "disabled");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "register_validates_and_submits", "Bad capacity keeps the form open; a valid form posts", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "POST", "/api/v1/harbors", Harbor("hbr_new", "laptop", "Unknown", true), box, HttpStatusCode.Created);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=harbors"))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    FormDialog dialog = TuiConfigTestHelpers.Dialog(host, "Register Harbor");
                    TuiConfigTestHelpers.Input(dialog, "Max Concurrent Jobs").Value = "zero";
                    host.Press("ctrl+s");
                    TuiCase.Contains(host.Screen(), "Enter a whole number.", "number error");
                    AssertEqual(0, box.Count, "nothing posted");
                    TuiConfigTestHelpers.Input(dialog, "Max Concurrent Jobs").Value = "8";
                    TuiConfigTestHelpers.Input(dialog, "Name").Value = "laptop";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => box.Body != null && !host.App.Modals.IsActive, "posted");
                    Armada.Core.Models.Harbor sent = box.As<Armada.Core.Models.Harbor>();
                    AssertEqual(8, sent.MaxConcurrentJobs, "max concurrent jobs: " + box.Body);
                    AssertEqual("laptop", sent.Name, "name: " + box.Body);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "delete_with_confirm", "Del confirms, then deletes", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "DELETE", "/api/v1/harbors/hbr_1", "{}", box);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=harbors"))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Its registration is removed", "confirm");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => box.Count == 1, "deleted");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "read_only_user", "Non-admins get the hint and no manage actions", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.RegularUserServer();
                stub.Json("GET", "/api/v1/harbors", "[" + Harbor("hbr_1", "build-box", "Connected", true) + "]");
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=harbors"))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 1, "rows");
                    TuiCase.Contains(host.Screen(), "Ask a tenant administrator to connect a Harbor.", "hint");
                    host.Press(".");
                    string frame = host.Screen();
                    TuiCase.NotContains(frame, "Disable", "no toggle");
                    TuiCase.NotContains(frame, "Delete", "no delete");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI harbors", cases: cases);
        }

        internal static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/harbors", "[" + Harbor("hbr_1", "build-box", "Connected", true) + "," + Harbor("hbr_2", "old-box", "Disconnected", false) + "]");
            stub.Json("GET", "/api/v1/harbors/hbr_1/metrics", HarborMetricsFixture.Json("hbr_1"));
            stub.Json("GET", "/api/v1/harbors/hbr_2/metrics", HarborMetricsFixture.Json("hbr_2"));
            return stub;
        }

        private static string Harbor(string id, string name, string status, bool enabled)
        {
            return "{\"Id\":\"" + id + "\",\"Name\":\"" + name + "\",\"ConnectionStatus\":\"" + status + "\",\"Enabled\":" + (enabled ? "true" : "false")
                + ",\"MaxConcurrentJobs\":4,\"ProtocolVersion\":\"1\",\"OsPlatform\":\"linux\",\"Architecture\":\"x64\",\"Capabilities\":[{\"Name\":\"git\",\"Available\":true},{\"Name\":\"docker\",\"Available\":false}],"
                + "\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
