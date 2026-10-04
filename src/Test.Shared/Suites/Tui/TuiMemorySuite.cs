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
    /// Memory tab (W6.10) against a stubbed server: server type and search filters, sort, server paging, row menu,
    /// View JSON, delete with confirmation, and the create extension with validation.
    /// </summary>
    public sealed class TuiMemorySuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Memory";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens", "Memory tab shows rows with type, topic, summary, and salience", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=memory"))
                {
                    MemoryScreen screen = TuiEntityFixtures.Screen<MemoryScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Episodic", "deploys", "Deploy on Fridays fails", "0.80", "vsl_1", "Showing 1-2 of 2");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_go_to_server", "Type and search filters reach the server", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=memory&type=Procedural"))
                {
                    MemoryScreen screen = TuiEntityFixtures.Screen<MemoryScreen>(host);
                    TuiEntityFixtures.WaitForRequest(host, stub, "type=Procedural");
                    host.Press("/");
                    host.Press("tab");
                    host.Type("fri");
                    TuiEntityFixtures.WaitForRequest(host, stub, "search=fri");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Paging asks the server; sorting by salience reads every match", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.Server();
                string[] many = Enumerable.Range(1, 25).Select(i => Memory("mem_" + i.ToString("00"), "Semantic", "t" + i, i / 100.0)).ToArray();
                stub.Json("GET", "/api/v1/memories", TuiEntityFixtures.PageWithTotal(40, many));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=memory"))
                {
                    MemoryScreen screen = TuiEntityFixtures.Screen<MemoryScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForRequest(host, stub, "GET /api/v1/memories?pageNumber=2");
                    host.Press("<");
                    screen.Grid.SortBy("salience", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Id == "mem_25", "sorted by salience");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "menu_json_delete", "Row menu, Enter for JSON, and Del with confirmation", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "DELETE", "/api/v1/memories/mem_1", "{}", box);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=memory"))
                {
                    MemoryScreen screen = TuiEntityFixtures.Screen<MemoryScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    TuiConfigTestHelpers.Contains(host.Screen(), "View JSON", "Edit", "Copy ID", "Delete");
                    host.Press("esc");
                    host.Press("enter");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Memory: mem_1", "\"Salience\": 0.8");
                    host.Press("esc");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete this memory? This cannot be undone.", "confirm");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => box.Count == 1, "deleted");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_extension", "n creates a memory; blank content and bad salience keep the form open", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "POST", "/api/v1/memories", Memory("mem_new", "Procedural", "ops", 0.9), box, HttpStatusCode.Created);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=memory"))
                {
                    MemoryScreen screen = TuiEntityFixtures.Screen<MemoryScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    FormDialog dialog = TuiConfigTestHelpers.Dialog(host, "Create Memory");
                    TuiConfigTestHelpers.Input(dialog, "Salience").Value = "2";
                    host.Press("ctrl+s");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "This field is required.", "content required");
                    TuiCase.Contains(frame, "The number is out of range.", "salience range");
                    AssertEqual(0, box.Count, "nothing posted");
                    TuiConfigTestHelpers.Field<TextAreaField>(dialog, "Content").Value = "Restart the agent after upgrades.";
                    TuiConfigTestHelpers.Input(dialog, "Salience").Value = "0.9";
                    TuiConfigTestHelpers.Input(dialog, "Tags").Value = "ops, upgrades";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => box.Body != null && !host.App.Modals.IsActive, "posted");
                    AssertTrue(box.Body!.Contains("Restart the agent after upgrades.") && box.Body.Contains("\"upgrades\""), "payload: " + box.Body);
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI memory", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/memories", TuiEntityFixtures.Page(Memory("mem_1", "Episodic", "deploys", 0.8), Memory("mem_2", "Semantic", "style", 0.5)));
            return stub;
        }

        private static string Memory(string id, string type, string topic, double salience)
        {
            return "{\"Id\":\"" + id + "\",\"TenantId\":\"ten_default\",\"Scope\":\"TenantWide\",\"Type\":\"" + type + "\",\"Topic\":\"" + topic + "\",\"Summary\":\"Deploy on Fridays fails\",\"Content\":\"Long content\","
                + "\"Salience\":" + salience.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"VesselId\":\"vsl_1\",\"Tags\":[],\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
