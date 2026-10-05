namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Jobs (W3.11) against a stubbed client: columns, error reason, cancel while unfinished, and View JSON.
    /// </summary>
    public sealed class TuiOpsJobsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.Jobs";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_and_cancel", "Jobs lists every column, cancels an unfinished job, and shows JSON", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/jobs", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":100,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                    "{\"Id\":\"job_run\",\"Name\":\"Evaluate vessel health\",\"Kind\":\"VesselDiscovery\",\"Status\":\"Running\",\"Progress\":40,\"CreatedUtc\":\"" + Iso(-5) + "\",\"LastUpdateUtc\":\"" + Iso(-1) + "\"}," +
                    "{\"Id\":\"job_bad\",\"Name\":\"Import repositories\",\"Kind\":\"VesselImport\",\"Status\":\"Failed\",\"Progress\":100,\"ErrorReason\":\"disk full\",\"CreatedUtc\":\"" + Iso(-60) + "\",\"LastUpdateUtc\":\"" + Iso(-30) + "\"}]}");
                stub.Json("POST", "/api/v1/jobs/job_run/cancel", "{\"Id\":\"job_run\",\"Name\":\"Evaluate vessel health\",\"Status\":\"Cancelled\"}");
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/jobs", stub))
                {
                    AssertTrue(host.WaitForText("Evaluate vessel health"), "row shown\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Background jobs and their status.", "subtitle");
                    TuiCase.Contains(frame, "Progress", "progress column");
                    TuiCase.Contains(frame, "40%", "progress value");
                    TuiCase.Contains(frame, "(disk full)", "error reason");
                    TuiCase.Contains(frame, "~ Running", "status badge");
                    JobsScreen screen = (JobsScreen)host.Tui.Shell.Screen!;
                    AssertEqual("job_run", screen.Grid.Current!.Id, "newest first");
                    host.Press("x");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/jobs/job_run/cancel") == 1), "cancel call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Warning && t.Text.Contains("Job \"Evaluate vessel health\" cancelled."))), "toast");
                    host.Press("down");
                    int before = stub.CountFor("POST", "/api/v1/jobs/job_bad/cancel");
                    host.Press("x");
                    host.Pump();
                    AssertEqual(before, stub.CountFor("POST", "/api/v1/jobs/job_bad/cancel"), "finished jobs cannot be cancelled");
                    host.Press("j");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "JSON viewer");
                    TuiCase.Contains(host.Screen(), "\"ErrorReason\": \"disk full\"", "JSON body");
                    host.Press("esc");
                    host.Press(".");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "row menu");
                    TuiCase.Contains(host.Screen(), "View JSON", "menu item");
                    TuiCase.NotContains(host.Screen(), "! Cancel", "no cancel for failed job");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI jobs", cases: cases);
        }

        private static string Iso(int minutes)
        {
            return DateTime.UtcNow.AddMinutes(minutes).ToString("o");
        }
    }
}
