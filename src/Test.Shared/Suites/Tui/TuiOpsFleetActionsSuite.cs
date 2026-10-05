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
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Fleet Actions (W3.7) against a stubbed client: the actions grid, the create form with validation and the
    /// settings default timeout, hiding a built-in, the run flow from the vessel picker through the review step to
    /// the run page, the runs tab cancel, and the run page with its target drawer and live refresh.
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

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI fleet actions", cases: cases);
        }

        private static readonly string _ActionsJson = "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                "{\"Id\":\"fa_builtin\",\"Name\":\"Fast-forward default branch\",\"Kind\":\"Command\",\"CommandText\":\"git pull --ff-only\",\"TimeoutSeconds\":300,\"DefaultConcurrency\":4,\"RequiresCleanWorkingTree\":true,\"IsBuiltIn\":true,\"Active\":true,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}," +
                "{\"Id\":\"fa_echo\",\"Name\":\"Echo name\",\"Description\":\"Prints the vessel name\",\"Kind\":\"Command\",\"CommandText\":\"echo {{vessel.name}}\",\"TimeoutSeconds\":60,\"DefaultConcurrency\":2,\"RequiresCleanWorkingTree\":true,\"IsBuiltIn\":false,\"Active\":true,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}]}";

        private static T Content<T>(TuiTestHost host) where T : class
        {
            HubScreen hub = (HubScreen)host.Tui.Shell.Screen!;
            return (T)(object)hub.Content;
        }

        private static StubHttpHandler Stub()
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
