namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Delivery;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Environments tab and environment detail (W5.2) against a stubbed server: open, KPIs, server filters, sort,
    /// paging, row menu with Duplicate, create form validation and submit, the verification definitions editor and
    /// save, delete with confirmation, and the Deploy hand-off.
    /// </summary>
    public sealed class TuiEnvironmentsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Environments";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Environments tab shows KPIs, columns, and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=environments"))
                {
                    EnvironmentsScreen screen = TuiEntityFixtures.Screen<EnvironmentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Total Environments 2", "kpi total");
                    TuiCase.Contains(frame, "Require Approval 1", "kpi approval");
                    TuiCase.Contains(frame, "production", "row");
                    TuiCase.Contains(frame, "Approval required", "policy column");
                    TuiCase.Contains(frame, "web-app", "vessel column");
                    TuiCase.Contains(frame, "Showing 1-2 of 2", "paging bar");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_go_to_server", "Kind, active, and search filters are sent to the server", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=environments&kind=Production&active=true"))
                {
                    EnvironmentsScreen screen = TuiEntityFixtures.Screen<EnvironmentsScreen>(host);
                    TuiEntityFixtures.WaitForRequest(host, stub, "kind=Production");
                    TuiEntityFixtures.WaitForRequest(host, stub, "active=true");
                    AssertEqual("Production", screen.Filters.Value("kind"), "deep-linked kind");
                    host.Press("/");
                    host.Type("prod");
                    TuiEntityFixtures.WaitForRequest(host, stub, "search=prod");
                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Grid), "esc returns to the grid");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting sorts every match locally; paging asks for the next page", () =>
            {
                StubHttpHandler stub = Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Environment("env_" + i.ToString("00"), "env " + i.ToString("00"), false)).ToList();
                stub.On("GET", "/api/v1/environments", body => StubHttpHandler.Response(HttpStatusCode.OK, TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray())));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=environments"))
                {
                    EnvironmentsScreen screen = TuiEntityFixtures.Screen<EnvironmentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForRequest(host, stub, "GET /api/v1/environments?pageNumber=2");
                    host.Press("<");
                    screen.Grid.SortBy("name", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Name == "env 25", "sorted descending");
                    TuiCase.Contains(host.Screen(), "Environment v", "sort indicator");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu_duplicate", "The row menu has Duplicate; choosing it posts a copy", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/environments", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Environment("env_copy", "production (Copy)", true));
                });
                stub.Json("GET", "/api/v1/environments/env_copy", Environment("env_copy", "production (Copy)", true));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=environments"))
                {
                    EnvironmentsScreen screen = TuiEntityFixtures.Screen<EnvironmentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Open", "Edit", "Duplicate", "View JSON", "Copy ID", "Delete" }) TuiCase.Contains(frame, item, "menu item " + item);
                    host.Type("Duplicate");
                    host.Press("enter");
                    TuiEntityFixtures.WaitFor(host, () => posted != null, "duplicate posted");
                    AssertTrue(posted!.Contains("\"Name\":\"production (Copy)\""), "copy name: " + posted);
                    AssertTrue(posted.Contains("\"IsDefault\":false"), "copy is not default");
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/environments/env_copy", "opened the copy");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "Create Environment requires a name, then posts the payload", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/environments", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Environment("env_new", "qa", false));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=environments"))
                {
                    EnvironmentsScreen screen = TuiEntityFixtures.Screen<EnvironmentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Environment"), "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    InputField name = (InputField)dialog.Form.Rows.First(r => r.Label == "Name").Field!;
                    name.Value = "";
                    host.Press("ctrl+s");
                    TuiCase.Contains(host.Screen(), "This field is required.", "validation message");
                    AssertTrue(host.App.Modals.IsActive, "dialog stays open");
                    AssertTrue(posted == null, "nothing posted");
                    name.Value = "qa";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted and closed");
                    AssertTrue(posted!.Contains("\"Name\":\"qa\""), "name in payload: " + posted);
                    AssertTrue(posted.Contains("\"RolloutMonitoringWindowMinutes\":60"), "monitoring default");
                    AssertTrue(posted.Contains("\"Active\":true"), "active default");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_verification_editor_saves", "The detail editor adds a verification definition and saves it", () =>
            {
                StubHttpHandler stub = Server();
                string? put = null;
                stub.On("PUT", "/api/v1/environments/env_1", body =>
                {
                    put = body;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Environment("env_1", "production", true));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/environments/env_1"))
                {
                    EnvironmentScreen screen = TuiEntityFixtures.Screen<EnvironmentScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "production", "title");
                    TuiCase.Contains(frame, "[ Deploy ]", "deploy action");
                    TuiCase.Contains(frame, "Monitoring Interval", "overview field");
                    AssertTrue(screen.ShowPanel("edit"), "editor panel");
                    AssertTrue(screen.Definitions!.AddNew(), "add verification");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Add Verification"), "definition dialog");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => screen.Definitions.Items.Count == 1 && !host.App.Modals.IsActive, "definition added");
                    AssertTrue(screen.Editor!.View.IsDirty, "editor dirty");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => put != null, "saved");
                    AssertTrue(put!.Contains("\"Path\":\"/health\""), "definition in payload: " + put);
                    AssertTrue(put.Contains("\"ExpectedStatusCode\":200"), "expected status");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_delete_with_confirm", "Delete asks for confirmation, deletes, and returns to the tab", () =>
            {
                StubHttpHandler stub = Server();
                bool deleted = false;
                stub.On("DELETE", "/api/v1/environments/env_1", body =>
                {
                    deleted = true;
                    return StubHttpHandler.Response(HttpStatusCode.NoContent, "");
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/environments/env_1"))
                {
                    EnvironmentScreen screen = TuiEntityFixtures.Screen<EnvironmentScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete \"production\"? This removes only the environment record.", "confirm text");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => deleted, "delete call");
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath.Contains("tab=environments"), "back to the tab");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "deploy_hands_off_prefill", "Deploy opens Create Deployment prefilled with the environment", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/environments/env_1"))
                {
                    EnvironmentScreen screen = TuiEntityFixtures.Screen<EnvironmentScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    AssertTrue(screen.RunAction("deploy"), "deploy");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Deployment"), "deployment form");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    InputField title = (InputField)dialog.Form.Rows.First(r => r.Label == "Title").Field!;
                    AssertEqual("production Deploy", title.Value, "prefilled title");
                    AssertEqual("/deployments/new", host.Tui.Context.Router.Current!.Path, "route");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "read_only_user", "Non-admins see the read-only hint and no editor", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.RegularUserServer();
                AddEnvironments(stub);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=environments"))
                {
                    EnvironmentsScreen screen = TuiEntityFixtures.Screen<EnvironmentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    TuiCase.Contains(host.Screen(), "Ask a tenant administrator to create and manage environment records.", "hint");
                    host.Press(".");
                    TuiCase.NotContains(host.Screen(), "Duplicate", "no duplicate in menu");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI environments", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            AddEnvironments(stub);
            return stub;
        }

        private static void AddEnvironments(StubHttpHandler stub)
        {
            stub.Json("GET", "/api/v1/environments", TuiEntityFixtures.Page(
                Environment("env_1", "production", true),
                Environment("env_2", "staging", false)));
            stub.Json("GET", "/api/v1/environments/env_1", Environment("env_1", "production", true));
            stub.Json("GET", "/api/v1/vessels", TuiEntityFixtures.Page("{\"Id\":\"vsl_1\",\"Name\":\"web-app\"}"));
        }

        private static string Environment(string id, string name, bool approval)
        {
            return "{\"Id\":\"" + id + "\",\"Name\":\"" + name + "\",\"Kind\":\"" + (approval ? "Production" : "Staging") + "\",\"VesselId\":\"vsl_1\",\"BaseUrl\":\"https://" + name.Replace(" ", "") + ".example.com\",\"HealthEndpoint\":\"/health\",\"RequiresApproval\":" + (approval ? "true" : "false")
                + ",\"IsDefault\":" + (approval ? "true" : "false") + ",\"Active\":true,\"RolloutMonitoringWindowMinutes\":60,\"RolloutMonitoringIntervalSeconds\":300,\"AlertOnRegression\":true,\"VerificationDefinitions\":[],\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
