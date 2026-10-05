namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Merge Queue and merge entry detail (W3.10) against a stubbed client.
    /// </summary>
    public sealed class TuiOpsMergeQueueSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.MergeQueue";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list", "Lists entries, filters by vessel, enqueues, processes, cancels, deletes, processes all, and bulk deletes", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 45, "/missions?tab=merge-queue", stub))
                {
                    AssertTrue(host.WaitForText("armada/one"), "rows\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Merge Queue]", "tab");
                    TuiCase.Contains(frame, "Completed missions awaiting merge.", "subtitle");
                    TuiCase.Contains(frame, "DemoRepo", "vessel name");
                    MergeQueueScreen screen = (MergeQueueScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.PumpUntil(() => screen.VesselFilter.Options.Count > 1);
                    screen.VesselFilter.Choose(screen.VesselFilter.Options.First(o => o.Value == "vsl_other"));
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("armada/one")), "vessel filter");
                    screen.VesselFilter.Choose(screen.VesselFilter.Options[0]);
                    AssertTrue(host.PumpUntil(() => host.Screen().Contains("armada/one")), "vessel filter cleared");

                    host.Press("n");
                    AssertTrue(host.WaitForText("Enqueue Merge"), "enqueue dialog");
                    host.Type("feature/x");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/merge-queue") == 1), "enqueue call");
                    StubRequest enqueue = stub.Last("POST", "/api/v1/merge-queue");
                    AssertEqual("feature/x", enqueue.BodyAs<Armada.Core.Models.MergeEntry>().BranchName, "enqueue branch: " + enqueue.Body);
                    AssertEqual("main", enqueue.BodyProperty("TargetBranch")?.ScalarText, "target branch sent (main is also the model default): " + enqueue.Body);
                    AssertEqual("0", enqueue.BodyProperty("Priority")?.ScalarText, "priority sent (0 is also the model default): " + enqueue.Body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Merge entry enqueued."))), "enqueue toast");

                    host.Press("home");
                    AssertEqual("mrg_1", screen.Grid.Current!.Id, "first");
                    host.Press("p");
                    TuiCase.Contains(host.Screen(), "Process merge entry mrg_1 now?", "process text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/merge-queue/mrg_1/process") == 1), "process");
                    host.Press("x");
                    TuiCase.Contains(host.Screen(), "Cancel merge entry mrg_1?", "cancel text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/merge-queue/mrg_1") == 1), "cancel uses the server's delete-or-cancel route");
                    host.Press(".");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "row menu");
                    TuiCase.Contains(host.Screen(), "Mission Diff", "diff offered with a mission");
                    host.Press("esc");
                    host.Press("d");
                    AssertTrue(host.WaitForText("+merge change"), "mission diff\n" + host.Screen());
                    host.Press("esc");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete merge entry mrg_1? This cannot be undone.", "delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/merge-queue/mrg_1") == 2), "delete");
                    host.Press("P");
                    TuiCase.Contains(host.Screen(), "Process all queued entries in the merge queue now?", "process all text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/merge-queue/process") == 1), "process all");
                    host.Press("home").Press("space").Press("down").Press("space");
                    host.Press("D");
                    TuiCase.Contains(host.Screen(), "Delete 2 selected merge queue entries? This cannot be undone.", "bulk text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/merge-queue/mrg_2") == 1), "bulk delete");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Deleted 2 merge entries."))), "bulk toast");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path.StartsWith("/merge-queue/", StringComparison.Ordinal)), "Enter opens the entry");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail", "Merge entry shows landing preview, fields, and test output; processes and deletes", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/merge-queue/mrg_1", stub))
                {
                    AssertTrue(host.WaitForText("Protected target branch"), "landing preview\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "armada/one -> main", "branches");
                    TuiCase.Contains(frame, "Merge queue required for release branches", "policy flag");
                    TuiCase.Contains(frame, "Behind target", "issue");
                    TuiCase.Contains(frame, "Test Exit Code", "fields");
                    TuiCase.Contains(frame, "[ Diff d ]", "diff button");
                    host.Press("]");
                    AssertTrue(host.WaitForText("3 tests failed"), "test output");
                    host.Press("p");
                    TuiCase.Contains(host.Screen(), "Process merge entry for branch \"armada/one\"?", "process text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/merge-queue/mrg_1/process") == 1), "process");
                    host.Press("l");
                    AssertTrue(host.WaitForText("Log: Mission msn_1..."), "log title\n" + host.Screen());
                    host.Press("esc");
                    host.Press("del");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/merge-queue/mrg_1") == 1), "delete");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions"), "back to the queue");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI merge queue", cases: cases);
        }

        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            string entry1 = "{\"Id\":\"mrg_1\",\"BranchName\":\"armada/one\",\"TargetBranch\":\"main\",\"Status\":\"Failed\",\"Priority\":5,\"MissionId\":\"msn_1\",\"VesselId\":\"vsl_demo\",\"TestCommand\":\"make test\",\"TestOutput\":\"3 tests failed\",\"TestExitCode\":1,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}";
            stub.Json("GET", "/api/v1/merge-queue", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" + entry1 + "," +
                "{\"Id\":\"mrg_2\",\"BranchName\":\"armada/two\",\"TargetBranch\":\"main\",\"Status\":\"Queued\",\"Priority\":1,\"VesselId\":\"vsl_other\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/merge-queue/mrg_1", entry1);
            stub.Json("POST", "/api/v1/merge-queue", "{\"Id\":\"mrg_3\",\"BranchName\":\"feature/x\",\"TargetBranch\":\"main\"}");
            stub.Json("POST", "/api/v1/merge-queue/mrg_1/process", "{}");
            stub.Json("POST", "/api/v1/merge-queue/process", "{}");
            stub.On("DELETE", "/api/v1/merge-queue/mrg_1", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/merge-queue/mrg_2", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("GET", "/api/v1/missions/msn_1/diff", "{\"Diff\":\"diff --git a/z b/z\\n+merge change\\n\"}");
            stub.Json("GET", "/api/v1/missions/msn_1/log", "{\"Log\":\"line\",\"Lines\":1,\"TotalLines\":1}");
            stub.Json("GET", "/api/v1/vessels/vsl_demo/landing-preview", "{\"VesselId\":\"vsl_demo\",\"SourceBranch\":\"armada/one\",\"TargetBranch\":\"main\",\"BranchCategory\":\"Feature\",\"TargetBranchProtected\":true,\"RequireMergeQueueForReleaseBranches\":true,\"IsReadyToLand\":false,\"Issues\":[{\"Code\":\"behind\",\"Severity\":\"Error\",\"Title\":\"Behind target\",\"Message\":\"Rebase first\"}]}");
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\",\"DefaultBranch\":\"main\"},{\"Id\":\"vsl_other\",\"Name\":\"Other\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":2}");
            stub.Json("GET", "/api/v1/users", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/voyages", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            return stub;
        }
    }
}
