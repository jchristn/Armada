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
    /// Checks tab and check run detail (W5.5) against a stubbed server: open, KPIs and comparison, server filters, sort,
    /// paging, row menu, Run Check validation, preview, readiness, and submit, the prefilled hand-off, detail panels,
    /// delete with confirmation, retry, and Draft Release.
    /// </summary>
    public sealed class TuiChecksSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Checks";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Checks tab shows KPIs, rows, and the comparison with the previous run", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=checks"))
                {
                    ChecksScreen screen = TuiEntityFixtures.Screen<ChecksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    Armada.Tui.Widgets.GridColumn<CheckRun> comparison = screen.Grid.Columns.First(c => c.Key == "comparison");
                    TuiEntityFixtures.WaitFor(host, () => comparison.Value(screen.Grid.Rows[0]).Contains("vs previous same check type"), "comparison column");
                    AssertTrue(comparison.Value(screen.Grid.Rows[0]).Contains("status Passed -> Failed"), "regression summary");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Total Runs 2", "kpi total");
                    TuiCase.Contains(frame, "Passed 1", "kpi passed");
                    TuiCase.Contains(frame, "Failed 1", "kpi failed");
                    TuiCase.Contains(frame, "Nightly tests", "row");
                    TuiCase.Contains(frame, "web-app", "vessel name");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_go_to_server", "Vessel, status, source, and type filters are sent to the server", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=checks&source=External&type=UnitTest"))
                {
                    ChecksScreen screen = TuiEntityFixtures.Screen<ChecksScreen>(host);
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/check-runs", "source", "External");
                    TuiEntityFixtures.WaitForRequest(host, stub, "GET", "/api/v1/check-runs", r => r.QueryValue("source") == "External" && r.QueryValue("type") == "UnitTest", "source and type");
                    AssertEqual("External", screen.Filters.Value("source"), "deep-linked source");
                    SelectField<string> status = (SelectField<string>)screen.Filters.Field("status")!;
                    status.Choose(status.Options.First(o => o.Value == "Failed"));
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/check-runs", "status", "Failed");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting sorts every match locally; paging asks the server for the next page", () =>
            {
                StubHttpHandler stub = Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Run("chk_" + i.ToString("00"), "Run " + i.ToString("00"), "Passed", "2026-10-01T00:" + i.ToString("00") + ":00Z", 0)).ToList();
                stub.On("GET", "/api/v1/check-runs", body => StubHttpHandler.Response(HttpStatusCode.OK, TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray())));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=checks"))
                {
                    ChecksScreen screen = TuiEntityFixtures.Screen<ChecksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/check-runs", "pageNumber", "2");
                    host.Press("<");
                    screen.Grid.SortBy("created", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Id == "chk_25", "sorted by created descending");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu", "The row menu offers Open, Draft Release, View JSON, and Copy ID", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=checks"))
                {
                    ChecksScreen screen = TuiEntityFixtures.Screen<ChecksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Open", "Draft Release", "View JSON", "Copy ID" }) TuiCase.Contains(frame, item, "menu item " + item);
                    TuiCase.NotContains(frame, "Delete", "no delete on the list");
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "run_check_validates_previews_and_submits", "Run Check requires a vessel, shows the resolved profile and preflight, then posts the request", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/check-runs", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Run("chk_new", "Smoke", "Passed", "2026-10-03T00:00:00Z", 0));
                });
                stub.Json("GET", "/api/v1/check-runs/chk_new", Run("chk_new", "Smoke", "Passed", "2026-10-03T00:00:00Z", 0));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=checks"))
                {
                    ChecksScreen screen = TuiEntityFixtures.Screen<ChecksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is FormDialog, "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    TuiCase.Contains(host.Screen(), "Select a vessel to evaluate readiness.", "empty preflight");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => dialog.Status != null && dialog.Status.Contains("Select a vessel before running a check."), "vessel required");
                    AssertTrue(host.App.Modals.Top is FormDialog, "dialog stays open");
                    SelectField<string> vessel = (SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Vessel").Field!;
                    vessel.Choose(vessel.Options.First(o => o.Value == "vsl_1"));
                    TuiEntityFixtures.WaitForRequest(host, stub, "GET", "/api/v1/workflow-profiles/preview/vessels/vsl_1");
                    TuiEntityFixtures.WaitForRequest(host, stub, "GET", "/api/v1/vessels/vsl_1/readiness");
                    TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Default profile (Global)"), "resolved profile shown");
                    TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Ready"), "preflight shown");
                    SelectField<string> type = (SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Check Type").Field!;
                    AssertEqual(2, type.Options.Count, "types narrowed to the resolved profile");
                    ((InputField)dialog.Form.Rows.First(r => r.Label == "Label").Field!).Value = "Smoke";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted and closed");
                    CheckRunRequest sent = JsonHelper.Deserialize<CheckRunRequest>(posted!);
                    AssertEqual("vsl_1", sent.VesselId, "vessel in payload: " + posted);
                    AssertEqual("Smoke", sent.Label, "label in payload");
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/checks/chk_new", "navigated to the run");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "prefill_opens_run_check", "A Run Check hand-off opens the modal prefilled on the Checks tab", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments"))
                {
                    CheckRunRequest prefill = new CheckRunRequest();
                    prefill.VesselId = "vsl_1";
                    prefill.DeploymentId = "dpl_1";
                    prefill.Type = CheckRunTypeEnum.DeploymentVerification;
                    prefill.Label = "Deploy web verification";
                    NavigationPrefill.Set(host.Tui.Context, PrefillSlots.RunCheck, prefill, "/delivery?tab=checks");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is FormDialog, "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    AssertEqual("Deploy web verification", ((InputField)dialog.Form.Rows.First(r => r.Label == "Label").Field!).Value, "label");
                    AssertEqual("dpl_1", ((InputField)dialog.Form.Rows.First(r => r.Label == "Deployment ID").Field!).Value, "deployment");
                    AssertEqual("vsl_1", ((SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Vessel").Field!).Value, "vessel");
                    AssertFalse(NavigationPrefill.Has(host.Tui.Context, PrefillSlots.RunCheck), "prefill consumed");
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_panels_and_delete", "Check run detail shows comparison, results, artifacts, and output; Delete confirms", () =>
            {
                StubHttpHandler stub = Server();
                bool deleted = false;
                stub.On("DELETE", "/api/v1/check-runs/chk_2", body => { deleted = true; return StubHttpHandler.Response(HttpStatusCode.NoContent, ""); });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/checks/chk_2"))
                {
                    CheckRunScreen screen = TuiEntityFixtures.Screen<CheckRunScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Nightly tests", "title");
                    TuiCase.Contains(frame, "Exit Code", "overview");
                    TuiCase.Contains(frame, "Compare to Previous Run", "comparison tab");
                    AssertNotNull(screen.Comparison, "comparison computed");
                    AssertTrue(screen.Comparison!.HasRegression, "regression");
                    host.Press("]");
                    TuiCase.Contains(host.Screen(), "Regression detected", "comparison panel");
                    TuiCase.Contains(host.Screen(), "+2 failed", "failed delta");
                    host.Press("]");
                    TuiCase.Contains(host.Screen(), "Coverage", "results panel");
                    host.Press("]");
                    TuiCase.Contains(host.Screen(), "out/report.xml", "artifact");
                    host.Press("]");
                    TuiCase.Contains(host.Screen(), "3 tests failed", "output");
                    AssertTrue(screen.RunAction("delete"), "delete offered");
                    TuiCase.Contains(host.Screen(), "Delete check run \"chk_2\"?", "confirm text");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => deleted, "delete call");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_retry_and_draft_release", "Retry posts and opens the new run; Draft Release hands off a prefilled release", () =>
            {
                StubHttpHandler stub = Server();
                stub.Json("POST", "/api/v1/check-runs/chk_2/retry", Run("chk_3", "Nightly tests", "Passed", "2026-10-03T00:00:00Z", 0));
                stub.Json("GET", "/api/v1/check-runs/chk_3", Run("chk_3", "Nightly tests", "Passed", "2026-10-03T00:00:00Z", 0));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/checks/chk_2"))
                {
                    CheckRunScreen screen = TuiEntityFixtures.Screen<CheckRunScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    host.Press("r");
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/checks/chk_3", "opened the retried run");
                    CheckRunScreen retried = TuiEntityFixtures.Screen<CheckRunScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => retried.Entity != null, "retried loaded");
                    retried.RunAction("draft-release");
                    host.Pump();
                    AssertEqual("/releases/new", host.Tui.Context.Router.Current!.Path, "release draft route");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI checks", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/check-runs", TuiEntityFixtures.Page(
                Run("chk_2", "Nightly tests", "Failed", "2026-10-02T00:00:00Z", 3),
                Run("chk_1", "Nightly tests", "Passed", "2026-10-01T00:00:00Z", 1)));
            stub.Json("GET", "/api/v1/check-runs/chk_2", Run("chk_2", "Nightly tests", "Failed", "2026-10-02T00:00:00Z", 3));
            stub.Json("GET", "/api/v1/vessels", TuiEntityFixtures.Page("{\"Id\":\"vsl_1\",\"Name\":\"web-app\"}"));
            stub.Json("GET", "/api/v1/vessels/vsl_1", "{\"Id\":\"vsl_1\",\"Name\":\"web-app\"}");
            stub.Json("GET", "/api/v1/workflow-profiles/preview/vessels/vsl_1", "{\"ResolvedProfile\":{\"Id\":\"wfp_1\",\"Name\":\"Default profile\",\"Scope\":\"Global\",\"BuildCommand\":\"make\",\"UnitTestCommand\":\"make test\"},\"AvailableCheckTypes\":[\"Build\",\"UnitTest\"],\"CommandPreviews\":[{\"CheckType\":\"Build\",\"Command\":\"make\"}]}");
            stub.Json("GET", "/api/v1/vessels/vsl_1/readiness", "{\"VesselId\":\"vsl_1\",\"IsReady\":true,\"ErrorCount\":0,\"WarningCount\":0,\"Issues\":[]}");
            return stub;
        }

        private static string Run(string id, string label, string status, string created, int failed)
        {
            return "{\"Id\":\"" + id + "\",\"Label\":\"" + label + "\",\"Type\":\"UnitTest\",\"Source\":\"Armada\",\"Status\":\"" + status + "\",\"VesselId\":\"vsl_1\",\"Command\":\"make test\",\"ExitCode\":" + (failed > 1 ? "1" : "0")
                + ",\"Output\":\"" + failed + " tests failed\",\"TestSummary\":{\"Format\":\"junit\",\"Total\":10,\"Passed\":" + (10 - failed) + ",\"Failed\":" + failed + ",\"Skipped\":0}"
                + ",\"CoverageSummary\":{\"Format\":\"cobertura\",\"Lines\":{\"Covered\":80,\"Total\":100,\"Percentage\":80}}"
                + ",\"Artifacts\":[{\"Path\":\"out/report.xml\",\"SizeBytes\":2048,\"LastWriteUtc\":\"" + created + "\"}],\"DurationMs\":1500,\"CreatedUtc\":\"" + created + "\",\"LastUpdateUtc\":\"" + created + "\"}";
        }
    }
}
