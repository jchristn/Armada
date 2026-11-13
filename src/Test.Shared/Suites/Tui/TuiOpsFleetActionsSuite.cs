namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Fleet Actions (W3.7) against a stubbed client: the actions grid, the create form with validation and the
    /// settings default timeout, hiding a built-in, the run flow from the vessel picker through the review step to
    /// the run page, the runs tab cancel, and the run page with its target drawer and live refresh; the form's mission
    /// mode and applicability commands, the inputs step rejecting an invalid package identifier, a follow-up run from a
    /// finished run, and the report view of a report-only Mission run.
    /// </summary>
    public sealed class TuiOpsFleetActionsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.FleetActions";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "actions_grid_and_form", "Lists actions, validates and saves a new action, hides a built-in", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 45, "/fleet-actions", stub))
                {
                    AssertTrue(host.WaitForText("Fast-forward default branch"), "rows\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Actions]", "tab");
                    TuiCase.Contains(frame, "# Built-in", "built-in source");
                    TuiCase.Contains(frame, "Custom", "custom source");
                    TuiCase.Contains(frame, "Run action... R", "run toolbar");
                    host.Press("n");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "form");
                    TuiCase.Contains(host.Screen(), "New fleet action", "form title");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Name is required."), "name validation");
                    host.Type("Show status");
                    host.Press("tab").Press("tab").Press("tab");
                    host.Type("git status {{bogus}}");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Unknown template variable: bogus"), "unknown variable\n" + host.Screen());
                    for (int i = 0; i < 10; i++) host.Press("backspace");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Saw("POST", "/api/v1/fleet-actions", r => r.TryBodyAs<FleetActionUpsertRequest>()?.Name == "Show status")), "create call\n" + host.Screen());
                    StubRequest create = stub.Last("POST", "/api/v1/fleet-actions");
                    FleetActionUpsertRequest created = create.BodyAs<FleetActionUpsertRequest>();
                    AssertEqual("Show status", created.Name, "create name: " + create.Body);
                    AssertEqual(120, created.TimeoutSeconds, "create timeout: " + create.Body);
                    AssertEqual(4, created.DefaultConcurrency, "create concurrency: " + create.Body);
                    AssertEqual(FleetActionKindEnum.Command, created.Kind, "create kind: " + create.Body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Fleet action \"Show status\" saved."))), "saved toast");

                    FleetActionsScreen screen = Content<FleetActionsScreen>(host);
                    host.Press("home");
                    AssertEqual("fa_builtin", screen.Grid.Current?.Id, "built-in first");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "is a built-in action. Deleting it hides", "hide text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/fleet-actions/fa_builtin") == 1), "delete call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Warning && t.Text.Contains("Built-in action \"Fast-forward default branch\" hidden."))), "hidden toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "run_flow", "Run picks vessels, previews, reviews, starts the run, and opens it", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 45, "/fleet-actions", stub))
                {
                    AssertTrue(host.WaitForText("Echo name"), "rows");
                    FleetActionsScreen screen = Content<FleetActionsScreen>(host);
                    host.Press("home").Press("down");
                    AssertEqual("fa_echo", screen.Grid.Current?.Id, "echo selected");
                    host.Press("r");
                    AssertTrue(host.WaitForText("Choose vessels to run on"), "picker");
                    AssertTrue(host.WaitForText("beta-repo"), "vessels listed\n" + host.Screen());
                    host.Press("ctrl+s");
                    TuiCase.Contains(host.Screen(), "0 vessels selected", "nothing selected yet");
                    host.Press("ctrl+a");
                    AssertTrue(host.WaitForText("2 vessels selected"), "select all");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Run fleet action"), "run dialog\n" + host.Screen());
                    FleetActionRunFlow flow = screen.LastFlow ?? throw new AssertionException("run flow started");
                    AssertTrue(host.PumpUntil(() => flow.SelectedAction()?.Id == "fa_echo" && flow.PreviewVessel != null), "configure step loaded the action and the preview vessel\n" + host.Screen());
                    AssertTrue(host.WaitForText("Preview for alpha-repo"), "preview vessel\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "echo alpha-repo", "rendered preview");
                    TuiCase.Contains(host.Screen(), "(and 1 more vessel)", "more vessels");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Run on 2 vessels"), "review step\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Vessels with uncommitted changes are skipped.", "clean-tree note");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/fleet-actions/fa_echo/run") == 1), "run call");
                    StubRequest run = stub.Last("POST", "/api/v1/fleet-actions/fa_echo/run");
                    FleetActionRunRequest started = run.BodyAs<FleetActionRunRequest>();
                    AssertEqual("vsl_a,vsl_b", String.Join(",", started.VesselIds.OrderBy(v => v, StringComparer.Ordinal)), "run vessels: " + run.Body);
                    AssertEqual(2, started.Concurrency, "run concurrency: " + run.Body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Fleet action started on 2 vessels."))), "started toast");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/fleet-actions/runs/far_1"), "navigated to the run");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "run_flow_waits_for_actions", "Run configure step: the preview vessel can arrive before the action list; Ctrl+S is refused until the action is chosen", () =>
            {
                // The configure step loads the saved actions and the preview vessel with independent calls, so
                // "Preview for <vessel>" on screen does not mean the action is selected. The end-to-end flow test used
                // that text as its precondition for Ctrl+S and failed on slow runners when the vessel won the race.
                // Hold the flow's action list (the second enumerate; the first fills the grid) to force that order.
                StubHttpHandler stub = Stub();
                int enumerateCalls = 0;
                using (ManualResetEventSlim release = new ManualResetEventSlim(false))
                {
                    stub.On("POST", "/api/v1/fleet-actions/enumerate", body =>
                    {
                        if (Interlocked.Increment(ref enumerateCalls) > 1) release.Wait(TimeSpan.FromSeconds(30));
                        return StubHttpHandler.Response(HttpStatusCode.OK, _ActionsJson);
                    });
                    using (TuiTestHost host = TuiCase.SignedIn(150, 45, "/fleet-actions", stub))
                    {
                        AssertTrue(host.WaitForText("Echo name"), "rows");
                        FleetActionsScreen screen = Content<FleetActionsScreen>(host);
                        host.Press("home").Press("down");
                        AssertEqual("fa_echo", screen.Grid.Current?.Id, "echo selected");
                        host.Press("r");
                        AssertTrue(host.WaitForText("beta-repo"), "vessels listed\n" + host.Screen());
                        host.Press("ctrl+a");
                        AssertTrue(host.WaitForText("2 vessels selected"), "select all");
                        host.Press("ctrl+s");
                        AssertTrue(host.WaitForText("Preview for alpha-repo"), "preview vessel arrives first\n" + host.Screen());
                        FleetActionRunFlow flow = screen.LastFlow ?? throw new AssertionException("run flow started");
                        AssertNull(flow.SelectedAction(), "the action list is still loading");

                        host.Press("ctrl+s");
                        AssertTrue(host.WaitForText("A selection is required."), "Ctrl+S before the action arrives is refused\n" + host.Screen());
                        AssertTrue(host.App.Modals.Top == flow.Dialog, "still on the configure step");

                        release.Set();
                        AssertTrue(host.PumpUntil(() => flow.SelectedAction()?.Id == "fa_echo"), "the action arrives and is preselected");
                        host.Press("ctrl+s");
                        AssertTrue(host.WaitForText("Run on 2 vessels"), "review step\n" + host.Screen());
                    }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "runs_tab", "Runs tab filters by status and cancels an active run after confirmation", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(200, 45, "/fleet-actions?tab=runs", stub))
                {
                    AssertTrue(host.WaitForText("Echo name"), "runs\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Runs]", "tab");
                    TuiCase.Contains(frame, "1 succeeded, 0 failed, 0 skipped of 2", "progress");
                    TuiCase.Contains(frame, "Ad hoc", "ad hoc tag");
                    FleetActionRunsScreen screen = Content<FleetActionRunsScreen>(host);
                    screen.StatusFilter.Choose(screen.StatusFilter.Options.First(o => o.Value == "Running"));
                    AssertTrue(host.PumpUntil(() => stub.Saw("POST", "/api/v1/fleet-action-runs/enumerate", r => r.TryBodyAs<Armada.Client.Models.FleetActionRunEnumerateQuery>()?.Status == FleetActionRunStatusEnum.Running)), "server status filter");
                    host.Press("home");
                    host.Press("x");
                    AssertTrue(host.WaitForText("Keep running"), "cancel confirm\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Pending targets are cancelled", "cancel text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/fleet-action-runs/far_1/cancel") == 1), "cancel call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Warning && t.Text.Contains("Run \"Echo name\" cancelled."))), "cancel toast");
                    host.Press("enter");
                    AssertEqual("/fleet-actions/runs/far_1", host.Tui.Context.Router.Current!.FullPath, "Enter opens the run");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "run_detail", "Run page shows progress and targets, opens the target drawer and output, and refreshes while live", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(190, 45, "/fleet-actions/runs/far_1", stub))
                {
                    AssertTrue(host.WaitForText("alpha-repo"), "targets\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Echo name", "heading");
                    TuiCase.Contains(frame, "Live: refreshing every 5 s.", "live state");
                    TuiCase.Contains(frame, "1 succeeded, 0 failed, 0 skipped of 2 (50% finished)", "progress");
                    TuiCase.Contains(frame, "Command (snapshot)", "snapshot");
                    TuiCase.Contains(frame, "[Truncated]", "truncated tag");
                    TuiCase.Contains(frame, "Command exited with a non-zero code", "reason label");
                    FleetActionRunScreen screen = (FleetActionRunScreen)host.Tui.Shell.Screen!;
                    int loads = screen.Loads;
                    AssertTrue(host.PumpUntil(() => screen.Loads > loads, 8000), "polled while active");
                    host.Press("enter");
                    AssertTrue(host.WaitForText("Rendered command"), "drawer\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "hello from alpha", "stdout tail");
                    TuiCase.Contains(host.Screen(), "Auto-refresh paused while a panel is open.", "paused state");
                    host.Press("y");
                    AssertEqual("echo alpha-repo", host.Tui.Context.Clipboard.LastCopied, "copied command");
                    host.Press("o");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "full output");
                    TuiCase.Contains(host.Screen(), "alpha-repo: Standard output", "output title");
                    host.Press("esc");
                    host.Press("esc");
                    AssertFalse(screen.TargetDrawer.IsOpen, "drawer closed");
                    host.Press("x");
                    AssertTrue(host.WaitForText("Keep running"), "cancel from detail");
                    host.Press("n");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "form_mission_mode_and_applicability", "The form sends the mission mode and both applicability commands for a Mission action", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 45, "/fleet-actions", stub))
                {
                    AssertTrue(host.WaitForText("Echo name"), "rows\n" + host.Screen());
                    host.Press("n");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is OpsFormDialog), "form");
                    OpsFormDialog dialog = (OpsFormDialog)host.App.Modals.Top!;
                    SelectField<string> mode = Field<SelectField<string>>(dialog, "Mission mode");
                    AssertFalse(mode.Visible, "mission mode is hidden for Command actions");
                    host.Type("Audit docs");
                    SelectField<string> kind = Field<SelectField<string>>(dialog, "Kind");
                    kind.Choose(kind.Options.First(o => o.Value == "Mission"));
                    AssertTrue(mode.Visible, "mission mode shows for Mission actions");
                    Field<OpsTextArea>(dialog, "Prompt template").Text = "Review the docs of {{vessel.name}}";
                    mode.Choose(mode.Options.First(o => o.Value == "Audit"));
                    TuiCase.Contains(host.Screen(), "Audit (report-only)", "report-only mode label");
                    Field<InputField>(dialog, "Applicability check").Value = "test -d docs";
                    Field<InputField>(dialog, "Applicability (Windows)").Value = "Test-Path docs";
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Saw("POST", "/api/v1/fleet-actions", r => r.TryBodyAs<FleetActionUpsertRequest>()?.Name == "Audit docs")), "create call\n" + host.Screen());
                    StubRequest create = stub.Last("POST", "/api/v1/fleet-actions");
                    FleetActionUpsertRequest created = create.BodyAs<FleetActionUpsertRequest>();
                    AssertEqual(FleetActionKindEnum.Mission, created.Kind, "kind: " + create.Body);
                    AssertEqual(MissionModeEnum.Audit, created.MissionMode, "mission mode: " + create.Body);
                    AssertEqual("test -d docs", created.ApplicabilityCommand, "applicability: " + create.Body);
                    AssertEqual("Test-Path docs", created.ApplicabilityCommandWindows, "windows applicability: " + create.Body);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "run_flow_package_input", "The inputs step rejects an invalid package identifier and sends a valid one", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("POST", "/api/v1/fleet-actions/enumerate", _PackageActionsJson);
                stub.Json("POST", "/api/v1/fleet-actions/fa_pkg/run", "{\"RunId\":\"far_9\",\"ActionId\":\"fa_pkg\",\"Kind\":\"Mission\",\"Status\":\"Pending\",\"TargetCount\":2,\"Concurrency\":2}");
                using (TuiTestHost host = TuiCase.SignedIn(150, 45, "/fleet-actions", stub))
                {
                    AssertTrue(host.WaitForText("Update a package"), "rows\n" + host.Screen());
                    FleetActionsScreen screen = Content<FleetActionsScreen>(host);
                    host.Press("home");
                    AssertEqual("fa_pkg", screen.Grid.Current?.Id, "package action selected");
                    host.Press("r");
                    AssertTrue(host.WaitForText("beta-repo"), "vessels listed\n" + host.Screen());
                    host.Press("ctrl+a");
                    AssertTrue(host.WaitForText("2 vessels selected"), "select all");
                    host.Press("ctrl+s");
                    FleetActionRunFlow flow = screen.LastFlow ?? throw new AssertionException("run flow started");
                    AssertTrue(host.PumpUntil(() => flow.SelectedAction()?.Id == "fa_pkg"), "action selected\n" + host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Action inputs"), "inputs step\n" + host.Screen());
                    host.Type("bad;rm -rf");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Enter a package identifier"), "invalid package rejected\n" + host.Screen());
                    AssertTrue(host.App.Modals.Top == flow.Dialog, "still on the inputs step");
                    for (int i = 0; i < 10; i++) host.Press("backspace");
                    host.Type("@scope/name");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Run on 2 vessels"), "review step\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "package = @scope/name", "input value reviewed");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/fleet-actions/fa_pkg/run") == 1), "run call");
                    StubRequest run = stub.Last("POST", "/api/v1/fleet-actions/fa_pkg/run");
                    FleetActionRunRequest started = run.BodyAs<FleetActionRunRequest>();
                    AssertEqual("@scope/name", started.InputValues.TryGetValue("package", out string? pkg) ? pkg : null, "package input: " + run.Body);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "follow_up_run", "Follow-up run from a finished scan suggests the linked fix and posts FromRunId with Findings and Failed", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("POST", "/api/v1/fleet-actions/enumerate", _ScanFixActionsJson);
                string scanRun = "{\"Id\":\"far_3\",\"ActionId\":\"fa_scan\",\"ActionName\":\"Run tests\",\"Kind\":\"Command\",\"CommandText\":\"{{vessel.testCommand}}\",\"TimeoutSeconds\":600,\"Concurrency\":2,\"Status\":\"CompletedWithFailures\",\"TargetCount\":2,\"SucceededCount\":1,\"FailedCount\":1,\"StartedUtc\":\"2026-10-04T10:00:00Z\",\"CompletedUtc\":\"2026-10-04T10:05:00Z\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:05:00Z\"}";
                stub.Json("GET", "/api/v1/fleet-action-runs/far_3", "{\"Run\":" + scanRun + ",\"Targets\":[]}");
                stub.Json("POST", "/api/v1/fleet-action-runs/far_3/targets/enumerate", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":0,\"Objects\":[]}");
                stub.Json("POST", "/api/v1/fleet-actions/fa_fix/run", "{\"RunId\":\"far_4\",\"ActionId\":\"fa_fix\",\"Kind\":\"Mission\",\"Status\":\"Pending\",\"TargetCount\":1,\"Concurrency\":4}");
                using (TuiTestHost host = TuiCase.SignedIn(190, 45, "/fleet-actions/runs/far_3", stub))
                {
                    AssertTrue(host.WaitForText("Follow-up run..."), "follow-up button\n" + host.Screen());
                    FleetActionRunScreen screen = (FleetActionRunScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.CurrentRun != null), "run loaded");
                    host.Press("f");
                    AssertTrue(host.WaitForText("Follow-up of run far_3"), "follow-up dialog\n" + host.Screen());
                    FleetActionRunFlow flow = screen.LastFollowUp ?? throw new AssertionException("follow-up flow started");
                    AssertTrue(host.PumpUntil(() => flow.SelectedAction()?.Id == "fa_fix"), "linked fix action is suggested first\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "[Suggested]", "suggested marker");
                    AssertEqual("Failed,Findings", String.Join(",", flow.FromStatuses.Values.Select(v => v.ToString()).OrderBy(v => v, StringComparer.Ordinal)), "default statuses");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Start follow-up run"), "review step\n" + host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/fleet-actions/fa_fix/run") == 1), "run call");
                    StubRequest run = stub.Last("POST", "/api/v1/fleet-actions/fa_fix/run");
                    FleetActionRunRequest started = run.BodyAs<FleetActionRunRequest>();
                    AssertEqual("far_3", started.FromRunId, "from run: " + run.Body);
                    AssertEqual("Failed,Findings", String.Join(",", (started.FromTargetStatuses ?? new List<FleetActionTargetStatusEnum>()).Select(v => v.ToString()).OrderBy(v => v, StringComparer.Ordinal)), "from statuses: " + run.Body);
                    AssertEqual(0, started.VesselIds.Count, "no vessel ids: " + run.Body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/fleet-actions/runs/far_4"), "navigated to the new run");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "report_view", "A report-only Mission run advertises and shows every target's report under vessel and status headings", () =>
            {
                StubHttpHandler stub = Stub();
                string auditRun = "{\"Id\":\"far_5\",\"ActionId\":\"fa_audit\",\"ActionName\":\"Security review\",\"Kind\":\"Mission\",\"MissionMode\":\"Audit\",\"PromptTemplate\":\"Review {{vessel.name}}\",\"Concurrency\":2,\"Status\":\"Completed\",\"TargetCount\":2,\"SucceededCount\":2,\"StartedUtc\":\"2026-10-04T10:00:00Z\",\"CompletedUtc\":\"2026-10-04T10:30:00Z\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:30:00Z\"}";
                stub.Json("GET", "/api/v1/fleet-action-runs/far_5", "{\"Run\":" + auditRun + ",\"Targets\":[]}");
                stub.Json("POST", "/api/v1/fleet-action-runs/far_5/targets/enumerate", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":100,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                    "{\"Id\":\"fat_5a\",\"RunId\":\"far_5\",\"VesselId\":\"vsl_a\",\"VesselName\":\"alpha-repo\",\"Status\":\"Succeeded\",\"OutputLength\":30,\"VoyageId\":\"vyg_a\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}," +
                    "{\"Id\":\"fat_5b\",\"RunId\":\"far_5\",\"VesselId\":\"vsl_b\",\"VesselName\":\"beta-repo\",\"Status\":\"Succeeded\",\"OutputLength\":30,\"VoyageId\":\"vyg_b\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}]}");
                stub.Json("GET", "/api/v1/fleet-action-runs/far_5/targets/fat_5a", "{\"Id\":\"fat_5a\",\"RunId\":\"far_5\",\"VesselId\":\"vsl_a\",\"VesselName\":\"alpha-repo\",\"Status\":\"Succeeded\",\"OutputText\":\"Alpha has no SQL injection risks.\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}");
                stub.Json("GET", "/api/v1/fleet-action-runs/far_5/targets/fat_5b", "{\"Id\":\"fat_5b\",\"RunId\":\"far_5\",\"VesselId\":\"vsl_b\",\"VesselName\":\"beta-repo\",\"Status\":\"Succeeded\",\"OutputText\":\"Beta stores a token in config.\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}");
                using (TuiTestHost host = TuiCase.SignedIn(190, 45, "/fleet-actions/runs/far_5", stub))
                {
                    AssertTrue(host.WaitForText("beta-repo"), "targets\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Report-only]", "report-only subtitle marker");
                    TuiCase.Contains(frame, "Press v to view the report.", "report key advertised in the summary");
                    TuiCase.Contains(frame, "View report", "report button");
                    FleetActionRunScreen screen = (FleetActionRunScreen)host.Tui.Shell.Screen!;
                    host.Press("v");
                    AssertTrue(host.PumpUntil(() => screen.LastReport != null && host.App.Modals.IsActive), "report shown\n" + host.Screen());
                    string report = screen.LastReport!;
                    TuiCase.Contains(report, "## alpha-repo (Succeeded)", "first heading");
                    TuiCase.Contains(report, "Alpha has no SQL injection risks.", "first report");
                    TuiCase.Contains(report, "## beta-repo (Succeeded)", "second heading");
                    TuiCase.Contains(report, "Beta stores a token in config.", "second report");
                    AssertTrue(report.IndexOf("alpha-repo", StringComparison.Ordinal) < report.IndexOf("beta-repo", StringComparison.Ordinal), "targets in order");
                    TuiCase.Contains(host.Screen(), "Report: Security review", "report title");
                    TuiCase.Contains(host.Screen(), "Alpha has no SQL injection risks.", "report rendered");
                    AssertEqual(1, stub.CountFor("GET", "/api/v1/fleet-action-runs/far_5/targets/fat_5b"), "second target read once");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI fleet actions", cases: cases);
        }

        private static readonly string _ActionsJson = "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                "{\"Id\":\"fa_builtin\",\"Name\":\"Fast-forward default branch\",\"Kind\":\"Command\",\"CommandText\":\"git pull --ff-only\",\"TimeoutSeconds\":300,\"DefaultConcurrency\":4,\"RequiresCleanWorkingTree\":true,\"IsBuiltIn\":true,\"Active\":true,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}," +
                "{\"Id\":\"fa_echo\",\"Name\":\"Echo name\",\"Description\":\"Prints the vessel name\",\"Kind\":\"Command\",\"CommandText\":\"echo {{vessel.name}}\",\"TimeoutSeconds\":60,\"DefaultConcurrency\":2,\"RequiresCleanWorkingTree\":true,\"IsBuiltIn\":false,\"Active\":true,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}]}";

        private static readonly string _PackageActionsJson = "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[" +
                "{\"Id\":\"fa_pkg\",\"Name\":\"Update a package\",\"Kind\":\"Mission\",\"PromptTemplate\":\"Update {{input.package}} in {{vessel.name}}\",\"DefaultConcurrency\":2,\"Inputs\":[{\"Name\":\"package\",\"Type\":\"package\",\"Required\":true,\"Description\":\"Package identifier\"}],\"Active\":true,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}]}";

        private static readonly string _ScanFixActionsJson = "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":3,\"Objects\":[" +
                "{\"Id\":\"fa_echo\",\"Name\":\"Echo name\",\"Kind\":\"Command\",\"CommandText\":\"echo {{vessel.name}}\",\"TimeoutSeconds\":60,\"DefaultConcurrency\":2,\"Active\":true,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}," +
                "{\"Id\":\"fa_scan\",\"Name\":\"Run tests\",\"Kind\":\"Command\",\"CommandText\":\"{{vessel.testCommand}}\",\"TimeoutSeconds\":600,\"DefaultConcurrency\":2,\"IsBuiltIn\":true,\"BuiltInKey\":\"run-tests\",\"Active\":true,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}," +
                "{\"Id\":\"fa_fix\",\"Name\":\"Fix failing tests\",\"Kind\":\"Mission\",\"PromptTemplate\":\"Fix {{scan.summary}}\",\"DefaultConcurrency\":4,\"IsBuiltIn\":true,\"BuiltInKey\":\"fix-failing-tests\",\"ScanSourceActionKey\":\"run-tests\",\"Active\":true,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}]}";

        private static TField Field<TField>(OpsFormDialog dialog, string label) where TField : class
        {
            Armada.Tui.Widgets.FormRow row = dialog.Form.Rows.First(r => r.Label == label);
            return (TField)(object)row.Field!;
        }

        private static T Content<T>(TuiTestHost host) where T : class
        {
            HubScreen hub = (HubScreen)host.Tui.Shell.Screen!;
            return (T)(object)hub.Content;
        }

        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/settings", "{\"FleetActions\":{\"DefaultTimeoutSeconds\":120}}");
            stub.Json("POST", "/api/v1/fleet-actions/enumerate", _ActionsJson);
            stub.Json("POST", "/api/v1/fleet-actions", "{\"Id\":\"fa_new\",\"Name\":\"Show status\",\"Kind\":\"Command\",\"CommandText\":\"git status\",\"TimeoutSeconds\":120,\"DefaultConcurrency\":4,\"Active\":true}");
            stub.On("DELETE", "/api/v1/fleet-actions/fa_builtin", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_a\",\"Name\":\"alpha-repo\",\"WorkingDirectory\":\"/src/alpha\",\"DefaultBranch\":\"main\",\"FleetId\":\"flt_1\"},{\"Id\":\"vsl_b\",\"Name\":\"beta-repo\",\"WorkingDirectory\":\"/src/beta\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":2}");
            stub.Json("GET", "/api/v1/fleets", "{\"Success\":true,\"Objects\":[{\"Id\":\"flt_1\",\"Name\":\"Core\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/vessels/vsl_a", "{\"Id\":\"vsl_a\",\"Name\":\"alpha-repo\",\"WorkingDirectory\":\"/src/alpha\",\"DefaultBranch\":\"main\"}");
            stub.Json("GET", "/api/v1/pipelines", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/personas", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("POST", "/api/v1/fleet-actions/fa_echo/run", "{\"RunId\":\"far_1\",\"ActionId\":\"fa_echo\",\"Kind\":\"Command\",\"Status\":\"Pending\",\"TargetCount\":2,\"Concurrency\":2}");
            string run = "{\"Id\":\"far_1\",\"ActionId\":\"fa_echo\",\"ActionName\":\"Echo name\",\"Kind\":\"Command\",\"CommandText\":\"echo {{vessel.name}}\",\"TimeoutSeconds\":60,\"RequiresCleanWorkingTree\":true,\"Concurrency\":2,\"Status\":\"Running\",\"TargetCount\":2,\"SucceededCount\":1,\"FailedCount\":0,\"SkippedCount\":0,\"CancelledCount\":0,\"StartedUtc\":\"2026-10-04T10:00:00Z\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}";
            string adhoc = "{\"Id\":\"far_2\",\"ActionName\":\"Quick look\",\"Kind\":\"Command\",\"CommandText\":\"ls\",\"TimeoutSeconds\":60,\"Concurrency\":1,\"Status\":\"Completed\",\"TargetCount\":1,\"SucceededCount\":1,\"CreatedUtc\":\"2026-10-04T08:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T08:00:00Z\"}";
            stub.Json("POST", "/api/v1/fleet-action-runs/enumerate", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" + run + "," + adhoc + "]}");
            stub.Json("GET", "/api/v1/fleet-action-runs/far_1", "{\"Run\":" + run + ",\"Targets\":[]}");
            stub.Json("POST", "/api/v1/fleet-action-runs/far_1/cancel", run.Replace("\"Running\"", "\"Cancelled\""));
            stub.Json("POST", "/api/v1/fleet-action-runs/far_1/targets/enumerate", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                "{\"Id\":\"fat_a\",\"RunId\":\"far_1\",\"VesselId\":\"vsl_a\",\"VesselName\":\"alpha-repo\",\"Status\":\"Succeeded\",\"ExitCode\":0,\"OutputTruncated\":true,\"DurationMs\":850,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}," +
                "{\"Id\":\"fat_b\",\"RunId\":\"far_1\",\"VesselId\":\"vsl_b\",\"VesselName\":\"beta-repo\",\"Status\":\"Failed\",\"FailureReason\":\"NonZeroExit\",\"ExitCode\":2,\"DurationMs\":4200,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/fleet-action-runs/far_1/targets/fat_a", "{\"Id\":\"fat_a\",\"RunId\":\"far_1\",\"VesselId\":\"vsl_a\",\"VesselName\":\"alpha-repo\",\"Status\":\"Succeeded\",\"RenderedText\":\"echo alpha-repo\",\"ExitCode\":0,\"OutputText\":\"hello from alpha\",\"ErrorText\":\"\",\"OutputTruncated\":true,\"DurationMs\":850,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}");
            return stub;
        }
    }
}
