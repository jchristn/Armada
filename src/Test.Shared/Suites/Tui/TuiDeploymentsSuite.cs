namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Delivery;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Deployments tab and deployment detail (W5.1) against a stubbed server: open, KPIs, server filters, sort, paging,
    /// row menu, create form submit, approve with confirmation, read-only hint, and live refresh.
    /// </summary>
    public sealed class TuiDeploymentsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Deployments";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Deployments tab shows KPIs, columns, and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments"))
                {
                    DeploymentsScreen screen = TuiEntityFixtures.Screen<DeploymentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 5, "rows and KPIs");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Total Deployments 2", "kpi total");
                    TuiCase.Contains(frame, "Pending Approval 1", "kpi pending");
                    TuiCase.Contains(frame, "Deploy web", "row");
                    TuiCase.Contains(frame, "PendingApproval", "status badge");
                    TuiCase.Contains(frame, "web-app", "vessel name column");
                    TuiCase.Contains(frame, "Showing 1-2 of 2", "paging bar");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_go_to_server", "Search and status filters are sent to the server", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments&status=PendingApproval"))
                {
                    DeploymentsScreen screen = TuiEntityFixtures.Screen<DeploymentsScreen>(host);
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/deployments", "status", "PendingApproval");
                    AssertEqual("PendingApproval", screen.Filters.Value("status"), "deep-linked filter");
                    host.Press("/");
                    host.Type("web");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/deployments", "search", "web");
                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Grid), "esc returns to the grid");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting reads every match and sorts locally; paging asks the server for the next page", () =>
            {
                StubHttpHandler stub = Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Deployment("dpl_" + i.ToString("00"), "Deploy " + i.ToString("00"), "Succeeded")).ToList();
                stub.On("GET", "/api/v1/deployments", body => StubHttpHandler.Response(HttpStatusCode.OK, TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray())));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments"))
                {
                    DeploymentsScreen screen = TuiEntityFixtures.Screen<DeploymentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/deployments", "pageNumber", "2");
                    host.Press("<");
                    screen.Grid.SortBy("title", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Title == "Deploy 25", "sorted descending");
                    TuiCase.Contains(host.Screen(), "Deployment v", "sort indicator");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu", "The row menu lists Open, Edit, View JSON, Copy ID, and Delete", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments"))
                {
                    DeploymentsScreen screen = TuiEntityFixtures.Screen<DeploymentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Open", "Edit", "View JSON", "Copy ID", "Delete" }) TuiCase.Contains(frame, item, "menu item " + item);
                    host.Press("esc");
                    host.Press("j");
                    TuiCase.Contains(host.Screen(), "\"Id\": \"dpl_1\"", "View JSON");
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_submits", "n opens Create Deployment; Ctrl+S posts the payload and refreshes", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/deployments", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Deployment("dpl_new", "Hotfix", "Running"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments"))
                {
                    DeploymentsScreen screen = TuiEntityFixtures.Screen<DeploymentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Deployment"), "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    Armada.Tui.Widgets.InputField title = (Armada.Tui.Widgets.InputField)dialog.Form.Rows.First(r => r.Label == "Title").Field!;
                    title.Value = "Hotfix";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted and closed");
                    DeploymentUpsertRequest sent = JsonHelper.Deserialize<DeploymentUpsertRequest>(posted!);
                    AssertEqual("Hotfix", sent.Title, "title in payload: " + posted);
                    AssertEqual(true, sent.AutoExecute, "auto execute in payload");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_approve_with_confirm", "Deployment detail shows panels; a approves after confirmation", () =>
            {
                StubHttpHandler stub = Server();
                bool approved = false;
                stub.On("POST", "/api/v1/deployments/dpl_1/approve", body =>
                {
                    approved = true;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Deployment("dpl_1", "Deploy web", "Running"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/deployments/dpl_1"))
                {
                    DeploymentScreen screen = TuiEntityFixtures.Screen<DeploymentScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Deploy web", "title");
                    TuiCase.Contains(frame, "[ Approve a ]", "approve action");
                    TuiCase.Contains(frame, "Linked Checks", "panel tab");
                    TuiCase.Contains(frame, "Monitoring Window", "overview field");
                    AssertTrue(screen.VisibleActions().Contains("Deny"), "deny offered");
                    AssertFalse(screen.VisibleActions().Contains("Rollback"), "rollback hidden while pending");
                    host.Press("a");
                    // Environment first, then the title: the same label as the Approvals queue, the inbox, and the dashboard.
                    TuiCase.Contains(host.Screen(), "Approve and execute \"Deploy to staging: Deploy web\"?", "confirm text");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => approved, "approve call");
                    host.Press("]");
                    AssertEqual("checks", screen.ActivePanel, "next panel");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "read_only_user", "Non-admins see the read-only hint and no create or delete", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.RegularUserServer();
                AddDeployments(stub);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments"))
                {
                    DeploymentsScreen screen = TuiEntityFixtures.Screen<DeploymentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    TuiCase.Contains(host.Screen(), "Ask a tenant administrator to create and manage deployment records.", "hint");
                    host.Press("n");
                    AssertFalse(host.App.Modals.IsActive, "no create form");
                    host.Press(".");
                    TuiCase.NotContains(host.Screen(), "Delete", "no delete in menu");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "live_refresh", "deployment.changed reloads the list", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments"))
                {
                    DeploymentsScreen screen = TuiEntityFixtures.Screen<DeploymentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.LoadCount >= 1, "first load");
                    int before = screen.LoadCount;
                    host.Tui.Context.Events.CoalesceMs = 0;
                    host.Tui.Context.Events.Inject(Armada.Client.Socket.ArmadaSocketMessage.Parse("{\"type\":\"deployment.changed\",\"data\":{\"Id\":\"dpl_1\",\"Status\":\"Running\"}}")!);
                    TuiEntityFixtures.WaitFor(host, () => screen.LoadCount > before, "reloaded");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI deployments", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            AddDeployments(stub);
            return stub;
        }

        private static void AddDeployments(StubHttpHandler stub)
        {
            stub.Json("GET", "/api/v1/deployments", TuiEntityFixtures.Page(
                Deployment("dpl_1", "Deploy web", "PendingApproval"),
                Deployment("dpl_2", "Deploy api", "Succeeded")));
            stub.Json("GET", "/api/v1/deployments/dpl_1", Deployment("dpl_1", "Deploy web", "PendingApproval"));
            stub.Json("GET", "/api/v1/vessels", TuiEntityFixtures.Page("{\"Id\":\"vsl_1\",\"Name\":\"web-app\"}"));
        }

        private static string Deployment(string id, string title, string status)
        {
            return "{\"Id\":\"" + id + "\",\"Title\":\"" + title + "\",\"Status\":\"" + status + "\",\"VerificationStatus\":\"NotRun\",\"VesselId\":\"vsl_1\",\"EnvironmentName\":\"staging\",\"ApprovalRequired\":true,\"CheckRunIds\":[\"chk_1\"],\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-0" + (id.Length % 9 + 1) + "T00:00:00Z\"}";
        }
    }
}
