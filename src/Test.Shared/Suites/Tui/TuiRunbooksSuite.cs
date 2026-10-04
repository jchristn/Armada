namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Delivery;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Runbooks tab and runbook detail (W5.6) against a stubbed server: open, KPIs, server filters, sort, paging, row
    /// menu, create form validation and submit, the execution hand-off, start execution, step checklist and progress,
    /// and Cancel Execution with confirmation.
    /// </summary>
    public sealed class TuiRunbooksSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Runbooks";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Runbooks tab shows KPIs, binding, and execution counts", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=runbooks"))
                {
                    RunbooksScreen screen = TuiEntityFixtures.Screen<RunbooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    host.PumpUntil(() => host.Screen().Contains("1 running"), 2000);
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Total Runbooks 2", "kpi total");
                    TuiCase.Contains(frame, "Executions 2", "kpi executions");
                    TuiCase.Contains(frame, "Running 1", "kpi running");
                    TuiCase.Contains(frame, "Release checklist", "row");
                    TuiCase.Contains(frame, "2 total, 1 running", "execution counts");
                    TuiCase.Contains(frame, "Tenant-wide", "visibility");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_sort_page", "Search and state filters go to the server; sort reads all; paging asks for the next page", () =>
            {
                StubHttpHandler stub = Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Runbook("rbk_" + i.ToString("00"), "Runbook " + i.ToString("00"), true)).ToList();
                stub.On("GET", "/api/v1/runbooks", body => StubHttpHandler.Response(HttpStatusCode.OK, TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray())));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=runbooks&state=inactive"))
                {
                    RunbooksScreen screen = TuiEntityFixtures.Screen<RunbooksScreen>(host);
                    TuiEntityFixtures.WaitForRequest(host, stub, "GET /api/v1/runbooks?active=false");
                    host.Press("/");
                    host.Type("deploy");
                    TuiEntityFixtures.WaitForRequest(host, stub, "search=deploy");
                    host.Press("esc");
                    screen.Filters.Clear();
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForRequest(host, stub, "pageNumber=2");
                    host.Press("<");
                    screen.Grid.SortBy("title", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Title == "Runbook 25", "sorted descending");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu", "The row menu lists Open, Duplicate, View JSON, Running, and Delete", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=runbooks"))
                {
                    RunbooksScreen screen = TuiEntityFixtures.Screen<RunbooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Open", "Duplicate", "View JSON", "Running: 1", "Delete" }) TuiCase.Contains(frame, item, "menu item " + item);
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "Create Runbook keeps the dialog open on a blank title, then posts", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/runbooks", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Runbook("rbk_new", "Rollback drill", true));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=runbooks"))
                {
                    RunbooksScreen screen = TuiEntityFixtures.Screen<RunbooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Runbook"), "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    InputField title = (InputField)dialog.Form.Rows.First(r => r.Label == "Title").Field!;
                    title.Value = "";
                    host.Press("ctrl+s");
                    AssertTrue(host.App.Modals.IsActive, "dialog stays open");
                    TuiCase.Contains(host.Screen(), "This field is required.", "required error");
                    AssertNull(posted, "nothing posted");
                    title.Value = "Rollback drill";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted and closed");
                    AssertTrue(posted!.Contains("\"Title\":\"Rollback drill\""), "title: " + posted);
                    AssertTrue(posted.Contains("\"FileName\":\"RUNBOOK.md\""), "file name");
                    AssertTrue(posted.Contains("\"Scope\":\"TenantWide\""), "scope");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "handoff_and_start_execution", "A deployment hand-off is announced, carried to the runbook, and seeds Start Execution", () =>
            {
                StubHttpHandler stub = Server();
                string? started = null;
                stub.On("POST", "/api/v1/runbooks/rbk_1/executions", body =>
                {
                    started = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Execution("rex_new", "rbk_1", "Running"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/delivery?tab=deployments"))
                {
                    RunbookExecutionStartRequest prefill = new RunbookExecutionStartRequest { Title = "Deploy web Runbook", DeploymentId = "dpl_1", EnvironmentName = "staging", Notes = "handoff" };
                    NavigationPrefill.Set(host.Tui.Context, PrefillSlots.RunbookExecution, prefill, "/delivery?tab=runbooks");
                    host.Pump();
                    RunbooksScreen list = TuiEntityFixtures.Screen<RunbooksScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => list.Grid.Rows.Count == 2, "rows");
                    TuiCase.Contains(host.Screen(), "handed off a prefilled runbook execution context", "notice");
                    host.Press("enter");
                    RunbookScreen detail = TuiEntityFixtures.Screen<RunbookScreen>(host);
                    AssertNotNull(detail.Prefill, "prefill carried");
                    TuiEntityFixtures.WaitFor(host, () => detail.Entity != null, "loaded");
                    host.Press("x");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Start Execution"), "start form");
                    TuiCase.Contains(host.Screen(), "Target", "parameter field");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => started != null, "started");
                    AssertTrue(started!.Contains("\"DeploymentId\":\"dpl_1\""), "deployment carried: " + started);
                    AssertTrue(started.Contains("\"EnvironmentName\":\"staging\""), "environment carried");
                    AssertTrue(started.Contains("\"target\":\"prod\""), "parameter default");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_progress_and_cancel", "Detail shows panels; Space toggles a step, Save Progress puts it; Cancel Execution confirms", () =>
            {
                StubHttpHandler stub = Server();
                List<string> puts = new List<string>();
                stub.On("PUT", "/api/v1/runbook-executions/rex_1", body =>
                {
                    lock (puts) puts.Add(body);
                    return StubHttpHandler.Response(HttpStatusCode.OK, Execution("rex_1", "rbk_1", body.Contains("Cancelled") ? "Cancelled" : "Running"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/runbooks/rbk_1?executionId=rex_1"))
                {
                    RunbookScreen screen = TuiEntityFixtures.Screen<RunbookScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null && screen.SelectedExecution != null, "loaded");
                    AssertEqual("progress", screen.ActivePanel, "execution deep link opens progress");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Release checklist", "title");
                    TuiCase.Contains(frame, "Execution Progress", "panel tab");
                    TuiCase.Contains(frame, "[ ] Freeze merges", "step checklist");
                    foreach (string a in new[] { "Start Execution", "Save Execution", "Mark Completed", "Cancel Execution", "Duplicate", "Delete" })
                        AssertTrue(screen.VisibleActions().Contains(a), "action " + a);
                    screen.StepChecklist.ToggleCurrent();
                    TuiCase.Contains(host.Screen(), "[x] Freeze merges", "toggled");
                    screen.ProgressForm.View.RequestSave();
                    TuiEntityFixtures.WaitFor(host, () => { lock (puts) return puts.Count == 1; }, "save progress");
                    AssertTrue(puts[0].Contains("\"CompletedStepIds\":[\"rbs_1\"]"), "completed steps: " + puts[0]);
                    AssertTrue(screen.RunAction("cancel-execution"), "cancel offered");
                    TuiCase.Contains(host.Screen(), "Cancel Execution", "confirm title");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => { lock (puts) return puts.Count == 2; }, "cancel put");
                    AssertTrue(puts[1].Contains("\"Status\":\"Cancelled\""), "cancelled status");
                    host.Press("alt+1");
                    AssertEqual("overview", screen.ActivePanel, "back to overview");
                    TuiCase.Contains(host.Screen(), "pbk_1", "playbook id");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_edit_steps_and_save", "Runbook panel edits parameters and steps and saves with PUT", () =>
            {
                StubHttpHandler stub = Server();
                string? put = null;
                stub.On("PUT", "/api/v1/runbooks/rbk_1", body =>
                {
                    put = body;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Runbook("rbk_1", "Release checklist", true));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/runbooks/rbk_1"))
                {
                    RunbookScreen screen = TuiEntityFixtures.Screen<RunbookScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    screen.ShowPanel("runbook");
                    AssertEqual(2, screen.StepList.Items.Count, "steps loaded");
                    screen.StepList.AddNew();
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Add Step"), "step editor");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    ((InputField)dialog.Form.Rows.First(r => r.Label == "Title").Field!).Value = "Verify";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => screen.StepList.Items.Count == 3 && !host.App.Modals.IsActive, "step added");
                    AssertTrue(screen.Editor.View.IsDirty, "form dirty");
                    screen.Editor.View.RequestSave();
                    TuiEntityFixtures.WaitFor(host, () => put != null, "saved");
                    AssertTrue(put!.Contains("\"Title\":\"Verify\""), "new step in payload: " + put);
                    AssertTrue(put.Contains("\"Name\":\"target\""), "parameters in payload");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "regular_user_scoping", "A regular user cannot delete or edit a tenant-wide runbook", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.RegularUserServer();
                AddRunbooks(stub);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/runbooks/rbk_1"))
                {
                    RunbookScreen screen = TuiEntityFixtures.Screen<RunbookScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    AssertFalse(screen.VisibleActions().Contains("Delete"), "no delete");
                    AssertFalse(screen.VisibleActions().Contains("Save Runbook"), "no save");
                    AssertTrue(screen.VisibleActions().Contains("Start Execution"), "can start");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI runbooks", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            AddRunbooks(stub);
            return stub;
        }

        private static void AddRunbooks(StubHttpHandler stub)
        {
            stub.Json("GET", "/api/v1/runbooks", TuiEntityFixtures.Page(Runbook("rbk_1", "Release checklist", true), Runbook("rbk_2", "Hotfix drill", false)));
            stub.Json("GET", "/api/v1/runbooks/rbk_1", Runbook("rbk_1", "Release checklist", true));
            stub.Json("GET", "/api/v1/runbook-executions", TuiEntityFixtures.Page(Execution("rex_1", "rbk_1", "Running"), Execution("rex_2", "rbk_1", "Completed")));
        }

        private static string Runbook(string id, string title, bool active)
        {
            return "{\"Id\":\"" + id + "\",\"PlaybookId\":\"pbk_1\",\"TenantId\":\"ten_default\",\"UserId\":\"usr_admin\",\"Scope\":\"TenantWide\",\"FileName\":\"RUNBOOK.md\",\"Title\":\"" + title + "\",\"Description\":\"Steps\",\"EnvironmentName\":\"staging\",\"DefaultCheckType\":\"SmokeTest\","
                + "\"Parameters\":[{\"Name\":\"target\",\"Label\":\"Target\",\"DefaultValue\":\"prod\",\"Required\":true}],"
                + "\"Steps\":[{\"Id\":\"rbs_1\",\"Title\":\"Freeze merges\",\"Instructions\":\"Pause the queue\"},{\"Id\":\"rbs_2\",\"Title\":\"Tag release\",\"Instructions\":\"git tag\"}],"
                + "\"OverviewMarkdown\":\"# Release\",\"Active\":" + (active ? "true" : "false") + ",\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }

        private static string Execution(string id, string runbookId, string status)
        {
            return "{\"Id\":\"" + id + "\",\"RunbookId\":\"" + runbookId + "\",\"PlaybookId\":\"pbk_1\",\"Title\":\"Run " + id + "\",\"Status\":\"" + status + "\",\"EnvironmentName\":\"staging\",\"CheckType\":\"SmokeTest\",\"DeploymentId\":\"dpl_1\","
                + "\"ParameterValues\":{},\"CompletedStepIds\":[],\"StepNotes\":{},\"StartedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-01T00:00:00Z\"}";
        }
    }
}
