namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Build;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Vessel Health (W4.3): summary chips, server filters and sort, route round-trip, detail sections, overrides,
    /// and evaluation tracking, against a stubbed client.
    /// </summary>
    public sealed class TuiBuildHealthSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Build.Health";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "grid", "Health restores filters from the route, filters on the server, and writes the filters back", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(190, 50, "/vessels/health?deps=Fail&sort=VulnerableCount&dir=desc", stub))
                {
                    AssertTrue(host.WaitForText("DemoRepo"), "rows\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Health]", "tab");
                    TuiCase.Contains(frame, "1 2 Fail", "fail chip");
                    TuiCase.Contains(frame, "| 4 vessels", "total");
                    TuiCase.Contains(frame, "x Fail", "overall badge");
                    TuiCase.Contains(frame, "^2 v5", "divergence");
                    TuiCase.Contains(frame, "! Dirty", "dirty");
                    TuiCase.Contains(frame, "3 (1 stale)", "branches");
                    TuiCase.Contains(frame, "Last evaluation", "last run");
                    string first = stub.Bodies.First(b => b.Contains("\"SortBy\""));
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"SortBy\":\"VulnerableCount\"") && b.Contains("\"SortDescending\":true") && b.Contains("\"DependencyStatus\":[\"Fail\"]")), "restored request: " + first);
                    VesselHealthScreen screen = (VesselHealthScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("1");
                    AssertTrue(host.PumpUntil(() => stub.Bodies.Any(b => b.Contains("\"OverallStatus\":[\"Fail\"]"))), "chip filters on the server");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath.Contains("overall=Fail")), "route updated: " + host.Tui.Context.Router.Current!.FullPath);
                    AssertTrue(host.Tui.Context.Router.Current!.FullPath.Contains("deps=Fail") && host.Tui.Context.Router.Current!.FullPath.Contains("sort=VulnerableCount"), "route keeps the rest: " + host.Tui.Context.Router.Current!.FullPath);
                    screen.DirtyFilter.Choose(screen.DirtyFilter.Options.First(o => o.Value == "yes"));
                    AssertTrue(host.PumpUntil(() => stub.Bodies.Any(b => b.Contains("\"IsDirty\":true"))), "dirty filter");
                    screen.CommitAfter.Value = "2026-09-01";
                    AssertTrue(host.PumpUntil(() => stub.Bodies.Any(b => b.Contains("LastCommitAfterUtc"))), "date filter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath.Contains("after=2026-09-01")), "date in route");
                    host.Press("X");
                    AssertTrue(host.PumpUntil(() => !host.Tui.Context.Router.Current!.FullPath.Contains("overall=")), "cleared");
                    host.Press("home").Press("space");
                    host.Press("R");
                    AssertTrue(host.PumpUntil(() => stub.Bodies.Any(b => b.Contains("\"VesselIds\":[\"vsl_demo\"]") && b.Contains("\"Force\":true"))), "re-evaluate selected");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Evaluation started for 1 vessel."))), "start toast");
                    AssertTrue(host.PumpUntil(() => stub.Count("GET /api/v1/jobs/job_eval") >= 1), "polls the job");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Evaluation finished: 1 evaluated, 0 failed."))), "finished toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail", "The health inspector shows summary, findings sentences, dependencies, saves and removes overrides", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(190, 55, "/vessels/health", stub))
                {
                    AssertTrue(host.WaitForText("DemoRepo"), "rows");
                    host.Press("home").Press("enter");
                    AssertTrue(host.WaitForText("Versus main"), "summary\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "2 ahead, 5 behind", "divergence");
                    TuiCase.Contains(frame, "3 (1 stale, 1 armada/*)", "branches");
                    host.Press("2");
                    AssertTrue(host.WaitForText("Behind by 5 commits (ahead 2)."), "finding sentence\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "2 outdated packages (1 major).", "deps finding");
                    TuiCase.Contains(host.Screen(), "Overridden to", "override chip");
                    host.Press("3");
                    AssertTrue(host.WaitForText("Newtonsoft.Json"), "dependencies");
                    TuiCase.Contains(host.Screen(), "12.0.1 -> 13.0.3", "version drift");
                    TuiCase.Contains(host.Screen(), "High", "severity");
                    host.Press("4");
                    AssertTrue(host.WaitForText("Accepted risk"), "overrides\n" + host.Screen());
                    VesselHealthDialog dialog = (VesselHealthDialog)host.App.Modals.Top!;
                    dialog.FormCriterion.Choose(dialog.FormCriterion.Options.First(o => o.Value == "WorkingTree"));
                    dialog.FormStatus.Choose(dialog.FormStatus.Options.First(o => o.Value == "Pass"));
                    dialog.FormNote.Value = "Scratch files only";
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/vessels/vsl_demo/health/overrides/WorkingTree") == 1), "save override");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"Status\":\"Pass\"") && b.Contains("Scratch files only")), "override body");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Override saved for Working tree."))), "saved toast");
                    dialog.ShowSection("overrides");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Remove the Overall override?", "remove text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/vessels/vsl_demo/health/overrides/Overall") == 1), "remove override");
                    host.Press("5");
                    AssertTrue(host.WaitForText("\"Findings\""), "raw json");
                    host.Press("o");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/vessels/vsl_demo"), "open vessel");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI vessel health", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = BuildStubs.Server();
            stub.Json("POST", "/api/v1/vessel-health/enumerate", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                "{\"Id\":\"vh_1\",\"VesselId\":\"vsl_demo\",\"VesselName\":\"DemoRepo\",\"FleetName\":\"Web\",\"CurrentBranch\":\"main\",\"OverallStatus\":\"Fail\",\"AheadOfDefault\":2,\"BehindDefault\":5,\"DivergenceStatus\":\"Fail\",\"IsDirty\":true,\"UntrackedCount\":1,\"BranchCount\":3,\"StaleBranchCount\":1,\"OutdatedCount\":2,\"OutdatedMajorCount\":1,\"DependencyStatus\":\"Fail\",\"VulnerableCount\":1,\"MaxVulnerabilitySeverity\":\"High\",\"VulnerabilityStatus\":\"Fail\",\"TestInfraStatus\":\"Pass\",\"CiStatus\":\"Warn\",\"EvaluatedUtc\":\"2026-10-04T09:00:00Z\"}," +
                "{\"VesselId\":\"vsl_api\",\"VesselName\":\"ApiRepo\",\"OverallStatus\":\"Unknown\"}]}");
            stub.Json("GET", "/api/v1/vessel-health/summary", "{\"TotalVessels\":4,\"Pass\":1,\"Warn\":0,\"Fail\":2,\"Unknown\":1,\"NotApplicable\":0,\"NotEvaluated\":1}");
            stub.Json("GET", "/api/v1/jobs", "{\"Success\":true,\"Objects\":[{\"Id\":\"job_old\",\"Name\":\"Vessel health evaluation\",\"Kind\":\"Report\",\"Status\":\"Succeeded\",\"CreatedUtc\":\"2026-10-04T08:00:00Z\",\"CompletedUtc\":\"2026-10-04T08:01:00Z\",\"LastUpdateUtc\":\"2026-10-04T08:01:00Z\"}],\"TotalRecords\":1}");
            stub.Json("POST", "/api/v1/vessel-health/evaluate", "{\"JobId\":\"job_eval\",\"AlreadyRunning\":false,\"VesselCount\":1}", System.Net.HttpStatusCode.Accepted);
            stub.Json("GET", "/api/v1/jobs/job_eval", "{\"Id\":\"job_eval\",\"Name\":\"Vessel health evaluation\",\"Kind\":\"Report\",\"Status\":\"Succeeded\",\"Progress\":100,\"ResultJson\":\"{\\\"Evaluated\\\":1,\\\"Failed\\\":0}\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}");
            string detail = "{\"Health\":{\"Id\":\"vh_1\",\"VesselId\":\"vsl_demo\",\"VesselName\":\"DemoRepo\",\"FleetName\":\"Web\",\"CurrentBranch\":\"main\",\"OverallStatus\":\"Fail\",\"AheadOfDefault\":2,\"BehindDefault\":5,\"BranchCount\":3,\"StaleBranchCount\":1,\"ArmadaBranchCount\":1,\"EvaluatedUtc\":\"2026-10-04T09:00:00Z\"}," +
                "\"Findings\":[{\"Criterion\":\"Dependencies\",\"Status\":\"Fail\",\"DetailCode\":\"OutdatedPackages\",\"ValueA\":2,\"ValueB\":1},{\"Criterion\":\"GitDivergence\",\"Status\":\"Fail\",\"DetailCode\":\"Behind\",\"ValueA\":2,\"ValueB\":5}]," +
                "\"Dependencies\":[{\"Id\":\"dep_1\",\"VesselId\":\"vsl_demo\",\"Ecosystem\":\"NuGet\",\"ProjectPath\":\"src/App.csproj\",\"PackageName\":\"Newtonsoft.Json\",\"CurrentVersion\":\"12.0.1\",\"LatestVersion\":\"13.0.3\",\"Drift\":\"Major\",\"IsVulnerable\":true,\"Severity\":\"High\",\"AdvisoryUrl\":\"https://example.test/adv\"}]," +
                "\"Overrides\":[{\"Criterion\":\"Overall\",\"Status\":\"Warn\",\"Note\":\"Accepted risk\",\"LastUpdateUtc\":\"2026-10-04T09:30:00Z\"},{\"Criterion\":\"GitDivergence\",\"Status\":\"Pass\",\"Note\":\"Release branch\",\"LastUpdateUtc\":\"2026-10-04T09:30:00Z\"}]}";
            stub.Json("GET", "/api/v1/vessels/vsl_demo/health", detail);
            stub.Json("PUT", "/api/v1/vessels/vsl_demo/health/overrides/WorkingTree", detail);
            stub.Json("DELETE", "/api/v1/vessels/vsl_demo/health/overrides/Overall", detail);
            return stub;
        }
    }
}
