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
    /// Incidents tab and incident detail (W5.4) against a stubbed server: open, KPIs, server filters, sort, paging, row
    /// menu, create form validation and submit, prefilled create page, rollback with confirmation, delete, and live
    /// refresh.
    /// </summary>
    public sealed class TuiIncidentsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Incidents";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Incidents tab shows KPIs, columns, and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=incidents"))
                {
                    IncidentsScreen screen = TuiEntityFixtures.Screen<IncidentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 5, "rows and KPIs");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Total Incidents 2", "kpi total");
                    TuiCase.Contains(frame, "Open 1", "kpi open");
                    TuiCase.Contains(frame, "Closed / Rolled Back 1", "kpi closed");
                    TuiCase.Contains(frame, "API outage", "row");
                    TuiCase.Contains(frame, "Critical", "severity");
                    TuiCase.Contains(frame, "Deploy web", "deployment name column");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_go_to_server", "Search, status, and severity filters are sent to the server", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=incidents&severity=Critical"))
                {
                    IncidentsScreen screen = TuiEntityFixtures.Screen<IncidentsScreen>(host);
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/incidents", "severity", "Critical");
                    AssertEqual("Critical", screen.Filters.Value("severity"), "deep-linked filter");
                    host.Press("/");
                    host.Type("outage");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/incidents", "search", "outage");
                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Grid), "esc returns to the grid");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting sorts every match locally; paging asks the server for the next page", () =>
            {
                StubHttpHandler stub = Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Incident("inc_" + i.ToString("00"), "Incident " + i.ToString("00"), "Open", "Low", null)).ToList();
                stub.On("GET", "/api/v1/incidents", body => StubHttpHandler.Response(HttpStatusCode.OK, TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray())));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=incidents"))
                {
                    IncidentsScreen screen = TuiEntityFixtures.Screen<IncidentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/incidents", "pageNumber", "2");
                    host.Press("<");
                    screen.Grid.SortBy("title", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Title == "Incident 25", "sorted descending");
                    TuiCase.Contains(host.Screen(), "Incident v", "sort indicator");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu_and_delete", "The row menu offers Open, View JSON, Copy ID, and Delete; Delete confirms and calls the API", () =>
            {
                StubHttpHandler stub = Server();
                bool deleted = false;
                stub.On("DELETE", "/api/v1/incidents/inc_1", body => { deleted = true; return StubHttpHandler.Response(HttpStatusCode.NoContent, ""); });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=incidents"))
                {
                    IncidentsScreen screen = TuiEntityFixtures.Screen<IncidentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Open", "View JSON", "Copy ID", "Delete" }) TuiCase.Contains(frame, item, "menu item " + item);
                    host.Press("esc");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "This removes the incident snapshot chain", "delete confirmation");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => deleted, "delete call");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "Create Incident requires a title, then posts the payload", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/incidents", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Incident("inc_new", "DB failover", "Open", "High", null));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=incidents"))
                {
                    IncidentsScreen screen = TuiEntityFixtures.Screen<IncidentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is FormDialog, "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    TuiCase.Contains(host.Screen(), "Create Incident", "title");
                    InputField title = (InputField)dialog.Form.Rows.First(r => r.Label == "Title").Field!;
                    title.Value = "";
                    host.Press("ctrl+s");
                    AssertTrue(host.App.Modals.Top is FormDialog, "dialog stays open");
                    TuiCase.Contains(host.Screen(), "This field is required.", "required error");
                    AssertNull(posted, "nothing posted");
                    title.Value = "DB failover";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted and closed");
                    IncidentUpsertRequest sent = JsonHelper.Deserialize<IncidentUpsertRequest>(posted!);
                    AssertEqual("DB failover", sent.Title, "title in payload: " + posted);
                    AssertEqual(IncidentSeverityEnum.High, sent.Severity, "default severity in payload: " + posted);
                    AssertEqual(IncidentStatusEnum.Open, sent.Status, "default status in payload");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "prefilled_create_page", "A Create Incident hand-off opens /incidents/new with the full form prefilled", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=incidents"))
                {
                    IncidentUpsertRequest prefill = new IncidentUpsertRequest();
                    prefill.Title = "Deploy web Incident";
                    prefill.Severity = IncidentSeverityEnum.Medium;
                    prefill.DeploymentId = "dpl_1";
                    NavigationPrefill.Set(host.Tui.Context, PrefillSlots.CreateIncident, prefill, "/incidents/new");
                    IncidentScreen screen = TuiEntityFixtures.Screen<IncidentScreen>(host);
                    AssertTrue(screen.IsCreateMode, "create mode");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is FormDialog, "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    AssertEqual("Deploy web Incident", ((InputField)dialog.Form.Rows.First(r => r.Label == "Title").Field!).Value, "prefilled title");
                    AssertEqual("Medium", ((SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Severity").Field!).Value, "prefilled severity");
                    AssertEqual("dpl_1", ((SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Deployment").Field!).Value, "prefilled deployment");
                    AssertTrue(dialog.Form.Rows.Any(r => r.Label == "Postmortem"), "full form");
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_rollback_with_confirm", "Incident detail shows panels; Rollback Deployment confirms, rolls back, and records it", () =>
            {
                StubHttpHandler stub = Server();
                bool rolledBack = false;
                string? update = null;
                stub.On("POST", "/api/v1/deployments/dpl_1/rollback", body => { rolledBack = true; return StubHttpHandler.Response(HttpStatusCode.OK, "{\"Id\":\"dpl_1\",\"Title\":\"Deploy web\",\"Status\":\"RollingBack\"}"); });
                stub.On("PUT", "/api/v1/incidents/inc_1", body => { update = body; return StubHttpHandler.Response(HttpStatusCode.OK, Incident("inc_1", "API outage", "RolledBack", "Critical", "dpl_1")); });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/incidents/inc_1"))
                {
                    IncidentScreen screen = TuiEntityFixtures.Screen<IncidentScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "API outage", "title");
                    TuiCase.Contains(frame, "Runbook Executions", "panel tab");
                    TuiCase.Contains(frame, "Root Cause", "overview field");
                    foreach (string action in new[] { "Plan Hotfix", "Dispatch Hotfix", "Runbook", "Rollback Deployment", "Open Deployment", "View JSON", "Edit", "Delete" })
                        AssertTrue(screen.VisibleActions().Contains(action), "action " + action);
                    AssertTrue(screen.RunAction("rollback"), "rollback offered");
                    TuiCase.Contains(host.Screen(), "Rollback deployment \"dpl_1\" and attach the result to this incident?", "confirm text");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => rolledBack && update != null, "rollback and update");
                    IncidentUpsertRequest updated = JsonHelper.Deserialize<IncidentUpsertRequest>(update!);
                    AssertEqual(IncidentStatusEnum.RolledBack, updated.Status, "update status: " + update);
                    AssertEqual("dpl_1", updated.RollbackDeploymentId, "update rollback deployment: " + update);
                    host.Press("]");
                    AssertEqual("runbooks", screen.ActivePanel, "next panel");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "runbook_handoff", "Runbook hands a prefilled execution to the Runbooks tab", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/incidents/inc_1"))
                {
                    IncidentScreen screen = TuiEntityFixtures.Screen<IncidentScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    screen.RunAction("runbook");
                    host.Pump();
                    AssertEqual("/delivery", host.Tui.Context.Router.Current!.Path, "navigated to runbooks");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "live_refresh", "incident.changed reloads the list", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=incidents"))
                {
                    IncidentsScreen screen = TuiEntityFixtures.Screen<IncidentsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.LoadCount >= 1, "first load");
                    int before = screen.LoadCount;
                    host.Tui.Context.Events.CoalesceMs = 0;
                    host.Tui.Context.Events.Inject(Armada.Client.Socket.ArmadaSocketMessage.Parse("{\"type\":\"incident.changed\",\"data\":{\"Id\":\"inc_1\",\"Status\":\"Mitigated\"}}")!);
                    TuiEntityFixtures.WaitFor(host, () => screen.LoadCount > before, "reloaded");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI incidents", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/incidents", TuiEntityFixtures.Page(
                Incident("inc_1", "API outage", "Open", "Critical", "dpl_1"),
                Incident("inc_2", "Slow pages", "Closed", "Low", null)));
            stub.Json("GET", "/api/v1/incidents/inc_1", Incident("inc_1", "API outage", "Open", "Critical", "dpl_1"));
            stub.Json("GET", "/api/v1/deployments", TuiEntityFixtures.Page("{\"Id\":\"dpl_1\",\"Title\":\"Deploy web\",\"Status\":\"Succeeded\",\"VesselId\":\"vsl_1\",\"CheckRunIds\":[]}"));
            stub.Json("GET", "/api/v1/vessels", TuiEntityFixtures.Page("{\"Id\":\"vsl_1\",\"Name\":\"web-app\"}"));
            return stub;
        }

        private static string Incident(string id, string title, string status, string severity, string? deploymentId)
        {
            return "{\"Id\":\"" + id + "\",\"Title\":\"" + title + "\",\"Status\":\"" + status + "\",\"Severity\":\"" + severity + "\",\"VesselId\":\"vsl_1\",\"EnvironmentName\":\"production\""
                + (deploymentId != null ? ",\"DeploymentId\":\"" + deploymentId + "\"" : "")
                + ",\"Summary\":\"Users see errors\",\"DetectedUtc\":\"2026-10-01T00:00:00Z\",\"RescueMissionIds\":[],\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
